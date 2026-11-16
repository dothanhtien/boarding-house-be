using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

// invoice_generation_runs.skipped (jsonb): [{ "leaseId", "leaseCode", "roomNumber", "reasons": [{ "code", "message" }] }]
public static class InvoiceGenerationSkipsDbValueConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static readonly ValueConverter<IReadOnlyList<InvoiceGenerationSkip>, string> Instance = new(
        skips => JsonSerializer.Serialize(skips, JsonOptions),
        json => JsonSerializer.Deserialize<List<InvoiceGenerationSkip>>(json, JsonOptions) ?? new List<InvoiceGenerationSkip>());

    // Runs are never edited; the comparer keeps EF from tracking the list by reference
    public static readonly ValueComparer<IReadOnlyList<InvoiceGenerationSkip>> Comparer = new(
        (left, right) => left!.SequenceEqual(right!),
        skips => skips.Aggregate(0, (hash, skip) => HashCode.Combine(hash, skip)),
        skips => skips.ToList());
}
