using BoardingHouse.Api.Common.CurrentOrganization;
using BoardingHouse.Api.Common.CurrentUser;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services;
using BoardingHouse.Api.Services.Caching;
using BoardingHouse.Api.Services.Throttling;
using BoardingHouse.Application.Common.CurrentOrganization;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Application.Services.Caching;
using BoardingHouse.Application.Services.Throttling;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Api.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddRepositories(this IServiceCollection services)
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

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IOrganizationSettingsService, OrganizationSettingsService>();
        services.AddScoped<IOrganizationMemberService, OrganizationMemberService>();
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IRoomAssetService, RoomAssetService>();
        services.AddScoped<IRoomAssetConditionHistoryService, RoomAssetConditionHistoryService>();
        services.AddScoped<IUtilityServiceService, UtilityServiceService>();
        services.AddScoped<ILeaseService, LeaseService>();
        services.AddScoped<ITenantProfileService, TenantProfileService>();
        services.AddScoped<ITenantService, TenantService>();

        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<ICurrentOrganizationAccessor, CurrentOrganizationAccessor>();

        services.AddScoped<IUserCache, UserCache>();
        services.AddScoped<IRolePermissionCache, RolePermissionCache>();
        services.AddScoped<IOrganizationMembershipCache, OrganizationMembershipCache>();
        services.AddScoped<IAttemptLimiter, DistributedAttemptLimiter>();

        return services;
    }
}
