namespace BoardingHouse.Application.DTOs.Invoices;

public record UpdateInvoiceCustomItemRequest
{
    // Null = add a new custom item (all fields required); set = update (or delete, with IsDeleted) an existing one.
    public Guid? Id { get; init; }
    public string? Description { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public bool IsDeleted { get; init; }
}
