using MiniBank.Web.Components.Extensions;
using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;
using MiniBank.Web.Models.Notifications;
using System.Globalization;
using System.Text;

namespace MiniBank.Web.Services.Notifications;

public sealed class BaleMemberMenuService
{
    private static readonly CultureInfo MoneyCulture = CultureInfo.GetCultureInfo("fa-IR");

    private readonly BaleBotRepository _baleRepo;
    private readonly BaleApiClient _bale;
    private readonly MemberRepository _members;
    private readonly AccountsRepository _accounts;
    private readonly TransactionReportRepository _txReports;
    private readonly MemberFinancialSnapshotService _snapshots;
    private readonly BankCardService _bankCards;
    private readonly ILogger<BaleMemberMenuService> _logger;

    public BaleMemberMenuService(
        BaleBotRepository baleRepo,
        BaleApiClient bale,
        MemberRepository members,
        AccountsRepository accounts,
        TransactionReportRepository txReports,
        MemberFinancialSnapshotService snapshots,
        BankCardService bankCards,
        ILogger<BaleMemberMenuService> logger)
    {
        _baleRepo = baleRepo;
        _bale = bale;
        _members = members;
        _accounts = accounts;
        _txReports = txReports;
        _snapshots = snapshots;
        _bankCards = bankCards;
        _logger = logger;
    }

    public async Task SendMenuAsync(long chatId, string text, CancellationToken ct = default)
    {
        var result = await _bale.SendMenuAsync(chatId, text, ct);
        if (!result.Ok)
            await _bale.SendMessageAsync(chatId, text, ct);
    }

    public async Task<bool> TryHandleAsync(long chatId, string text, CancellationToken ct = default)
    {
        var action = MatchAction(text);
        if (action == null)
            return false;

        try
        {
            var subscribers = await _baleRepo.GetActiveByChatListAsync(chatId);
            if (subscribers.Count == 0)
            {
                await _bale.SendContactRequestAsync(chatId,
                    "برای مشاهده اطلاعات، ابتدا حساب خود را به ربات متصل کنید.\n" +
                    "شماره موبایل ثبت‌شده در صندوق را با دکمه «ارسال شماره موبایل» بفرستید یا از بخش «پروفایل» سایت، لینک اتصال اختصاصی را باز کنید.",
                    ct);
                return true;
            }

            IReadOnlyList<BaleSubscriber> active = subscribers.Count > 10
                ? subscribers.Take(10).ToList()
                : subscribers;

            var body = action switch
            {
                "household" => await BuildHouseholdAsync(active),
                "balance" => await BuildBalanceAsync(active),
                "loan" => await BuildLoanAsync(active),
                "cards" => await BuildCardsAsync(),
                _ => BuildHelp()
            };

            await SendMenuAsync(chatId, body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bale menu action failed: {Action}", action);
            await SendMenuAsync(chatId, "خطا در دریافت اطلاعات رخ داد. لطفاً چند لحظه بعد دوباره تلاش کنید.", ct);
        }

        return true;
    }

    private async Task<string> BuildHouseholdAsync(IReadOnlyList<BaleSubscriber> subscribers)
    {
        var blocks = new List<string>();
        var seenHouseholds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subscriber in subscribers)
        {
            var member = await _members.GetByIdAsync(subscriber.MemberId);
            if (member == null || !member.IsActive)
                continue;

            if (string.IsNullOrWhiteSpace(member.HouseholdCode))
            {
                blocks.Add($"👤 {DisplayName(subscriber, member)}\nخانواری برای این عضو ثبت نشده است.");
                continue;
            }

            if (!seenHouseholds.Add(member.HouseholdCode))
                continue;

            var rows = (await _txReports.GetHouseholdMemberBankingBalancesAsync(member.HouseholdCode)).ToList();
            if (rows.Count == 0)
            {
                blocks.Add($"👤 {DisplayName(subscriber, member)}\nعضوی برای خانوار {member.HouseholdCode} یافت نشد.");
                continue;
            }

            var householdName = string.IsNullOrWhiteSpace(rows[0].HouseholdName)
                ? "خانوار"
                : rows[0].HouseholdName;

            var ownerId = member.Household?.OwnerMemberId;
            var lines = new List<string>
            {
                $"🏡 خانوار: {householdName}",
                $"🏷️ کد خانوار: {member.HouseholdCode}",
                $"👥 تعداد اعضا: {rows.Count}"
            };

            foreach (var row in rows)
            {
                var name = string.IsNullOrWhiteSpace(row.MemberName) ? "بدون نام" : row.MemberName.Trim();
                var isOwner = ownerId.HasValue && row.MemberId == ownerId.Value;
                var accountCode = string.IsNullOrWhiteSpace(row.BankAccountNumber) ? "-" : row.BankAccountNumber;

                lines.Add("━━━━━━━━━━━━");
                lines.Add($"👤 {name}{(isOwner ? " (سرپرست)" : string.Empty)}");
                lines.Add($"🆔 کد عضویت: {row.MemberId}");
                lines.Add($"💳 کد حساب: {accountCode}");
            }

            blocks.Add(string.Join("\n", lines));
        }

        if (blocks.Count == 0)
            return "اطلاعاتی برای نمایش یافت نشد.";

        var separator = blocks.Count > 1 ? "\n\n━━━━━━━━━━━━\n\n" : "\n\n";
        return "🏠 اعضای خانوار\n\n" + string.Join(separator, blocks);
    }

