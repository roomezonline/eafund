using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using MudBlazor.Services;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using MiniBank.Web.Components.DesignSystem;
using MiniBank.Web.Core.Interfaces;
using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;
using MiniBank.Web.Models.Notifications;
using MiniBank.Web.Services;
using MiniBank.Web.Reports;
using MiniBank.Web.Middleware;
using MiniBank.Web.Infrastructure;
using MiniBank.Web.Components.Extensions;
using Microsoft.Data.SqlClient;
using FluentValidation;
using MediatR;
using Serilog;
using Serilog.Events;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using MiniBank.Web.Services.Auth;
using MiniBank.Web.Services.Notifications;
using MiniBank.Web.Models.Notifications;
using System.IO;
using System.Security.Cryptography;

// Configure Serilog with structured logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information() // Changed to Information to show startup details
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Components.Web.ErrorBoundary", LogEventLevel.Warning)
    .MinimumLevel.Override("MiniBank", LogEventLevel.Information)
    .MinimumLevel.Override("MiniBank.Web.Infrastructure", LogEventLevel.Debug) // Show migration details
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "MiniBank")
    .WriteTo.Console(
        restrictedToMinimumLevel: LogEventLevel.Information, // Show Info and above in console
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/minibank-.txt", 
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} - {Message:lj} {Properties:j}{NewLine}{Exception}",
        retainedFileCountLimit: 30)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToPage("/_Host");
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("bale", c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<HttpClient>(sp =>
{
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var request = httpContextAccessor.HttpContext?.Request;
    if (request != null)
    {
        var baseUri = $"{request.Scheme}://{request.Host}";
        return new HttpClient { BaseAddress = new Uri(baseUri) };
    }
    return new HttpClient { BaseAddress = new Uri("https://localhost:55205") };
});
// Configure SignalR/Blazor Server timeouts and circuit options to improve mobile reconnection
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    // If the server doesn't hear from the client within this interval, it considers the connection disconnected
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
    // Server ping interval to keep the connection alive (should be less than any proxy idle timeout and ClientTimeoutInterval)
    options.KeepAliveInterval = TimeSpan.FromSeconds(30);
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    options.MaximumReceiveMessageSize = 64 * 1024; // 64KB
});

// (moved) Accounting reports APIs are mapped after app = builder.Build()

builder.Services.AddServerSideBlazor(options =>
{
    // Enable detailed error reporting for debugging
    options.DetailedErrors = true;
}).AddCircuitOptions(options =>
{
    // Keep circuits for longer so that a user returning to the app can reconnect without full reload
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
    // Increase JS interop timeout in case the device is under load
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(2);
    // Allow some render batches to buffer without ACK to handle spotty networks
    options.MaxBufferedUnacknowledgedRenderBatches = 20;
});
builder.Services.AddMudServices(config =>
{
    // Snackbar (Toast) configuration
    config.SnackbarConfiguration.ShowCloseIcon = false; // remove X button
    config.SnackbarConfiguration.VisibleStateDuration = 5000; // 5s visible
    config.SnackbarConfiguration.HideTransitionDuration = 200; // smooth hide
    config.SnackbarConfiguration.ShowTransitionDuration = 150; // smooth show
    config.SnackbarConfiguration.RequireInteraction = false; // auto-close
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.MaxDisplayedSnackbars = 4;
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.TopCenter;
    config.SnackbarConfiguration.SnackbarVariant = MudBlazor.Variant.Filled;
});

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Add FluentValidation
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// Rate limiting will be implemented in a future version

// Add caching
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICacheService, CacheService>();

// Configure shutdown timeout
builder.Services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(5));

// Register database connection with proper lifecycle management
builder.Services.AddScoped<IDbConnection>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    // Connection will be opened lazily by repositories (EnsureOpenAsync)
    return new SqlConnection(connectionString);
});

// DateConversionLog disabled temporarily — no record noise
// MiniBank.Web.Components.Extensions.DateTimeExtensions.ConfigureLogging(
//     builder.Configuration.GetConnectionString("DefaultConnection") ?? "");

// Add connection factory for better management
builder.Services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();

// Register repositories and unit of work
builder.Services.AddScoped<IHouseholdRepository, HouseholdRepository>();
builder.Services.AddScoped<AuditRepository>();
builder.Services.AddScoped<MemberRepository>();
builder.Services.AddScoped<BankAccountRepository>();
builder.Services.AddScoped<BankCardRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<TransactionRepository>();
builder.Services.AddScoped<DepositRatePlanRepository>();
builder.Services.AddScoped<DepositRateTierRepository>();
builder.Services.AddScoped<LoanRatePlanRepository>();
builder.Services.AddScoped<LoanRepository>();
builder.Services.AddScoped<LoanInstallmentRepository>();
builder.Services.AddScoped<LoanInstallmentReminderRepository>();
builder.Services.AddScoped<LoanGuaranteeRepository>();
builder.Services.AddScoped<CreditHoldRepository>();
builder.Services.AddScoped<LoanPaymentRepository>();
builder.Services.AddScoped<LoanReportsRepository>();
builder.Services.AddScoped<AccountsRepository>();
builder.Services.AddScoped<AccountTypeRepository>();
builder.Services.AddScoped<PostingTemplateRepository>();
builder.Services.AddScoped<AccountingReportRepository>();
builder.Services.AddScoped<TransactionAuditRepository>();
builder.Services.AddScoped<HouseholdReceiptRepository>();
builder.Services.AddScoped<HouseholdReceiptAllocationRepository>();
builder.Services.AddScoped<JournalsRepository>();
builder.Services.AddScoped<JournalLinesRepository>();
builder.Services.AddScoped<DepositInterestRunRepository>();
builder.Services.AddScoped<FiscalYearRepository>();
builder.Services.AddScoped<CashFlowBudgetRepository>();

// Register custom services
builder.Services.AddScoped<HouseholdService>();
builder.Services.AddScoped<MemberService>();
builder.Services.AddScoped<BankAccountService>();
builder.Services.AddScoped<BankCardService>();
builder.Services.AddScoped<DepositRateService>();
builder.Services.AddScoped<LoanRateService>();
builder.Services.AddScoped<LoanCalculatorService>();
builder.Services.AddScoped<LoanService>();
builder.Services.AddScoped<MemberFinancialSnapshotService>();
builder.Services.AddScoped<SafeTransactionService>();
builder.Services.AddScoped<TransactionReportRepository>();
builder.Services.AddScoped<TrialBalanceRepository>();
builder.Services.AddScoped<AccountingPostingService>();
builder.Services.AddScoped<PostingService>();
builder.Services.AddScoped<DepositInterestPostingService>();
builder.Services.AddScoped<FiscalYearService>();
builder.Services.AddScoped<FiscalYearContext>();
builder.Services.AddScoped<FiscalYearRangeProvider>();
builder.Services.AddScoped<ProfitLossService>();
builder.Services.AddScoped<SmsPanelRepository>();
builder.Services.AddScoped<SmsSendService>();
builder.Services.AddScoped<SmsSender>();
builder.Services.AddScoped<SmsMessageLogRepository>();
builder.Services.AddScoped<BaleBotRepository>();
builder.Services.AddScoped<NotificationRepository>();
builder.Services.AddScoped<BaleApiClient>();
builder.Services.AddScoped<BaleMemberMenuService>();
builder.Services.AddScoped<NotificationDispatcher>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddHostedService<NotificationDispatcherHostedService>();
builder.Services.AddHostedService<AutomaticLoanReminderHostedService>();
builder.Services.AddScoped<LoadingService>();
builder.Services.AddScoped<DatabaseMigrationService>();
builder.Services.AddScoped<MiniBank.Web.Infrastructure.ConstraintFixService>();
builder.Services.AddScoped<MiniBank.Web.Components.DesignSystem.DsToastService>();
builder.Services.AddScoped<PasswordHashService>();
builder.Services.AddScoped<DbBackupRepository>();
builder.Services.AddScoped<DbBackupService>();
builder.Services.AddSingleton<OnlineUsersTracker>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, OnlineUsersCircuitHandler>();
// Warm-up hosted service to reduce first-load latency (preload transaction groups)
builder.Services.AddHostedService<WarmupService>();
builder.Services.AddHostedService<DbBackupHostedService>();

// Auth (Cookie)
{
    var keyPath = builder.Configuration["DataProtection:KeyPath"];
    if (string.IsNullOrWhiteSpace(keyPath))
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        keyPath = Path.Combine(baseDir, "MiniBank", "DataProtectionKeys");
    }

    Directory.CreateDirectory(keyPath);

    builder.Services
        .AddDataProtection()
        .SetApplicationName("MiniBank")
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
}
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/forbidden";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    // Lock down everything by default
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Auth services
builder.Services.AddScoped<AuthRepository>();
builder.Services.AddScoped<CryptoService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuthSyncService>();
builder.Services.AddScoped<AuthPermissionService>();
builder.Services.AddScoped<AuthPageSeeder>();
builder.Services.AddScoped<AuthActivityService>();

