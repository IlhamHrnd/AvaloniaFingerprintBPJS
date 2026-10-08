using Newtonsoft.Json;

namespace AvaloniaFingerprint.Filters.FaceRecognition
{
    public class NIK
    {
        [JsonProperty("nik")]
        public string nik { get; set; } = string.Empty;

        [JsonProperty("token")]
        public string token { get; set; } = string.Empty;
    }
}
