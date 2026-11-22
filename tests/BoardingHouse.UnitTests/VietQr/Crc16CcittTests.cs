using BoardingHouse.Domain.VietQr;

namespace BoardingHouse.UnitTests.VietQr;

public class Crc16CcittTests
{
    [Fact]
    public void Compute_StandardCheckInput_ReturnsTheCatalogueCheckValue()
    {
        // CRC-16/CCITT-FALSE check value from the CRC catalogue (reveng)
        Assert.Equal(0x29B1, Crc16Ccitt.Compute("123456789"));
    }

    [Fact]
    public void Compute_EmptyInput_ReturnsTheInitialValue()
    {
        Assert.Equal(0xFFFF, Crc16Ccitt.Compute(""));
    }

    [Fact]
    public void Compute_SingleCharacterChange_ChangesTheChecksum()
    {
        Assert.NotEqual(Crc16Ccitt.Compute("INV2026110001"), Crc16Ccitt.Compute("INV2026110002"));
    }
}
