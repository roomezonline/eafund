namespace MiniBank.Web.Components.Extensions
{
    public static class BankCardFormat
    {
        public static string DigitsOnly(string? value)
            => new string((value ?? string.Empty).Where(char.IsDigit).ToArray());

        public static string CardDigits(string? value)
        {
            var digits = DigitsOnly(value);
            return digits.Length > 16 ? digits[..16] : digits;
        }

        public static string FormatCard(string? value)
        {
            var digits = CardDigits(value);
            return JoinGroups(digits);
        }

        public static string ShabaDigits(string? value)
        {
            var digits = DigitsOnly(value);
            return digits.Length > 24 ? digits[^24..] : digits;
        }

        public static string NormalizeShaba(string? value)
        {
            var digits = ShabaDigits(value);
            return digits.Length == 0 ? string.Empty : "IR" + digits;
        }

        public static string FormatShaba(string? value)
        {
            var digits = ShabaDigits(value);
            return digits.Length == 0 ? string.Empty : "IR " + JoinGroups(digits);
        }

        private static string JoinGroups(string digits)
        {
            if (digits.Length == 0)
                return string.Empty;

            var groups = new List<string>();
            for (var i = 0; i < digits.Length; i += 4)
                groups.Add(digits.Substring(i, Math.Min(4, digits.Length - i)));

            return string.Join(" ", groups);
        }
    }
}
