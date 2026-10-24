using FluentValidation;
using Mapster;
using Microsoft.Extensions.DependencyInjection;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Application.Services;
using BoardingHouse.Application.Jobs;

namespace BoardingHouse.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IOrganizationSettingsService, OrganizationSettingsService>();
        services.AddScoped<IOrganizationMemberService, OrganizationMemberService>();
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IRoomAssetService, RoomAssetService>();
        services.AddScoped<IRoomAssetConditionHistoryService, RoomAssetConditionHistoryService>();
        services.AddScoped<IUtilityServiceService, UtilityServiceService>();
        services.AddScoped<ILeaseService, LeaseService>();
        services.AddScoped<ILeaseUtilityServiceService, LeaseUtilityServiceService>();
        services.AddScoped<ITenantProfileService, TenantProfileService>();
        services.AddScoped<ITenantService, TenantService>();

        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IOrganizationScopeAccessor, OrganizationScopeAccessor>();
        services.AddScoped<IMediaAttachmentService, MediaAttachmentService>();
        services.AddScoped<IMediaCollectionService, MediaCollectionService>();
        services.AddScoped<IMediaUploader, MediaUploader>();

        services.AddScoped<ExpireLeasesJob>();

        TypeAdapterConfig.GlobalSettings.Scan(typeof(DependencyInjection).Assembly);

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Stop at the first failed rule within a property so each field surfaces a single error.
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;

        return services;
    }
}
