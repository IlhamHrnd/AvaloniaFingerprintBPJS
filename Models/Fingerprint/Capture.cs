namespace AvaloniaFingerprint.Models.Fingerprint
{
    public class Capture
    {
        public string ImageBase64 { get; init; } = string.Empty;

        public string VerificationTemplate { get; init; } = string.Empty;

        public byte[]? RawImage { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }
}