// Configure Persian culture
var persianCulture = new CultureInfo("fa-IR");
persianCulture.DateTimeFormat.Calendar = new System.Globalization.PersianCalendar();

CultureInfo.DefaultThreadCurrentCulture = persianCulture;
CultureInfo.DefaultThreadCurrentUICulture = persianCulture;

// Configure QuestPDF license globally (Community)
QuestPDF.Settings.License = LicenseType.Community;

Log.Information("🔨 Building application...");
var app = builder.Build();
Log.Information("✅ Application built successfully");

// Configure graceful shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    Log.Information("🛑 Application is shutting down...");
});

lifetime.ApplicationStarted.Register(() =>
{
    Log.Information("🎉 MiniBank Application started successfully!");
    Log.Information("🌐 Application is ready to serve requests");
});

// Initialize database on startup (central migration runner only) - conditional
var runMigrations = app.Configuration.GetValue<bool?>("Database:RunMigrationsOnStartup")
                   ?? !app.Environment.IsProduction();

Log.Information("🚀 MiniBank Application Starting...");
Log.Information("📊 Environment: {Environment}", app.Environment.EnvironmentName);
Log.Information("🔧 Configuration: RunMigrationsOnStartup = {RunMigrations}", runMigrations);

if (runMigrations)
{
    Log.Information("📋 Starting database migration process...");
    var migrationStartTime = DateTime.Now;
    using var scope = app.Services.CreateScope();
    try 
    {
        var migrationService = scope.ServiceProvider.GetRequiredService<DatabaseMigrationService>();
        
        Log.Information("🔍 Checking database connection...");
        await migrationService.ApplyMigrationsAsync();
        
        // Fix subsidiary constraint after migrations
        Log.Information("🔧 Applying constraint fixes...");
        var constraintFix = scope.ServiceProvider.GetRequiredService<MiniBank.Web.Infrastructure.ConstraintFixService>();
        await constraintFix.FixSubsidiaryConstraintAsync();
        
        var migrationDuration = DateTime.Now - migrationStartTime;
        Log.Information("✅ Database migrations and fixes completed successfully in {Duration}ms", migrationDuration.TotalMilliseconds);
    }
    catch (Exception ex)
    {
        var migrationDuration = DateTime.Now - migrationStartTime;
        Log.Warning("⚠️ Database migration failed after {Duration}ms, trying constraint fix only: {Error}", migrationDuration.TotalMilliseconds, ex.Message);
        Log.Debug("Migration error details: {Exception}", ex);
        
        // Even if migration fails, try to fix the constraint
        try
        {
            Log.Information("🔧 Attempting constraint fix without migration...");
            var constraintFix = scope.ServiceProvider.GetRequiredService<MiniBank.Web.Infrastructure.ConstraintFixService>();
            await constraintFix.FixSubsidiaryConstraintAsync();
            Log.Information("✅ Constraint fix completed successfully");
        }
        catch (Exception constraintEx)
        {
            Log.Warning("⚠️ Constraint fix also failed: {ConstraintError}", constraintEx.Message);
        }
    }
}
else
{
    Log.Information("⏭️ Skipping database migrations on startup (Database:RunMigrationsOnStartup = false)");
}

try
{
    using var authScope = app.Services.CreateScope();
    var authRepo = authScope.ServiceProvider.GetRequiredService<AuthRepository>();
    var seeder = authScope.ServiceProvider.GetRequiredService<AuthPageSeeder>();

    try { await seeder.EnsurePagesSeededFromRoutesAsync(); } catch { }

    try
    {
        var memberRole = await authRepo.GetRoleByNameAsync("Member");
        if (memberRole != null)
        {
            try { await authRepo.UpsertPageAsync("/dashboard", "داشبورد", "/dashboard", true); } catch { }

            try
            {
                var page = await authRepo.GetPageByKeyAsync("/dashboard");
                if (page != null)
                    await authRepo.SetRolePagePermissionAsync(memberRole.RoleId, page.PageId, true);
            }
            catch { }
        }
    }
    catch { }

    if (!await authRepo.AnyUsersAsync())
    {
        var cfg = app.Configuration;
        var seedUser = (cfg["Auth:SeedAdmin:Username"] ?? string.Empty).Trim();
        var seedPass = cfg["Auth:SeedAdmin:Password"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(seedUser) || string.IsNullOrWhiteSpace(seedPass))
        {
            Log.Warning("AuthUsers is empty but Auth:SeedAdmin:Username/Password is not set; skipping admin seed");
        }
        else
        {
            var hasher = authScope.ServiceProvider.GetRequiredService<PasswordHashService>();

            var adminRole = await authRepo.GetRoleByNameAsync("Admin");
            if (adminRole == null)
            {
                var roleId = await authRepo.CreateRoleAsync("Admin", isSystem: true, isActive: true);
                adminRole = new MiniBank.Web.Models.Auth.AuthRole { RoleId = roleId, Name = "Admin", IsSystem = true, IsActive = true };
            }

            try
            {
                var pages = await authRepo.GetPagesAsync(includeInactive: true);
                foreach (var p in pages)
                {
                    try { await authRepo.SetRolePagePermissionAsync(adminRole.RoleId, p.PageId, true); } catch { }
                }
            }
            catch { }

            try
            {
                var existing = await authRepo.GetUserByUsernameAsync(seedUser);
                if (existing == null)
                {
                    await authRepo.CreateUserAsync(new MiniBank.Web.Models.Auth.AuthUser
                    {
                        Username = seedUser,
                        PasswordEnc = hasher.HashPassword(seedPass),
                        RoleId = adminRole.RoleId,
                        MemberId = null,
                        IsActive = true
                    });
                }
            }
            catch { }
        }
    }
}
catch { }

// Configure the HTTP request pipeline.
// Add global exception handling middleware
app.UseMiddleware<GlobalExceptionMiddleware>();

if (!app.Environment.IsDevelopment())
{
    // In Production, prefer HTTPS when endpoint is configured by hosting
    app.UseHsts();
    app.UseHttpsRedirection();
}
else
{
    // In Development, avoid HTTPS redirect warning when no HTTPS port is configured
    // Use HTTP only unless you explicitly run with https
    // app.UseHttpsRedirection();
}
// Static files with no-cache in Development to avoid stale assets during rapid changes
if (app.Environment.IsDevelopment())
{
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
    });
    // Also disable caching for dynamic responses in Development
    app.Use(async (context, next) =>
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
        await next();
    });
}
else
{
    app.UseStaticFiles();
    // Prevent caching of HTML in Production to avoid stale app shell after deploys.
    // The header must be registered in OnStarting (before the response starts); setting it after
    // next() throws "Headers are read-only, response has already started" once the body is streamed.
    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            try
            {
                var contentType = context.Response.ContentType;
                if (!string.IsNullOrEmpty(contentType) && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                    context.Response.Headers["Pragma"] = "no-cache";
                    context.Response.Headers["Expires"] = "0";
                }
            }
            catch { }
            return Task.CompletedTask;
        });
        await next();
    });
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/" && HttpMethods.IsGet(context.Request.Method))
    {
        var authenticated = context.User.Identity?.IsAuthenticated == true;
        var role = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var target = !authenticated
            ? "/login"
            : (string.Equals(role, "Member", StringComparison.OrdinalIgnoreCase) ? "/dashboard" : "/admin/dashboard2");

        context.Response.Redirect(target);
        return;
    }

    await next();
});

app.MapRazorPages();
app.MapBlazorHub().AllowAnonymous();
app.MapFallbackToPage("/_Host").AllowAnonymous();