    private async Task<string> BuildBalanceAsync(IReadOnlyList<BaleSubscriber> subscribers)
    {
        var blocks = new List<string>();
        var seenHouseholds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var singleSubscriber = subscribers.Count == 1;

        foreach (var subscriber in subscribers)
        {
            var member = await _members.GetByIdAsync(subscriber.MemberId);
            if (member == null || !member.IsActive)
                continue;

            var name = DisplayName(subscriber, member);
            var memberCode = await ResolveMemberCodeAsync(member);

            var lines = new List<string> { $"👤 {name}" };

            if (string.IsNullOrWhiteSpace(memberCode))
            {
                lines.Add("حساب مالی این عضو در سامانه ثبت نشده است.");
            }
            else
            {
                var snapshot = await _snapshots.GetSnapshotByMemberCodeAsync(memberCode);
                lines.Add("💵 موجودی (اصل + سود):");
                lines.Add(Money(snapshot.TotalNet));
            }

            if (!string.IsNullOrWhiteSpace(member.HouseholdCode) && seenHouseholds.Add(member.HouseholdCode))
            {
                var rows = (await _txReports.GetHouseholdMemberBankingBalancesAsync(member.HouseholdCode)).ToList();
                if (rows.Count > 0)
                {
                    var householdName = string.IsNullOrWhiteSpace(rows[0].HouseholdName)
                        ? "خانوار"
                        : rows[0].HouseholdName;

                    lines.Add("━━━━━━━━━━━━");
                    lines.Add($"👥 موجودی اعضای خانوار ({rows.Count} نفر)");
                    lines.Add($"🏡 {householdName} (کد: {member.HouseholdCode})");

                    foreach (var row in rows.Take(20))
                    {
                        var rowName = string.IsNullOrWhiteSpace(row.MemberName) ? "بدون نام" : row.MemberName.Trim();
                        var isCurrent = singleSubscriber && row.MemberId == member.MemberId;

                        lines.Add("━━━━━━━━━━━━");
                        lines.Add($"👤 {rowName}{(isCurrent ? " (شما)" : string.Empty)}");
                        lines.Add($"💵 {Money(row.TotalBalance)}");
                    }

                    if (rows.Count > 20)
                        lines.Add("...");
                }
            }

            blocks.Add(string.Join("\n", lines));
        }

        if (blocks.Count == 0)
            return "اطلاعاتی برای نمایش یافت نشد.";

        var separator = blocks.Count > 1 ? "\n\n━━━━━━━━━━━━\n\n" : "\n\n";
        return "💰 موجودی من\n\n" + string.Join(separator, blocks);
    }

