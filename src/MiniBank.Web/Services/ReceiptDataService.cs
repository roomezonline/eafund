using System.Collections.Generic;
using System.Linq;
using MiniBank.Web.Components.DesignSystem;

namespace MiniBank.Web.Services
{
    public static class ReceiptDataService
    {
        // Document Types
        public static readonly Dictionary<byte, string> DocumentTypeNames = new()
        {
            [1] = "اعضایی",
            [2] = "عمومی"
        };

        // Transaction Types
        public static readonly Dictionary<string, string> TransactionTypeNames = new()
        {
            ["1-1"] = "واریز ",
            ["1-2"] = "پرداخت وام",
            ["1-3"] = "جایزه خوش حسابی",
            ["1-4"] = "سهام جایزه",
            ["1-5"] = "برداشت جریمه تاخیر قسط وام",
            ["1-6"] = "برداشت قسط از وام گیرنده",
            ["1-7"] = "برداشت قسط از ضامن",
            ["1-8"] = "انتقال درون خانواری",
            ["1-9"] = "انتقال بلاعوض",
            ["1-10"] = "سود سپرده اعضاء",
            ["1-11"] = "انتقال از وارث به وراث",
            ["1-12"] = "برداشت کارمزد وام",
            ["1-13"] = "استرداد ",
            ["1-14"] = "سند افتتاحیه",
            ["2-1"] = "نقل وانتقالات بانک وبورس",
            ["2-2"] = "هزینه های عمومی صندوق",
            ["2-3"] = "درآمدهای بورسی",
            ["2-4"] = "سایر اسناد عمومی",
            ["2-5"] = "سند افتتاحیه"
        };

        // Channel Types
        public static readonly Dictionary<byte, string> ChannelNames = new()
        {
            [1] = "بانک",
            [2] = "بورس",
            [3] = "تعهدی"
        };

        // Get methods
        public static string GetDocumentTypeName(byte documentType)
        {
            return DocumentTypeNames.TryGetValue(documentType, out var name) 
                ? name 
                : $"نوع {documentType}";
        }

        public static string GetTransactionTypeName(string? transactionTypeCode)
        {
            if (string.IsNullOrWhiteSpace(transactionTypeCode))
                return "—";

            return TransactionTypeNames.TryGetValue(transactionTypeCode.Trim(), out var name) 
                ? name 
                : $"کد {transactionTypeCode}";
        }

        public static string GetChannelName(byte channel)
        {
            return ChannelNames.TryGetValue(channel, out var name) 
                ? name 
                : $"کانال {channel}";
        }

        // Get dropdown items for UI - returning proper DsDropdownItem types
        public static List<DsDropdown.DsDropdownItem> GetDocumentTypeItems()
        {
            return new List<DsDropdown.DsDropdownItem>
            {
                new DsDropdown.DsDropdownItem { Value = "", Text = "لطفاً انتخاب کنید" },
                new DsDropdown.DsDropdownItem { Value = "1", Text = DocumentTypeNames[1] },
                new DsDropdown.DsDropdownItem { Value = "2", Text = DocumentTypeNames[2] }
            };
        }

        public static List<DsDropdown.DsDropdownItem> GetChannelItems()
        {
            return new List<DsDropdown.DsDropdownItem>
            {
                new DsDropdown.DsDropdownItem { Value = "", Text = "لطفاً انتخاب کنید" },
                new DsDropdown.DsDropdownItem { Value = "1", Text = ChannelNames[1] },
                new DsDropdown.DsDropdownItem { Value = "2", Text = ChannelNames[2] },
                new DsDropdown.DsDropdownItem { Value = "3", Text = ChannelNames[3] }
            };
        }

        public static List<DsDropdown.DsDropdownItem> GetTransactionTypeItems(string documentTypeCode)
        {
            var items = new List<DsDropdown.DsDropdownItem>
            {
                new DsDropdown.DsDropdownItem { Value = "", Text = "لطفاً نوع تراکنش را انتخاب کنید" }
            };

            if (string.IsNullOrWhiteSpace(documentTypeCode))
                return items;

            var prefix = documentTypeCode.Trim() + "-";
            var ordered = TransactionTypeNames
                .Where(kvp => kvp.Key.StartsWith(prefix))
                .OrderBy(kvp =>
                {
                    var parts = kvp.Key.Split('-');
                    return (parts.Length == 2 && int.TryParse(parts[1], out var n)) ? n : int.MaxValue;
                })
                .ThenBy(kvp => kvp.Key);

            items.AddRange(ordered.Select(kvp => new DsDropdown.DsDropdownItem { Value = kvp.Key, Text = kvp.Value }));

            return items;
        }
    }
}
