using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models.Auth;
using System.Security.Claims;
using System.Text;

namespace MiniBank.Web.Services.Auth
{
    public class AuthService
    {
        private readonly AuthRepository _repo;
        private readonly CryptoService _crypto;
        private readonly PasswordHashService _hasher;
        private readonly IHttpContextAccessor _http;

        private static string NormalizeDigits(string s)
        {
            s ??= string.Empty;
            return s
                .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
                .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
                .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
                .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
        }

        private static string NormalizePassword(string s)
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

        public AuthService(AuthRepository repo, CryptoService crypto, PasswordHashService hasher, IHttpContextAccessor http)
        {
            _repo = repo;
            _crypto = crypto;
            _hasher = hasher;
            _http = http;
        }

        public async Task<(bool ok, string? error)> SignInAsync(string username, string password)
        {
            username = NormalizeDigits((username ?? string.Empty).Trim());
            password = NormalizeDigits(NormalizePassword(password ?? string.Empty));

            if (string.IsNullOrWhiteSpace(username))
                return (false, "نام کاربری را وارد کنید");

            var user = await _repo.GetUserByUsernameAsync(username);
            if (user == null)
                return (false, "نام کاربری یا رمز عبور اشتباه است");

            if (!user.IsActive)
                return (false, "کاربر غیرفعال است");

            if (string.IsNullOrWhiteSpace(user.PasswordEnc))
                return (false, "نام کاربری یا رمز عبور اشتباه است");

            var raw = NormalizeDigits((password ?? string.Empty).Trim());

            if (_hasher.CanVerify(user.PasswordEnc))
            {
                if (!_hasher.Verify(user.PasswordEnc, password) && !_hasher.Verify(user.PasswordEnc, raw))
                    return (false, "نام کاربری یا رمز عبور اشتباه است");

                try { await _repo.UpdateUserPasswordAsync(user.UserId, password, _crypto.Protect(password)); } catch { }
            }
            else
            {
                var matched = false;

                // legacy plain-text
                if (string.Equals(user.PasswordEnc, password, StringComparison.Ordinal))
                {
                    matched = true;
                }
                else
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(user.PasswordViewEnc))
                        {
                            var view = _crypto.Unprotect(user.PasswordViewEnc);
                            if (string.Equals(NormalizeDigits(NormalizePassword(view)), password, StringComparison.Ordinal))
                                matched = true;
                        }
                    }
                    catch { }

                    // legacy DataProtection protected
                    try
                    {
                        var stored = _crypto.Unprotect(user.PasswordEnc);
                        matched = string.Equals(NormalizeDigits(NormalizePassword(stored)), password, StringComparison.Ordinal);
                    }
                    catch
                    {
                        matched = false;
                    }
                }

                if (!matched)
                    return (false, "نام کاربری یا رمز عبور اشتباه است");

                try { await _repo.UpdateUserPasswordAsync(user.UserId, password, _crypto.Protect(password)); } catch { }
            }

            var ctx = _http.HttpContext;
            if (ctx == null)
                return (false, "خطا در ورود");

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role?.Name ?? string.Empty),
                new Claim("RoleId", user.RoleId.ToString())
            };

            if (user.MemberId.HasValue)
                claims.Add(new Claim("MemberId", user.MemberId.Value.ToString()));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            });

            await _repo.TouchLoginAsync(user.UserId);
            return (true, null);
        }

        public async Task SignOutAsync()
        {
            var ctx = _http.HttpContext;
            if (ctx == null)
                return;

            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        public string EncryptPassword(string plain) => plain ?? string.Empty;

        public string EncryptViewPassword(string plain) => _crypto.Protect(plain);

        public bool TryDecryptViewPassword(string? enc, out string plain)
        {
            plain = string.Empty;
            if (string.IsNullOrWhiteSpace(enc))
                return false;

            try
            {
                plain = _crypto.Unprotect(enc);
                return true;
            }
            catch
            {
                plain = string.Empty;
                return false;
            }
        }

        public string DecryptPassword(string enc)
        {
            enc ??= string.Empty;
            if (_hasher.CanVerify(enc))
                throw new InvalidOperationException("Password hashes cannot be decrypted");

            return _crypto.Unprotect(enc);
        }

        public async Task<(bool ok, string? error, object? debug)> ChangePasswordForCurrentUserAsync(string currentPassword, string newPassword)
        {
            var ctx = _http.HttpContext;
            if (ctx == null)
                return (false, "خطا در تغییر رمز", null);

            var userIdClaim = ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!long.TryParse(userIdClaim, out var userId) || userId <= 0)
                return (false, "دسترسی غیرمجاز", null);

            var cur = NormalizeDigits(NormalizePassword(currentPassword ?? string.Empty));
            var nw = NormalizeDigits(NormalizePassword(newPassword ?? string.Empty));

            if (string.IsNullOrWhiteSpace(cur) || string.IsNullOrWhiteSpace(nw))
                return (false, "رمز فعلی و رمز جدید را وارد کنید", null);

            var user = await _repo.GetUserByIdAsync(userId);
            if (user == null)
                return (false, "دسترسی غیرمجاز", null);

            if (!user.IsActive)
                return (false, "کاربر غیرفعال است", null);

            if (string.IsNullOrWhiteSpace(user.PasswordEnc))
                return (false, "رمز فعلی صحیح نیست", null);

            var encBefore = user.PasswordEnc ?? string.Empty;
            var kindBefore = encBefore.StartsWith("PBKDF2$", StringComparison.Ordinal) ? "PBKDF2" : "PLAIN_OR_LEGACY";

            var ok = false;
            if (_hasher.CanVerify(user.PasswordEnc))
            {
                ok = _hasher.Verify(user.PasswordEnc, cur) || _hasher.Verify(user.PasswordEnc, NormalizeDigits((currentPassword ?? string.Empty).Trim()));
            }
            else
            {
                if (string.Equals(user.PasswordEnc, cur, StringComparison.Ordinal))
                {
                    ok = true;
                }
                else
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(user.PasswordViewEnc))
                        {
                            var view = _crypto.Unprotect(user.PasswordViewEnc);
                            if (string.Equals(NormalizeDigits(NormalizePassword(view)), cur, StringComparison.Ordinal))
                                ok = true;
                        }
                    }
                    catch { }

                    if (!ok)
                    {
                        try
                        {
                            var stored = _crypto.Unprotect(user.PasswordEnc);
                            ok = string.Equals(NormalizeDigits(NormalizePassword(stored)), cur, StringComparison.Ordinal);
                        }
                        catch
                        {
                            ok = false;
                        }
                    }
                }
            }

            if (!ok)
                return (false, "رمز فعلی صحیح نیست", new { userId, beforeKind = kindBefore, beforeLen = encBefore.Length });

            await _repo.UpdateUserPasswordAsync(user.UserId, nw, _crypto.Protect(nw));

            var after = await _repo.GetUserByIdAsync(user.UserId);
            if (after == null || !string.Equals(after.PasswordEnc ?? string.Empty, nw, StringComparison.Ordinal))
                return (false, "رمز ذخیره نشد یا با خطا ذخیره شد. لطفاً دوباره تلاش کنید.", new { userId, beforeKind = kindBefore, beforeLen = encBefore.Length });

            var encAfter = after.PasswordEnc ?? string.Empty;
            var kindAfter = encAfter.StartsWith("PBKDF2$", StringComparison.Ordinal) ? "PBKDF2" : "PLAIN_OR_LEGACY";

            return (true, null, new { userId, beforeKind = kindBefore, beforeLen = encBefore.Length, afterKind = kindAfter, afterLen = encAfter.Length });
        }
    }
}
