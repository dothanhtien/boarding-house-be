using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public class User : BaseEntity
{
    public string? Email { get; private set; }
    public DateTimeOffset? EmailVerifiedAt { get; private set; }
    public string? Phone { get; private set; }
    public string PasswordHash { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public DateTimeOffset? LastLoginAt { get; private set; }
    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<RefreshToken> RefreshTokens { get; private set; } = new List<RefreshToken>();
    public IReadOnlyCollection<UserRole> UserRoles { get; private set; } = new List<UserRole>();
    public IReadOnlyCollection<OrganizationMember> OrganizationMembers { get; private set; } = new List<OrganizationMember>();

    private User() { }

    public static User Register(string? email, string? phone, string passwordHash, string fullName, Guid createdBy)
    {
        var normalizedEmail = EmailAddress.Normalize(email);
        var normalizedPhone = PhoneNumber.Normalize(phone);
        EnsureHasContact(normalizedEmail, normalizedPhone);

        var user = new User { Email = normalizedEmail, Phone = normalizedPhone, PasswordHash = passwordHash, CreatedBy = createdBy };
        user.Rename(fullName);
        return user;
    }

    public void ChangeEmail(string? email)
    {
        var normalized = EmailAddress.Normalize(email);
        EnsureHasContact(normalized, Phone);
        Email = normalized;
    }

    public void ChangePhone(string? phone)
    {
        var normalized = PhoneNumber.Normalize(phone);
        EnsureHasContact(Email, normalized);
        Phone = normalized;
    }

    public void Rename(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("FullName is required");
        }

        FullName = fullName;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void RecordLogin(DateTimeOffset now) => LastLoginAt = now;

    // Email or phone is the login identifier: a user without both could never sign in again
    private static void EnsureHasContact(string? email, string? phone)
    {
        if (email is null && phone is null)
        {
            throw new DomainException("A user must have an email or a phone");
        }
    }
}
