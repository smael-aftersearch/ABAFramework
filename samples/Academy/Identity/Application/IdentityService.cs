using Aba.Foundation;
using Aba.LocalMessaging;
using Academy.Identity.Contracts;
using Academy.Identity.Domain;
using Academy.Identity.Infrastructure;

namespace Academy.Identity.Application;

public sealed class IdentityService(
    DurableContextStore<IdentityData> store, IClock clock) : IIdentityReaderV1
{
    public Task<UserViewV1> RegisterAsync(string email, CancellationToken cancellationToken = default)
        => store.ExecuteAsync((data, outbox) =>
        {
            var user = UserAccount.Register(email);
            if (data.Users.Any(x => x.Email == user.Email))
                throw new DomainRuleException("This email is already registered.");
            data.Users.Add(user);
            outbox.Add(OutboxMessage.Create(new UserRegisteredV1(user.Id, user.Email, clock.UtcNow), clock.UtcNow));
            return new UserViewV1(user.Id, user.Email);
        }, cancellationToken);

    public Task<UserViewV1?> GetAsync(Guid userId, CancellationToken ct = default)
        => store.ReadAsync(data => data.Users.Where(u => u.Id == userId)
            .Select(u => new UserViewV1(u.Id, u.Email)).FirstOrDefault(), ct);

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default)
        => store.ReadAsync(data => data.Users.Any(u => u.Id == userId), cancellationToken);
}
