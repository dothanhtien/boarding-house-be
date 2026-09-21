using Mapster;

namespace BoardingHouse.UnitTests.Common;

internal static class MapsterTestSupport
{
    private static readonly Lazy<bool> Initialization = new(() =>
    {
        TypeAdapterConfig.GlobalSettings.Scan(typeof(Program).Assembly);
        return true;
    });

    public static void EnsureUserMappingRegistered() => _ = Initialization.Value;
}
