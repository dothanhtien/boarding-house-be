using System.ComponentModel.DataAnnotations;

namespace BoardingHouse.Infrastructure.Services.PaymentGateways.VnPay;

public class VnPayOptions
{
    public const string SectionName = "VnPay";

    [Required, Url]
    public string PaymentUrl { get; init; } = "";

    [Required, Url]
    public string ReturnUrl { get; init; } = "";

    [Required]
    public string Locale { get; init; } = "vn";
}
