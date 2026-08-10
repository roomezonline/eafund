using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniBank.Web.Components.Extensions;
using MiniBank.Web.Infrastructure.Repositories;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace MiniBank.Web.Reports
{
    public class LoansInstallmentsReport : IDocument
    {
        private readonly IReadOnlyList<LoanReportsRepository.LoanSummaryReportRow> _rows;
        private readonly string _rangeTitle;
        private readonly DateTime _generatedAt;
        private readonly IWebHostEnvironment _env;
        private static bool _fontsRegistered;

        public LoansInstallmentsReport(
            IReadOnlyList<LoanReportsRepository.LoanSummaryReportRow> rows,
            string rangeTitle,
            IWebHostEnvironment env)
        {
            _rows = rows ?? Array.Empty<LoanReportsRepository.LoanSummaryReportRow>();
            _rangeTitle = rangeTitle;
            _generatedAt = DateTime.Now;
            _env = env;
        }

        public DocumentMetadata GetMetadata() => new DocumentMetadata
        {
            Title = "گزارش وام‌ها",
            Author = "MiniBank",
            Subject = "Loans & Installments",
            Keywords = "Loans, Installments, Report",
        };

        public void Compose(IDocumentContainer container)
        {
            EnsureFontsRegistered();
            QuestPDF.Settings.DocumentLayoutExceptionThreshold = 50;

            container.Page(page =>
            {
                page.Margin(18);
                page.Size(PageSizes.A4.Landscape());
                page.DefaultTextStyle(ts => ts.FontFamily("Vazirmatn").FontSize(9));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("صفحه ");
                    x.CurrentPageNumber();
                    x.Span(" از ");
                    x.TotalPages();
                });
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().AlignCenter().Text("صندوق همیاری امام علی (ع)").FontSize(12).SemiBold();
                col.Item().AlignCenter().Text("گزارش خلاصه وام‌ها").FontSize(11).SemiBold();
                col.Item().AlignCenter().DefaultTextStyle(ts => ts.FontSize(10)).Text(t =>
                {
                    var parts = (_rangeTitle ?? string.Empty).Split('|');
                    var left = parts.Length > 0 ? parts[0].Trim() : (_rangeTitle ?? string.Empty);
                    var right = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                    t.Span(left);
                    if (!string.IsNullOrWhiteSpace(right))
                    {
                        t.Span(" | ");
                        t.Span(right).FontColor("#b91c1c").SemiBold();
                    }
                });
                col.Item().AlignCenter().Text($"تاریخ تولید: {_generatedAt:yyyy/MM/dd HH:mm}").FontSize(9).FontColor("#6b7280");
                col.Item().PaddingTop(8).LineHorizontal(1).LineColor("#111827");
            });
        }

        private void ComposeContent(IContainer container)
        {
            var headerBg = "#ffeb3b";
            var headerFg = "#b91c1c";

            container.PaddingTop(8).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.0f); // تاریخ معوق
                    columns.ConstantColumn(85);  // مبلغ معوق
                    columns.ConstantColumn(80);  // سود قسط جاری
                    columns.ConstantColumn(55);  // تعداد معوق
                    columns.ConstantColumn(75);  // پرداخت شده
                    columns.ConstantColumn(55);  // وصول شده
                    columns.ConstantColumn(50);  // اقساط
                    columns.ConstantColumn(95);  // مانده
                    columns.RelativeColumn(1.05f); // مبلغ وام
                    columns.RelativeColumn(1.55f); // وام گیرنده
                    columns.ConstantColumn(45);  // شماره وام
                    columns.ConstantColumn(28);  // ردیف
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeader).Text("تاریخ معوق");
                    h.Cell().Element(CellHeader).Text("قسط معوق");
                    h.Cell().Element(CellHeader).Text("سود قسط جاری");
                    h.Cell().Element(CellHeader).Text("تعداد معوق");
                    h.Cell().Element(CellHeader).Text("پرداخت شده");
                    h.Cell().Element(CellHeader).Text("وصول شده");
                    h.Cell().Element(CellHeader).Text("اقساط");
                    h.Cell().Element(CellHeader).Text("مانده");
                    h.Cell().Element(CellHeader).Text("مبلغ وام (اصل + سود)");
                    h.Cell().Element(CellHeader).Text("وام گیرنده");
                    h.Cell().Element(CellHeader).Text("شماره وام");
                    h.Cell().Element(CellHeader).Text("ردیف");

                    IContainer CellHeader(IContainer c)
                        => c.Background(headerBg).Border(1).BorderColor("#111827")
                            .PaddingVertical(3).PaddingHorizontal(4)
                            .AlignMiddle().AlignCenter()
                            .DefaultTextStyle(ts => ts.FontColor(headerFg).SemiBold());
                });

                var idx = 0;
                foreach (var r in _rows)
                {
                    idx++;

                    var rowBg = Colors.White;
                    if (r.Status == 5) rowBg = "#cfe9c8";
                    else if (r.OverdueInstallments > 0) rowBg = "#fee2e2";
                    else if (r.PaidInstallments > 0) rowBg = "#b9dbf5";

                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text(FormatOverdueDates(r.OverdueDueDatesText));
                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text(r.OverdueAmountNoPenalty.ToString("N0"));
                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text($"{r.CurrentInstallmentInterest:N0}\nجمع: {r.CurrentAndOverdueInstallmentsInterest:N0}");
                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text(r.OverdueInstallments.ToString());
                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text(r.LastPaidInstallmentDueDate.HasValue ? r.LastPaidInstallmentDueDate.Value.ToPersianDate() : "-");
                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text(r.PaidInstallments.ToString());
                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text(r.TotalInstallments.ToString());
                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text(r.RemainingTotal.ToString("N0"));
                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text(r.TotalLoanAmount.ToString("N0"));
                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text(r.MemberFullName).ClampLines(1);
                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text(r.LoanId.ToString());
                    table.Cell().Element(CellRow(rowBg)).AlignCenter().Text(idx.ToString());
                }

                static Func<IContainer, IContainer> CellRow(string bg)
                    => c => c.Background(bg).Border(1).BorderColor("#111827")
                        .PaddingVertical(2).PaddingHorizontal(4)
                        .DefaultTextStyle(ts => ts.FontSize(8));
            });
        }

        private static string FormatOverdueDates(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "-";

            var lines = text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (lines.Count == 0)
                return "-";

            var converted = new List<string>(lines.Count);
            foreach (var l in lines)
            {
                if (TryParseOverdueDate(l, out var dt))
                    converted.Add(dt.ToPersianDate());
                else
                    converted.Add(l);
            }

            return string.Join("\n", converted);
        }

        private static bool TryParseOverdueDate(string input, out DateTime dt)
        {
            dt = default;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var s = input.Trim();
            var formats = new[] { "yyyy-MM-dd", "yyyy/MM/dd", "yyyy/M/d", "yyyy/M/dd", "yyyy/MM/d" };
            return DateTime.TryParseExact(
                s,
                formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out dt);
        }

        private static string LoanStatusText(byte s)
            => s == 1 ? "پیش‌نویس" : (s == 2 ? "تایید" : (s == 3 ? "پرداخت" : (s == 4 ? "جاری" : (s == 5 ? "تسویه" : "لغو"))));

        private void EnsureFontsRegistered()
        {
            if (_fontsRegistered) return;

            try
            {
                var webRoot = _env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
                var fontsDir = Path.Combine(webRoot, "fonts");

                var vazirRegular = Path.Combine(fontsDir, "Vazirmatn-Regular.ttf");
                var vazirBold = Path.Combine(fontsDir, "Vazirmatn-Bold.ttf");

                if (File.Exists(vazirRegular))
                    QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(vazirRegular));
                if (File.Exists(vazirBold))
                    QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(vazirBold));

                var irsansRegular = Path.Combine(fontsDir, "IRANSansX-Regular.ttf");
                var irsansBold = Path.Combine(fontsDir, "IRANSansX-Bold.ttf");

                if (File.Exists(irsansRegular))
                    QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(irsansRegular));
                if (File.Exists(irsansBold))
                    QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(irsansBold));

                _fontsRegistered = true;
            }
            catch
            {
                _fontsRegistered = true;
            }
        }
    }
}
