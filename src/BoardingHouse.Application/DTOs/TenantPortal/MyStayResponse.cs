namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyStayResponse
{
    public required DateOnly JoinedDate { get; init; }
    public DateOnly? LeftDate { get; init; }
}
