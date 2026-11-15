namespace BoardingHouse.Domain.Entities;

// ItemId null = add a custom item (every value required); set = change the item's given values, or delete it
public sealed record InvoiceCustomItemChange(
    Guid? ItemId, string? Description, decimal? Quantity, decimal? UnitPrice, bool IsDeleted);
