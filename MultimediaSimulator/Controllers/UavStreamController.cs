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

    [HttpPost("{uavId}/stream")]
    [RequestSizeLimit(long.MaxValue)]
    public async Task<IActionResult> StartStream(string uavId, IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        await _streamService.StartStreamAsync(uavId, stream, cancellationToken);
        return Ok();
    }

    [HttpPost("{uavId}/stop")]
    public async Task<IActionResult> StopStream(string uavId)
    {
        var stopped = await _streamService.StopStreamAsync(uavId);
        return stopped ? Ok() : NotFound();
    }
}