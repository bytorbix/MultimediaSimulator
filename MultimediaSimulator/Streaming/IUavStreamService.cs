namespace MultimediaSimulator.Streaming;

public interface IUavStreamService
{
    Task StartStreamAsync(string uavId, Stream tsFile, CancellationToken cancellationToken);

    bool StopStream(string uavId);

    bool IsStreaming(string uavId);
}
