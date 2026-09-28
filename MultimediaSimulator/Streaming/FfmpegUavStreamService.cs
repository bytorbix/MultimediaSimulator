using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MultimediaSimulator.Streaming;

public class FfmpegUavStreamService : IUavStreamService
{
    private readonly StreamingOptions _options;
    private readonly ILogger<FfmpegUavStreamService> _logger;
    private readonly ConcurrentDictionary<string, Process> _processes = new();

    public FfmpegUavStreamService(IOptions<StreamingOptions> options, ILogger<FfmpegUavStreamService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private string UploadPath(string uavId) => Path.Combine(_options.UploadDirectory, $"{uavId}.ts");

    public async Task SaveUploadAsync(string uavId, Stream tsFile, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.UploadDirectory);

        await using var fileStream = File.Create(UploadPath(uavId));
        await tsFile.CopyToAsync(fileStream, cancellationToken);
        _logger.LogInformation("Saved upload for UAV '{UavId}' ({Bytes} bytes)", uavId, fileStream.Length);
    }

    public async Task<StartStreamResult> StartStreamAsync(string uavId, Stream tsFile, CancellationToken cancellationToken)
    {
        await SaveUploadAsync(uavId, tsFile, cancellationToken);
        return StartStream(uavId);
    }

    // starts from an already uploaded file, so the call returns as soon as ffmpeg is launched
    public StartStreamResult StartStream(string uavId)
    {
        var filePath = UploadPath(uavId);
        if (!File.Exists(filePath))
        {
            return StartStreamResult.UploadNotFound;
        }

        string rtspUrl = $"rtsp://{_options.MediaMtxHost}:{_options.MediaMtxPort}/{uavId}";
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
            _processes.TryRemove(uavId, out _);
        };

        if (!_processes.TryAdd(uavId, process))
        {
            process.Dispose();
            return StartStreamResult.AlreadyStreaming;
        }

        process.Start();
        process.BeginErrorReadLine();
        _logger.LogInformation("Started stream for UAV '{UavId}' to {RtspUrl}", uavId, rtspUrl);
        return StartStreamResult.Started;
    }


    public async Task<bool> StopStreamAsync(string uavId)
    {
        if (!_processes.TryRemove(uavId, out var process)) 
        {
            return false;
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        process.Dispose();
        _logger.LogInformation("Stopped stream for UAV '{UavId}'", uavId);
        return true;
    }

    public bool IsStreaming(string  uavId)
    {
        return _processes.TryGetValue(uavId, out var process) && !process.HasExited;
    }
} 