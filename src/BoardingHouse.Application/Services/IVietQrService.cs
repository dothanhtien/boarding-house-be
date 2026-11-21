using BoardingHouse.Application.DTOs.VietQr;

namespace BoardingHouse.Application.Services;

public interface IVietQrService
{
    List<VietQrBankResponse> GetBanks();
}
