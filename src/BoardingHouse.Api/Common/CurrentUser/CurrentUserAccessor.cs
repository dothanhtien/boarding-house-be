using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Common;

public class CurrentUserAccessor : ICurrentUserAccessor
{
    public User? User { get; set; }
}
