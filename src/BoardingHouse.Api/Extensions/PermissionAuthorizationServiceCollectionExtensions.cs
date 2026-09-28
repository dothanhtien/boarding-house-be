using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace BoardingHouse.Api.Extensions;

public static class PermissionAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IOrganizationScopeAccessor, OrganizationScopeAccessor>();

        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, OrganizationScopedPermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        services.AddAuthorization();

        return services;
    }
}
