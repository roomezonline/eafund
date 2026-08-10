using System;
using System.IO;
using System.Threading.Tasks;
using MiniBank.Web.Models;
using MiniBank.Web.Services;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Helpers;
using Microsoft.AspNetCore.Hosting;

namespace MiniBank.Web.Reports
{
    public class ReportService
    {
        private readonly DepositTypeService _depositTypeService;
        private readonly IWebHostEnvironment _env;
        private static bool _fontsRegistered = false;

        public ReportService(DepositTypeService depositTypeService, IWebHostEnvironment env)
        {
            _depositTypeService = depositTypeService;
            _env = env;
        }

        public async Task<byte[]?> GenerateDepositRatesReportAsync(long depositTypeId)
        {
            EnsureFontsRegistered();
            var depositType = await _depositTypeService.GetByIdAsync(depositTypeId);
            if (depositType == null) return null;

            var rates = await _depositTypeService.GetRatesAsync(depositTypeId);

            // Ensure QuestPDF is using reasonable settings
            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.DocumentLayoutExceptionThreshold = 50;

            var report = new DepositRatesReport(depositType, rates);
            using var ms = new MemoryStream();
            report.GeneratePdf(ms);
            return ms.ToArray();
        }

        public async Task<byte[]?> GenerateAllDepositRatesReportAsync()
        {
            EnsureFontsRegistered();
            var types = (await _depositTypeService.GetAsync()).ToList();
            if (!types.Any()) return Array.Empty<byte>();

            // Bulk load rates
            var typeIds = types.Select(t => t.DepositTypeId);
            var dict = await _depositTypeService.GetRatesForTypesAsync(typeIds);

            var report = new AllDepositRatesReport(types, dict);
            using var ms = new MemoryStream();
            report.GeneratePdf(ms);
            return ms.ToArray();
        }

        private void EnsureFontsRegistered()
        {
            if (_fontsRegistered) return;

            try
            {
                var webRoot = _env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
                var fontsDir = Path.Combine(webRoot, "fonts");

                // Try Vazirmatn first (recommended)
                var vazirRegular = Path.Combine(fontsDir, "Vazirmatn-Regular.ttf");
                var vazirBold = Path.Combine(fontsDir, "Vazirmatn-Bold.ttf");

                if (File.Exists(vazirRegular))
                    QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(vazirRegular));
                if (File.Exists(vazirBold))
                    QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(vazirBold));

                // Optional fallback: IRANSansX
                var irsansRegular = Path.Combine(fontsDir, "IRANSansX-Regular.ttf");
                var irsansBold = Path.Combine(fontsDir, "IRANSansX-Bold.ttf");

                if (File.Exists(irsansRegular))
                    QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(irsansRegular));
                if (File.Exists(irsansBold))
                    QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(irsansBold));

                _fontsRegistered = true;
            }
            catch
            {
                // Ignore font registration errors; QuestPDF will fallback to system fonts
                _fontsRegistered = true;
            }
        }
    }
}
