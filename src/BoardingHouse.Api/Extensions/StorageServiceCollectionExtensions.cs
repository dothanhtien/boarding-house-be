using BoardingHouse.Api.Services.Storage;

namespace BoardingHouse.Api.Extensions;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddMediaStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CloudinaryOptions>()
            .Bind(configuration.GetSection(CloudinaryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IStorageProvider, CloudinaryStorageProvider>();

        return services;
    }
}
