using MiniBank.Web.Infrastructure.Repositories;

namespace MiniBank.Web.Services
{
    public class SmsSendService
    {
        private readonly SmsPanelRepository _smsRepo;

        public SmsSendService(SmsPanelRepository smsRepo)
        {
            _smsRepo = smsRepo;
        }

        public async Task<(long spentRial, long remainingRial, bool lowCredit, long thresholdRial)> ConsumeCreditAfterSuccessfulSendAsync(int parts)
        {
            var result = await _smsRepo.ConsumeCreditForPartsAsync(parts);
            return (result.spentRial, result.remainingRial, result.lowCredit, result.thresholdRial);
        }
    }
}
