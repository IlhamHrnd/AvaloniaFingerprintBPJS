using System.Collections.Generic;
using Newtonsoft.Json;

namespace AvaloniaFingerprint.Models.Fingerprint;

public class RightFingerModel
{
    public class Finger
    {
        public string finger { get; set; } = string.Empty;
        public int index { get; set; }
        public string imgStr { get; set; } = string.Empty;
    }
}

public class LeftFingerModel
{
    public class Finger
    {
        public string finger { get; set; } = string.Empty;
        public int index { get; set; }
        public string imgStr { get; set; } = string.Empty;
    }
}

public class MergeFingerModel
{
    public class Finger
    {
        [JsonProperty("finger")]
        public string finger { get; set; } = string.Empty;

        [JsonProperty("index")]
        public int index { get; set; }

        [JsonProperty("imgStr")]
        public string imgStr { get; set; } = string.Empty;
    }

    public class FingerRoot
    {
        [JsonProperty("nokapst")]
        public string nokapst { get; set; } = string.Empty;

        [JsonProperty("finger")]
        public List<Finger> finger { get; set; } = new();
    }
}
