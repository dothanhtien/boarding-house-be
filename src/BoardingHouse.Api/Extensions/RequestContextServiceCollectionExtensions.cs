using BoardingHouse.Api.Common.CurrentOrganization;
using BoardingHouse.Api.Common.CurrentUser;
using BoardingHouse.Application.Common.CurrentOrganization;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Api.Extensions;

public static class RequestContextServiceCollectionExtensions
{
    public static IServiceCollection AddRequestContext(this IServiceCollection services)
    {
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<ICurrentOrganizationAccessor, CurrentOrganizationAccessor>();

        return services;
    }
}
