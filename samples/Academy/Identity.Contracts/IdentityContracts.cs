namespace Academy.Identity.Contracts;

public sealed record UserRegisteredV1(Guid UserId, string Email, DateTimeOffset OccurredAtUtc);
public sealed record UserViewV1(Guid UserId, string Email);

/// <summary>Published read-only application contract, not a Domain reference.</summary>
public interface IIdentityReaderV1
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default);
}
