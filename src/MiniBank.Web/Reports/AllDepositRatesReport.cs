using System;
using System.Collections.Generic;
using System.Linq;
using MiniBank.Web.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MiniBank.Web.Reports
{
    public class AllDepositRatesReport : IDocument
    {
        private readonly IReadOnlyList<DepositType> _types;
        private readonly Dictionary<long, List<DepositInterestRate>> _ratesDict;
        private readonly DateTime _generatedAt;

        public AllDepositRatesReport(IEnumerable<DepositType> types, Dictionary<long, List<DepositInterestRate>> ratesDict)
        {
            _types = types.OrderBy(t => t.Title).ToList();
            _ratesDict = ratesDict;
            _generatedAt = DateTime.Now;
        }

        public DocumentMetadata GetMetadata() => new DocumentMetadata
        {
            Title = "گزارش جامع نرخ‌های سپرده",
            Author = "MiniBank",
            Subject = "All Deposit Interest Rates",
            Keywords = "Deposit, Interest, Rates, Report, All",
        };

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(ts => ts.FontFamily("Vazirmatn").FontSize(10));

                page.Header().Row(h =>
                {
                    // Left: date and page numbers
                    h.RelativeItem().AlignLeft().Column(c =>
                    {
                        c.Item().Text($"تاریخ تولید: {_generatedAt:yyyy/MM/dd HH:mm}").FontSize(10);
                        c.Item().Text(x =>
                        {
                            x.Span("صفحه ");
                            x.CurrentPageNumber();
                            x.Span(" از ");
                            x.TotalPages();
                        });
                    });

                    // Center: fund name and logo/badge
                    h.RelativeItem().AlignCenter().Row(r =>
                    {
                        r.Spacing(8);
                        r.AutoItem().Width(26).Height(26).Background("#0ea5e9").Border(1).BorderColor("#7dd3fc")
                            .AlignMiddle().AlignCenter().Text(t => t.Span("٪").FontColor("#ffffff").Bold());
                        r.AutoItem().AlignMiddle().Column(cc =>
                        {
                            cc.Item().AlignCenter().Text("صندوق امام علی").FontSize(12).SemiBold();
                            cc.Item().AlignCenter().Text("گزارش جامع نرخ‌های سپرده").FontSize(11);
                        });
                    });

                    // Right: spacer
                    h.RelativeItem();
                });

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

        private void ComposeContent(IContainer container)
        {
            var today = DateTime.Today;
            container.Column(col =>
            {
                // Legend
                col.Item().Row(r =>
                {
                    r.Spacing(8);
                    r.AutoItem().PaddingVertical(4).PaddingHorizontal(8).Background("#dcfce7").Border(1).BorderColor("#86efac").Text("فعلی").FontColor("#16a34a").Bold();
                    r.AutoItem().PaddingVertical(4).PaddingHorizontal(8).Background("#dbeafe").Border(1).BorderColor("#93c5fd").Text("برنامه‌ریزی‌شده").FontColor("#2563eb").Bold();
                    r.AutoItem().PaddingVertical(4).PaddingHorizontal(8).Background("#e5e7eb").Border(1).BorderColor("#9ca3af").Text("منقضی").FontColor("#374151").Bold();
                });

                foreach (var type in _types)
                {
                    col.Item().PaddingTop(12).BorderTop(1).BorderColor("#e5e7eb");

                    // Title (account/deposit type name) — right aligned (RTL)
                    col.Item().AlignRight().Text(type.Title).FontSize(12).SemiBold();

                    // Compact row: break rate and status (push to right)
                    col.Item().PaddingTop(4).Row(r =>
                    {
                        r.Spacing(12);
                        // filler to push following items to the right side in the row
                        r.RelativeItem();
                        if (type.AccrualBasis.HasValue)
                            r.AutoItem().Text($"پایه محاسبه: {type.AccrualBasis!.Value.ToFa()}");
                        if (type.PostingFrequency.HasValue)
                            r.AutoItem().Text($"تناوب واریز: {type.PostingFrequency!.Value.ToFa()}");
                        if (type.Compounding.HasValue)
                            r.AutoItem().Text($"مرکب: {type.Compounding!.Value.ToFa()}");
                        if (type.PostingTarget.HasValue)
                            r.AutoItem().Text($"مقصد واریز: {type.PostingTarget!.Value.ToFa()}");
                        if (type.DayCountConvention.HasValue)
                            r.AutoItem().Text($"روزشمار: {type.DayCountConvention!.Value.ToFa()}");
                        if (type.RoundingRule.HasValue)
                            r.AutoItem().Text($"گرد کردن: {type.RoundingRule!.Value.ToFa()}");
                        if (type.MinBalanceForInterest.HasValue)
                            r.AutoItem().Text($"حداقل مانده سود: {type.MinBalanceForInterest:0,0}");
                        r.AutoItem().Text($"نرخ شکست: {(type.BreakRate.HasValue ? type.BreakRate.Value.ToString("0.##") + "%" : "—")}");
                        r.AutoItem().Text($"وضعیت: {(type.IsActive ? "فعال" : "غیرفعال")}");
                    });

                    var list = _ratesDict.TryGetValue(type.DepositTypeId, out var tmp) ? tmp : new List<DepositInterestRate>();
                    var rates = list
                        .OrderByDescending(r => r.EffectiveFrom)
                        .ThenByDescending(r => r.EffectiveTo ?? DateTime.MaxValue)
                        .ToList();

                    // Rates table header
                    col.Item().BorderBottom(1).BorderColor("#e5e7eb").PaddingBottom(6).Row(r =>
                    {
                        r.RelativeItem().AlignRight().Text("نرخ سود").Bold();
                        r.RelativeItem().AlignRight().Text("تاریخ شروع").Bold();
                        r.RelativeItem().AlignRight().Text("تاریخ پایان").Bold();
                        r.RelativeItem().AlignRight().Text("وضعیت").Bold();
                        r.RelativeItem().AlignRight().Text("توضیحات").Bold();
                    });

                    foreach (var rate in rates)
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
                }
            });
        }
    }
}
