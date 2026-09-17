namespace AvaloniaFingerprint.Models.Fingerprint
{
    public class Capture
    {
        /// <summary>Base64 JPEG of the scanned finger.</summary>
        public string ImageBase64 { get; init; } = string.Empty;

        /// <summary>Base64 DP_VERIFICATION template — what the Verify endpoint expects.</summary>
        public string VerificationTemplate { get; init; } = string.Empty;

        /// <summary>
        /// Opaque DP_PRE_REGISTRATION FMD. Collected across several scans and handed back to
        /// <see cref="FingerprintService.CreateEnrollmentTemplate"/>; the concrete DPUruNet type
        /// is kept out of the public surface so the project still compiles without the SDK.
        /// </summary>
        public object? PreRegistrationFmd { get; init; }

        /// <summary>Raw 8-bit grayscale image plus its dimensions, for on-screen preview.</summary>
        public byte[]? RawImage { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }
}
