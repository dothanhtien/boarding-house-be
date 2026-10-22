using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Application.Services.Caching;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Application.Services.Throttling;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Interceptors;
using BoardingHouse.Infrastructure.Repositories;
using BoardingHouse.Infrastructure.Services;
using BoardingHouse.Infrastructure.Services.Caching;
using BoardingHouse.Infrastructure.Services.Storage;
using BoardingHouse.Infrastructure.Services.Throttling;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddPersistence(configuration)
            .AddRepositories()
            .AddCaching(configuration)
            .AddMediaStorage(configuration)
            .AddSecurity();

    private static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(new AuditableEntitySaveChangesInterceptor(
                    sp.GetRequiredService<ICurrentUserAccessor>(),
                    sp.GetRequiredService<ILogger<AuditableEntitySaveChangesInterceptor>>()))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }

    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IOrganizationSettingsRepository, OrganizationSettingsRepository>();
        services.AddScoped<IOrganizationMemberRepository, OrganizationMemberRepository>();
        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IRoomRepository, RoomRepository>();
        services.AddScoped<IUtilityServiceRepository, UtilityServiceRepository>();
        services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
        services.AddScoped<IRoomAssetRepository, RoomAssetRepository>();
        services.AddScoped<IRoomAssetConditionHistoryRepository, RoomAssetConditionHistoryRepository>();
        services.AddScoped<ILeaseRepository, LeaseRepository>();
        services.AddScoped<ILeaseStatusHistoryRepository, LeaseStatusHistoryRepository>();
        services.AddScoped<ITenantProfileRepository, TenantProfileRepository>();

        return services;
    }

    private static IServiceCollection AddCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
            options.InstanceName = "BoardingHouse";
        });

        services.AddScoped<IUserCache, UserCache>();
        services.AddScoped<IRolePermissionCache, RolePermissionCache>();
        services.AddScoped<IOrganizationMembershipCache, OrganizationMembershipCache>();
        services.AddScoped<IAttemptLimiter, DistributedAttemptLimiter>();

        return services;
    }

    private static IServiceCollection AddMediaStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CloudinaryOptions>()
            .Bind(configuration.GetSection(CloudinaryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IStorageProvider, CloudinaryStorageProvider>();

        return services;
    }

    private static IServiceCollection AddSecurity(this IServiceCollection services)
    {
        services.AddSingleton<ITokenService, TokenService>();

        return services;
    }
}
