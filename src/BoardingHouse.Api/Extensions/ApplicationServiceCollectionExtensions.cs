using BoardingHouse.Api.Common;
using BoardingHouse.Api.Common.CurrentOrganization;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services;
using BoardingHouse.Api.Services.Caching;
using BoardingHouse.Api.Services.Storage;
using FluentValidation;
using Mapster;

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
        services.AddScoped<IMediaAttachmentService, MediaAttachmentService>();
        services.AddScoped<IMediaCollectionService, MediaCollectionService>();
        services.AddScoped<IMediaUploader, MediaUploader>();

        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<ICurrentOrganizationAccessor, CurrentOrganizationAccessor>();

        services.AddScoped<IUserCache, UserCache>();
        services.AddScoped<IRolePermissionCache, RolePermissionCache>();
        services.AddScoped<IOrganizationMembershipCache, OrganizationMembershipCache>();

        // Process-wide static configuration — runs once when the host is built.
        TypeAdapterConfig.GlobalSettings.Scan(typeof(Program).Assembly);

        services.AddValidatorsFromAssemblyContaining<Program>();

        // Stop at the first failed rule within a property so each field surfaces a single error.
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;

        return services;
    }
}
