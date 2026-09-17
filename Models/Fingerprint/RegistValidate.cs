namespace AvaloniaFingerprint.Models.Fingerprint;

public class RegistValidate
{
    public class MetaData
    {
        public string? message { get; set; }
        public int code { get; set; }
        public bool isValid { get; set; }
    }

    public class RegistValidateRoot
    {
        public bool response { get; set; }
        public MetaData? metaData { get; set; }
    }
}
