using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Drawing.Printing;
using Telerik.Reporting;
using Telerik.Reporting.Drawing;
using Telerik.Reporting.Processing;

namespace AvaloniaFingerprint.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected void printProcess(Filters.Printout.Print print, bool isMargin = false)
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
                var printerPage = ps.DefaultPageSettings;
                var printerPaperWidth = printerPage.PaperSize.Width;   // 1/100 inci
                var printerPaperHeight = printerPage.PaperSize.Height; // 1/100 inci
                var printerPrintable = printerPage.PrintableArea;      // 1/100 inci, relatif ke kertas

                if (printerPrintable.Width > 0F && printerPrintable.Height > 0F)
                {
                    // Inset fisik printer (inci) pada tiap sisi.
                    var printerInsetX = Math.Max(printerPrintable.Left, printerPaperWidth - printerPrintable.Right) / 100.0;
                    var printerInsetY = Math.Max(printerPrintable.Top, printerPaperHeight - printerPrintable.Bottom) / 100.0;
                    printerInsetX = Math.Max(printerInsetX, printerPage.HardMarginX / 100.0);
                    printerInsetY = Math.Max(printerInsetY, printerPage.HardMarginY / 100.0);

                    // Target margin kecil & proporsional (5 mm), tidak kurang dari inset fisik printer.
                    var targetInset = Math.Min(Math.Max(5.0 / 25.4, Math.Max(printerInsetX, printerInsetY)), 10.0 / 25.4);
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
                            {
                                contentHeight += section.Height.ChangeType(UnitType.Inch).Value;
                            }
                        }

                        var currentInsetX = Math.Max(reportPage.Margins.Left.ChangeType(UnitType.Inch).Value, reportPage.Margins.Right.ChangeType(UnitType.Inch).Value);
                        var currentInsetY = Math.Max(reportPage.Margins.Top.ChangeType(UnitType.Inch).Value, reportPage.Margins.Bottom.ChangeType(UnitType.Inch).Value);

                        // Sisa ruang pada kertas; laporan berhalaman banyak dibiarkan apa adanya.
                        var maxInsetX = (paperWidth - contentWidth) / 2.0;
                        var maxInsetY = contentHeight <= 0.0 || contentHeight > paperHeight ? currentInsetY : (paperHeight - contentHeight) / 2.0;

                        var insetX = Math.Max(currentInsetX, Math.Min(targetInset, maxInsetX));
                        var insetY = Math.Max(currentInsetY, Math.Min(targetInset, maxInsetY));

                        reportPage.Margins = new MarginsU(Unit.Inch(insetX), Unit.Inch(insetX), Unit.Inch(insetY), Unit.Inch(insetY));
                    }
                }
            }

            var spc = new StandardPrintController();
            var rp = new ReportProcessor
            {
                PrintController = spc
            };

            if (isMargin)
            {
                Unit.DefaultType = UnitType.Mm;
                Unit u = new(1);
                var zero = new Unit(1);
                var width = new Unit(80);
                var height = new Unit(30);
                var size = new SizeU
                {
                    Width = width,
                    Height = height
                };

                foreach (var reportSource in rbs.ReportSources)
                {
                    if (reportSource is not InstanceReportSource instance) continue;
                    if (instance.ReportDocument is not Telerik.Reporting.Report report) continue;

                    report.PageSettings.Margins.Left = u;
                    report.PageSettings.Margins.Right = zero;
                    report.PageSettings.Margins.Top = zero;
                    report.PageSettings.Margins.Bottom = zero;
                }
            }

            rp.PrintReport(irs, ps);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error : {e.Message} - {e.InnerException?.Message}");
        }

        return;
    }
}
