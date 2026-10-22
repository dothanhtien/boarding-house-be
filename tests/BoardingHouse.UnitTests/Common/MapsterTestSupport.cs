using BoardingHouse.Application;
using Mapster;

namespace BoardingHouse.UnitTests.Common;

internal static class MapsterTestSupport
{
    private static readonly Lazy<bool> Initialization = new(() =>
    {
        TypeAdapterConfig.GlobalSettings.Scan(typeof(DependencyInjection).Assembly);
        return true;
    });

    public static void EnsureUserMappingRegistered() => _ = Initialization.Value;
}
