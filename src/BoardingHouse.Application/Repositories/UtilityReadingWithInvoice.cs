using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public sealed record UtilityReadingWithInvoice(
    UtilityReading Reading, MediaLocation? Media, Guid? InvoiceId, string? InvoiceNumber);
