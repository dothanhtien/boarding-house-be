using Mapster;
using BoardingHouse.Application.DTOs.Payments;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Mappings;

// Needs Invoice → Lease → Room → Property and PayerUser loaded (IPaymentRepository *WithDetails / List / Search)
public class PaymentMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Payment, PaymentResponse>()
            .Map(dest => dest.InvoiceId, src => src.InvoiceId!.Value)
            .Map(dest => dest.InvoiceNumber, src => src.Invoice!.InvoiceNumber)
            .Map(dest => dest.OrganizationId, src => src.Invoice!.OrganizationId)
            .Map(dest => dest.LeaseId, src => src.Invoice!.LeaseId)
            .Map(dest => dest.LeaseCode, src => src.Invoice!.Lease!.LeaseCode)
            .Map(dest => dest.RoomId, src => src.Invoice!.Lease!.RoomId)
            .Map(dest => dest.RoomNumber, src => src.Invoice!.Lease!.Room!.RoomNumber)
            .Map(dest => dest.PropertyId, src => src.Invoice!.Lease!.Room!.PropertyId)
            .Map(dest => dest.PropertyName, src => src.Invoice!.Lease!.Room!.Property!.Name)
            .Map(dest => dest.PayerName, src => src.PayerNameOverride ?? (src.PayerUser == null ? null : src.PayerUser.FullName));
    }
}
