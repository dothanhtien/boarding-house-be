using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.DTOs.VietQr;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Authorize]
public class VietQrController(IVietQrService vietQrService) : ControllerBase
{
    [HttpGet("api/vietqr/banks")]
    public ActionResult<ApiResponse<List<VietQrBankResponse>>> GetBanks() =>
        Ok(new ApiResponse<List<VietQrBankResponse>> { Data = vietQrService.GetBanks() });
}