    private async Task<string> BuildLoanAsync(IReadOnlyList<BaleSubscriber> subscribers)
    {
        var blocks = new List<string>();

        foreach (var subscriber in subscribers)
        {
            var member = await _members.GetByIdAsync(subscriber.MemberId);
            if (member == null || !member.IsActive)
                continue;

            var name = DisplayName(subscriber, member);
            var memberCode = await ResolveMemberCodeAsync(member);
            if (string.IsNullOrWhiteSpace(memberCode))
            {
                blocks.Add($"👤 {name}\nحساب مالی این عضو در سامانه ثبت نشده است.");
                continue;
            }

            var snapshot = await _snapshots.GetSnapshotByMemberCodeAsync(memberCode);
            var loan = snapshot.ActiveLoan;
            if (loan == null)
            {
                blocks.Add($"👤 {name}\nوام فعالی برای این عضو ثبت نشده است.");
                continue;
            }

            var paidPrincipal = Math.Max(0m, loan.Principal - loan.RemainingPrincipal);
            var paidInterest = Math.Max(0m, loan.TotalInterest - loan.RemainingInterest);

            var lines = new List<string>
            {
                $"👤 {name}",
                "━━━━━━━━━━━━",
                $"📌 اصل وام: {Money(loan.Principal)}",
                $"📈 سود کل: {Money(loan.TotalInterest)}",
                $"🧾 کل بازپرداخت: {Money(loan.TotalPayable)}"
            };

            if (loan.DisbursedAt != default)
                lines.Add($"🗓️ تاریخ پرداخت: {loan.DisbursedAt.ToPersianDate()}");

            lines.Add("━━━━━━━━━━━━");
            lines.Add($"✅ پرداخت‌شده: {Money(paidPrincipal + paidInterest)}");
            lines.Add($"▪️ اصل: {Money(paidPrincipal)}");
            lines.Add($"▪️ سود: {Money(paidInterest)}");
            lines.Add($"📅 اقساط: {loan.PaidInstallments} از {loan.TotalInstallments}");

            lines.Add("━━━━━━━━━━━━");
            if (loan.RemainingTotal > 0m)
            {
                lines.Add($"💳 مانده کل: {Money(loan.RemainingTotal)}");
                lines.Add($"▪️ مانده اصل: {Money(loan.RemainingPrincipal)}");
                lines.Add($"▪️ مانده سود: {Money(loan.RemainingInterest)}");
            }
            else
            {
                lines.Add("💳 مانده: تسویه‌شده ✅");
            }

            if (loan.EmiAmount > 0m)
                lines.Add($"💰 مبلغ هر قسط: {Money(loan.EmiAmount)}");

            if (loan.FirstUnpaidDueDate.HasValue)
            {
                var dueLabel = loan.CurrentInstallmentOverdueDays > 0 ? "سررسید معوق" : "سررسید بعدی";
                lines.Add("━━━━━━━━━━━━");
                lines.Add($"📆 {dueLabel}: {loan.FirstUnpaidDueDate.Value.ToPersianDate()}");

                if (loan.CurrentInstallmentNo.HasValue && loan.TotalInstallments > 0)
                    lines.Add($"🔢 قسط: {loan.CurrentInstallmentNo} از {loan.TotalInstallments}");

                if (loan.CurrentInstallmentTotal > 0m)
                    lines.Add($"💵 مبلغ قسط: {Money(loan.CurrentInstallmentTotal)}");
            }

            if (loan.CurrentInstallmentOverdueDays > 0)
            {
                lines.Add("━━━━━━━━━━━━");
                lines.Add($"⚠️ {loan.CurrentInstallmentOverdueDays} روز معوق");
                if (loan.CurrentInstallmentPenaltyAmount > 0m)
                    lines.Add($"🧮 جریمه تا امروز: {Money(loan.CurrentInstallmentPenaltyAmount)}");
            }

            blocks.Add(string.Join("\n", lines));
        }

        if (blocks.Count == 0)
            return "اطلاعاتی برای نمایش یافت نشد.";

        var separator = blocks.Count > 1 ? "\n\n━━━━━━━━━━━━\n\n" : "\n\n";
        return "🏦 وضعیت وام\n\n" + string.Join(separator, blocks);
    }

