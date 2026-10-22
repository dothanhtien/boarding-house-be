using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.DTOs.Users;

public record UserListQuery : ListQuery
{
    public bool? IsActive { get; init; }
}
