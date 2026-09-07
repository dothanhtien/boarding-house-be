using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Common;

public interface ICurrentUserAccessor
{
    User? User { get; set; }
    User RequiredUser => User ?? throw new InvalidOperationException("Current user is not set on an authenticated request");
}
