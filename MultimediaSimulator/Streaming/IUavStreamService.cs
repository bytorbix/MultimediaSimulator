namespace MultimediaSimulator.Streaming;

public enum StartStreamResult
{
    Started,
    UploadNotFound,
    AlreadyStreaming
}

public interface IUavStreamService
{
    Task SaveUploadAsync(string uavId, Stream tsFile, CancellationToken cancellationToken);

    StartStreamResult StartStream(string uavId);

    Task<StartStreamResult> StartStreamAsync(string uavId, Stream tsFile, CancellationToken cancellationToken);

    Task<bool> StopStreamAsync(string uavId);

    bool IsStreaming(string uavId);
}
