using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.Entities;

public class User : BaseEntity
{
    public required string Email { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public string? Phone { get; set; }
    public required string PasswordHash { get; set; }
    public required string FullName { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
}
