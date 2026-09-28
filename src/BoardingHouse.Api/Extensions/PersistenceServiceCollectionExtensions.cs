using BoardingHouse.Api.Common;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Extensions;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(new AuditableEntitySaveChangesInterceptor(
                    sp.GetRequiredService<ICurrentUserAccessor>(),
                    sp.GetRequiredService<ILogger<AuditableEntitySaveChangesInterceptor>>()))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
            options.InstanceName = "BoardingHouse";
        });

        return services;
    }
}
