using System.Collections.Generic;

namespace AvaloniaFingerprint.Filters.FaceRecognition
{
    public class Verify
    {
        public List<double> encoding { get; set; } = [];
        public string id { get; set; } = string.Empty;
        public string token { get; set; } = string.Empty;
    }
}
