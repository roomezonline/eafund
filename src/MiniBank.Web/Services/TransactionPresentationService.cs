using System;
using System.Collections.Generic;

namespace MiniBank.Web.Services
{
    public static class TransactionPresentationService
    {
        public enum TransactionCategory
        {
            Unknown = 0,
            UserEntered = 1,
            LoanDisbursement = 2,
            DepositInterest = 3,
            LoanPrepay = 4
        }

        public sealed class PresentationInput
        {
            public long JournalId { get; set; }
            public string Notes { get; set; } = string.Empty;

            public byte? DocumentType { get; set; }
            public string? TransactionType { get; set; }
            public byte? PayChannel { get; set; }
            public string? CounterpartyName { get; set; }
            public string DisplayName { get; set; } = string.Empty;

            public byte Status { get; set; }
            public bool IsDeleted { get; set; }
            public int? SerialNumber { get; set; }
        }

        public sealed class PresentationResult
        {
            public TransactionCategory Category { get; set; }

            public string DocumentTypeName { get; set; } = string.Empty;
            public string TransactionTypeName { get; set; } = string.Empty;
            public string ChannelName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;

            public string BadgeText { get; set; } = string.Empty;
            public string BadgeCssClass { get; set; } = string.Empty;

            public string StatusBadgeText { get; set; } = string.Empty;
            public string StatusBadgeCssClass { get; set; } = string.Empty;
        }

        public static PresentationResult Build(
            PresentationInput input,
            IReadOnlyDictionary<long, string>? memberNameById = null)
        {
            // سند با TransactionType != null یعنی رسید دارد → دستی، داده دیتابیس ملاک است
            // سند بدون TransactionType یعنی سیستمی واقعی → از Notes تشخیص بده
            var category = !string.IsNullOrWhiteSpace(input.TransactionType)
                ? TransactionCategory.UserEntered
                : DetectCategory(input.Notes);

            var result = new PresentationResult
            {
                Category = category,
                DocumentTypeName = input.DocumentType.HasValue ? ReceiptDataService.GetDocumentTypeName(input.DocumentType.Value) : "-",
                TransactionTypeName = !string.IsNullOrWhiteSpace(input.TransactionType) ? ReceiptDataService.GetTransactionTypeName(input.TransactionType) : "-",
                ChannelName = input.PayChannel.HasValue ? ReceiptDataService.GetChannelName(input.PayChannel.Value) : "-",
                DisplayName = string.IsNullOrWhiteSpace(input.DisplayName)
                    ? (string.IsNullOrWhiteSpace(input.CounterpartyName) ? "-" : input.CounterpartyName)
                    : input.DisplayName
            };

            ApplySystemOverrides(input, result, memberNameById);
            ApplyBadge(result);
            ApplyStatusBadge(result, input.Status, input.IsDeleted, input.SerialNumber);
            return result;
        }

        public static TransactionCategory PeekCategory(string? notes)
            => DetectCategory(notes);

        private static TransactionCategory DetectCategory(string? notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
                return TransactionCategory.Unknown;

            var n = notes.Trim();

            if (n.Contains("LoanDisburse", StringComparison.OrdinalIgnoreCase))
                return TransactionCategory.LoanDisbursement;

            if (n.Contains("LoanPrepay", StringComparison.OrdinalIgnoreCase))
                return TransactionCategory.LoanPrepay;

            if (n.Contains("Run:", StringComparison.OrdinalIgnoreCase)
                && n.Contains("واریز سود", StringComparison.OrdinalIgnoreCase))
                return TransactionCategory.DepositInterest;

            return TransactionCategory.UserEntered;
        }

