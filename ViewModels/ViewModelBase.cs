using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using Telerik.Reporting;
using Telerik.Reporting.Processing;

namespace AvaloniaFingerprint.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected void printProcess(List<InstanceReportSource> rptSource, List<Models.Master.Parameter> parameters, string printerName, int fromPage, int toPage, int copies = 1)
    {
        if (string.IsNullOrEmpty(printerName)) return;
        else if (rptSource.Count == 0) return;

        try
        {
            var rbs = new ReportBook();
            foreach (var item in rptSource)
            {
                rbs.ReportSources.Add(item);
            }

            var irs = new InstanceReportSource
            {
                ReportDocument = rbs
            };

            foreach (var item in parameters)
            {
                irs.Parameters.Add(new Telerik.Reporting.Parameter(item.name, item.value));
            }

            var printCopies = copies <= 0 ? (short)1 : (copies > short.MaxValue ? short.MaxValue : (short)copies);
            var ps = new PrinterSettings
            {
                PrinterName = printerName,
                FromPage = fromPage,
                ToPage = toPage,
                Copies = printCopies
            };
            var spc = new StandardPrintController();
            var rp = new ReportProcessor
            {
                PrintController = spc
            };
            rp.PrintReport(irs, ps);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error : {e.Message} - {e.InnerException?.Message}");
        }

        return;
    }
}
