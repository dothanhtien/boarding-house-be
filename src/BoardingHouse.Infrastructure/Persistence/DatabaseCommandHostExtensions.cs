using BoardingHouse.Application.Services.Caching;
using BoardingHouse.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BoardingHouse.Infrastructure.Persistence;

public static class DatabaseCommandHostExtensions
{
    /// <summary>
    /// Runs a one-off CLI command (--migrate, --seed-rbac, --seed-admin) if one was passed.
    /// Returns true when a command was handled and the process should exit without starting the web host.
    /// Must not start the host: ValidateOnStart (e.g. Cloudinary credentials) must not run for these commands.
    /// </summary>
    public static async Task<bool> TryRunCliCommandAsync(this IHost host, string[] args)
    {
        if (args.Contains("--migrate"))
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            Log.Information("Migration completed");
            return true;
        }

        if (args.Contains("--seed-rbac"))
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rolePermissionCache = scope.ServiceProvider.GetRequiredService<IRolePermissionCache>();
            await RbacSeeder.SeedAsync(db, rolePermissionCache);
            Log.Information("RBAC seed completed");
            return true;
        }

        if (args.Contains("--seed-admin"))
        {
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            var adminEmail = configuration["ADMIN_EMAIL"];
            var adminPassword = configuration["ADMIN_PASSWORD"];
            var adminFullName = configuration["ADMIN_FULLNAME"] ?? "Platform Admin";

            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                Log.Fatal("--seed-admin requires ADMIN_EMAIL and ADMIN_PASSWORD environment variables to be set");
                return true;
            }

            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await AdminSeeder.SeedAsync(db, adminEmail, adminPassword, adminFullName);
            Log.Information("Platform admin seed completed");
            return true;
        }

        return false;
    }

    public static async Task ApplyDevelopmentDatabaseSetupAsync(this IHost host)
    {
        if (!host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return;
        }

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rolePermissionCache = scope.ServiceProvider.GetRequiredService<IRolePermissionCache>();
        await db.Database.MigrateAsync();
        await RbacSeeder.SeedAsync(db, rolePermissionCache);
    }
}