app.MapPost("/api/auth/login", async (HttpContext ctx, AuthRepository repo, MemberRepository members, CryptoService crypto, LoginRequest req) =>
{
    string NormalizeDigits(string s)
    {
        s ??= string.Empty;
        return s
            .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
            .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
            .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
            .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
    }

    static string NormalizePassword(string s)
    {
        s ??= string.Empty;
        s = s.Normalize(NormalizationForm.FormKC);

        // Remove common invisible/formatting characters that keyboards may insert
        // (ZWNJ, ZWJ, BOM, LRM/RLM, bidi overrides/isolates)
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\u200C' || ch == '\u200D' || ch == '\uFEFF' || ch == '\u200E' || ch == '\u200F')
                continue;
            if (ch >= '\u202A' && ch <= '\u202E')
                continue;
            if (ch >= '\u2066' && ch <= '\u2069')
                continue;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    var username = NormalizeDigits((req.Username ?? string.Empty).Trim());
    var rawPassword = NormalizeDigits((req.Password ?? string.Empty).Trim());
    var password = NormalizeDigits(NormalizePassword(req.Password ?? string.Empty));

    string Last4(string s)
    {
        s = (s ?? string.Empty).Trim();
        if (s.Length <= 4) return s;
        return s.Substring(s.Length - 4);
    }

    if (string.IsNullOrWhiteSpace(username))
        return Results.BadRequest(new { ok = false, error = "نام کاربری را وارد کنید" });

    var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var seedUser = (cfg["Auth:SeedAdmin:Username"] ?? string.Empty).Trim();
    var seedPass = cfg["Auth:SeedAdmin:Password"] ?? string.Empty;

    var user = await repo.GetUserByUsernameAsync(username);

    // Bootstrap: if this is the configured seed admin, allow login via config password
    // even when legacy DataProtection cannot be unprotected (e.g., DB moved between machines).
    if (!string.IsNullOrWhiteSpace(seedUser)
        && !string.IsNullOrWhiteSpace(seedPass)
        && string.Equals(username, seedUser, StringComparison.Ordinal)
        && string.Equals(password, seedPass, StringComparison.Ordinal))
    {
        if (user == null)
        {
            var adminRole = await repo.GetRoleByNameAsync("Admin");
            if (adminRole == null)
            {
                var roleId = await repo.CreateRoleAsync("Admin", isSystem: true, isActive: true);
                adminRole = new MiniBank.Web.Models.Auth.AuthRole { RoleId = roleId, Name = "Admin", IsSystem = true, IsActive = true };
            }

            try
            {
                await repo.CreateUserAsync(new MiniBank.Web.Models.Auth.AuthUser
                {
                    Username = username,
                    PasswordEnc = password,
                    PasswordViewEnc = crypto.Protect(password),
                    RoleId = adminRole.RoleId,
                    MemberId = null,
                    IsActive = true
                });
            }
            catch { }

            user = await repo.GetUserByUsernameAsync(username);
        }
        else
        {
            try { await repo.UpdateUserPasswordAsync(user.UserId, password, crypto.Protect(password)); } catch { }
        }
    }

    if (user == null)
    {
        var member = await members.GetByNationalCodeAsync(username);
        if (member != null && member.IsActive && string.Equals(Last4(username), password, StringComparison.Ordinal))
        {
            var memberRole = await repo.GetRoleByNameAsync("Member");
            if (memberRole == null)
            {
                var roleId = await repo.CreateRoleAsync("Member", isSystem: true, isActive: true);
                memberRole = new MiniBank.Web.Models.Auth.AuthRole { RoleId = roleId, Name = "Member", IsSystem = true, IsActive = true };
            }

            try
            {
                await repo.CreateUserAsync(new MiniBank.Web.Models.Auth.AuthUser
                {
                    Username = username,
                    PasswordEnc = password,
                    PasswordViewEnc = crypto.Protect(password),
                    RoleId = memberRole.RoleId,
                    MemberId = member.MemberId,
                    IsActive = true
                });
            }
            catch { }

            user = await repo.GetUserByUsernameAsync(username);
        }
    }

    if (user == null)
        return Results.Json(new { ok = false, status = 401 }, statusCode: 401);

    if (!user.IsActive)
        return Results.BadRequest(new { ok = false, error = "کاربر غیرفعال است" });

    if (string.IsNullOrWhiteSpace(user.PasswordEnc))
        return Results.Json(new { ok = false, status = 401 }, statusCode: 401);

    try
    {
        var enc = user.PasswordEnc ?? string.Empty;
        var kind = enc.StartsWith("PBKDF2$", StringComparison.Ordinal) ? "PBKDF2" : "LEGACY";
        var hasView = !string.IsNullOrWhiteSpace(user.PasswordViewEnc);
        Log.Information("Login debug: user={Username} kind={Kind} encLen={EncLen} hasView={HasView}", username, kind, enc.Length, hasView);
    }
    catch { }

    // Password verification strategy (simplified as requested):
    // - Preferred: plain PasswordEnc (string compare)
    // - Backward compatible: PBKDF2 hashes and legacy DataProtection protected strings
    // - Repair path: if PBKDF2 mismatch, fall back to PasswordViewEnc and migrate to plain.
    var matched = false;
    var storedEnc = user.PasswordEnc ?? string.Empty;

    var matchPath = "";

    if (string.Equals(storedEnc, password, StringComparison.Ordinal))
    {
        matched = true;
        matchPath = "PLAIN";
    }
    else
    {
        var hasher = ctx.RequestServices.GetRequiredService<PasswordHashService>();
        if (hasher.CanVerify(storedEnc))
        {
            if (hasher.Verify(storedEnc, password) || hasher.Verify(storedEnc, rawPassword))
            {
                matched = true;
                matchPath = hasher.Verify(storedEnc, password) ? "PBKDF2(NORMALIZED)" : "PBKDF2(RAW)";
            }
            else
            {
                // fallback: decrypt viewable password (if exists) and compare
                try
                {
                    if (!string.IsNullOrWhiteSpace(user.PasswordViewEnc))
                    {
                        var plain = crypto.Unprotect(user.PasswordViewEnc);
                        if (string.Equals(NormalizeDigits(NormalizePassword(plain)), password, StringComparison.Ordinal))
                        {
                            matched = true;
                            matchPath = "VIEW";
                        }
                    }
                }
                catch { }
            }

            if (matched)
            {
                // migrate to simple storage
                try { await repo.UpdateUserPasswordAsync(user.UserId, password, crypto.Protect(password)); } catch { }
                try { Log.Information("Login migrate: user={Username} path={Path}", username, matchPath); } catch { }
            }
        }
        else
        {
            // legacy DataProtection protected
            try
            {
                var stored = crypto.Unprotect(storedEnc);
                matched = string.Equals(NormalizeDigits(NormalizePassword(stored)), password, StringComparison.Ordinal);
                if (matched)
                {
                    matchPath = "LEGACY_UNPROTECT";
                    try { await repo.UpdateUserPasswordAsync(user.UserId, password, crypto.Protect(password)); } catch { }
                }
            }
            catch
            {
                matched = false;
            }
        }
    }

    if (!matched)
    {
        try { Log.Information("Login failed: password mismatch for user {Username}", username); } catch { }

        try
        {
            var stored = user.PasswordEnc ?? string.Empty;
            var kind = stored.StartsWith("PBKDF2$", StringComparison.Ordinal) ? "PBKDF2" : "PLAIN_OR_LEGACY";
            return Results.Json(new
            {
                ok = false,
                status = 401,
                debug = new
                {
                    user = username,
                    kind,
                    encLen = stored.Length,
                    hasView = !string.IsNullOrWhiteSpace(user.PasswordViewEnc),
                    matchPath = string.IsNullOrWhiteSpace(matchPath) ? "NONE" : matchPath
                }
            }, statusCode: 401);
        }
        catch { }

        return Results.Json(new { ok = false, status = 401 }, statusCode: 401);
    }

    try { Log.Information("Login success: user={Username} path={Path}", username, string.IsNullOrWhiteSpace(matchPath) ? "UNKNOWN" : matchPath); } catch { }

    var claims = new List<System.Security.Claims.Claim>
    {
        new(System.Security.Claims.ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new(System.Security.Claims.ClaimTypes.Name, user.Username),
        new(System.Security.Claims.ClaimTypes.Role, user.Role?.Name ?? string.Empty),
        new("RoleId", user.RoleId.ToString())
    };

    if (user.MemberId.HasValue)
        claims.Add(new System.Security.Claims.Claim("MemberId", user.MemberId.Value.ToString()));

    var identity = new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new System.Security.Claims.ClaimsPrincipal(identity);

    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
    });

    try { await repo.TouchLoginAsync(user.UserId); } catch { }

    try
    {
        var stored = user.PasswordEnc ?? string.Empty;
        var kind = stored.StartsWith("PBKDF2$", StringComparison.Ordinal) ? "PBKDF2" : "PLAIN_OR_LEGACY";
        return Results.Ok(new
        {
            ok = true,
            debug = new
            {
                user = username,
                kind,
                encLen = stored.Length,
                hasView = !string.IsNullOrWhiteSpace(user.PasswordViewEnc),
                matchPath = string.IsNullOrWhiteSpace(matchPath) ? "UNKNOWN" : matchPath
            }
        });
    }
    catch { }

    return Results.Ok(new { ok = true });
}).AllowAnonymous();

