namespace MultimediaSimulator.Streaming;

public class StreamingOptions
{
    public const string SectionName = "Streaming";

    // where ffmpeg publishes over RTSP, {0} = uavId; Wowza app {0}, stream {0}.stream.
    // Wowza must receive the stream itself (not pull it through a relay) so its first chunk is the file's first frame.
    // If the app requires publish auth, put credentials in the URL via config/env, not in this file.
    public string PublishUrlFormat { get; set; } = "rtsp://localhost:1935/{0}/{0}.stream";

    public string UploadDirectory { get; set; } = "uploads";

    // Wowza module endpoint that registers a stream's clock origin before it is published
    public string WowzaClockUrl { get; set; } = "http://localhost:1935/streamclock";

    // name Wowza knows the stream by, {0} = uavId; must match the stream part of PublishUrlFormat
    public string WowzaStreamNameFormat { get; set; } = "{0}.stream";
}
