using MiniBank.Web.Models;
using MiniBank.Web.Infrastructure.Repositories;
using System.Data;
using Dapper;

namespace MiniBank.Web.Services;

/// <summary>
/// سرویس ساده‌سازی فرم دریافت وجه با الگوهای پیش‌فرض
/// </summary>
public class ReceiptWizardService
{
    private readonly IDbConnection _db;
    private readonly PostingTemplateRepository _templateRepo;
    private readonly ILogger<ReceiptWizardService> _logger;

    public ReceiptWizardService(
        IDbConnection db, 
        PostingTemplateRepository templateRepo,
        ILogger<ReceiptWizardService> logger)
    {
        _db = db;
        _templateRepo = templateRepo;
        _logger = logger;
    }

    /// <summary>
    /// دریافت الگوهای پیش‌فرض برای انواع مختلف واریز
    /// </summary>
    public async Task<List<DefaultPostingTemplate>> GetDefaultTemplatesAsync()
    {
        try
        {
            var templates = await _templateRepo.GetAllAsync(onlyActive: true);
            
            var defaults = new List<DefaultPostingTemplate>
            {
                new DefaultPostingTemplate
                {
                    PurposeType = 1, // سپرده عضویت
                    PurposeTitle = "سپرده عضویت",
                    Description = "واریز سپرده اولیه یا تکمیلی عضو",
                    Icon = "fa-solid fa-piggy-bank",
                    Color = "#10b981", // green
                    IsRecommended = true,
                    PostingTemplateId = templates.FirstOrDefault(t => t.CategoryCode == 1)?.PostingTemplateId,
                    PostingTemplateName = templates.FirstOrDefault(t => t.CategoryCode == 1)?.TemplateName
                },
                new DefaultPostingTemplate
                {
                    PurposeType = 2, // قسط وام
                    PurposeTitle = "بازپرداخت قسط وام",
                    Description = "پرداخت قسط ماهانه یا فوق‌العاده وام",
                    Icon = "fa-solid fa-hand-holding-dollar",
                    Color = "#3b82f6", // blue
                    IsRecommended = true,
                    PostingTemplateId = templates.FirstOrDefault(t => t.CategoryCode == 2)?.PostingTemplateId,
                    PostingTemplateName = templates.FirstOrDefault(t => t.CategoryCode == 2)?.TemplateName
                },
                new DefaultPostingTemplate
                {
                    PurposeType = 3, // اعتبار سود
                    PurposeTitle = "اعتبار سود سپرده",
                    Description = "واریز سود تعلق گرفته به سپرده عضو",
                    Icon = "fa-solid fa-percentage",
                    Color = "#8b5cf6", // purple
                    IsRecommended = false,
                    PostingTemplateId = templates.FirstOrDefault(t => t.CategoryCode == 3)?.PostingTemplateId,
                    PostingTemplateName = templates.FirstOrDefault(t => t.CategoryCode == 3)?.TemplateName
                },
                new DefaultPostingTemplate
                {
                    PurposeType = 4, // جریمه
                    PurposeTitle = "جریمه دیرکرد",
                    Description = "دریافت جریمه تأخیر در پرداخت قسط",
                    Icon = "fa-solid fa-exclamation-triangle",
                    Color = "#ef4444", // red
                    IsRecommended = false,
                    PostingTemplateId = templates.FirstOrDefault(t => t.CategoryCode == 4)?.PostingTemplateId,
                    PostingTemplateName = templates.FirstOrDefault(t => t.CategoryCode == 4)?.TemplateName
                },
                new DefaultPostingTemplate
                {
                    PurposeType = 5, // کارمزد
                    PurposeTitle = "کارمزد خدمات",
                    Description = "دریافت کارمزد انجام خدمات مختلف",
                    Icon = "fa-solid fa-coins",
                    Color = "#f59e0b", // amber
                    IsRecommended = false,
                    PostingTemplateId = templates.FirstOrDefault(t => t.CategoryCode == 5)?.PostingTemplateId,
                    PostingTemplateName = templates.FirstOrDefault(t => t.CategoryCode == 5)?.TemplateName
                }
            };

            // فقط الگوهایی که PostingTemplate دارند را برگردان
            return defaults.Where(d => d.PostingTemplateId.HasValue).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در دریافت الگوهای پیش‌فرض");
            return new List<DefaultPostingTemplate>();
        }
    }

