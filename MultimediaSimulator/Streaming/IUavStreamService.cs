namespace MultimediaSimulator.Streaming;

public enum StartStreamResult
{
    Started,
    UploadNotFound,
    AlreadyStreaming,
    ClockRegistrationFailed
}

public interface IUavStreamService
{
    Task SaveUploadAsync(string uavId, Stream tsFile, CancellationToken cancellationToken);

    // registers startAt with Wowza, then streams the uploaded file from startAt on (in the background)
    Task<StartStreamResult> StartStreamAsync(string uavId, DateTimeOffset startAt, CancellationToken cancellationToken);

    Task<bool> StopStreamAsync(string uavId);

    // true while scheduled (waiting for startAt) or streaming
    bool IsStreaming(string uavId);
}
