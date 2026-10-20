using BoardingHouse.Api.Common.CurrentOrganization;
using BoardingHouse.Api.Common.CurrentUser;
using BoardingHouse.Api.Services;
using BoardingHouse.Application.Common.CurrentOrganization;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Api.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IAuthService, AuthService>();
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

        return services;
    }
}
