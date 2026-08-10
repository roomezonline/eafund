using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using MiniBank.Web.Models;
using MiniBank.Web.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;

namespace MiniBank.Web.Services;

public class AccountingPostingService
{
    private readonly IDbConnection _db;
    private readonly AuditRepository _audit;
    private readonly SafeTransactionService _safeTransaction;
    private readonly ILogger<AccountingPostingService> _logger;
    private readonly FiscalYearRepository _fiscalYearRepo;

    public AccountingPostingService(IDbConnection db, AuditRepository audit, SafeTransactionService safeTransaction, ILogger<AccountingPostingService> logger, FiscalYearRepository fiscalYearRepo)
    {
        _db = db;
        _audit = audit;
        _safeTransaction = safeTransaction;
        _logger = logger;
        _fiscalYearRepo = fiscalYearRepo;
    }

    public async Task PostTransactionAsync(long transactionId)
    {
        string? processId = null;

        try
        {
            // شروع فرآیند امن
            processId = await _safeTransaction.StartSafeProcessAsync("JournalPosting", transactionId);
            await _safeTransaction.LogProcessStepAsync(processId, "LoadTransaction", "Started", "بارگیری اطلاعات تراکنش");

            // Load transaction and template
            var tx = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT t.*, t.PostingTemplateId, t.SubsidiaryAccountId FROM dbo.Transactions t WHERE t.TransactionId = @transactionId", new { transactionId });

            if (tx == null)
            {
                await _safeTransaction.LogProcessErrorAsync(processId, new InvalidOperationException("تراکنش مورد نظر یافت نشد"), transactionId);
                throw new UserFriendlyException("تراکنش مورد نظر یافت نشد. لطفاً صفحه را بازخوانی کنید.");
            }

            if (tx.PostingTemplateId == null)
            {
                await _safeTransaction.LogProcessStepAsync(processId, "PostingSkipped", "Completed", "الگوی حسابداری تعریف نشده - ثبت نادیده گرفته شد");
                return;
            }

            await _safeTransaction.LogProcessStepAsync(processId, "TemplateLoaded", "Completed", $"الگوی حسابداری بارگیری شد: {tx.PostingTemplateId}");

            // Load template lines
            var lines = (await _db.QueryAsync<dynamic>(@"
                SELECT [LineNo], AccountId, OperationalAccountId, Side, AmountSource, AllowMemberDimension, FixedAmount
                FROM dbo.PostingTemplateLines WHERE PostingTemplateId = @tpl ORDER BY [LineNo] ASC", new { tpl = (int)tx.PostingTemplateId })).ToList();

            if (lines.Count == 0)
            {
                await _safeTransaction.LogProcessStepAsync(processId, "PostingSkipped", "Completed", "خطوط الگوی حسابداری یافت نشد");
                return;
            }

            await _safeTransaction.LogProcessStepAsync(processId, "LinesLoaded", "Completed", $"{lines.Count} خط الگوی حسابداری یافت شد");

            // Compute amounts
            decimal GetAmount(byte source, decimal? fixedAmount = null)
            {
                return source switch
                {
                    1 => (decimal)tx.AmountTotal,
                    2 => (decimal?)tx.AmountPrincipal ?? 0m,
                    3 => (decimal?)tx.AmountInterest ?? 0m,
                    4 => (decimal?)tx.AmountFee ?? 0m,
                    5 => (decimal?)tx.AmountTax ?? 0m,
                    6 => (decimal?)tx.AmountPenalty ?? 0m,
                    7 => (decimal?)tx.AmountCommission ?? 0m,
                    8 => fixedAmount ?? 0m,
                    _ => 0m
                };
            }

            // Check fiscal year
            var openYear = await _fiscalYearRepo.GetOpenYearForDateAsync(DateTime.UtcNow);
            if (openYear == null)
            {
                await _safeTransaction.LogProcessStepAsync(processId, "FiscalYearCheck", "Failed", "سال مالی باز برای ثبت سند وجود ندارد");
                throw new UserFriendlyException("هیچ سال مالی بازی برای ثبت سند وجود ندارد. لطفاً با مدیر سیستم تماس بگیرید.");
            }

            // Create journal
            await _safeTransaction.LogProcessStepAsync(processId, "CreateJournal", "Started", "ایجاد سند حسابداری");

            long journalId;
            await _db.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.JournalLines");
            await _db.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.Journals");
            try
            {
                journalId = await _db.QuerySingleAsync<long>(@"
                    INSERT INTO dbo.Journals (TransactionId, ProcessId, OccurredAt, CreatedAt, CreatedBy) 
                    VALUES (@transactionId, @processId, GETUTCDATE(), GETUTCDATE(), 'system');
                    SELECT CAST(SCOPE_IDENTITY() AS BIGINT)", new { transactionId, processId });

                // Assign DocumentNumber
                await _db.ExecuteAsync(
                    @"IF NOT EXISTS (SELECT 1 FROM dbo.SerialCounters WHERE FiscalYearId = @id)
                      INSERT INTO dbo.SerialCounters (FiscalYearId, LastDocumentNumber)
                      SELECT @id, ISNULL(MAX(DocumentNumber), 0) FROM dbo.Journals WHERE FiscalYearId = @id",
                    new { id = openYear.FiscalYearId });
                var docNum = await _db.QuerySingleAsync<int>(@"
                    UPDATE dbo.SerialCounters
                    SET LastDocumentNumber = LastDocumentNumber + 1
                    OUTPUT INSERTED.LastDocumentNumber
                    WHERE FiscalYearId = @fyId", new { fyId = openYear.FiscalYearId });
                await _db.ExecuteAsync(
                    "UPDATE dbo.Journals SET DocumentNumber = @docNum WHERE JournalId = @journalId",
                    new { docNum, journalId });
            }
            finally
            {
                await _db.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines");
                await _db.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals");
            }

            // Build a mapping for operational bank accounts -> GL AccountId (from BankAccounts.GlAccountId)
            var opIds = lines.Where(l => l.OperationalAccountId != null).Select(l => (long)l.OperationalAccountId).Distinct().ToArray();
            var opToGl = new Dictionary<long, long>();

            if (opIds.Length > 0)
            {
                var rows = await _db.QueryAsync<(long BankAccountId, long? GlAccountId)>(
                    "SELECT AccountId as BankAccountId, GlAccountId FROM dbo.BankAccounts WHERE AccountId IN @ids",
                    new { ids = opIds });

                foreach (var r in rows)
                {
                    if (r.GlAccountId.HasValue)
                        opToGl[r.BankAccountId] = r.GlAccountId.Value;
                }

                await _safeTransaction.LogProcessStepAsync(processId, "OperationalToGLMapping", "Completed", $"نقشه عملیاتی به GL: {string.Join(", ", opToGl.Select(kv => kv.Key + "->" + kv.Value))}");
            }

            // Build journal lines in memory first
            await _safeTransaction.LogProcessStepAsync(processId, "BuildJournalLines", "Started", "محاسبه خطوط سند حسابداری");

            var journalLines = new List<(int LineNo, long AccountId, long? MemberId, decimal Debit, decimal Credit)>();
            int lineNo = 1;

            foreach (var l in lines)
            {
                decimal amt = GetAmount((byte)l.AmountSource, (decimal?)l.FixedAmount);
                await _safeTransaction.LogProcessStepAsync(processId, "LineAmountCalculation", "Completed", $"خط {lineNo}: مبلغ={amt}, سمت={l.Side}, کد حساب={l.AccountId}, کد عملیاتی={l.OperationalAccountId}");

                if (amt == 0) { lineNo++; continue; }

                decimal debit = ((int)l.Side) == 1 ? amt : 0m;
                decimal credit = ((int)l.Side) == 2 ? amt : 0m;

                long? memberId = null;
                if ((bool)l.AllowMemberDimension && tx.MemberId != null)
                    memberId = (long)tx.MemberId;

                // اگر بعد عضو فعال است، سعی کن حساب تفضیلی لینک‌شده به عضو را پیدا کنی
                long? memberSubsidiaryAccountId = null;
                if (memberId.HasValue)
                {
                    memberSubsidiaryAccountId = await _db.QueryFirstOrDefaultAsync<long?>(
                        @"SELECT TOP 1 AccountId FROM dbo.Accounts
                          WHERE MemberId = @memberId AND Level = 4 AND IsActive = 1
                          ORDER BY AccountId",
                        new { memberId });
                }

                // منطق پویا: اگر حساب تفصیلی انتخابی وجود دارد و حساب فعلی از نوع بانک است، آن را جایگزین کن
                long accountToPost;
                
                // ابتدا بررسی کنیم که آیا حساب فعلی از نوع بانک است (کد 1101)
                bool isBankAccount = false;
                if (tx.SubsidiaryAccountId != null)
                {
                    var accountInfo = await _db.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT Code, ParentAccountId FROM dbo.Accounts WHERE AccountId = @accountId",
                        new { accountId = (long)l.AccountId });
                    
                    if (accountInfo != null)
                    {
                        // بررسی اینکه آیا این حساب یا والدش کد 1101 دارد
                        isBankAccount = accountInfo.Code == "1101" || 
                                       (accountInfo.ParentAccountId != null && 
                                        await _db.QueryFirstOrDefaultAsync<bool>(
                                            "SELECT CASE WHEN Code = '1101' THEN 1 ELSE 0 END FROM dbo.Accounts WHERE AccountId = @parentId",
                                            new { parentId = (long)accountInfo.ParentAccountId }));
                    }
                }
                
                if (isBankAccount && tx.SubsidiaryAccountId != null)
                {
                    // استفاده از حساب تفصیلی انتخابی بجای حساب بانک در الگو
                    accountToPost = (long)tx.SubsidiaryAccountId;
                    await _safeTransaction.LogProcessStepAsync(processId, "DynamicAccountReplacement", "Completed", 
                        $"حساب بانک {l.AccountId} با حساب تفصیلی انتخابی {tx.SubsidiaryAccountId} جایگزین شد");
                }
                else if (memberSubsidiaryAccountId.HasValue && (bool)l.AllowMemberDimension)
                {
                    // برای خطوطی که بعد عضو دارند، از حساب تفضیلی اختصاصی عضو استفاده کن
                    accountToPost = memberSubsidiaryAccountId.Value;
                    await _safeTransaction.LogProcessStepAsync(processId, "MemberSubsidiaryReplacement", "Completed",
                        $"حساب الگو {l.AccountId} برای عضو {memberId} با حساب تفضیلی عضو {memberSubsidiaryAccountId} جایگزین شد");
                }
                else if (l.OperationalAccountId != null && opToGl.ContainsKey((long)l.OperationalAccountId))
                {
                    accountToPost = opToGl[(long)l.OperationalAccountId];
                }
                else
                {
                    accountToPost = (long)l.AccountId;
                }

                await _safeTransaction.LogProcessStepAsync(processId, "LineDetailsCalculation", "Completed", $"خط {lineNo}: کد حساب={accountToPost} (عملیاتی={(l.OperationalAccountId != null)}), بدهکار={debit}, بستانکار={credit}, کد عضو={memberId}");

                journalLines.Add((lineNo, accountToPost, memberId, debit, credit));

                lineNo++;
            }

            if (journalLines.Count == 0)
            {
                await _safeTransaction.LogProcessErrorAsync(processId, new InvalidOperationException("خطوط سند حسابداری یافت نشد"), transactionId);
                throw new UserFriendlyException("خطایی در محاسبات حسابداری رخ داد.");
            }

            // Validate balance
            await _safeTransaction.LogProcessStepAsync(processId, "ValidateBalance", "Started", "بررسی تعادل بدهکار و بستانکار");

            var totalDebit = journalLines.Sum(x => x.Debit);
            var totalCredit = journalLines.Sum(x => x.Credit);

            if (totalDebit != totalCredit)
            {
                var errorMsg = $"سند حسابداری متعادل نیست: بدهکار={totalDebit:N0}, بستانکار={totalCredit:N0}";
                await _safeTransaction.LogProcessErrorAsync(processId, new InvalidOperationException(errorMsg), transactionId);
                throw new UserFriendlyException($"خطا در محاسبات حسابداری: مجموع بدهکار ({totalDebit:N0} ریال) با مجموع بستانکار ({totalCredit:N0} ریال) برابر نیست.");
            }

            // Temporarily disable trigger, insert all lines, then re-enable
            await _db.ExecuteAsync("DISABLE TRIGGER TR_Journals_EnforceBalance ON dbo.JournalLines");

            try
            {
                // Insert journal lines individually
                await _safeTransaction.LogProcessStepAsync(processId, "InsertJournalLines", "Started", $"درج {journalLines.Count} خط سند حسابداری");

                foreach (var jl in journalLines)
                {
                    await _db.ExecuteAsync(@"
                        INSERT INTO dbo.JournalLines (JournalId, [LineNo], AccountId, MemberId, Debit, Credit)
                        VALUES (@journalId, @LineNo, @AccountId, @MemberId, @Debit, @Credit)",
                        new { journalId, jl.LineNo, jl.AccountId, jl.MemberId, jl.Debit, jl.Credit });
                }
                await _safeTransaction.LogProcessStepAsync(processId, "InsertJournalLines", "Completed", $"{journalLines.Count} خط سند با موفقیت درج شد");
                await _safeTransaction.LogProcessStepAsync(processId, "PostingCompleted", "Completed", "ثبت حسابداری با موفقیت تکمیل شد");
            }
            finally
            {
                // Always re-enable the trigger
                await _db.ExecuteAsync("ENABLE TRIGGER TR_Journals_EnforceBalance ON dbo.JournalLines");
            }
        }
        catch (Exception ex)
        {
            if (processId != null)
            {
                await _safeTransaction.LogProcessErrorAsync(processId, ex, transactionId);
                
                // تلاش برای پاکسازی در صورت خطا
                var cleanupResult = await _safeTransaction.CleanupFailedProcessAsync(processId);
                if (!cleanupResult)
                {
                    _logger.LogWarning("پاکسازی فرآیند ناقص {ProcessId} ناموفق بود", processId);
                }
            }
            
            // اگر خطا از نوع UserFriendlyException است، همان پیام را نمایش می‌دهیم
            if (ex is UserFriendlyException)
            {
                throw;
            }
            
            // در غیر این صورت پیام کاربرپسند تولید می‌کنیم
            throw new UserFriendlyException("خطا در ثبت حسابداری. لطفاً اطلاعات وارد شده را بررسی کرده و دوباره تلاش کنید.", ex);
        }
    }

    public async Task<IReadOnlyList<JournalLinePreview>> PreviewLinesAsync(long typeId, decimal amountTotal, decimal? amountPrincipal, decimal? amountInterest, decimal? amountFee, decimal? amountTax, long? memberId)
    {
        var tpl = await _db.QueryFirstOrDefaultAsync<int?>("SELECT PostingTemplateId FROM dbo.TransactionTypes WHERE TypeId=@typeId", new { typeId });
        if (tpl == null) return Array.Empty<JournalLinePreview>();
        var lines = (await _db.QueryAsync<(int LineNo, long AccountId, long? OperationalAccountId, byte Side, byte AmountSource, bool AllowMemberDim, decimal? FixedAmount)>(
            @"SELECT [LineNo], AccountId, OperationalAccountId, Side, AmountSource, AllowMemberDimension, FixedAmount FROM dbo.PostingTemplateLines WHERE PostingTemplateId=@tpl ORDER BY [LineNo]",
            new { tpl = tpl.Value })).ToList();
        if (lines.Count == 0) return Array.Empty<JournalLinePreview>();

        decimal GetAmount(byte src, decimal? fixedAmount = null) => src switch
        {
            1 => amountTotal,
            2 => amountPrincipal ?? 0m,
            3 => amountInterest ?? 0m,
            4 => amountFee ?? 0m,
            5 => amountTax ?? 0m,
            6 => 0m, // AmountPenalty - not available in preview
            7 => 0m, // AmountCommission - not available in preview
            8 => fixedAmount ?? 0m,
            _ => 0m
        };

        // We'll show a single line per template line, using OperationalAccountId if present
        var allAccountIds = lines
            .Select(l => l.OperationalAccountId ?? l.AccountId)
            .Distinct()
            .ToArray();
        var accounts = (await _db.QueryAsync<(long AccountId, string Code, string Name)>(
            "SELECT AccountId, Code, Name FROM dbo.Accounts WHERE AccountId IN @ids",
            new { ids = allAccountIds })).ToDictionary(x => x.AccountId, x => (x.Code, x.Name));

        var result = new List<JournalLinePreview>();
        int lineNo = 1;
        foreach (var l in lines)
        {
            var amt = GetAmount(l.AmountSource, l.FixedAmount);
            if (amt == 0) continue;
            
            // Single entry per line: use OperationalAccountId if present (replacement)
            var accountToShow = l.OperationalAccountId ?? l.AccountId;
            var acc = accounts[accountToShow];
            result.Add(new JournalLinePreview
            {
                LineNo = lineNo,
                AccountId = accountToShow,
                AccountCode = acc.Code,
                AccountName = acc.Name + (l.OperationalAccountId.HasValue ? " (عملیاتی)" : ""),
                MemberId = l.AllowMemberDim ? memberId : null,
                Debit = l.Side == 1 ? amt : 0m,
                Credit = l.Side == 2 ? amt : 0m
            });
            lineNo++;
        }
        return result;
    }

    private async Task SafeAudit(long txId, string stage, string? message)
    {
        try { await _audit.LogAsync(txId, stage, message, createdBy: "system"); }
        catch { /* ignore audit failures */ }
    }
}
