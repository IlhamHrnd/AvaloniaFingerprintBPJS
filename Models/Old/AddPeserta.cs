using System.Collections.Generic;

namespace AvaloniaFingerprint.Models.Old;

public class AddPeserta
{
    public string nokapst { get; set; } = string.Empty;
    public List<Finger> finger { get; set; } = new();

    public class Finger
    {
        public string finger { get; set; } = string.Empty;
        public int index { get; set; }
        public string imgStr { get; set; } = string.Empty;
    }
}
