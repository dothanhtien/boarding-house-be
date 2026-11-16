using System.Text.Json;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.UnitTests.Persistence;

public class InvoiceGenerationSkipsDbValueConverterTests
{
    private static readonly Guid LeaseId = Guid.Parse("8f2b7c1e-3d4a-4b5c-9e6f-0a1b2c3d4e5f");

    private static IReadOnlyList<InvoiceGenerationSkip> Skips() =>
    [
        new(LeaseId, "HD-001", "101",
        [
            new InvoiceGenerationSkipReason("missingReading", "Electricity reading for 2026-11 is missing"),
            new InvoiceGenerationSkipReason("failed", "Unexpected error, see the server logs")
        ])
    ];

    private static string ToProvider(IReadOnlyList<InvoiceGenerationSkip> skips) =>
        (string)InvoiceGenerationSkipsDbValueConverter.Instance.ConvertToProvider(skips)!;

    private static IReadOnlyList<InvoiceGenerationSkip> FromProvider(string json) =>
        (IReadOnlyList<InvoiceGenerationSkip>)InvoiceGenerationSkipsDbValueConverter.Instance.ConvertFromProvider(json)!;

    [Fact]
    public void ToProvider_WritesTheDocumentedCamelCaseShape()
    {
        using var document = JsonDocument.Parse(ToProvider(Skips()));

        var skip = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(LeaseId, skip.GetProperty("leaseId").GetGuid());
        Assert.Equal("HD-001", skip.GetProperty("leaseCode").GetString());
        Assert.Equal("101", skip.GetProperty("roomNumber").GetString());
        var reasons = skip.GetProperty("reasons").EnumerateArray().ToList();
        Assert.Equal(2, reasons.Count);
        Assert.Equal("missingReading", reasons[0].GetProperty("code").GetString());
        Assert.Equal("Electricity reading for 2026-11 is missing", reasons[0].GetProperty("message").GetString());
        Assert.Equal("failed", reasons[1].GetProperty("code").GetString());
    }

    [Fact]
    public void FromProvider_ReadsBackWhatToProviderWrote()
    {
        var skip = Assert.Single(FromProvider(ToProvider(Skips())));

        Assert.Equal(LeaseId, skip.LeaseId);
        Assert.Equal("HD-001", skip.LeaseCode);
        Assert.Equal("101", skip.RoomNumber);
        Assert.Equal(Skips()[0].Reasons, skip.Reasons);
    }

    [Fact]
    public void EmptyList_IsAnEmptyJsonArray()
    {
        Assert.Equal("[]", ToProvider([]));
        Assert.Empty(FromProvider("[]"));
    }

    [Fact]
    public void FromProvider_JsonNull_ReturnsAnEmptyList()
    {
        Assert.Empty(FromProvider("null"));
    }

    [Fact]
    public void Comparer_SnapshotEqualsTheOriginal()
    {
        var comparer = InvoiceGenerationSkipsDbValueConverter.Comparer;
        var skips = Skips();

        var snapshot = comparer.Snapshot(skips);

        Assert.NotSame(skips, snapshot);
        Assert.True(comparer.Equals(skips, snapshot));
        Assert.Equal(comparer.GetHashCode(skips), comparer.GetHashCode(snapshot));
    }

    [Fact]
    public void Comparer_DifferentLists_AreNotEqual()
    {
        var comparer = InvoiceGenerationSkipsDbValueConverter.Comparer;
        var skips = Skips();

        Assert.False(comparer.Equals(skips, []));
        Assert.False(comparer.Equals(skips, [skips[0] with { RoomNumber = "102" }]));
    }
}
