using System.Diagnostics.CodeAnalysis;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyInvoiceDetailResponse : MyInvoiceResponse
{
    public MyInvoiceDetailResponse() { }

    [SetsRequiredMembers]
    public MyInvoiceDetailResponse(MyInvoiceResponse invoice) : base(invoice) { }

    public List<MyInvoiceItemResponse> Items { get; init; } = [];
}