app.MapPost("/auth/login", async (HttpContext ctx, AuthRepository repo, MemberRepository members, CryptoService crypto) =>
{
    string NormalizeDigits(string s)
    {
        s ??= string.Empty;
        return s
            .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
            .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
            .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
            .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
    }

    static string NormalizePassword(string s)
    {
        s ??= string.Empty;
        s = s.Normalize(NormalizationForm.FormKC);

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\u200C' || ch == '\u200D' || ch == '\uFEFF' || ch == '\u200E' || ch == '\u200F')
                continue;
            if (ch >= '\u202A' && ch <= '\u202E')
                continue;
            if (ch >= '\u2066' && ch <= '\u2069')
                continue;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    var form = await ctx.Request.ReadFormAsync();
    var username = NormalizeDigits((form["username"].ToString() ?? string.Empty).Trim());
    var password = NormalizeDigits(NormalizePassword(form["password"].ToString() ?? string.Empty));
    var returnUrl = (form["returnUrl"].ToString() ?? string.Empty).Trim();

    string Last4(string s)
    {
        s = (s ?? string.Empty).Trim();
        if (s.Length <= 4) return s;
        return s.Substring(s.Length - 4);
    }

    if (string.IsNullOrWhiteSpace(returnUrl)) returnUrl = "/";
    if (!returnUrl.StartsWith('/')) returnUrl = "/";

    if (string.IsNullOrWhiteSpace(username))
        return Results.Redirect("/login?error=1");

    var user = await repo.GetUserByUsernameAsync(username);
    if (user == null)
    {
        var member = await members.GetByNationalCodeAsync(username);
        if (member != null && member.IsActive && string.Equals(Last4(username), password, StringComparison.Ordinal))
        {
            var memberRole = await repo.GetRoleByNameAsync("Member");
            if (memberRole == null)
            {
                var roleId = await repo.CreateRoleAsync("Member", isSystem: true, isActive: true);
                memberRole = new MiniBank.Web.Models.Auth.AuthRole { RoleId = roleId, Name = "Member", IsSystem = true, IsActive = true };
            }

            try
            {
                await repo.CreateUserAsync(new MiniBank.Web.Models.Auth.AuthUser
                {
                    Username = username,
                    PasswordEnc = crypto.Protect(password),
                    RoleId = memberRole.RoleId,
                    MemberId = member.MemberId,
                    IsActive = true
                });
            }
            catch { }

            user = await repo.GetUserByUsernameAsync(username);
        }
    }

    if (user == null)
        return Results.Redirect("/login?error=2");

    if (!user.IsActive)
        return Results.Redirect("/login?error=3");

    if (string.IsNullOrWhiteSpace(user.PasswordEnc))
        return Results.Redirect("/login?error=4");

    if (string.Equals(user.PasswordEnc, password, StringComparison.Ordinal))
    {
        // best-effort migrate to protected
        try { await repo.UpdateUserPasswordAsync(user.UserId, crypto.Protect(password)); } catch { }
    }
    else
    {
        string stored;
        try { stored = crypto.Unprotect(user.PasswordEnc); }
        catch { return Results.Redirect("/login?error=2"); }

        if (!string.Equals(stored, password, StringComparison.Ordinal))
            return Results.Redirect("/login?error=2");
    }

    var claims = new List<System.Security.Claims.Claim>
    {
        new(System.Security.Claims.ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new(System.Security.Claims.ClaimTypes.Name, user.Username),
        new(System.Security.Claims.ClaimTypes.Role, user.Role?.Name ?? string.Empty),
        new("RoleId", user.RoleId.ToString())
    };
    if (user.MemberId.HasValue)
        claims.Add(new System.Security.Claims.Claim("MemberId", user.MemberId.Value.ToString()));

    var identity = new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new System.Security.Claims.ClaimsPrincipal(identity);

    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
    });

    try { await repo.TouchLoginAsync(user.UserId); } catch { }
    return Results.Redirect(returnUrl);
}).AllowAnonymous();

app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { ok = true });
}).AllowAnonymous();

static bool BaleSecretEquals(string? provided, string? expected)
{
    var a = Encoding.UTF8.GetBytes(provided ?? string.Empty);
    var b = Encoding.UTF8.GetBytes(expected ?? string.Empty);
    return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
}

static string NormalizePhoneDigits(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return string.Empty;

    var sb = new StringBuilder(value.Length);
    foreach (var ch in value)
    {
        if (ch >= '0' && ch <= '9')
            sb.Append(ch);
        else if (ch >= '\u06F0' && ch <= '\u06F9')
            sb.Append((char)('0' + (ch - '\u06F0')));
        else if (ch >= '\u0660' && ch <= '\u0669')
            sb.Append((char)('0' + (ch - '\u0660')));
    }

    return sb.ToString();
}

