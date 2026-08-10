using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniBank.Web.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace MiniBank.Web.Services
{
    // Warm-up service to reduce first-request latency by JITing and opening DB connection
    public class WarmupService : IHostedService
    {
        private readonly ILogger<WarmupService> _logger;
        private readonly IServiceProvider _provider;

        public WarmupService(ILogger<WarmupService> logger, IServiceProvider provider)
        {
            _logger = logger;
            _provider = provider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // small delay to ensure app is up
                    await Task.Delay(500, cancellationToken);
                    _logger.LogInformation("Warmup: Loading accounts once to prime DB connection and JIT...");
                    using var scope = _provider.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<AccountsRepository>();
                    var _ = await repo.GetAllAsync();
                    _logger.LogInformation("Warmup: Accounts primed.");
                }
                catch (TaskCanceledException) { }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, "Warmup failed (non-fatal)");
                }
            }, cancellationToken);

            await Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
