using System.Collections.Generic;
using Telerik.Reporting;

namespace AvaloniaFingerprint.Filters.Printout
{
    public class Print
    {
        public List<InstanceReportSource> rptSource { get; set; } = [];
        public List<Models.Master.Parameter> parameters { get; set; } = [];
        public string printerName { get; set; } = string.Empty;
        public int fromPage { get; set; } = 1;
        public int toPage { get; set; } = 1;
        public int copies { get; set; } = 3;
    }
}