app.MapPost("/api/bale/webhook/{secret}", async (
    string secret,
    HttpContext ctx,
    BaleBotRepository baleRepo,
    BaleApiClient bale,
    BaleMemberMenuService baleMenu,
    MemberRepository members,
    ILogger<Program> logger) =>
{
    try
    {
        var expected = await baleRepo.GetWebhookSecretAsync();
        if (string.IsNullOrWhiteSpace(expected) || !BaleSecretEquals(secret, expected))
            return Results.NotFound();

        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
        var root = doc.RootElement;

        if (!root.TryGetProperty("update_id", out var updateIdEl) || !updateIdEl.TryGetInt64(out var updateId))
            return Results.Ok(new { ok = true });

        if (!(root.TryGetProperty("message", out var message) || root.TryGetProperty("edited_message", out message)))
            return Results.Ok(new { ok = true });

        if (!message.TryGetProperty("chat", out var chat) ||
            !chat.TryGetProperty("id", out var chatIdEl) ||
            !chatIdEl.TryGetInt64(out var chatId))
        {
            return Results.Ok(new { ok = true });
        }

        long? fromId = null;
        string? username = null;
        string? firstName = null;
        string? lastName = null;

        if (message.TryGetProperty("from", out var fromEl) && fromEl.ValueKind == JsonValueKind.Object)
        {
            if (fromEl.TryGetProperty("id", out var fidEl) && fidEl.TryGetInt64(out var fid))
                fromId = fid;
            if (fromEl.TryGetProperty("username", out var unEl))
                username = unEl.GetString();
            if (fromEl.TryGetProperty("first_name", out var fnEl))
                firstName = fnEl.GetString();
            if (fromEl.TryGetProperty("last_name", out var lnEl))
                lastName = lnEl.GetString();
        }

        var text = message.TryGetProperty("text", out var textEl) ? (textEl.GetString() ?? string.Empty).Trim() : string.Empty;

        var firstSeen = await baleRepo.TryMarkUpdateProcessedAsync(updateId, chatId, text);
        if (!firstSeen)
            return Results.Ok(new { ok = true });

        await baleRepo.TouchSubscriberAsync(chatId);

        async Task ReplyAsync(string reply)
        {
            try
            {
                await bale.SendMessageAsync(chatId, reply, ctx.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send Bale webhook reply");
            }
        }

        static string MemberLines(IReadOnlyList<BaleSubscriber> subs)
        {
            if (subs.Count == 0)
                return "• -";

            return string.Join("\n", subs.Select(s =>
            {
                var name = string.IsNullOrWhiteSpace(s.MemberFullName) ? "بدون نام" : s.MemberFullName;
                return $"• {name} (کد عضویت: {s.MemberId})";
            }));
        }

        if (message.TryGetProperty("contact", out var contactEl) && contactEl.ValueKind == JsonValueKind.Object)
        {
            long? contactUserId = null;
            if (contactEl.TryGetProperty("user_id", out var cuidEl) && cuidEl.TryGetInt64(out var cuid))
                contactUserId = cuid;

            if (fromId.HasValue && contactUserId.HasValue && contactUserId.Value != fromId.Value)
            {
                await ReplyAsync("لطفاً فقط شماره موبایل خودتان را با دکمه «ارسال شماره موبایل» ارسال کنید.");
                return Results.Ok(new { ok = true });
            }

            var phoneRaw = contactEl.TryGetProperty("phone_number", out var phoneEl) ? phoneEl.GetString() : null;
            var digits = NormalizePhoneDigits(phoneRaw);
            var last10 = digits.Length > 10 ? digits[^10..] : digits;

            if (last10.Length != 10)
            {
                await bale.SendRemoveKeyboardAsync(chatId,
                    "شماره ارسال‌شده معتبر نیست. لطفاً دوباره دکمه «ارسال شماره موبایل» را بزنید.",
                    ctx.RequestAborted);
                return Results.Ok(new { ok = true });
            }

            var matches = await members.GetActiveMemberIdsByMobileLast10Async(last10);
            if (matches.Count == 0)
            {
                await bale.SendRemoveKeyboardAsync(chatId,
                    "شماره موبایل شما در سامانه صندوق یافت نشد.\n" +
                    "اگر شماره شما تغییر کرده، با مدیر صندوق هماهنگ کنید یا از بخش «پروفایل» سایت، لینک اتصال اختصاصی را دریافت کنید.",
                    ctx.RequestAborted);
                return Results.Ok(new { ok = true });
            }

            var matchedIds = matches.Distinct().OrderBy(x => x).ToArray();

            var existing = await baleRepo.GetActiveByChatListAsync(chatId);
            var existingIds = existing.Select(x => x.MemberId).Distinct().OrderBy(x => x).ToArray();

            if (existingIds.SequenceEqual(matchedIds))
            {
                var already = matchedIds.Length == 1
                    ? $"عضو: {existing.FirstOrDefault()?.MemberFullName ?? "-"}"
                    : $"این شماره برای {matchedIds.Length} عضو صندوق ثبت شده است و اطلاع‌رسانی همهٔ آن‌ها به همین گفتگو ارسال می‌شود.";

                await baleMenu.SendMenuAsync(chatId,
                    $"اتصال شما از قبل فعال است.\n{already}",
                    ctx.RequestAborted);
                return Results.Ok(new { ok = true });
            }

            string? singleName = null;
            if (matchedIds.Length == 1)
            {
                var member = await members.GetByIdAsync(matchedIds[0]);
                if (member == null || !member.IsActive)
                {
                    await ReplyAsync("حساب عضویت شما فعال نیست. برای پیگیری با صندوق تماس بگیرید.");
                    return Results.Ok(new { ok = true });
                }

                singleName = member.FullName;
            }

            await baleRepo.UpsertPhoneSubscribersAsync(matchedIds, chatId, fromId, username, firstName, lastName);

            var linked = await baleRepo.GetActiveByChatListAsync(chatId);
            var linkedLines = MemberLines(linked);

            var success = matchedIds.Length == 1
                ? $"عضو محترم {singleName}\n" +
                  "اتصال شما به سامانه اطلاع‌رسانی صندوق با موفقیت انجام شد.\n" +
                  "از این پس اطلاع‌رسانی‌های صندوق را در بله دریافت می‌کنید.\n" +
                  $"این شماره موبایل برای عضو زیر فعال شده است:\n{linkedLines}"
                : "اتصال شما به سامانه اطلاع‌رسانی صندوق با موفقیت انجام شد.\n" +
                  $"این شماره موبایل برای {linked.Count} عضو صندوق فعال شده است:\n{linkedLines}\n" +
                  "از این پس اطلاع‌رسانی هر یک از این اعضا به همین گفتگو ارسال می‌شود.";

            success += "\n\nاز منوی پایین می‌توانید اعضای خانوار، موجودی و وضعیت وام را ببینید.";

            await baleMenu.SendMenuAsync(chatId, success, ctx.RequestAborted);

            return Results.Ok(new { ok = true });
        }

        if (string.IsNullOrWhiteSpace(text))
            return Results.Ok(new { ok = true });

        if (await baleMenu.TryHandleAsync(chatId, text, ctx.RequestAborted))
            return Results.Ok(new { ok = true });

        if (!text.StartsWith('/'))
        {
            var linkedForHint = await baleRepo.GetActiveByChatListAsync(chatId);
            if (linkedForHint.Count > 0)
            {
                await baleMenu.SendMenuAsync(chatId,
                    "برای دریافت اطلاعات، یکی از گزینه‌های منوی پایین را انتخاب کنید.",
                    ctx.RequestAborted);
            }

            return Results.Ok(new { ok = true });
        }

        var spaceIndex = text.IndexOf(' ');
        var command = (spaceIndex >= 0 ? text[..spaceIndex] : text).Trim();
        var argument = spaceIndex >= 0 ? text[(spaceIndex + 1)..].Trim() : string.Empty;

        var atIndex = command.IndexOf('@');
        if (atIndex > 0)
            command = command[..atIndex];

        command = command.ToLowerInvariant();

        if (command == "/start")
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                var memberId = await baleRepo.ConsumeLinkTokenAsync(argument, chatId);
                if (!memberId.HasValue)
                {
                    var prompt = await bale.SendContactRequestAsync(chatId,
                        "لینک اتصال معتبر نیست یا منقضی شده است.\n" +
                        "می‌توانید شماره موبایل ثبت‌شده در صندوق را با دکمه زیر ارسال کنید تا حساب شما متصل شود.",
                        ctx.RequestAborted);

                    if (!prompt.Ok)
                        await ReplyAsync("لینک اتصال معتبر نیست یا منقضی شده است. لطفاً از بخش پروفایل سایت، لینک جدید دریافت کنید.");

                    return Results.Ok(new { ok = true });
                }

                var member = await members.GetByIdAsync(memberId.Value);
                if (member == null || !member.IsActive)
                {
                    await ReplyAsync("حساب عضویت شما فعال نیست. برای پیگیری با صندوق تماس بگیرید.");
                    return Results.Ok(new { ok = true });
                }

                await baleRepo.UpsertSubscriberAsync(member.MemberId, chatId, fromId, username, firstName, lastName);

                var linked = await baleRepo.GetActiveByChatListAsync(chatId);

                await baleMenu.SendMenuAsync(chatId,
                    $"عضو محترم {member.FullName}\n" +
                    "اتصال شما به سامانه اطلاع‌رسانی صندوق با موفقیت انجام شد.\n" +
                    "از این پس اطلاع‌رسانی‌های صندوق را در بله دریافت می‌کنید.\n" +
                    $"این شماره موبایل برای اعضای زیر فعال شده است:\n{MemberLines(linked)}\n\n" +
                    "از منوی پایین می‌توانید اعضای خانوار، موجودی و وضعیت وام را ببینید.",
                    ctx.RequestAborted);

                return Results.Ok(new { ok = true });
            }

            var activeLinks = await baleRepo.GetActiveByChatListAsync(chatId);
            if (activeLinks.Count > 0)
            {
                var already = activeLinks.Count == 1
                    ? "اتصال شما از قبل فعال است."
                    : $"اتصال شما از قبل فعال است ({activeLinks.Count} عضو).";

                await baleMenu.SendMenuAsync(chatId,
                    already + "\nاز منوی پایین می‌توانید اعضای خانوار، موجودی و وضعیت وام را ببینید.",
                    ctx.RequestAborted);
            }
            else
            {
                var prompt = await bale.SendContactRequestAsync(chatId,
                    "برای اتصال حساب، شماره موبایل ثبت‌شده در صندوق را با دکمه «ارسال شماره موبایل» بفرستید.\n" +
                    "یا از بخش «پروفایل» در سایت صندوق، لینک اتصال مخصوص خود را باز کنید.",
                    ctx.RequestAborted);

                if (!prompt.Ok)
                    await ReplyAsync("برای اتصال، از بخش «پروفایل» در سایت صندوق، لینک اتصال مخصوص خود را دریافت کرده و روی آن بزنید.");
            }

            return Results.Ok(new { ok = true });
        }

        if (command == "/stop")
        {
            await bale.SendRemoveKeyboardAsync(chatId,
                "اتصال به بله در این سامانه اجباری است و از طریق ربات قابل قطع نیست.\n" +
                "در صورت نیاز به تغییر یا قطع اتصال، با مدیر صندوق تماس بگیرید.",
                ctx.RequestAborted);
            return Results.Ok(new { ok = true });
        }

        if (command == "/status")
        {
            var subscribers = await baleRepo.GetActiveByChatListAsync(chatId);
            if (subscribers.Count == 0)
            {
                var prompt = await bale.SendContactRequestAsync(chatId,
                    "وضعیت: متصل نیستید.\n" +
                    "برای اتصال، شماره موبایل ثبت‌شده در صندوق را با دکمه زیر ارسال کنید یا از بخش پروفایل سایت لینک بگیرید.",
                    ctx.RequestAborted);

                if (!prompt.Ok)
                    await ReplyAsync("وضعیت: متصل نیستید.\nبرای اتصال از بخش پروفایل سایت لینک دریافت کنید.");

                return Results.Ok(new { ok = true });
            }

            var lines = new List<string>
            {
                "وضعیت: متصل",
                $"تعداد اعضای متصل به این گفتگو: {subscribers.Count}"
            };

            foreach (var s in subscribers.Take(10))
            {
                var name = string.IsNullOrWhiteSpace(s.MemberFullName) ? "-" : s.MemberFullName;
                lines.Add($"• {name} (کد عضویت: {s.MemberId})");
            }

            if (subscribers.Count > 10)
                lines.Add("...");

            lines.Add("اتصال بله اجباری است و از طریق ربات قطع نمی‌شود.");
            lines.Add("از منوی پایین می‌توانید اعضای خانوار، موجودی و وضعیت وام را ببینید.");
            await baleMenu.SendMenuAsync(chatId, string.Join("\n", lines), ctx.RequestAborted);
            return Results.Ok(new { ok = true });
        }

        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Bale webhook processing failed");
        return Results.Ok(new { ok = true });
    }
}).AllowAnonymous();

