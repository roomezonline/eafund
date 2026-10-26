using MiniBank.Web.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using System.Text.RegularExpressions;

namespace MiniBank.Web.Services.Auth
{
    public class AuthPageSeeder
    {
        private readonly AuthRepository _repo;
        private readonly IWebHostEnvironment _env;

        public AuthPageSeeder(AuthRepository repo, IWebHostEnvironment env)
        {
            _repo = repo;
            _env = env;
        }

        public async Task<int> EnsurePagesSeededFromRoutesAsync()
        {
            var added = 0;

            try
            {
                added += await EnsurePagesSeededFromRazorFilesAsync();
            }
            catch
            {
                // ignore
            }

            var asm = typeof(Program).Assembly;
            var pageTypes = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Select(t => new { Type = t, Routes = t.GetCustomAttributes<RouteAttribute>(inherit: false).ToList() })
                .Where(x => x.Routes.Count > 0)
                .ToList();

            foreach (var x in pageTypes)
            {
                foreach (var r in x.Routes)
                {
                    var path = (r.Template ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    if (!path.StartsWith('/'))
                        path = "/" + path;

                    var key = path;
                    var title = path;

                    try
                    {
                        var applied = await UpsertIfBetterTitleAsync(key, title, path, isActive: true);
                        if (applied) added++;
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            // Ensure required app routes exist
            try { await _repo.UpsertPageAsync("/", "/", "/", true); } catch { }
            try { await _repo.UpsertPageAsync("/admin/auth/users", "مدیریت کاربران", "/admin/auth/users", true); } catch { }
            try { await _repo.UpsertPageAsync("/admin/auth/roles", "نقش‌ها و دسترسی‌ها", "/admin/auth/roles", true); } catch { }

            return added;
        }

        private async Task<int> EnsurePagesSeededFromRazorFilesAsync()
        {
            var pagesRoot = Path.Combine(_env.ContentRootPath, "Pages");
            if (!Directory.Exists(pagesRoot))
                return 0;

            var files = Directory.EnumerateFiles(pagesRoot, "*.razor", SearchOption.AllDirectories)
                .Where(x => !x.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Where(x => !x.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var added = 0;
            foreach (var file in files)
            {
                string text;
                try { text = await File.ReadAllTextAsync(file); }
                catch { continue; }

                var routes = ExtractRazorRoutes(text);
                if (routes.Count == 0) continue;

                var title = ExtractRazorTitle(text);
                foreach (var r in routes)
                {
                    var path = NormalizePath(r);
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    var key = path;
                    var applied = await UpsertIfBetterTitleAsync(key, title, path, isActive: true);
                    if (applied) added++;
                }
            }

            return added;
        }

        private static List<string> ExtractRazorRoutes(string text)
        {
            // Examples:
            // @page "/admin/loans"
            // @page "/admin/loans/{id:int}"
            // @page "/"
            var routes = new List<string>();
            var rx = new Regex("(?m)^\\s*@page\\s+\"([^\"]+)\"\\s*$");
            foreach (Match m in rx.Matches(text))
            {
                var r = (m.Groups[1].Value ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(r)) routes.Add(r);
            }
            return routes;
        }

        private static string ExtractRazorTitle(string text)
        {
            // Prefer DsPageHeader Title="..." (common across admin pages)
            var rx = new Regex("<\\s*DsPageHeader\\b[^>]*\\bTitle\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            var m = rx.Match(text);
            if (m.Success)
            {
                var t = (m.Groups[1].Value ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }

            // fallback: first h1/h2 text (very conservative)
            var hx = new Regex(@"<h[12][^>]*>\s*([^<]{2,80})\s*</h[12]>", RegexOptions.IgnoreCase);
            var hm = hx.Match(text);
            if (hm.Success)
            {
                var t = (hm.Groups[1].Value ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }

            return string.Empty;
        }

        private static string NormalizePath(string raw)
        {
            var p = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(p)) return string.Empty;
            if (!p.StartsWith('/')) p = "/" + p;
            if (p.Length > 1 && p.EndsWith('/')) p = p.TrimEnd('/');

            // Normalize parameterized route templates to a stable base path so they can be granted as a permission.
            // Example: /admin/reports/tafsili-moeen/{TafsiliAccountId:long} => /admin/reports/tafsili-moeen
            p = Regex.Replace(p, @"/\{[^}]+\}", string.Empty);
            if (string.IsNullOrWhiteSpace(p)) p = "/";
            if (!p.StartsWith('/')) p = "/" + p;
            if (p.Length > 1 && p.EndsWith('/')) p = p.TrimEnd('/');

            return p;
        }

        private static string InferFaTitleFromPath(string path)
        {
            path = NormalizePath(path);

            // common explicit mappings
            return path switch
            {
                "/" => "صفحه اصلی",
                "/login" => "ورود",
                "/forbidden" => "عدم دسترسی",

                "/dashboard" => "داشبورد",
                "/admin/dashboard" => "داشبورد",
                "/admin/dashboard2" => "داشبورد (۲)",

                "/admin/auth/users" => "کاربران سامانه",
                "/admin/auth/roles" => "نقش‌ها و دسترسی‌ها",

                "/admin/members" => "اعضا",
                "/admin/households" => "خانوارها",
                "/admin/bank-accounts" => "حساب‌های بانکی",
                "/admin/bank-cards" => "کارت‌های بانکی",
                "/admin/deposit-types" => "انواع سپرده",
                "/admin/tx-groups" => "گروه‌های تراکنش",

                "/admin/account-types" => "گروه حساب‌ها",
                "/admin/accounting/accounts" => "حساب‌ها",
                "/admin/kol" => "حساب‌های کل",
                "/admin/moeen" => "حساب‌های معین",
                "/admin/subsidiary" => "حساب‌های تفضیلی",
                "/admin/posting-templates" => "الگوهای ثبت حسابداری",

                "/admin/loans" => "وام‌ها",
                "/admin/loan-rates" => "نرخ‌های وام",
                "/admin/payment-transactions" => "تراکنش‌های پرداخت",
                "/admin/deposit-interest-calc" => "محاسبه سود سپرده",
                "/admin/manual-journal-entry" => "ثبت سند دستی",
                "/admin/manual-journal-entry-v2" => "ثبت سند دستی (جدید)",
                "/admin/manual-journal-entry-v2/history" => "تاریخچه اسناد (ثبت سند دستی)",

                "/admin/reports/trial-balance" => "تراز آزمایشی",
                "/admin/reports/balance-sheet" => "ترازنامه",
                "/admin/reports/tx-summary" => "گزارش خلاصه تراکنش‌ها",
                "/admin/reports/transactions" => "گزارش تراکنش‌ها",
                "/admin/reports/member-banking" => "گزارش بانکی اعضا",
                "/admin/reports/financial-statements" => "صورت‌های مالی",
                "/admin/reports/loans" => "گزارش وام‌ها",
                "/admin/reports/whatsapp-shares" => "گزارش سهام ارسالی واتساپ",
                "/admin/reports/tafsili-moeen" => "گزارش معین تفضیلی",
                "/admin/reports/subsidiary-transactions" => "ریز تراکنش‌های تفضیلی",

                "/admin/receipts/household-wizard-v2" => "ثبت رسید خانوار (چند مرحله‌ای)",
                "/admin/sms" => "ارسال پیامک",

                "/me/profile" => "پروفایل عضو",
                "/me/messages" => "پیام‌های من",
                "/me/household" => "خانوار من",
                "/me/loans" => "وام‌های من",
                "/me/transactions" => "تراکنش‌های من",

                _ => path
            };
        }

        private static bool IsPlaceholderTitle(string title)
        {
            title = (title ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title)) return true;
            if (title.StartsWith('/')) return true;
            return false;
        }

        private async Task<bool> UpsertIfBetterTitleAsync(string pageKey, string title, string path, bool isActive)
        {
            pageKey = NormalizePath(pageKey);
            path = NormalizePath(path);

            var desiredTitle = (title ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(desiredTitle) || IsPlaceholderTitle(desiredTitle))
                desiredTitle = InferFaTitleFromPath(path);

            var existing = await _repo.GetPageByKeyAsync(pageKey);
            if (existing != null)
            {
                var existingTitle = (existing.Title ?? string.Empty).Trim();

                // Don't overwrite meaningful Persian titles with placeholders.
                if (!IsPlaceholderTitle(existingTitle) && IsPlaceholderTitle(desiredTitle))
                {
                    await _repo.UpdatePageAsync(existing.PageId, existingTitle, path, isActive);
                    return false;
                }

                // If existing title is meaningful and desired is also meaningful but identical, no-op.
                if (string.Equals(existingTitle, desiredTitle, StringComparison.Ordinal))
                {
                    await _repo.UpdatePageAsync(existing.PageId, existingTitle, path, isActive);
                    return false;
                }

                await _repo.UpdatePageAsync(existing.PageId, desiredTitle, path, isActive);
                return true;
            }

            await _repo.UpsertPageAsync(pageKey, desiredTitle, path, isActive);
            return true;
        }
    }
}
