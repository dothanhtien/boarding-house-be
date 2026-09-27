using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Seed;
using BoardingHouse.Api.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BoardingHouse.Api.Extensions;

public static class StartupCommandExtensions
{
    /// <summary>
    /// Runs a one-off CLI command (--migrate, --seed-rbac, --seed-admin) if one was passed.
    /// Returns true when a command was handled and the process should exit without starting the web host.
    /// Must not start the host: ValidateOnStart (e.g. Cloudinary credentials) must not run for these commands.
    /// </summary>
    public static async Task<bool> TryRunCliCommandAsync(this WebApplication app, string[] args)
    {
        if (args.Contains("--migrate"))
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            Log.Information("Migration completed");
            return true;
        }

        if (args.Contains("--seed-rbac"))
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rolePermissionCache = scope.ServiceProvider.GetRequiredService<IRolePermissionCache>();
            await RbacSeeder.SeedAsync(db, rolePermissionCache);
            Log.Information("RBAC seed completed");
            return true;
        }

        if (args.Contains("--seed-admin"))
        {
            var adminEmail = app.Configuration["ADMIN_EMAIL"];
            var adminPassword = app.Configuration["ADMIN_PASSWORD"];
            var adminFullName = app.Configuration["ADMIN_FULLNAME"] ?? "Platform Admin";

            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                Log.Fatal("--seed-admin requires ADMIN_EMAIL and ADMIN_PASSWORD environment variables to be set");
                return true;
            }

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await AdminSeeder.SeedAsync(db, adminEmail, adminPassword, adminFullName);
            Log.Information("Platform admin seed completed");
            return true;
        }

        return false;
    }

    /// <summary>Development only: applies pending migrations and syncs RBAC seed on every startup.</summary>
    public static async Task ApplyDevelopmentDatabaseSetupAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rolePermissionCache = scope.ServiceProvider.GetRequiredService<IRolePermissionCache>();
        await db.Database.MigrateAsync();
        await RbacSeeder.SeedAsync(db, rolePermissionCache);
    }
}
