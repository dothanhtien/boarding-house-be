using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace BoardingHouse.IntegrationTests.Fixtures;

public class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
                ["Jwt:Secret"] = TestJwtOptions.Secret,
                ["Redis:UserCacheTtlSeconds"] = "1",
                ["Cors:AllowedOrigins:0"] = "https://allowed.example.com"
            });
        });
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_container.StartAsync(), _redis.StartAsync());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var context = new AppDbContext(options))
        {
            await context.Database.MigrateAsync();
        }
    }

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                users, refresh_tokens,
                roles, permissions, user_roles, role_permissions,
                organizations, organization_settings,
                properties, rooms, room_amenities
            CASCADE
            """);

        var redisOptions = ConfigurationOptions.Parse(_redis.GetConnectionString());
        redisOptions.AllowAdmin = true;

        await using var connection = await ConnectionMultiplexer.ConnectAsync(redisOptions);
        await connection.GetServer(connection.GetEndPoints().Single()).FlushDatabaseAsync();
    }

    public async Task GrantPlatformAdminRoleAsync(Guid userId)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await RbacSeeder.SeedAsync(context);

        var role = await context.Roles.SingleAsync(r => r.Slug == "platform_admin");

        context.UserRoles.Add(new UserRole
        {
            UserId = userId,
            RoleId = role.Id,
            CreatedBy = SentinelActors.System
        });

        await context.SaveChangesAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Task.WhenAll(_container.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
        await base.DisposeAsync();
    }
}
