using Academy.Identity.Domain;

namespace Academy.Identity.Infrastructure;

public sealed class IdentityData
{
    public List<UserAccount> Users { get; set; } = [];
}
