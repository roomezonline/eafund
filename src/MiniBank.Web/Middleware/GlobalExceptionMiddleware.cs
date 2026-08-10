using System.Net;
using System.Text.Json;

namespace MiniBank.Web.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                // Log with full stack trace and detailed information
                _logger.LogError(ex, "🚨 EXCEPTION: {ExceptionType} at {RequestPath} - {Message}", 
                    ex.GetType().Name, context.Request.Path, ex.Message);
                
                // Also log to console for immediate visibility - only critical info
                Console.WriteLine($"🚨 [{DateTime.Now:HH:mm:ss}] {ex.GetType().Name} at {context.Request.Path}");
                Console.WriteLine($"   💬 {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"   🔗 Inner: {ex.InnerException.Message}");
                Console.WriteLine("─────────────────────────────────────");
                
                await HandleExceptionAsync(context, ex);
            }
        }

        private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            
            var response = new ErrorResponse();

            switch (exception)
            {
                case ArgumentNullException:
                case ArgumentException:
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    response.Message = "درخواست نامعتبر است";
                    response.Details = exception.Message;
                    break;
                
                case UnauthorizedAccessException:
                    response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    response.Message = "دسترسی غیرمجاز";
                    break;
                
                case KeyNotFoundException:
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.Message = "اطلاعات مورد نظر یافت نشد";
                    break;
                
                case InvalidOperationException when exception.Message.Contains("connection"):
                    response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    response.Message = "خطا در اتصال به پایگاه داده";
                    response.Details = "لطفاً دوباره تلاش کنید";
                    break;
                
                case Services.UserFriendlyException userEx:
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    response.Message = userEx.Message;
                    response.Details = "خطای کاربری";
                    break;
                
                default:
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    response.Message = "خطای داخلی سرور";
                    response.Details = "لطفاً با پشتیبانی تماس بگیرید";
                    break;
            }

            context.Response.StatusCode = response.StatusCode;

            var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            await context.Response.WriteAsync(jsonResponse);
        }
    }

    public class ErrorResponse
    {
        public int StatusCode { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
