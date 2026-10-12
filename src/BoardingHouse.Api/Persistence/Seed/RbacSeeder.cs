using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BoardingHouse.Api.Persistence.Seed;

public class RbacSeeder
{
    private static readonly (string Resource, string Action, string? Description)[] PermissionSeeds =
    [
        (PermissionResources.User, PermissionActions.Read, "View user list/details"),
        (PermissionResources.User, PermissionActions.Create, "Create a new user"),
        (PermissionResources.User, PermissionActions.Update, "Update user information"),
        (PermissionResources.User, PermissionActions.Delete, "Delete a user"),
        (PermissionResources.Role, PermissionActions.Read, "View role list/details"),
        (PermissionResources.Role, PermissionActions.Assign, "Assign a role to a user"),
        (PermissionResources.Organization, PermissionActions.Read, "View organization list/details"),
        (PermissionResources.Organization, PermissionActions.Create, "Create a new organization"),
        (PermissionResources.Organization, PermissionActions.Update, "Update organization information"),
        (PermissionResources.Organization, PermissionActions.Delete, "Delete an organization"),
        (PermissionResources.OrganizationSetting, PermissionActions.Read, "View organization settings"),
        (PermissionResources.OrganizationSetting, PermissionActions.Update, "Update organization settings"),
        (PermissionResources.OrganizationMember, PermissionActions.Read, "View organization member list/details"),
        (PermissionResources.OrganizationMember, PermissionActions.Create, "Add a member to an organization"),
        (PermissionResources.OrganizationMember, PermissionActions.Update, "Change an organization member's role"),
        (PermissionResources.OrganizationMember, PermissionActions.Delete, "Remove a member from an organization"),
        (PermissionResources.Property, PermissionActions.Read, "View property list/details"),
        (PermissionResources.Property, PermissionActions.Create, "Create a new property"),
        (PermissionResources.Property, PermissionActions.Update, "Update property information"),
        (PermissionResources.Property, PermissionActions.Delete, "Delete a property"),
        (PermissionResources.Room, PermissionActions.Read, "View room list/details"),
        (PermissionResources.Room, PermissionActions.Create, "Create a new room"),
        (PermissionResources.Room, PermissionActions.Update, "Update room information"),
        (PermissionResources.Room, PermissionActions.Delete, "Delete a room"),
        (PermissionResources.UtilityService, PermissionActions.Read, "View utility service list/details"),
        (PermissionResources.UtilityService, PermissionActions.Create, "Create a new utility service"),
        (PermissionResources.UtilityService, PermissionActions.Update, "Update utility service information"),
        (PermissionResources.UtilityService, PermissionActions.Delete, "Delete a utility service"),
    ];

    internal static readonly (string Slug, string Name, string Description, RoleScope Scope, string[] Permissions)[] RoleSeeds =
    [
        (RoleSlugs.PlatformAdmin, "Platform Admin", "Full platform administration rights", RoleScope.Platform, ["*"]),
        (RoleSlugs.PlatformStaff, "Platform Staff", "Platform staff — limited permissions", RoleScope.Platform,
            [
                "user:read", "role:read", "organization:read", "organization-setting:read",
                "organization-member:read", "property:read", "room:read", "utility-service:read"]),
        (RoleSlugs.OrganizationAdmin, "Organization Admin", "Full rights within the organization", RoleScope.Organization,
            [
                "organization:read", "organization:update", "organization-setting:read", "organization-setting:update",
                "organization-member:read", "organization-member:create", "organization-member:update", "organization-member:delete",
                "property:read", "property:create", "property:update", "property:delete",
                "room:read", "room:create", "room:update", "room:delete",
                "utility-service:read", "utility-service:create", "utility-service:update", "utility-service:delete"]),
        (RoleSlugs.OrganizationStaff, "Organization staff", "Manage day-to-day operations within the organization", RoleScope.Organization,
            [
                "organization:read", "organization-setting:read",
                "organization-member:read", "organization-member:create", "organization-member:update", "organization-member:delete",
                "property:read", "property:create", "property:update", "property:delete",
                "room:read", "room:create", "room:update", "room:delete",
                "utility-service:read", "utility-service:create", "utility-service:update", "utility-service:delete"]),
    ];

    public static async Task SeedAsync(
        AppDbContext context,
        IRolePermissionCache? rolePermissionCache = null,
        CancellationToken cancellationToken = default)
    {
        var (permissionsByKey, _) = await SeedPermissionsAsync(context, rolePermissionCache, cancellationToken);
        var permissionNamesById = permissionsByKey.Values.ToDictionary(p => p.Id, p => $"{p.Resource}:{p.Action}");

        foreach (var seed in RoleSeeds)
        {
            var permissions = seed.Permissions.Contains("*")
                ? permissionsByKey.Values
                : seed.Permissions.Select(p => ResolvePermission(p, seed.Slug, permissionsByKey));

            await SeedRoleAsync(context, seed.Slug, seed.Name, seed.Description, seed.Scope, permissions, permissionNamesById, rolePermissionCache, cancellationToken);
        }
    }

