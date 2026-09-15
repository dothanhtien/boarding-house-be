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
        ("role", "assign", "Assign a role to a user"),
        ("organization", "read", "View organization list/details"),
        ("organization", "create", "Create a new organization"),
        ("organization", "update", "Update organization information"),
        ("organization", "delete", "Delete an organization"),
        ("organization-setting", "read", "View organization settings"),
        ("organization-setting", "update", "Update organization settings"),
    ];

    private static readonly (string Slug, string Name, string Description, string[] Permissions)[] RoleSeeds =
    [
        ("platform_admin", "Platform Admin", "Full platform administration rights", ["*"]),
        ("platform_staff", "Platform Staff", "Platform staff — limited permissions",
            ["user:read", "role:read"]),
    ];

    public static async Task SeedAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        var permissionsByKey = await SeedPermissionsAsync(context, cancellationToken);

        foreach (var seed in RoleSeeds)
        {
            var permissions = seed.Permissions.Contains("*")
                ? permissionsByKey.Values
                : seed.Permissions.Select(p => ResolvePermission(p, seed.Slug, permissionsByKey));

            await SeedRoleAsync(context, seed.Slug, seed.Name, seed.Description, permissions, cancellationToken);
        }
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

        var desiredKeys = PermissionSeeds.Select(s => (s.Resource, s.Action)).ToHashSet();
        var toRevoke = existing.Where(p => !desiredKeys.Contains((p.Resource, p.Action))).ToList();

        context.Permissions.RemoveRange(toRevoke);

        foreach (var permission in toRevoke)
        {
            existing.Remove(permission);
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

    private static Permission ResolvePermission(
        string key,
        string roleSlug,
        Dictionary<(string Resource, string Action), Permission> permissionsByKey)
    {
        var parts = key.Split(":", 2);

        if (parts.Length != 2)
        {
            throw new InvalidOperationException(
                $"Invalid permission \"{key}\" in RoleSeeds[\"{roleSlug}\"] - Expected format \"resource:action\"");
        }

        if (!permissionsByKey.TryGetValue((parts[0], parts[1]), out var permission))
        {
            throw new InvalidOperationException(
                $"Permission \"{key}\" in RoleSeeds[\"{roleSlug}\"] not found in PermissionSeeds.");
        }

        return permission;
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

        var desiredPermissionIds = permissions.Select(p => p.Id).ToHashSet();
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

        var toRevoke = role.RolePermissions
            .Where(rp => !desiredPermissionIds.Contains(rp.PermissionId))
            .ToList();

        context.RolePermissions.RemoveRange(toRevoke);

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
