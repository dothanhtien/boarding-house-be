namespace BoardingHouse.Domain.Entities;

public sealed record InvoiceDetails(decimal DiscountAmount, string? DiscountReason, string? Notes, DateOnly DueDate);
