using System;
using System.Collections.Generic;
using System.Linq;
using MiniBank.Web.Models;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Helpers;

namespace MiniBank.Web.Reports
{
    // Simple PDF document to render deposit type and its interest rates
    public class DepositRatesReport : IDocument
    {
        private readonly DepositType _depositType;
        private readonly IReadOnlyList<DepositInterestRate> _rates;
        private readonly DateTime _generatedAt;

        public DepositRatesReport(DepositType depositType, IEnumerable<DepositInterestRate> rates)
        {
            _depositType = depositType;
            _rates = rates.OrderByDescending(r => r.EffectiveFrom)
                          .ThenByDescending(r => r.EffectiveTo ?? DateTime.MaxValue)
                          .ToList();
            _generatedAt = DateTime.Now;
        }

        public DocumentMetadata GetMetadata() => new DocumentMetadata
        {
            Title = $"گزارش نرخ‌های سپرده - {_depositType.Title}",
            Author = "MiniBank",
            Subject = "Deposit Interest Rates",
            Keywords = "Deposit, Interest, Rates, Report",
        };

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);

                // Use Persian font (ensure it's registered via ReportService)
                page.DefaultTextStyle(ts => ts.FontFamily("Vazirmatn").FontSize(10));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().Column(footer =>
                {
                    footer.Item().AlignCenter().Text("www.eafund.ir | 09166912508").FontSize(10);
                    footer.Item().AlignCenter().Text(x =>
                    {
                        x.Span("صفحه ");
                        x.CurrentPageNumber();
                        x.Span(" از ");
                        x.TotalPages();
                    });
                });
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.Column(col =>
            {
                // Title row with simple badge icon and centered title
                col.Item().AlignCenter().Row(r =>
                {
                    r.Spacing(8);
                    r.AutoItem().Width(24).Height(24).Background("#0ea5e9").Border(1).BorderColor("#7dd3fc").Padding(0)
                        .AlignMiddle().AlignCenter().Text(t => t.Span("٪").FontColor("#ffffff").Bold());
                    r.AutoItem().AlignMiddle().Text("گزارش نرخ‌های سپرده").FontSize(16).SemiBold();
                });

                col.Item().AlignCenter().Text("صندوق امام علی").FontSize(12);
                col.Item().AlignCenter().Text($"عنوان سپرده: {_depositType.Title}").FontSize(12);
                col.Item().AlignCenter().Text($"تاریخ گزارش: {_generatedAt:yyyy/MM/dd HH:mm}").FontSize(10).FontColor("#6b7280");
            });
        }

        private void ComposeContent(IContainer container)
        {
            var today = DateTime.Today;

            container.Column(col =>
            {
                // Deposit type compact info
                col.Item().PaddingBottom(8).Column(info =>
                {
                    info.Item().Border(1).BorderColor("#e5e7eb").Padding(8).Row(r =>
                    {
                        r.Spacing(8);
                        r.RelativeItem().Text($"عنوان: {_depositType.Title}");
                        if (!string.IsNullOrWhiteSpace(_depositType.Category))
                            r.RelativeItem().Text($"دسته‌بندی: {_depositType.Category}");
                        if (_depositType.AccrualBasis.HasValue)
                            r.RelativeItem().Text($"پایه محاسبه: {_depositType.AccrualBasis}");
                        if (_depositType.PostingFrequency.HasValue)
                            r.RelativeItem().Text($"تناوب واریز: {_depositType.PostingFrequency}");
                        if (_depositType.Compounding.HasValue)
                            r.RelativeItem().Text($"مرکب: {_depositType.Compounding}");
                        if (_depositType.PostingTarget.HasValue)
                            r.RelativeItem().Text($"مقصد واریز: {_depositType.PostingTarget}");
                        if (_depositType.DayCountConvention.HasValue)
                            r.RelativeItem().Text($"روزشمار: {_depositType.DayCountConvention}");
                        if (_depositType.RoundingRule.HasValue)
                            r.RelativeItem().Text($"گرد کردن: {_depositType.RoundingRule}");
                        if (_depositType.MinBalanceForInterest.HasValue)
                            r.RelativeItem().Text($"حداقل مانده سود: {_depositType.MinBalanceForInterest:0,0}");
                        if (_depositType.BreakRate.HasValue)
                            r.RelativeItem().Text($"نرخ شکست: {_depositType.BreakRate:0.##}%");
                        r.RelativeItem().Text($"وضعیت: {(_depositType.IsActive ? "فعال" : "غیرفعال")}");
                    });
                });

                // Legend
                col.Item().Row(r =>
                {
                    r.Spacing(8);
                    r.AutoItem().PaddingVertical(4).PaddingHorizontal(8).Background("#dcfce7").Border(1).BorderColor("#86efac").Text("فعلی").FontColor("#16a34a").Bold();
                    r.AutoItem().PaddingVertical(4).PaddingHorizontal(8).Background("#dbeafe").Border(1).BorderColor("#93c5fd").Text("برنامه‌ریزی‌شده").FontColor("#2563eb").Bold();
                    r.AutoItem().PaddingVertical(4).PaddingHorizontal(8).Background("#e5e7eb").Border(1).BorderColor("#9ca3af").Text("منقضی").FontColor("#374151").Bold();
                });

                col.Item().PaddingTop(10);

                // Table header
                col.Item().BorderBottom(1).BorderColor("#e5e7eb").PaddingBottom(6).Row(r =>
                {
                    r.RelativeItem().AlignRight().Text("نرخ سود").Bold();
                    r.RelativeItem().AlignRight().Text("تاریخ شروع").Bold();
                    r.RelativeItem().AlignRight().Text("تاریخ پایان").Bold();
                    r.RelativeItem().AlignRight().Text("وضعیت").Bold();
                    r.RelativeItem().AlignRight().Text("توضیحات").Bold();
                });

                // Rows
                foreach (var rate in _rates)
                {
                    var isCurrent = rate.EffectiveFrom.Date <= today && (!rate.EffectiveTo.HasValue || rate.EffectiveTo.Value.Date >= today);
                    var isPlanned = rate.EffectiveFrom.Date > today;
                    var statusText = isCurrent ? "فعلی" : (isPlanned ? "برنامه‌ریزی‌شده" : "منقضی");
                    var statusBg = isCurrent ? "#dcfce7" : (isPlanned ? "#dbeafe" : "#e5e7eb");
                    var statusFg = isCurrent ? "#16a34a" : (isPlanned ? "#2563eb" : "#374151");

                    col.Item().PaddingVertical(6).Row(r =>
                    {
                        r.RelativeItem().AlignRight().Text($"{rate.InterestRate:0.##}%");
                        r.RelativeItem().AlignRight().Text(rate.EffectiveFrom.ToString("yyyy/MM/dd"));
                        r.RelativeItem().AlignRight().Text(rate.EffectiveTo.HasValue ? rate.EffectiveTo.Value.ToString("yyyy/MM/dd") : "—");
                        r.RelativeItem().AlignRight().PaddingVertical(2).PaddingHorizontal(6)
                            .Background(statusBg).Text(statusText).FontColor(statusFg).Bold();
                        r.RelativeItem().AlignRight().Text(rate.Notes ?? "");
                    });

                    col.Item().LineHorizontal(0.5f).LineColor("#f3f4f6");
                }
            });
        }
    }
}
