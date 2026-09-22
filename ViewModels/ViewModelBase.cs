using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Drawing.Printing;
using Telerik.Reporting;
using Telerik.Reporting.Processing;

namespace AvaloniaFingerprint.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected void printProcess(Filters.Printout.Print print)
    {
        if (string.IsNullOrEmpty(print.printerName)) return;
        else if (print.rptSource.Count == 0) return;

        try
        {
            var rbs = new ReportBook();
            foreach (var item in print.rptSource)
            {
                rbs.ReportSources.Add(item);
            }

            var irs = new InstanceReportSource
            {
                ReportDocument = rbs
            };

            foreach (var item in print.parameters)
            {
                irs.Parameters.Add(new Telerik.Reporting.Parameter(item.name, item.value));
            }

            var printCopies = print.copies <= 0 ? (short)1 : (print.copies > short.MaxValue ? short.MaxValue : (short)print.copies);
            var ps = new PrinterSettings
            {
                PrinterName = print.printerName,
                FromPage = print.fromPage,
                ToPage = print.toPage,
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
