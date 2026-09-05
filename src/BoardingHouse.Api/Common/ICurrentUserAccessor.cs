using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Common;

public interface ICurrentUserAccessor
{
    User? User { get; set; }
}
