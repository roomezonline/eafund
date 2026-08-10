using Microsoft.AspNetCore.DataProtection;

namespace MiniBank.Web.Services.Auth
{
    public class CryptoService
    {
        private readonly IDataProtector _protector;

        public CryptoService(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("MiniBank.Auth.PasswordEnc.v1");
        }

        public string Protect(string plain)
        {
            plain ??= string.Empty;
            return _protector.Protect(plain);
        }

        public string Unprotect(string protectedText)
        {
            protectedText ??= string.Empty;
            return _protector.Unprotect(protectedText);
        }
    }
}
