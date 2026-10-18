using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Common;

public class CurrentUserAccessor : ICurrentUserAccessor
{
    public User? User { get; set; }
}
