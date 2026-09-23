using BoardingHouse.Api.Common;

namespace BoardingHouse.UnitTests.Common;

public class EnumerableExtensionsTests
{
    [Fact]
    public void AllUnique_EmptySequence_ReturnsTrue()
    {
        Assert.True(Array.Empty<int>().AllUnique());
    }

    [Fact]
    public void AllUnique_DistinctElements_ReturnsTrue()
    {
        Assert.True(new[] { 1, 2, 3 }.AllUnique());
    }

    [Fact]
    public void AllUnique_DuplicateElement_ReturnsFalse()
    {
        Assert.False(new[] { 1, 2, 1 }.AllUnique());
    }

    [Fact]
    public void AllUnique_UsesComparer()
    {
        string[] names = ["WiFi", "wifi"];

        Assert.True(names.AllUnique(StringComparer.Ordinal));
        Assert.False(names.AllUnique(StringComparer.OrdinalIgnoreCase));
    }
}