    private static async Task<(Dictionary<(string Resource, string Action), Permission> Permissions, List<Guid> AffectedRoleIds)> SeedPermissionsAsync(
        AppDbContext context,
        IRolePermissionCache? rolePermissionCache,
        CancellationToken cancellationToken)
    {
        var existing = await context.Permissions.ToListAsync(cancellationToken);
        var existingKeys = existing.Select(p => (p.Resource, p.Action)).ToHashSet();

        var added = new List<string>();

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
            added.Add($"{seed.Resource}:{seed.Action}");
        }

        var desiredKeys = PermissionSeeds.Select(s => (s.Resource, s.Action)).ToHashSet();
        var toRevoke = existing.Where(p => !desiredKeys.Contains((p.Resource, p.Action))).ToList();

        var affectedRoleIds = toRevoke.Count == 0
            ? []
            : await context.RolePermissions
                .Where(rp => toRevoke.Select(p => p.Id).Contains(rp.PermissionId))
                .Select(rp => rp.RoleId)
                .Distinct()
                .ToListAsync(cancellationToken);

        context.Permissions.RemoveRange(toRevoke);

        foreach (var permission in toRevoke)
        {
            existing.Remove(permission);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            if (added.Count > 0)
            {
                Log.Information("RBAC permissions added: {Permissions}", added);
            }

            if (toRevoke.Count > 0)
            {
                Log.Warning(
                    "RBAC permissions revoked: {Permissions} (affecting {RoleCount} role(s))",
                    toRevoke.Select(p => $"{p.Resource}:{p.Action}"),
                    affectedRoleIds.Count);
            }

            if (rolePermissionCache is not null)
            {
                foreach (var roleId in affectedRoleIds)
                {
                    await rolePermissionCache.InvalidateAsync(roleId, cancellationToken);
                }
            }
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Another instance seeded concurrently; reload to pick up its rows.
            context.ChangeTracker.Clear();
            existing = await context.Permissions.ToListAsync(cancellationToken);
            affectedRoleIds = [];
        }

        return (existing.ToDictionary(p => (p.Resource, p.Action)), affectedRoleIds);
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
        RoleScope scope,
        IEnumerable<Permission> permissions,
        Dictionary<Guid, string> permissionNamesById,
        IRolePermissionCache? rolePermissionCache,
        CancellationToken cancellationToken)
    {
        var role = await context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Slug == slug, cancellationToken);

        var isNewRole = role is null;

        if (role is null)
        {
            role = new Role
            {
                Slug = slug,
                Name = name,
                Description = description,
                Scope = scope,
                IsSystem = true,
                CreatedBy = SentinelActors.System
            };
            context.Roles.Add(role);
        }

        var desiredPermissionIds = permissions.Select(p => p.Id).ToHashSet();
        var grantedPermissionIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();

        var grantedNames = new List<string>();

        foreach (var permission in permissions)
        {
            if (!grantedPermissionIds.Add(permission.Id)) continue;

            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id,
                CreatedBy = SentinelActors.System
            });
            grantedNames.Add($"{permission.Resource}:{permission.Action}");
        }

        var granted = grantedNames.Count > 0;

        var toRevoke = role.RolePermissions
            .Where(rp => !desiredPermissionIds.Contains(rp.PermissionId))
            .ToList();

        var revokedNames = toRevoke
            .Select(rp => permissionNamesById.GetValueOrDefault(rp.PermissionId, rp.PermissionId.ToString()))
            .ToList();

        context.RolePermissions.RemoveRange(toRevoke);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            if (isNewRole)
            {
                Log.Information("RBAC role created: {RoleSlug}", slug);
            }

            if (granted)
            {
                Log.Information("RBAC permissions granted to role {RoleSlug}: {Permissions}", slug, grantedNames);
            }

            if (revokedNames.Count > 0)
            {
                Log.Warning("RBAC permissions revoked from role {RoleSlug}: {Permissions}", slug, revokedNames);
            }

            if (rolePermissionCache is not null && (granted || toRevoke.Count > 0))
            {
                await rolePermissionCache.InvalidateAsync(role.Id, cancellationToken);
            }
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Another instance seeded this role/its permissions concurrently — already-seeded state, safe to ignore.
        }
    }
}
