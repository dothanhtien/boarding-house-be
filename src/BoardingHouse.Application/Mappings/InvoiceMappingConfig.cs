using Mapster;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.Mappings;

public class InvoiceMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<InvoiceIssue, InvoiceIssueResponse>();

        config.NewConfig<Invoice, InvoiceResponse>()
            .Map(dest => dest.LeaseCode, src => src.Lease!.LeaseCode)
            .Map(dest => dest.RoomId, src => src.Lease!.RoomId)
            .Map(dest => dest.RoomNumber, src => src.Lease!.Room!.RoomNumber)
            .Map(dest => dest.PropertyId, src => src.Lease!.Room!.PropertyId)
            .Map(dest => dest.PropertyName, src => src.Lease!.Room!.Property!.Name);

        config.NewConfig<Invoice, InvoiceDetailResponse>()
            .Inherits<Invoice, InvoiceResponse>()
            .Map(dest => dest.Items, src => src.Items.OrderBy(i => i.SortOrder));
    }
}
