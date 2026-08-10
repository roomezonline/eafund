using Microsoft.AspNetCore.Components;

namespace MiniBank.Web.Components
{
    public class NavigationService
    {
        public static List<CurvedNavigationItem> GetDefaultNavigationItems()
        {
            return new List<CurvedNavigationItem>
            {
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-credit-card"),
                    Label = "کارت‌ها",
                    Route = "/admin/cards"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-folder"),
                    Label = "حساب‌ها",
                    Route = "/admin/accounts"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-home"),
                    Label = "تسهیلات",
                    Route = "/admin/dashboard2"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-user"),
                    Label = "پروفایل",
                    Route = "/admin/profile"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-calculator"),
                    Label = "محاسبه‌گر",
                    Route = "/admin/calculator"
                }
            };
        }

        public static List<CurvedNavigationItem> GetBankingNavigationItems()
        {
            return new List<CurvedNavigationItem>
            {
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-wallet"),
                    Label = "کیف پول",
                    Route = "/admin/wallet"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-exchange-alt"),
                    Label = "تراکنش‌ها",
                    Route = "/admin/transactions"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-home"),
                    Label = "خانه",
                    Route = "/admin/dashboard"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-chart-line"),
                    Label = "گزارشات",
                    Route = "/admin/reports"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-cog"),
                    Label = "تنظیمات",
                    Route = "/admin/settings"
                }
            };
        }

        public static List<CurvedNavigationItem> GetAdminNavigationItems()
        {
            return new List<CurvedNavigationItem>
            {
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-users"),
                    Label = "کاربران",
                    Route = "/admin/users"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-database"),
                    Label = "داده‌ها",
                    Route = "/admin/data"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-tachometer-alt"),
                    Label = "داشبورد",
                    Route = "/admin/dashboard"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-bell"),
                    Label = "اعلانات",
                    Route = "/admin/notifications"
                },
                new CurvedNavigationItem
                {
                    Icon = CreateIcon("fa-solid fa-shield-alt"),
                    Label = "امنیت",
                    Route = "/admin/security"
                }
            };
        }

        private static RenderFragment CreateIcon(string iconClass)
        {
            return builder =>
            {
                builder.OpenElement(0, "i");
                builder.AddAttribute(1, "class", iconClass);
                builder.CloseElement();
            };
        }

        public static int GetActiveIndexForRoute(string currentRoute, List<CurvedNavigationItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Route == currentRoute)
                {
                    return i;
                }
            }
            return items.Count / 2; // Default to center item
        }
    }
}
