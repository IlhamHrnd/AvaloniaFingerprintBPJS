using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Drawing.Printing;
using Telerik.Reporting;
using Telerik.Reporting.Drawing;
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

            if (ps.IsValid)
            {
                foreach (var reportSource in rbs.ReportSources)
                {
                    if (reportSource is not InstanceReportSource instance) continue;
                    if (instance.ReportDocument is not Telerik.Reporting.Report report) continue;

                    var reportPage = report.PageSettings;
                    var paperWidth = reportPage.PaperSize.Width.ChangeType(UnitType.Inch).Value;
                    var paperHeight = reportPage.PaperSize.Height.ChangeType(UnitType.Inch).Value;
                    var contentWidth = report.Width.ChangeType(UnitType.Inch).Value;

                    var contentHeight = 0.0;
                    foreach (var reportItem in report.Items)
                    {
                        if (reportItem is Telerik.Reporting.ReportSection section)
                            contentHeight += section.Height.ChangeType(UnitType.Inch).Value;
                    }

                    // Laporan berhalaman banyak / lebih lebar dari kertas: biarkan margin desain apa adanya.
                    if (contentWidth > paperWidth || contentHeight > paperHeight) continue;

                    var marginX = toMargin((paperWidth - contentWidth) / 2.0);
                    var marginY = toMargin((paperHeight - contentHeight) / 2.0);

                    reportPage.Margins = new MarginsU(
                        Unit.Inch(marginX), // Left
                        Unit.Inch(marginX), // Right
                        Unit.Inch(marginY), // Top
                        Unit.Inch(marginY)  // Bottom
                    );
                }
            }

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

    private double toMargin(double available)
    {
        const double targetMarginInch = 20 / 100.0;
        var value = Math.Min(targetMarginInch, available);
        return value <= 0.0 ? 0.0 : Math.Floor(value * 1000.0) / 1000.0;
    }
}
