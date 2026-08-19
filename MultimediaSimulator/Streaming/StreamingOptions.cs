namespace MultimediaSimulator.Streaming;

public class StreamingOptions
{
    public const string SectionName = "Streaming";

    public string MediaMtxHost { get; set; } = "localhost";
    public int MediaMtxPort { get; set; } = 8554;
    public string UploadDirectory { get; set; } = "uploads";
}
