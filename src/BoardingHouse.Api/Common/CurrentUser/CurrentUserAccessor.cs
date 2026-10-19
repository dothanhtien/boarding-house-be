using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Api.Common.CurrentUser;

public class CurrentUserAccessor : ICurrentUserAccessor
{
    public User? User { get; set; }
}
