using System;

namespace AvaloniaFingerprint.Models.Paramedic
{
    public class ParamedicScheduleBpjsQuota
    {
        public string paramedicid { get; set; } = string.Empty;

        public string paramedicname { get; set; } = string.Empty;

        public string serviceunitid { get; set; } = string.Empty;

        public string serviceunitname { get; set; } = string.Empty;

        public DateTime? scheduledate { get; set; }

        public string bpjsparamedicid { get; set; } = string.Empty;

        public string bpjsserviceunitid { get; set; } = string.Empty;

        public string bpjssubserviceunitid { get; set; } = string.Empty;

        public string bpjsserviceunitname { get; set; } = string.Empty;

        public int examduration { get; set; }

        public string operationalid { get; set; } = string.Empty;

        public string operational1 { get; set; } = string.Empty;

        public string operational2 { get; set; } = string.Empty;

        public string operational3 { get; set; } = string.Empty;

        public string operational4 { get; set; } = string.Empty;

        public int quota { get; set; }

        public int quotasisa { get; set; }

        public int quotaterpakai { get; set; }
    }
}
