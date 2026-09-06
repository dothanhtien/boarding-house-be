using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Persistence.Seed;

public static class AdminSeeder
{
    private const string PlatformAdminRoleSlug = "platform_admin";

    public static async Task SeedAsync(
        AppDbContext context,
        string email,
        string password,
        string fullName,
        CancellationToken cancellationToken = default)
    {
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Slug == PlatformAdminRoleSlug, cancellationToken)
            ?? throw new InvalidOperationException($"Role '{PlatformAdminRoleSlug}' not found; run --seed-rbac first.");

        var normalizedEmail = email.Trim().ToLowerInvariant();

        var existingUser = await context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (existingUser is not null)
        {
            var alreadyHasRole = await context.UserRoles
                .AnyAsync(ur => ur.UserId == existingUser.Id && ur.RoleId == role.Id, cancellationToken);

            throw new InvalidOperationException(alreadyHasRole
                ? $"User '{normalizedEmail}' already exists and already has role '{PlatformAdminRoleSlug}'."
                : $"User '{normalizedEmail}' already exists but is missing role '{PlatformAdminRoleSlug}'.");
        }

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            FullName = fullName,
            CreatedBy = SentinelActors.System
        };

        context.Users.Add(user);

        context.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            CreatedBy = SentinelActors.System
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new InvalidOperationException(
                $"User '{normalizedEmail}' was created concurrently by another process; platform admin was not seeded by this invocation.");
        }
    }
}
