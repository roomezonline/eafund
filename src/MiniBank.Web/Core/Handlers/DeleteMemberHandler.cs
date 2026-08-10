using MediatR;
using MiniBank.Web.Core.Commands;
using MiniBank.Web.Infrastructure.Repositories;

namespace MiniBank.Web.Core.Handlers
{
    public class DeleteMemberHandler : IRequestHandler<DeleteMemberCommand>
    {
        private readonly MemberRepository _memberRepository;
        private readonly AccountsRepository _accountsRepository;

        public DeleteMemberHandler(MemberRepository memberRepository, AccountsRepository accountsRepository)
        {
            _memberRepository = memberRepository;
            _accountsRepository = accountsRepository;
        }

        public async Task Handle(DeleteMemberCommand request, CancellationToken cancellationToken)
        {
            // 1) If member has accounts and any of them has journal lines, block deletion.
            var accounts = await _accountsRepository.GetAccountsByMemberIdAsync(request.MemberId, includeInactive: true);
            foreach (var a in accounts)
            {
                if (await _accountsRepository.HasJournalLinesAsync(a.AccountId))
                {
                    throw new InvalidOperationException($"امکان حذف عضو وجود ندارد چون حساب/کد تفضیلی مرتبط دارای سند/تراکنش است (کد: {a.Code}).");
                }
            }

            // 2) If accounts exist but are inactive and have no journal lines, delete them physically to release FK.
            foreach (var a in accounts)
            {
                if (!a.IsActive)
                {
                    await _accountsRepository.HardDeleteAsync(a.AccountId);
                }
            }

            // 3) Block deletion if member is referenced by other records.
            var dep = await _memberRepository.GetDeleteDependencyInfoAsync(request.MemberId);
            if (!dep.CanDelete)
            {
                throw new InvalidOperationException($"امکان حذف عضو وجود ندارد. وابستگی‌ها: {dep.GetDependencyMessage()}");
            }

            await _memberRepository.DeleteAsync(request.MemberId);
        }
    }
}
