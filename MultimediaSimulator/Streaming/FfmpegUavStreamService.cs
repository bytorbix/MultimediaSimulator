using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MultimediaSimulator.Streaming;

public class FfmpegUavStreamService : IUavStreamService
{
    public const string WowzaHttpClientName = "Wowza";

    // one start of one UAV: scheduled until startAt, then streaming once Process is set
    private sealed class StreamRun
    {
        public CancellationTokenSource Cts { get; } = new();
        public Process? Process { get; set; }
    }

    private readonly StreamingOptions _options;
    private readonly ILogger<FfmpegUavStreamService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, StreamRun> _runs = new();

    public FfmpegUavStreamService(IOptions<StreamingOptions> options, ILogger<FfmpegUavStreamService> logger, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    private string UploadPath(string uavId) => Path.Combine(_options.UploadDirectory, $"{uavId}.ts");

    public async Task SaveUploadAsync(string uavId, Stream tsFile, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.UploadDirectory);

        await using var fileStream = File.Create(UploadPath(uavId));
        await tsFile.CopyToAsync(fileStream, cancellationToken);
        _logger.LogInformation("Saved upload for UAV '{UavId}' ({Bytes} bytes)", uavId, fileStream.Length);
    }

    // Wowza must know startAt before the first packet, so it's registered here and only then is the stream scheduled
    public async Task<StartStreamResult> StartStreamAsync(string uavId, DateTimeOffset startAt, CancellationToken cancellationToken)
    {
        if (!File.Exists(UploadPath(uavId)))
        {
            return StartStreamResult.UploadNotFound;
        }

        var run = new StreamRun();
        if (!_runs.TryAdd(uavId, run))
        {
            return StartStreamResult.AlreadyStreaming;
        }

        try
        {
            await RegisterClockAsync(uavId, startAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _runs.TryRemove(new KeyValuePair<string, StreamRun>(uavId, run));

            if (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logger.LogError(ex, "Registering the clock with Wowza failed for UAV '{UavId}'", uavId);
                return StartStreamResult.ClockRegistrationFailed;
            }
            throw;
        }

        _ = Task.Run(() => RunScheduledAsync(uavId, run, startAt));
        return StartStreamResult.Started;
    }

    private async Task RegisterClockAsync(string uavId, DateTimeOffset startAt, CancellationToken cancellationToken)
    {
        string streamName = string.Format(_options.WowzaStreamNameFormat, uavId);
        long startAtMs = startAt.ToUnixTimeMilliseconds();
        string url = $"{_options.WowzaClockUrl}?stream={Uri.EscapeDataString(streamName)}&startAtMs={startAtMs}";

        using var client = _httpClientFactory.CreateClient(WowzaHttpClientName);
        using var response = await client.PostAsync(url, content: null, cancellationToken);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Registered clock for UAV '{UavId}' with Wowza: stream '{StreamName}' starts at {StartAt:O}", uavId, streamName, startAt);
    }

    private async Task RunScheduledAsync(string uavId, StreamRun run, DateTimeOffset startAt)
    {
        try
        {
            TimeSpan wait = startAt - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                _logger.LogInformation("UAV '{UavId}' scheduled to start at {StartAt:O} (in {Wait})", uavId, startAt, wait);
                await Task.Delay(wait, run.Cts.Token);
            }

            LaunchFfmpeg(uavId, run);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Scheduled stream for UAV '{UavId}' was stopped before it started", uavId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start stream for UAV '{UavId}'", uavId);
            _runs.TryRemove(new KeyValuePair<string, StreamRun>(uavId, run));
        }
    }

    private void LaunchFfmpeg(string uavId, StreamRun run)
    {
        string filePath = UploadPath(uavId);
        string rtspUrl = string.Format(_options.PublishUrlFormat, uavId);
        string arguments = $"-stream_loop -1 -re -i \"{filePath}\" -c:v copy -c:a aac -rtsp_transport tcp -f rtsp \"{rtspUrl}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                _logger.LogInformation("[ffmpeg:{UavId}] {Line}", uavId, e.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            _logger.LogInformation("Stream for UAV '{UavId}' ended (exit code {ExitCode})", uavId, process.ExitCode);
            // only this run: a newer run for the same UAV may already be registered
            _runs.TryRemove(new KeyValuePair<string, StreamRun>(uavId, run));
        };

        lock (run)
        {
            if (run.Cts.IsCancellationRequested)
            {
                process.Dispose();
                return;
            }

            run.Process = process;
            process.Start();
        }

        process.BeginErrorReadLine();
        _logger.LogInformation("Started stream for UAV '{UavId}' to {RtspUrl}", uavId, WithoutCredentials(rtspUrl));
    }

    // publish URLs may carry user:password, keep it out of the logs
    private static string WithoutCredentials(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0
            ? url.Replace(uri.UserInfo + "@", "")
            : url;
    }

    public async Task<bool> StopStreamAsync(string uavId)
    {
        if (!_runs.TryRemove(uavId, out var run))
        {
            return false;
        }

        Process? process;
        lock (run)
        {
            run.Cts.Cancel();
            process = run.Process;
        }

        if (process is not null)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            process.Dispose();
        }

        _logger.LogInformation("Stopped stream for UAV '{UavId}'", uavId);
        return true;
    }

    public bool IsStreaming(string uavId)
    {
        return _runs.TryGetValue(uavId, out var run) && (run.Process is null || !run.Process.HasExited);
    }
}