app.MapPost("/api/auth/change-password", async (
    HttpContext ctx,
    AuthRepository repo,
    CryptoService crypto,
    PasswordHashService hasher,
    ChangePasswordRequest req) =>
{
    string NormalizeDigits(string s)
    {
        s ??= string.Empty;
        return s
            .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
            .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
            .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
            .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
    }

    static string NormalizePassword(string s)
    {
        s ??= string.Empty;
        s = s.Normalize(NormalizationForm.FormKC);

        // Remove common invisible/formatting characters that keyboards may insert
        // (ZWNJ, ZWJ, BOM, LRM/RLM, bidi overrides/isolates)
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\u200C' || ch == '\u200D' || ch == '\uFEFF' || ch == '\u200E' || ch == '\u200F')
                continue;
            if (ch >= '\u202A' && ch <= '\u202E')
                continue;
            if (ch >= '\u2066' && ch <= '\u2069')
                continue;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    var userIdClaim = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (!long.TryParse(userIdClaim, out var userId) || userId <= 0)
        return Results.Unauthorized();

    var currentPassword = NormalizeDigits(NormalizePassword(req.CurrentPassword ?? string.Empty));
    var newPassword = NormalizeDigits(NormalizePassword(req.NewPassword ?? string.Empty));

    if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        return Results.BadRequest("رمز فعلی و رمز جدید را وارد کنید");

    var user = await repo.GetUserByIdAsync(userId);
    if (user == null)
        return Results.Unauthorized();

    if (!user.IsActive)
        return Results.BadRequest("کاربر غیرفعال است");

    if (string.IsNullOrWhiteSpace(user.PasswordEnc))
        return Results.BadRequest("رمز فعلی صحیح نیست");

    var encBefore = user.PasswordEnc ?? string.Empty;
    var kindBefore = encBefore.StartsWith("PBKDF2$", StringComparison.Ordinal) ? "PBKDF2" : "PLAIN_OR_LEGACY";

    try { Log.Information("ChangePassword debug: userId={UserId} beforeKind={Kind} beforeLen={Len}", user.UserId, kindBefore, encBefore.Length); } catch { }

    var ok = false;
    if (hasher.CanVerify(user.PasswordEnc))
    {
        ok = hasher.Verify(user.PasswordEnc, currentPassword);
    }
    else
    {
        if (string.Equals(user.PasswordEnc, currentPassword, StringComparison.Ordinal))
        {
            ok = true;
        }
        else
        {
            try
            {
                var stored = crypto.Unprotect(user.PasswordEnc);
                ok = string.Equals(stored, currentPassword, StringComparison.Ordinal);
            }
            catch
            {
                ok = false;
            }
        }
    }

    if (!ok)
        return Results.BadRequest("رمز فعلی صحیح نیست");

    // Simplified storage as requested: store plain and view-encrypted copy.
    await repo.UpdateUserPasswordAsync(user.UserId, newPassword, crypto.Protect(newPassword));

    // Validate round-trip: ensure what is stored equals immediately.
    var after = await repo.GetUserByIdAsync(user.UserId);
    if (after == null || !string.Equals(after.PasswordEnc ?? string.Empty, newPassword, StringComparison.Ordinal))
    {
        try { Log.Error("ChangePassword failed: stored password mismatch for userId {UserId}", user.UserId); } catch { }
        return Results.Problem("رمز ذخیره نشد یا با خطا ذخیره شد. لطفاً دوباره تلاش کنید.");
    }

    var encAfter = after.PasswordEnc ?? string.Empty;
    var kindAfter = encAfter.StartsWith("PBKDF2$", StringComparison.Ordinal) ? "PBKDF2" : "PLAIN_OR_LEGACY";
    try { Log.Information("ChangePassword debug: userId={UserId} afterKind={Kind} afterLen={Len}", user.UserId, kindAfter, encAfter.Length); } catch { }

    try
    {
        return Results.Ok(new
        {
            ok = true,
            debug = new
            {
                userId = user.UserId,
                beforeKind = kindBefore,
                beforeLen = encBefore.Length,
                afterKind = kindAfter,
                afterLen = encAfter.Length
            }
        });
    }
    catch { }

    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.MapGet("/api/reports/loans/excel", async (
    string? asOf,
    string? filter,
    string? sort,
    string? sortDir,
    LoanReportsRepository repo) =>
{
    try
    {
        var asOfText = string.IsNullOrWhiteSpace(asOf)
            ? DateTime.Now.ToPersianDate()
            : asOf!;

        var asOfDate = MiniBank.Web.Components.Extensions.DateTimeExtensions.FromPersianDate(asOfText);
        var rowsAll = await repo.GetLoansSummaryReportAsync(asOfDate);
        var filterKey = string.IsNullOrWhiteSpace(filter) ? "all" : filter!;
        var rowsFiltered = ApplyLoansFilter(rowsAll, filterKey);
        var rows = ApplyLoansSort(rowsFiltered, sort, sortDir);

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.AddWorksheet("گزارش وام ها");

        ws.RightToLeft = true;
        ws.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        ws.Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center;
        ws.Style.Alignment.WrapText = true;

        var filterTitle = LoansFilterTitle(filterKey);
        var reportTitle = $"تا تاریخ {asOfText} | {filterTitle}";
        ws.Cell(1, 1).Value = "صندوق همیاری امام علی (ع)";
        ws.Range(1, 1, 1, 12).Merge();
        ws.Range(1, 1, 1, 12).Style.Font.Bold = true;
        ws.Range(1, 1, 1, 12).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        ws.Range(1, 1, 1, 12).Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center;

        // Title row (split into two merged blocks to highlight filter in red)
        ws.Range(2, 1, 2, 9).Merge();
        ws.Range(2, 10, 2, 12).Merge();
        ws.Range(2, 1, 2, 12).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        ws.Range(2, 1, 2, 12).Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center;
        ws.Cell(2, 1).Value = $"تا تاریخ {asOfText} |";
        ws.Cell(2, 1).Style.Font.Bold = true;
        ws.Cell(2, 10).Value = filterTitle;
        ws.Cell(2, 10).Style.Font.Bold = true;
        ws.Cell(2, 10).Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#b91c1c");

        // Header
        ws.Cell(3, 1).Value = "ردیف";
        ws.Cell(3, 2).Value = "شماره وام";
        ws.Cell(3, 3).Value = "وام گیرنده";
        ws.Cell(3, 4).Value = "مبلغ وام (اصل + سود)";
        ws.Cell(3, 5).Value = "مانده";
        ws.Cell(3, 6).Value = "اقساط";
        ws.Cell(3, 7).Value = "وصول شده";
        ws.Cell(3, 8).Value = "تاریخ پرداخت";
        ws.Cell(3, 9).Value = "تعداد معوق";
        ws.Cell(3, 10).Value = "مبلغ معوق";
        ws.Cell(3, 11).Value = "سود قسط جاری";
        ws.Cell(3, 12).Value = "تاریخ معوق";

        var header = ws.Range(3, 1, 3, 12);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#ffeb3b");
        header.Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#b91c1c");
        header.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center;
        header.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
        header.Style.Border.InsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;

        // Data
        var rowIndex = 4;
        var i = 0;
        foreach (var x in rows)
        {
            i++;
            ws.Cell(rowIndex, 1).Value = i;
            ws.Cell(rowIndex, 2).Value = x.LoanId;
            ws.Cell(rowIndex, 3).Value = x.MemberFullName;
            ws.Cell(rowIndex, 4).Value = x.TotalLoanAmount;
            ws.Cell(rowIndex, 5).Value = x.RemainingTotal;
            ws.Cell(rowIndex, 6).Value = x.TotalInstallments;
            ws.Cell(rowIndex, 7).Value = x.PaidInstallments;
            ws.Cell(rowIndex, 8).Value = x.LastPaidInstallmentDueDate.HasValue ? x.LastPaidInstallmentDueDate.Value.ToPersianDate() : "-";
            ws.Cell(rowIndex, 9).Value = x.OverdueInstallments;
            ws.Cell(rowIndex, 10).Value = x.OverdueAmountNoPenalty;
            ws.Cell(rowIndex, 11).Value = $"{x.CurrentInstallmentInterest:N0}\nجمع: {x.CurrentAndOverdueInstallmentsInterest:N0}";
            ws.Cell(rowIndex, 12).Value = string.IsNullOrWhiteSpace(x.OverdueDueDatesText)
                ? "-"
                : string.Join("\n", x.OverdueDueDatesText
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => TryParseOverdueDateForExport(s, out var dt) ? dt.ToPersianDate() : s));

            var dataRange = ws.Range(rowIndex, 1, rowIndex, 12);
            dataRange.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            dataRange.Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center;
            dataRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            dataRange.Style.Alignment.WrapText = true;

            if (x.Status == (byte)MiniBank.Web.Models.LoanStatus.Settled)
                dataRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#cfe9c8");
            else if (x.OverdueInstallments > 0)
                dataRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#fee2e2");
            else if (x.PaidInstallments > 0)
                dataRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#b9dbf5");

            rowIndex++;
        }

        // Numeric formatting for money columns
        ws.Columns(4, 5).Style.NumberFormat.Format = "#,##0";
        ws.Columns(10, 11).Style.NumberFormat.Format = "#,##0";

        ws.Columns().AdjustToContents();
        ws.Rows().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var bytes = ms.ToArray();

        var fileName = $"LoansReport_{asOfText.Replace('/', '-')}.xlsx";
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
    catch (Exception ex)
    {
        try { Serilog.Log.Error(ex, "loans report excel failed"); } catch { }
        return Results.Problem("خطا در تولید گزارش Excel وام‌ها");
    }
}).RequireAuthorization();

// Logout endpoint (force reload after signout)
app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    ctx.Response.Redirect("/login");
}).RequireAuthorization();

// Trial balance API used by /admin/reports/trial-balance page
app.MapGet("/api/reports/trial-balance", async (AccountingReportRepository repo) =>
{
    try
    {
        var rows = await repo.GetTrialBalanceAsync();
        return Results.Ok(rows);
    }
    catch (Exception ex)
    {
        // Log and return empty list to keep UI deserialization simple
        try { Serilog.Log.Error(ex, "trial-balance failed"); } catch {}
        return Results.Ok(Array.Empty<MiniBank.Web.Models.TrialBalanceRow>());
    }
});

// Balance sheet API used by /admin/reports/balance-sheet page
app.MapGet("/api/reports/balance-sheet", async (DateTime? asOf, AccountingReportRepository repo) =>
{
    try
    {
        var asOfDate = asOf ?? DateTime.UtcNow;
        var snapshot = await repo.GetBalanceSheetAsOfAsync(asOfDate);
        return Results.Ok(new { asOf = asOfDate, snapshot });
    }
    catch (Exception ex)
    {
        try { Serilog.Log.Error(ex, "balance-sheet failed"); } catch {}
        return Results.Problem("خطا در تولید ترازنامه");
    }
});

app.MapGet("/api/reports/loans/pdf", async (
    string? asOf,
    string? filter,
    string? sort,
    string? sortDir,
    LoanReportsRepository repo,
    IWebHostEnvironment env) =>
{
    try
    {
        var asOfText = string.IsNullOrWhiteSpace(asOf)
            ? DateTime.Now.ToPersianDate()
            : asOf!;

        var asOfDate = MiniBank.Web.Components.Extensions.DateTimeExtensions.FromPersianDate(asOfText);
        var rowsAll = await repo.GetLoansSummaryReportAsync(asOfDate);
        var filterKey = string.IsNullOrWhiteSpace(filter) ? "all" : filter!;
        var rowsFiltered = ApplyLoansFilter(rowsAll, filterKey);
        var rows = ApplyLoansSort(rowsFiltered, sort, sortDir);

        var doc = new MiniBank.Web.Reports.LoansInstallmentsReport(rows, $"تا تاریخ {asOfText} | {LoansFilterTitle(filterKey)}", env);
        using var ms = new MemoryStream();
        QuestPDF.Settings.License = LicenseType.Community;
        doc.GeneratePdf(ms);
        var bytes = ms.ToArray();

        var fileName = $"LoansReport_{asOfText.Replace('/', '-')}.pdf";
        return Results.File(bytes, "application/pdf", fileName);
    }
    catch (Exception ex)
    {
        try { Serilog.Log.Error(ex, "loans report pdf failed"); } catch { }
        return Results.Problem("خطا در تولید گزارش PDF وام‌ها");
    }
}).RequireAuthorization();

static IReadOnlyList<MiniBank.Web.Infrastructure.Repositories.LoanReportsRepository.LoanSummaryReportRow> ApplyLoansFilter(
    IReadOnlyList<MiniBank.Web.Infrastructure.Repositories.LoanReportsRepository.LoanSummaryReportRow> rows,
    string filterKey)
{
    if (rows.Count == 0)
        return rows;

    if (string.Equals(filterKey, "overdue", StringComparison.Ordinal))
        return rows.Where(x => x.OverdueInstallments > 0).ToList();

    if (string.Equals(filterKey, "settled", StringComparison.Ordinal))
        return rows.Where(x => x.Status == (byte)MiniBank.Web.Models.LoanStatus.Settled).ToList();

    if (string.Equals(filterKey, "inprogress", StringComparison.Ordinal))
        return rows.Where(x => x.Status == (byte)MiniBank.Web.Models.LoanStatus.Active || x.Status == (byte)MiniBank.Web.Models.LoanStatus.Disbursed).ToList();

    return rows;
}

static string LoansFilterTitle(string key)
{
    if (string.Equals(key, "overdue", StringComparison.Ordinal)) return "معوق";
    if (string.Equals(key, "settled", StringComparison.Ordinal)) return "تسویه شده ها";
    if (string.Equals(key, "inprogress", StringComparison.Ordinal)) return "در جریان";
    return "همه";
}

static IReadOnlyList<MiniBank.Web.Infrastructure.Repositories.LoanReportsRepository.LoanSummaryReportRow> ApplyLoansSort(
    IReadOnlyList<MiniBank.Web.Infrastructure.Repositories.LoanReportsRepository.LoanSummaryReportRow> rows,
    string? sort,
    string? sortDir)
{
    if (rows.Count == 0)
        return rows;

    var key = (sort ?? string.Empty).Trim();
    var asc = string.Equals((sortDir ?? string.Empty).Trim(), "asc", StringComparison.OrdinalIgnoreCase);

    IEnumerable<MiniBank.Web.Infrastructure.Repositories.LoanReportsRepository.LoanSummaryReportRow> q = rows;
    q = key switch
    {
        "member" => q.OrderBy(x => x.MemberFullName),
        "totalLoanAmount" => q.OrderBy(x => x.TotalLoanAmount),
        "remainingTotal" => q.OrderBy(x => x.RemainingTotal),
        "totalInstallments" => q.OrderBy(x => x.TotalInstallments),
        "paidInstallments" => q.OrderBy(x => x.PaidInstallments),
        "lastPaidInstallmentDate" => q.OrderBy(x => x.LastPaidInstallmentDueDate ?? DateTime.MinValue),
        "overdueInstallments" => q.OrderBy(x => x.OverdueInstallments),
        "overdueAmount" => q.OrderBy(x => x.OverdueAmountNoPenalty),
        "overdueDueDate" => q.OrderBy(x => GetMinOverdueDateForExport(x.OverdueDueDatesText) ?? DateTime.MaxValue),
        _ => q.OrderBy(x => x.LoanId),
    };

    if (!asc)
        q = q.Reverse();

    return q.ToList();
}

static DateTime? GetMinOverdueDateForExport(string? text)
{
    if (string.IsNullOrWhiteSpace(text))
        return null;

    var lines = text
        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .ToList();

    DateTime? min = null;
    foreach (var l in lines)
    {
        if (TryParseOverdueDateForExport(l, out var dt))
            min = min.HasValue ? (dt < min.Value ? dt : min.Value) : dt;
    }

    return min;
}

static bool TryParseOverdueDateForExport(string input, out DateTime dt)
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

// Legacy transaction APIs removed - using double-entry accounting system instead

app.MapPost("/api/tx/transactions", async (Transaction model, TransactionRepository repo, AccountingPostingService posting, TransactionAuditRepository audits) =>
{
    if (model.OccurredAt == default)
        model.OccurredAt = DateTime.UtcNow;
    if (string.IsNullOrWhiteSpace(model.CreatedBy))
        model.CreatedBy = "system";
    if (model.Status == 0)
        model.Status = 1; // approved by default for minimal flow

    var id = await repo.CreateAsync(model);
    // Audit: Created
    try { await audits.LogAsync(id, "Created", null, model.CreatedBy); } catch { }
    // Generate balanced journal based on posting template (if configured)
    await posting.PostTransactionAsync(id);
    return Results.Created($"/api/tx/transactions/{id}", new { id });
});

// Preview journal lines for a prospective transaction by type and amounts
app.MapPost("/api/accounting/preview", async (PreviewRequest req, AccountingPostingService posting) =>
{
    var lines = await posting.PreviewLinesAsync(
        req.TypeId,
        req.AmountTotal,
        req.Principal,
        req.Interest,
        req.Fee,
        req.Tax,
        req.MemberId);
    return Results.Ok(lines);
});

// Transaction audits API
app.MapGet("/api/tx/{id:long}/audits", async (long id, TransactionAuditRepository repo) =>
{
    var items = await repo.GetByTransactionAsync(id);
    return Results.Ok(items.Select(x => new { CreatedAt = x.CreatedAt, Stage = x.Stage, Message = x.Message }));
});

// Household receipts APIs
app.MapGet("/api/households/active", async (IHouseholdRepository repo) =>
{
    var list = await repo.GetActiveHouseholdsAsync();
    return Results.Ok(list);
});

// Get pending receipts (unallocated or partially allocated)
app.MapGet("/api/household-receipts/pending", async (int? limit, HouseholdReceiptRepository repo) =>
{
    try
    {
        var pendingReceipts = await repo.GetPendingSummariesAsync(limit ?? 10);
        return Results.Ok(pendingReceipts);
    }
    catch (Exception ex)
    {
        Serilog.Log.Error(ex, "Failed to get pending receipts");
        return Results.Problem("خطا در دریافت رسیدهای در انتظار");
    }
});

app.MapGet("/api/members", async (long? householdId, bool activeOnly, MemberRepository memberRepo) =>
{
    var list = await memberRepo.GetAllAsync(activeOnly: activeOnly, householdId: householdId, limit: 500);
    return Results.Ok(list);
});

app.MapPost("/api/receipts/household", async (HouseholdReceipt model, HouseholdReceiptRepository repo) =>
{
    if (model.OccurredAt == default) model.OccurredAt = DateTime.UtcNow;
    if (string.IsNullOrWhiteSpace(model.CreatedBy)) model.CreatedBy = "system";
    // New flow: step 1 creates receipt as Pending (0)
    var id = await repo.CreateAsync(model);
    var created = await repo.GetByIdAsync(id);
    return Results.Created($"/api/receipts/household/{id}", created);
});

app.MapDelete("/api/receipts/household/{id:long}", async (long id, bool cascade,
    HouseholdReceiptRepository receiptRepo,
    IDbConnection db) =>
{
    var receipt = await receiptRepo.GetByIdAsync(id);
    if (receipt == null) return Results.NotFound();
    
    if (cascade)
    {
        try
        {
            // Ensure the connection is open before starting a transaction
            if (db.State != ConnectionState.Open)
            {
                if (db is SqlConnection sc)
                    await sc.OpenAsync();
                else
                    db.Open();
            }

            IDbTransaction? transaction = null;
            try
            {
                transaction = db.BeginTransaction();

                // Minimal cascade relying on FK ON DELETE CASCADE where available
                // 1) Delete child transactions by ParentReceiptId (will cascade-delete Journals and JournalLines by FK)
                // 2) Delete receipt allocations
                // 3) Delete the receipt itself
                await db.ExecuteAsync(@"
                    -- Delete transactions (cascades to journals and lines)
                    DELETE FROM dbo.Transactions WHERE ParentReceiptId = @ReceiptId;

                    -- Delete allocations
                    DELETE FROM dbo.HouseholdReceiptAllocations WHERE HouseholdReceiptId = @ReceiptId;

                    -- Delete the receipt
                    DELETE FROM dbo.HouseholdReceipts WHERE HouseholdReceiptId = @ReceiptId;
                ", new { ReceiptId = id }, transaction);

                transaction.Commit();
            }
            catch
            {
                try { transaction?.Rollback(); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during cascade delete: {ex}");
            return Results.Problem($"خطا در حذف وابستگی‌ها: {ex.Message}");
        }
        return Results.NoContent();
    }

    // Non-cascade: only delete the receipt record
    try
    {
        if (db.State != ConnectionState.Open)
        {
            if (db is SqlConnection sc)
                await sc.OpenAsync();
            else
                db.Open();
        }
        await db.ExecuteAsync("DELETE FROM dbo.HouseholdReceipts WHERE HouseholdReceiptId = @ReceiptId", new { ReceiptId = id });
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error deleting receipt: {ex}");
        return Results.Problem($"خطا در حذف رسید: {ex.Message}");
    }
});

// Helper: post an existing transaction to generate journals (useful if a tx was created before posting hook existed)
app.MapPost("/api/tx/{id:long}/post", async (long id, AccountingPostingService posting) =>
{
    await posting.PostTransactionAsync(id);
    return Results.Ok(new { id, posted = true });
});


// Transaction summary report API
app.MapGet("/api/reports/tx/summary", async (
    DateTime? from,
    DateTime? to,
    long? memberId,
    long? householdId,
    TransactionReportRepository repo) =>
{
    var rows = await repo.GetSummaryAsync(from, to, memberId, householdId);
    return Results.Ok(rows);
});

// Posting Templates API for receipt wizard
app.MapGet("/api/posting-templates", async (PostingTemplateRepository repo, byte? categoryCode) =>
{
    var templates = await repo.GetAllAsync(true, categoryCode);
    return Results.Ok(templates.Select(t => new { 
        PostingTemplateId = t.PostingTemplateId, 
        TemplateName = t.TemplateName,
        CategoryCode = t.CategoryCode,
        IsActive = t.IsActive
    }));
});

// Get member deposit accounts
app.MapGet("/api/members/{memberId:long}/deposit-accounts", async (long memberId, IDbConnection db) =>
{
    var sql = @"
SELECT mda.MemberDepositAccountId, mda.AccountNo, mda.DepositTypeId, dt.Title as DepositTypeName, mda.OpenedAt, mda.IsActive
FROM dbo.MemberDepositAccounts mda
JOIN dbo.DepositTypes dt ON dt.DepositTypeId = mda.DepositTypeId
WHERE mda.MemberId = @memberId AND mda.IsActive = 1
ORDER BY mda.OpenedAt DESC";
    
    var accounts = await Dapper.SqlMapper.QueryAsync(db, sql, new { memberId });
    return Results.Ok(accounts);
});

// Get posting template lines
app.MapGet("/api/posting-templates/{templateId:int}/lines", async (int templateId, PostingTemplateRepository repo) =>
{
    var lines = await repo.GetLinesAsync(templateId);
    return Results.Ok(lines);
});

// Preview journal lines for a specific posting template with amounts
app.MapPost("/api/posting-templates/{templateId:int}/preview", async (
    int templateId, 
    PreviewTemplateRequest req, 
    PostingTemplateRepository templateRepo,
    IDbConnection connection) =>
{
    var template = await templateRepo.GetByIdAsync(templateId);
    if (template == null) return Results.NotFound("Template not found");

    var lines = (await templateRepo.GetLinesAsync(templateId)).Select(l => (
        LineNo: l.LineNo,
        AccountId: l.AccountId,
        OperationalAccountId: l.OperationalAccountId,
        Side: l.Side,
        AmountSource: l.AmountSource,
        AllowMemberDim: l.AllowMemberDimension,
        FixedAmount: l.FixedAmount
    )).ToList();

    if (!lines.Any()) return Results.Ok(Array.Empty<object>());

    decimal GetAmount(byte src, decimal? fixedAmount = null) => src switch
    {
        1 => req.AmountTotal,
        2 => req.AmountPrincipal ?? 0m,
        3 => req.AmountInterest ?? 0m,
        4 => req.AmountFee ?? 0m,
        5 => req.AmountTax ?? 0m,
        6 => req.AmountPenalty ?? 0m,
        7 => req.AmountCommission ?? 0m,
        8 => fixedAmount ?? 0m,
        _ => 0m
    };

    // Show template GL accounts exactly as defined in template lines
    var allAccountIds = lines
        .Select(l => l.AccountId)
        .Distinct()
        .ToArray();
    
    // Fetch account codes/names from Accounts table (GL)
    var accounts = (await Dapper.SqlMapper.QueryAsync<(long AccountId, string Code, string Name)>(
        connection,
        "SELECT AccountId, Code, Name FROM dbo.Accounts WHERE AccountId IN @ids",
        new { ids = allAccountIds }))
        .ToDictionary(x => x.AccountId, x => (x.Code, x.Name));

    var result = new List<object>();
    int i = 1;
    foreach (var l in lines)
    {
        var amt = GetAmount(l.AmountSource, l.FixedAmount);
        if (amt == 0) continue;
        
        var accId = l.AccountId;
        var (code, name) = accounts.TryGetValue(accId, out var info) ? info : ("", "");
        var debit = l.Side == 1 ? amt : 0m;
        var credit = l.Side == 2 ? amt : 0m;
        
        result.Add(new {
            LineNo = i++,
            AccountId = accId,
            AccountCode = code,
            AccountName = name,
            MemberId = l.AllowMemberDim ? req.MemberId : null,
            Debit = debit,
            Credit = credit
        });
    }
    return Results.Ok(result);
});

// API endpoint for getting Kol account names by codes
app.MapGet("/api/accounts/search", async (string? codes, byte? level, AccountsRepository repo) =>
{
    if (string.IsNullOrEmpty(codes)) return Results.BadRequest("codes parameter is required");
    
    var codeList = codes.Split(',', StringSplitOptions.RemoveEmptyEntries);
    if (codeList.Length == 0) return Results.BadRequest("No valid codes provided");
    
    var result = new List<object>();
    
    // Search for accounts matching the codes and level
    foreach (var code in codeList)
    {
        var accounts = await repo.SearchAsync(code, level);
        var matchingAccount = accounts.FirstOrDefault(a => a.Code == code);
        if (matchingAccount != null)
        {
            result.Add(new { code = matchingAccount.Code, name = matchingAccount.Name });
        }
    }
    
    return Results.Ok(result);
});

// Temporary endpoint to fix subsidiary constraint
app.MapPost("/api/admin/fix-constraint", async (MiniBank.Web.Infrastructure.ConstraintFixService constraintFix) =>
{
    try
    {
        await constraintFix.FixSubsidiaryConstraintAsync();
        return Results.Ok(new { success = true, message = "Constraint fixed successfully" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to fix constraint: {ex.Message}");
    }
});

// Download a database backup file by run id (restricted to the configured backup directory)
app.MapGet("/api/admin/db-backups/{runId:long}/download", async (long runId, DbBackupService svc) =>
{
    var info = await svc.TryGetDownloadInfoAsync(runId);
    if (!info.ok || string.IsNullOrWhiteSpace(info.fullPath) || string.IsNullOrWhiteSpace(info.downloadName))
        return Results.NotFound(info.error ?? "not found");

    return Results.File(info.fullPath, "application/octet-stream", info.downloadName);
}).RequireAuthorization();

app.Run();

public sealed class LoginRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
