using BoardingHouse.Api.Common.CurrentOrganization;

namespace BoardingHouse.Api.Middleware;

public class CurrentOrganizationMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Organization-Id";

    public async Task InvokeAsync(HttpContext context, ICurrentOrganizationAccessor currentOrganizationAccessor)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var value)
            && Guid.TryParse(value, out var organizationId))
        {
            currentOrganizationAccessor.OrganizationId = organizationId;
        }

        await next(context);
    }
}
