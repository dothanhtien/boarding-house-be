namespace BoardingHouse.Domain.VietQr;

public static class VietQrBanks
{
    public const string UnknownBinMessage = "Bank BIN is not a supported VietQR bank";

    // Source: https://api.vietqr.io/v2/banks, banks with transferSupported = 1 (e-wallets MoMo, ZaloPay, MVAS left out), in the order the API returns them. Fetched 2026-11-21
    public static readonly IReadOnlyList<VietQrBank> All =
    [
        new("970415", "ICB", "VietinBank"),
        new("970436", "VCB", "Vietcombank"),
        new("970418", "BIDV", "BIDV"),
        new("970405", "VBA", "Agribank"),
        new("970448", "OCB", "OCB"),
        new("970422", "MB", "MBBank"),
        new("970407", "TCB", "Techcombank"),
        new("970416", "ACB", "ACB"),
        new("970432", "VPB", "VPBank"),
        new("970423", "TPB", "TPBank"),
        new("970403", "STB", "Sacombank"),
        new("970437", "HDB", "HDBank"),
        new("970454", "VCCB", "VietCapitalBank"),
        new("970429", "SCB", "SCB"),
        new("970441", "VIB", "VIB"),
        new("970443", "SHB", "SHB"),
        new("970431", "EIB", "Eximbank"),
        new("970426", "MSB", "MSB"),
        new("546034", "CAKE", "CAKE"),
        new("546035", "Ubank", "Ubank"),
        new("963388", "TIMO", "Timo"),
        new("970400", "SGICB", "SaigonBank"),
        new("970409", "BAB", "BacABank"),
        new("971133", "PVDB", "PVcomBank Pay"),
        new("970412", "PVCB", "PVcomBank"),
        new("970414", "MBV", "MBV"),
        new("970419", "NCB", "NCB"),
        new("970424", "SHBVN", "ShinhanBank"),
        new("970425", "ABB", "ABBANK"),
        new("970427", "VAB", "VietABank"),
        new("970428", "NAB", "NamABank"),
        new("970430", "PGB", "PGBank"),
        new("970433", "VIETBANK", "VietBank"),
        new("970438", "BVB", "BaoVietBank"),
        new("970440", "SEAB", "SeABank"),
        new("970446", "COOPBANK", "COOPBANK"),
        new("970449", "LPB", "LPBank"),
        new("970452", "KLB", "KienLongBank"),
        new("668888", "KBank", "KBank"),
        new("970434", "IVB", "IndovinaBank"),
        new("422589", "CIMB", "CIMB"),
        new("970444", "CBB", "CBBank"),
        new("970457", "WVN", "Woori"),
        new("970462", "KBHN", "KookminHN"),
        new("970463", "KBHCM", "KookminHCM")
    ];

    private static readonly Dictionary<string, VietQrBank> ByBin = All.ToDictionary(b => b.Bin);

    public static VietQrBank? Find(string bin) => ByBin.GetValueOrDefault(bin);
}