    /// <summary>
    /// ایجاد تخصیص خودکار بر اساس الگوی انتخاب شده
    /// </summary>
    public async Task<HouseholdReceiptAllocation> CreateAutoAllocationAsync(
        long memberId, 
        decimal amount, 
        DefaultPostingTemplate template)
    {
        try
        {
            var allocation = new HouseholdReceiptAllocation
            {
                MemberId = memberId,
                Amount = amount,
                PurposeType = template.PurposeType,
                PostingTemplateId = template.PostingTemplateId,
                AmountPrincipal = template.PurposeType == 1 ? amount : null, // برای سپرده، کل مبلغ اصل است
                AmountInterest = template.PurposeType == 3 ? amount : null,   // برای سود، کل مبلغ سود است
                AmountPenalty = template.PurposeType == 4 ? amount : null,    // برای جریمه، کل مبلغ جریمه است
                Notes = $"تخصیص خودکار - {template.PurposeTitle}"
            };

            return allocation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در ایجاد تخصیص خودکار برای عضو {MemberId}", memberId);
            throw;
        }
    }

    /// <summary>
    /// پیشنهاد تقسیم هوشمند مبلغ بین اعضای خانوار
    /// </summary>
    public async Task<List<SmartAllocationSuggestion>> GetSmartAllocationSuggestionsAsync(
        long householdId, 
        decimal totalAmount,
        byte preferredPurposeType = 1)
    {
        try
        {
            var suggestions = new List<SmartAllocationSuggestion>();

            // دریافت اعضای فعال خانوار
            var members = await _db.QueryAsync<dynamic>(@"
                SELECT m.MemberId, m.FirstName, m.LastName, m.NationalCode
                FROM Members m
                WHERE m.HouseholdId = @householdId AND m.IsActive = 1
                ORDER BY m.FirstName, m.LastName",
                new { householdId });

            var memberList = members.ToList();
            if (!memberList.Any())
            {
                return suggestions;
            }

            // تقسیم مساوی بین اعضا
            var equalShare = totalAmount / memberList.Count;
            
            foreach (var member in memberList)
            {
                suggestions.Add(new SmartAllocationSuggestion
                {
                    MemberId = (long)member.MemberId,
                    MemberName = $"{member.FirstName} {member.LastName}",
                    SuggestedAmount = equalShare,
                    PurposeType = preferredPurposeType,
                    Reason = memberList.Count == 1 ? "تنها عضو خانوار" : "تقسیم مساوی بین اعضا",
                    Priority = 1,
                    HasDepositAccount = false,
                    CurrentBalance = 0m
                });
            }

            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در تولید پیشنهادات تخصیص هوشمند برای خانوار {HouseholdId}", householdId);
            return new List<SmartAllocationSuggestion>();
        }
    }

    /// <summary>
    /// اعتبارسنجی سریع تخصیص‌ها
    /// </summary>
    public ValidationResult ValidateAllocations(List<HouseholdReceiptAllocation> allocations, decimal totalAmount)
    {
        var result = new ValidationResult { IsValid = true, Messages = new List<string>() };

        if (!allocations.Any())
        {
            result.IsValid = false;
            result.Messages.Add("حداقل یک تخصیص باید تعریف شود");
            return result;
        }

        var totalAllocated = allocations.Sum(a => a.Amount);
        if (Math.Abs(totalAllocated - totalAmount) > 0.01m)
        {
            result.IsValid = false;
            result.Messages.Add($"مجموع تخصیص‌ها ({totalAllocated:N0} ریال) با مبلغ کل رسید ({totalAmount:N0} ریال) برابر نیست");
        }

        // بررسی تخصیص‌های تکراری برای یک عضو
        var duplicateMembers = allocations
            .GroupBy(a => new { a.MemberId, a.PurposeType })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var duplicate in duplicateMembers)
        {
            result.Messages.Add($"تخصیص تکراری برای عضو {duplicate.MemberId} با هدف {duplicate.PurposeType}");
        }

        // بررسی مبالغ منفی
        var negativeAmounts = allocations.Where(a => a.Amount <= 0);
        if (negativeAmounts.Any())
        {
            result.IsValid = false;
            result.Messages.Add("مبلغ تخصیص نمی‌تواند صفر یا منفی باشد");
        }

        return result;
    }
}

/// <summary>
/// الگوی پیش‌فرض برای انواع واریز
/// </summary>
public class DefaultPostingTemplate
{
    public byte PurposeType { get; set; }
    public string PurposeTitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsRecommended { get; set; }
    public int? PostingTemplateId { get; set; }
    public string? PostingTemplateName { get; set; }
}

/// <summary>
/// پیشنهاد تخصیص هوشمند
/// </summary>
public class SmartAllocationSuggestion
{
    public long MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public decimal SuggestedAmount { get; set; }
    public byte PurposeType { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool HasDepositAccount { get; set; }
    public decimal CurrentBalance { get; set; }
}

/// <summary>
/// نتیجه اعتبارسنجی
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Messages { get; set; } = new();
}
