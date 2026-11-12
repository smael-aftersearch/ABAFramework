using Aba.Foundation;

namespace Academy.Identity.Domain;

public sealed record UserAccount(Guid Id, string Email)
{
    public static UserAccount Register(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 254)
            throw new DomainRuleException("Enter a valid email address.");
        return new UserAccount(Guid.NewGuid(), email.Trim().ToLowerInvariant());
    }
}
