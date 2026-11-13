using Mapster;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.Mappings;

public class InvoiceMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<InvoiceIssue, InvoiceIssueResponse>();
    }
}
