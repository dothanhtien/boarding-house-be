using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Common.CurrentUser;

public interface ICurrentUserAccessor
{
    User? User { get; set; }

    // Set by background jobs: writes without a user are audited as SentinelActors.System.
    bool IsSystemActor { get; set; }

    User RequiredUser => User ?? throw new InvalidOperationException("Current user is not set on an authenticated request");
}
