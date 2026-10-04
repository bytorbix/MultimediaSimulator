using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MultimediaSimulator.Streaming;

namespace MultimediaSimulator.Controllers;

// StartAt: now or a future date up to 7 days ahead (past or further is rejected); null = now.
// It is both when streaming starts (the service waits until StartAt before launching ffmpeg)
// and the stream's clock origin: it's sent to Wowza, which stamps the first frame as StartAt
// and every later frame as StartAt + its offset. Give the telemetry simulator the same value.
public record StartStreamRequest(DateTimeOffset? StartAt);

[ApiController]
[Route("api/uav")]
public class UavStreamController : ControllerBase
{
    // the service waits in memory until StartAt, so a far-off date would only fail later, after the 202
    private static readonly TimeSpan MaxScheduleAhead = TimeSpan.FromDays(7);

    private readonly IUavStreamService _streamService;

    public UavStreamController(IUavStreamService streamService)
    {
        _streamService = streamService;
    }

    // saves the file only; streaming is started separately with /start
    [HttpPost("{uavId}/upload")]
    [RequestSizeLimit(long.MaxValue)]
    public async Task<IActionResult> Upload(string uavId, IFormFile file, CancellationToken cancellationToken)
    {
        if (_streamService.IsStreaming(uavId))
        {
            return Conflict($"UAV '{uavId}' is streaming; stop it before uploading a new file.");
        }

        await using var stream = file.OpenReadStream();
        await _streamService.SaveUploadAsync(uavId, stream, cancellationToken);
        return Ok();
    }

    // 202: the clock is registered with Wowza, streaming begins at StartAt
    [HttpPost("{uavId}/start")]
    public async Task<IActionResult> Start(string uavId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] StartStreamRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateStartAt(request?.StartAt) is { } invalid)
        {
            return invalid;
        }

        DateTimeOffset startAt = request?.StartAt ?? DateTimeOffset.UtcNow;
        return ToResult(uavId, await _streamService.StartStreamAsync(uavId, startAt, cancellationToken));
    }

    [HttpPost("{uavId}/stop")]
    public async Task<IActionResult> StopStream(string uavId)
    {
        var stopped = await _streamService.StopStreamAsync(uavId);
        return stopped ? Ok() : NotFound();
    }

    // null = valid (or not given, meaning now)
    private IActionResult? ValidateStartAt(DateTimeOffset? startAt)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (startAt < now)
        {
            return BadRequest($"StartAt '{startAt:O}' is already in the past.");
        }

        if (startAt > now + MaxScheduleAhead)
        {
            return BadRequest($"StartAt '{startAt:O}' is more than {MaxScheduleAhead.TotalDays} days ahead.");
        }

        return null;
    }

    private IActionResult ToResult(string uavId, StartStreamResult result) => result switch
    {
        StartStreamResult.Started => Accepted(),
        StartStreamResult.UploadNotFound => NotFound($"No uploaded file for UAV '{uavId}'. Upload one first."),
        StartStreamResult.AlreadyStreaming => Conflict($"UAV '{uavId}' is already streaming."),
        StartStreamResult.ClockRegistrationFailed => StatusCode(StatusCodes.Status502BadGateway, $"Could not register the stream clock with Wowza for UAV '{uavId}'."),
        _ => StatusCode(500)
    };
}
