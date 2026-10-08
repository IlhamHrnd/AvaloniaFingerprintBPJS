using Newtonsoft.Json;

namespace AvaloniaFingerprint.Models.FaceRecognition
{
    public class NIK
    {
        public class Root
        {
            [JsonProperty("nama")]
            public string nama { get; set; } = string.Empty;

            [JsonProperty("nik")]
            public string nik { get; set; } = string.Empty;

            [JsonProperty("nokartu")]
            public string nokartu { get; set; } = string.Empty;

            [JsonProperty("message")]
            public string message { get; set; } = string.Empty;

            [JsonProperty("status")]
            public bool status { get; set; } = false;
        }
    }
}
