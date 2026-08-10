using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace MiniBank.Web.Services;

/// <summary>
/// سرویس مدیریت امن تراکنش‌ها با پیام‌های خطای کاربرپسند
/// </summary>
public class SafeTransactionService
{
    private readonly IDbConnection _db;
    private readonly ILogger<SafeTransactionService> _logger;

    public SafeTransactionService(IDbConnection db, ILogger<SafeTransactionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// شروع فرآیند امن با ProcessId یکتا
    /// </summary>
    public async Task<string> StartSafeProcessAsync(string processType, long? relatedId = null, string? userId = null)
    {
        try
        {
            var parameters = new DynamicParameters();
            parameters.Add("@ProcessType", processType);
            parameters.Add("@RelatedId", relatedId);
            parameters.Add("@CreatedBy", userId);
            parameters.Add("@ProcessId", dbType: DbType.String, direction: ParameterDirection.Output, size: 50);
            
            await _db.ExecuteAsync("sp_StartSafeProcess", parameters, commandType: CommandType.StoredProcedure);

            return parameters.Get<string>("@ProcessId") ?? throw new InvalidOperationException("خطا در تولید شناسه فرآیند");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در شروع فرآیند امن: {ProcessType}", processType);
            throw new UserFriendlyException("خطا در شروع فرآیند. لطفاً دوباره تلاش کنید.");
        }
    }

    /// <summary>
    /// ثبت مرحله فرآیند با پیام کاربرپسند
    /// </summary>
    public async Task LogProcessStepAsync(string processId, string step, string status, string? message = null, long? relatedId = null)
    {
        try
        {
            await _db.ExecuteAsync("sp_LogProcessStep", 
                new { 
                    ProcessId = processId, 
                    Step = step, 
                    Status = status, 
                    Message = message, 
                    RelatedId = relatedId 
                }, 
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در ثبت مرحله فرآیند: {ProcessId} - {Step}", processId, step);
            // عدم ثبت مرحله نباید فرآیند را متوقف کند
        }
    }

    /// <summary>
    /// ثبت خطا با پیام‌های کاربرپسند
    /// </summary>
    public async Task LogProcessErrorAsync(string processId, Exception exception, long? transactionId = null, long? receiptId = null)
    {
        try
        {
            var userFriendlyMessage = GetUserFriendlyErrorMessage(exception);
            
            await _db.ExecuteAsync("sp_LogProcessError", 
                new { 
                    ProcessId = processId, 
                    ErrorMessage = userFriendlyMessage, 
                    ErrorCode = exception.GetType().Name, 
                    StackTrace = exception.StackTrace, 
                    TransactionId = transactionId, 
                    ReceiptId = receiptId 
                }, 
                commandType: CommandType.StoredProcedure);

            _logger.LogError(exception, "خطای فرآیند {ProcessId}: {Message}", processId, userFriendlyMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در ثبت خطای فرآیند: {ProcessId}", processId);
        }
    }

    /// <summary>
    /// پاکسازی فرآیند ناقص با بازگردانی تغییرات
    /// </summary>
    public async Task<bool> CleanupFailedProcessAsync(string processId, long? receiptId = null)
    {
        try
        {
            await LogProcessStepAsync(processId, "Cleanup", "Started", "شروع پاکسازی فرآیند ناقص");

            var result = await _db.ExecuteAsync("sp_CleanupFailedProcess", 
                new { ProcessId = processId, ReceiptId = receiptId }, 
                commandType: CommandType.StoredProcedure);

            if (result >= 0)
            {
                await LogProcessStepAsync(processId, "Cleanup", "Completed", "پاکسازی با موفقیت انجام شد");
                return true;
            }
            else
            {
                await LogProcessStepAsync(processId, "Cleanup", "Failed", "خطا در پاکسازی");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در پاکسازی فرآیند: {ProcessId}", processId);
            await LogProcessErrorAsync(processId, ex);
            return false;
        }
    }

    /// <summary>
    /// بررسی وضعیت فرآیند
    /// </summary>
    public async Task<ProcessStatusInfo?> GetProcessStatusAsync(string processId)
    {
        try
        {
            var result = await _db.QueryFirstOrDefaultAsync<ProcessStatusInfo>(
                "SELECT * FROM dbo.fn_GetProcessStatus(@ProcessId)", 
                new { ProcessId = processId });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در بررسی وضعیت فرآیند: {ProcessId}", processId);
            return null;
        }
    }

    /// <summary>
    /// تبدیل خطاهای فنی به پیام‌های کاربرپسند
    /// </summary>
    private string GetUserFriendlyErrorMessage(Exception exception)
    {
        return exception switch
        {
            SqlException sqlEx when sqlEx.Message.Contains("FOREIGN KEY") => 
                "خطا در ارتباط داده‌ها. لطفاً اطمینان حاصل کنید که تمام اطلاعات مرتبط موجود است.",
            
            SqlException sqlEx when sqlEx.Message.Contains("PRIMARY KEY") || sqlEx.Message.Contains("UNIQUE") => 
                "این اطلاعات قبلاً ثبت شده است. لطفاً اطلاعات متفاوتی وارد کنید.",
            
            SqlException sqlEx when sqlEx.Message.Contains("CHECK constraint") => 
                "اطلاعات وارد شده معتبر نیست. لطفاً مقادیر صحیح وارد کنید.",
            
            SqlException sqlEx when sqlEx.Message.Contains("Cannot insert NULL") => 
                "برخی از فیلدهای ضروری خالی است. لطفاً تمام اطلاعات مورد نیاز را وارد کنید.",
            
            SqlException sqlEx when sqlEx.Message.Contains("divide by zero") => 
                "خطا در محاسبات حسابداری. لطفاً مبالغ وارد شده را بررسی کنید.",
            
            SqlException sqlEx when sqlEx.Message.Contains("timeout") => 
                "عملیات بیش از حد طولانی شد. لطفاً دوباره تلاش کنید.",
            
            SqlException sqlEx when sqlEx.Message.Contains("deadlock") => 
                "سیستم مشغول است. لطفاً چند لحظه صبر کرده و دوباره تلاش کنید.",
            
            InvalidOperationException => 
                "عملیات غیرمجاز. لطفاً مراحل را به ترتیب انجام دهید.",
            
            ArgumentException => 
                "اطلاعات وارد شده نامعتبر است. لطفاً مقادیر صحیح وارد کنید.",
            
            UnauthorizedAccessException => 
                "شما مجوز انجام این عملیات را ندارید.",
            
            TimeoutException => 
                "عملیات بیش از حد طولانی شد. لطفاً دوباره تلاش کنید.",
            
            _ => "خطای غیرمنتظره رخ داد. لطفاً با پشتیبانی تماس بگیرید."
        };
    }
}

/// <summary>
/// اطلاعات وضعیت فرآیند
/// </summary>
public class ProcessStatusInfo
{
    public string ProcessId { get; set; } = string.Empty;
    public string ProcessType { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime LastUpdateAt { get; set; }
    public int StepCount { get; set; }
    public string? LastMessage { get; set; }
    public bool RequiresCleanup { get; set; }
}

/// <summary>
/// استثناء با پیام کاربرپسند
/// </summary>
public class UserFriendlyException : Exception
{
    public UserFriendlyException(string message) : base(message) { }
    public UserFriendlyException(string message, Exception innerException) : base(message, innerException) { }
}
