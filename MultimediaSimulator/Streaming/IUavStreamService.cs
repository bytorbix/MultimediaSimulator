namespace MultimediaSimulator.Streaming;

public interface IUavStreamService
{
    Task StartStreamAsync(string uavId, Stream tsFile, CancellationToken cancellationToken);

    Task<bool> StopStreamAsync(string uavId);

    bool IsStreaming(string uavId);
}
