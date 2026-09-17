using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AvaloniaFingerprint.Models.Old;

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
        [JsonPropertyName("finger")]
        public string finger { get; set; } = string.Empty;

        [JsonPropertyName("index")]
        public int index { get; set; }

        [JsonPropertyName("imgStr")]
        public string imgStr { get; set; } = string.Empty;
    }

    public class FingerRoot
    {
        [JsonPropertyName("nokapst")]
        public string nokapst { get; set; } = string.Empty;

        [JsonPropertyName("finger")]
        public List<Finger> finger { get; set; } = new();
    }
}
