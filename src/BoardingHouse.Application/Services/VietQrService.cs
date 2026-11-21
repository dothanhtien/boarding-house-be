using BoardingHouse.Application.DTOs.VietQr;
using BoardingHouse.Domain.VietQr;

namespace BoardingHouse.Application.Services;

public class VietQrService : IVietQrService
{
    public List<VietQrBankResponse> GetBanks() =>
        VietQrBanks.All
            .Select(b => new VietQrBankResponse { Bin = b.Bin, Code = b.Code, ShortName = b.ShortName })
            .ToList();
}