        private static void ApplySystemOverrides(
            PresentationInput input,
            PresentationResult result,
            IReadOnlyDictionary<long, string>? memberNameById)
        {
            var notes = input.Notes ?? string.Empty;

            if (result.Category == TransactionCategory.DepositInterest)
            {
                var mid = TryExtractMemberIdFromNotes(notes);
                if (mid.HasValue && mid.Value > 0)
                {
                    result.DisplayName = (memberNameById != null && memberNameById.TryGetValue(mid.Value, out var mn))
                        ? mn
                        : mid.Value.ToString();
                }

                result.DocumentTypeName = "سیستمی";
                result.TransactionTypeName = "واریز سود";
                return;
            }

            if (result.Category == TransactionCategory.LoanDisbursement)
            {
                var mid = TryExtractMemberIdFromNotes(notes);
                if (mid.HasValue && mid.Value > 0)
                {
                    result.DisplayName = (memberNameById != null && memberNameById.TryGetValue(mid.Value, out var mn))
                        ? mn
                        : mid.Value.ToString();
                }

                result.DocumentTypeName = "سیستمی";
                result.TransactionTypeName = "پرداخت وام";
                result.ChannelName = "بانک";
                return;
            }

            if (result.Category == TransactionCategory.LoanPrepay)
            {
                var mid = TryExtractMemberIdFromNotes(notes);
                if (mid.HasValue && mid.Value > 0)
                {
                    result.DisplayName = (memberNameById != null && memberNameById.TryGetValue(mid.Value, out var mn))
                        ? mn
                        : mid.Value.ToString();
                }

                result.DocumentTypeName = "سیستمی";
                result.TransactionTypeName = "تسویه پیش از موعد";

                if (notes.Contains("Method:cash", StringComparison.OrdinalIgnoreCase))
                    result.ChannelName = "نقدی";
                else if (notes.Contains("Method:member", StringComparison.OrdinalIgnoreCase))
                    result.ChannelName = "برداشت از حساب عضو";

                return;
            }
        }

        private static void ApplyBadge(PresentationResult result)
        {
            switch (result.Category)
            {
                case TransactionCategory.LoanDisbursement:
                    result.BadgeText = "وام";
                    result.BadgeCssClass = "tx-badge tx-badge-loan";
                    break;
                case TransactionCategory.DepositInterest:
                    result.BadgeText = "سود";
                    result.BadgeCssClass = "tx-badge tx-badge-interest";
                    break;
                case TransactionCategory.LoanPrepay:
                    result.BadgeText = "تسویه";
                    result.BadgeCssClass = "tx-badge tx-badge-prepay";
                    break;
                default:
                    result.BadgeText = string.Empty;
                    result.BadgeCssClass = string.Empty;
                    break;
            }
        }

        private static void ApplyStatusBadge(PresentationResult result, byte status, bool isDeleted, int? serialNumber)
        {
            if (isDeleted)
            {
                result.StatusBadgeText = "حذف شده";
                result.StatusBadgeCssClass = "tx-badge tx-badge-deleted";
                return;
            }

            switch (status)
            {
                case 1:
                    result.StatusBadgeText = "موقت";
                    result.StatusBadgeCssClass = "tx-badge tx-badge-temp";
                    break;
                case 2:
                    result.StatusBadgeText = "دائمی";
                    result.StatusBadgeCssClass = "tx-badge tx-badge-perm";
                    break;
                case 3:
                    result.StatusBadgeText = "قطعی";
                    result.StatusBadgeCssClass = "tx-badge tx-badge-final";
                    break;
                default:
                    result.StatusBadgeText = string.Empty;
                    result.StatusBadgeCssClass = string.Empty;
                    break;
            }
        }

        private static long? TryExtractMemberIdFromNotes(string? notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
                return null;

            var idx = notes.IndexOf("Member:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            idx += "Member:".Length;
            while (idx < notes.Length && char.IsWhiteSpace(notes[idx]))
                idx++;

            var start = idx;
            while (idx < notes.Length && char.IsDigit(notes[idx]))
                idx++;

            if (idx <= start)
                return null;

            var s = notes.Substring(start, idx - start);
            return long.TryParse(s, out var id) ? id : null;
        }
    }
}