    private async Task<string> BuildCardsAsync()
    {
        var cards = (await _bankCards.GetAsync(isActive: true, top: 20))
            .Where(c => !string.IsNullOrWhiteSpace(c.CardNumber))
            .ToList();

        var header =
            "💳 کارت‌های صندوق\n\n" +
            "برای واریز وجه به صندوق امام علی (ع)، از شماره کارت، شماره حساب یا شبای زیر استفاده کنید.\n" +
            "پس از واریز، تصویر فیش واریزی را برای مدیر صندوق، حاج سید حسین حسینیان ارسال کنید.";

        if (cards.Count == 0)
            return header + "\n\nدر حال حاضر کارت بانکی فعالی برای صندوق ثبت نشده است.";

        var blocks = new List<string>();
        foreach (var card in cards)
        {
            var lines = new List<string> { $"🏦 {card.BankName}" };
            lines.Add($"💳 شماره کارت: {BankCardFormat.FormatCard(card.CardNumber)}");

            var accountNumber = BankCardFormat.DigitsOnly(card.AccountNumber);
            if (accountNumber.Length > 0)
                lines.Add($"🏷️ شماره حساب: {accountNumber}");

            var shaba = BankCardFormat.FormatShaba(card.Shaba);
            if (shaba.Length > 0)
                lines.Add($"🔗 شبا: {shaba}");

            blocks.Add(string.Join("\n", lines));
        }

        return header + "\n\n" + string.Join("\n\n━━━━━━━━━━━━\n\n", blocks);
    }

    private static string BuildHelp()
        => "ℹ️ راهنمای ربات صندوق\n\n" +
           "برای دریافت اطلاعات، یکی از گزینه‌های منوی پایین را انتخاب کنید:\n" +
           $"• {BaleMenuButtons.Household} — مشاهده اعضای خانوار\n" +
           $"• {BaleMenuButtons.Balance} — موجودی شما و اعضای خانوار به تفکیک\n" +
           $"• {BaleMenuButtons.Loan} — اطلاعات وام فعال\n" +
           $"• {BaleMenuButtons.Cards} — شماره کارت و حساب برای واریز وجه\n\n" +
           "دستورات:\n" +
           "/start — اتصال حساب با شماره موبایل یا لینک اختصاصی\n" +
           "/status — وضعیت اتصال\n" +
           "اتصال به بله اجباری است و از طریق ربات قابل قطع نیست.";

    private async Task<string?> ResolveMemberCodeAsync(Member member)
    {
        var account = await _accounts.GetSubsidiaryByMemberIdAsync(member.MemberId);
        if (account != null && !string.IsNullOrWhiteSpace(account.Code) &&
            account.Code.StartsWith("M-", StringComparison.OrdinalIgnoreCase))
        {
            return account.Code;
        }

        if (!string.IsNullOrWhiteSpace(member.HouseholdCode))
        {
            var rows = await _txReports.GetHouseholdMemberBankingBalancesAsync(member.HouseholdCode);
            var match = rows.FirstOrDefault(r => r.MemberId == member.MemberId);
            if (match != null && !string.IsNullOrWhiteSpace(match.BankAccountNumber))
                return match.BankAccountNumber;
        }

        return string.IsNullOrWhiteSpace(account?.Code) ? null : account!.Code;
    }

    private static string? MatchAction(string? text)
    {
        var normalized = NormalizeLabel(text);
        if (normalized.Length == 0)
            return null;

        return normalized switch
        {
            "اعضایخانوار" or "اعضا" or "خانوار" or "اعضایمن" => "household",
            "موجودیمن" or "موجودی" or "موجودیحساب" => "balance",
            "وضعیتوام" or "وام" or "واممن" or "اقساط" => "loan",
            "کارتهایصندوق" or "کارتهایبانکی" or "کارتصندوق" or "کارت" or "شمارهکارت" or "واریز" or "cards" => "cards",
            "راهنما" or "کمک" or "help" => "help",
            _ => null
        };
    }

    private static string NormalizeLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Trim().TrimStart('/'))
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    private static string DisplayName(BaleSubscriber subscriber, Member? member)
    {
        var name = member?.FullName;
        if (string.IsNullOrWhiteSpace(name))
            name = subscriber.MemberFullName;
        if (string.IsNullOrWhiteSpace(name))
            name = $"عضو {subscriber.MemberId}";

        return name!;
    }

    private static string Money(decimal value)
        => string.Format(MoneyCulture, "{0:n0}", value) + " ریال";
}
