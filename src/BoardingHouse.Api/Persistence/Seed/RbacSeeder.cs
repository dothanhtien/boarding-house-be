using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Persistence.Seed;

public class RbacSeeder
{
    private static readonly (string Resource, string Action, string? Description)[] PermissionSeeds =
    [
        ("user", "read", "View user list/details"),
        ("user", "create", "Create a new user"),
        ("user", "update", "Update user information"),
        ("user", "delete", "Delete a user"),
        ("role", "read", "View role list/details"),
        ("role", "assign", "Assign a role to a user")
    ];

    public static async Task SeedAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        var permissionsByKey = await SeedPermissionsAsync(context, cancellationToken);

        await SeedRoleAsync(
            context,
            slug: "platform_admin",
            name: "Platform Admin",
            description: "Full platform administration rights",
            permissions: permissionsByKey.Values,
            cancellationToken: cancellationToken);

        await SeedRoleAsync(
            context,
            slug: "platform_staff",
            name: "Platform Staff",
            description: "Platform staff — limited permissions",
            permissions:
            [
                permissionsByKey[("user", "read")],
                permissionsByKey[("role", "read")]
            ],
            cancellationToken: cancellationToken);
    }

    private static async Task<Dictionary<(string Resource, string Action), Permission>> SeedPermissionsAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var existing = await context.Permissions.ToListAsync(cancellationToken);
        var existingKeys = existing.Select(p => (p.Resource, p.Action)).ToHashSet();

        foreach (var seed in PermissionSeeds)
        {
            if (existingKeys.Contains((seed.Resource, seed.Action))) continue;

            var permission = new Permission
            {
                Resource = seed.Resource,
                Action = seed.Action,
                Description = seed.Description,
                CreatedBy = SentinelActors.System
            };
            context.Permissions.Add(permission);
            existing.Add(permission);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Another instance seeded concurrently; reload to pick up its rows.
            context.ChangeTracker.Clear();
            existing = await context.Permissions.ToListAsync(cancellationToken);
        }

        return existing.ToDictionary(p => (p.Resource, p.Action));
    }

    private static async Task SeedRoleAsync(AppDbContext context,
        string slug,
        string name,
        string description,
        IEnumerable<Permission> permissions,
        CancellationToken cancellationToken)
    {
        var role = await context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Slug == slug, cancellationToken);

        if (role is null)
        {
            role = new Role
            {
                Slug = slug,
                Name = name,
                Description = description,
                IsSystem = true,
                CreatedBy = SentinelActors.System
            };
            context.Roles.Add(role);
        }

        var grantedPermissionIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();

        foreach (var permission in permissions)
        {
            if (!grantedPermissionIds.Add(permission.Id)) continue;

            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id,
                CreatedBy = SentinelActors.System
            });
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Another instance seeded this role/its permissions concurrently — already-seeded state, safe to ignore.
        }
    }
}
