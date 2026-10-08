using Newtonsoft.Json;

namespace AvaloniaFingerprint.Models.FaceRecognition
{
    public class Verify
    {
        public class Root
        {
            [JsonProperty("message")]
            public string message { get; set; } = string.Empty;

            [JsonProperty("status")]
            public bool status { get; set; } = false;

            [JsonProperty("data")]
            public Data data { get; set; } = new Data();
        }

        public class Data
        {
            [JsonProperty("distance")]
            public double distance { get; set; } = 0;
        }
    }
}
