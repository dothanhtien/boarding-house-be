using BoardingHouse.Api.Common;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace BoardingHouse.IntegrationTests.Fixtures;

public class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public AppDbContext CreateContext(params IInterceptor[] extraInterceptors)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(new AuditableEntitySaveChangesInterceptor(
                new CurrentUserAccessor(),
                NullLogger<AuditableEntitySaveChangesInterceptor>.Instance))
            .AddInterceptors(extraInterceptors)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }

    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                users, refresh_tokens,
                roles, permissions, user_roles, role_permissions,
                organizations, organization_settings,
                properties, rooms, room_amenities, room_assets, room_asset_condition_histories,
                room_asset_condition_history_media,
                utility_services,
                leases, lease_tenants, lease_status_histories,
                media_assets,
                tenant_profiles
            CASCADE
            """);
    }
}
