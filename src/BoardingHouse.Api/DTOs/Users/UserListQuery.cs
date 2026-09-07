using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.DTOs.Users;

public record UserListQuery : ListQuery
{
    public bool? IsActive { get; init; }
}
