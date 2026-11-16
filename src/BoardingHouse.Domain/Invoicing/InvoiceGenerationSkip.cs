namespace BoardingHouse.Domain.Invoicing;

public sealed record InvoiceGenerationSkip(
    Guid LeaseId,
    string LeaseCode,
    string RoomNumber,
    IReadOnlyList<InvoiceGenerationSkipReason> Reasons);

public sealed record InvoiceGenerationSkipReason(string Code, string Message);
