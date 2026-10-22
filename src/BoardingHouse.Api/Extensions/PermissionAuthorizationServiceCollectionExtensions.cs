using BoardingHouse.Api.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace BoardingHouse.Api.Extensions;

public static class PermissionAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, OrganizationScopedPermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        services.AddAuthorization();

        return services;
    }
}
