using System.Text.Json.Serialization;
using BoardingHouse.Application.Common;

namespace BoardingHouse.Application.DTOs.Invoices;

public record UpdateInvoiceRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<decimal> DiscountAmount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DiscountReason { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Notes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateOnly> DueDate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<List<UpdateInvoiceCustomItemRequest>> CustomItems { get; init; }
}
