using Microsoft.AspNetCore.Mvc;
using MultimediaSimulator.Streaming;

namespace MultimediaSimulator.Controllers;

[ApiController]
[Route("api/uav")]
public class UavStreamController : ControllerBase
{
    private readonly IUavStreamService _streamService;

    public UavStreamController(IUavStreamService streamService)
    {
        _streamService = streamService;
    }

    // upload only: lets callers get the (large) file in place ahead of time, then start with /start
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

    [HttpPost("{uavId}/start")]
    public IActionResult Start(string uavId)
    {
        return ToResult(uavId, _streamService.StartStream(uavId));
    }

    // upload + start in one call (kept for existing clients)
    [HttpPost("{uavId}/stream")]
    [RequestSizeLimit(long.MaxValue)]
    public async Task<IActionResult> StartStream(string uavId, IFormFile file, CancellationToken cancellationToken)
    {
        if (_streamService.IsStreaming(uavId))
        {
            return Conflict($"UAV '{uavId}' is already streaming.");
        }

        await using var stream = file.OpenReadStream();
        return ToResult(uavId, await _streamService.StartStreamAsync(uavId, stream, cancellationToken));
    }

    [HttpPost("{uavId}/stop")]
    public async Task<IActionResult> StopStream(string uavId)
    {
        var stopped = await _streamService.StopStreamAsync(uavId);
        return stopped ? Ok() : NotFound();
    }

    private IActionResult ToResult(string uavId, StartStreamResult result) => result switch
    {
        StartStreamResult.Started => Ok(),
        StartStreamResult.UploadNotFound => NotFound($"No uploaded file for UAV '{uavId}'. Upload one first."),
        StartStreamResult.AlreadyStreaming => Conflict($"UAV '{uavId}' is already streaming."),
        _ => StatusCode(500)
    };
}
