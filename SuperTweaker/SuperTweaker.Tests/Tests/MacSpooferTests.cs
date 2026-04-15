using FluentAssertions;
using SuperTweaker.Modules.Spoofer;
using Xunit;

namespace SuperTweaker.Tests;

/// <summary>
/// Tests for MAC address helpers — no hardware or registry touched.
/// </summary>
public class MacSpooferTests
{
    // ──────────────── IsValidMac ────────────────

    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", true)]
    [InlineData("aa:bb:cc:dd:ee:ff", true)]
    [InlineData("AA-BB-CC-DD-EE-FF", true)]
    [InlineData("AABBCCDDEEFF",      true)]
    [InlineData("",                  false)]
    [InlineData("GG:BB:CC:DD:EE:FF", false)]  // G is not hex
    [InlineData("AA:BB:CC:DD:EE",    false)]  // too short
    [InlineData("AA:BB:CC:DD:EE:FF:00", false)] // too long
    public void IsValidMac_Returns_Expected(string mac, bool expected)
    {
        MacSpoofer.IsValidMac(mac).Should().Be(expected,
            $"MAC '{mac}' should be {(expected ? "valid" : "invalid")}");
    }

    // ──────────────── GenerateRandomMac ────────────────

    [Fact]
    public void GenerateRandomMac_Produces_Valid_Mac()
    {
        for (int i = 0; i < 100; i++)
        {
            var mac = MacSpoofer.GenerateRandomMac();
            MacSpoofer.IsValidMac(mac).Should().BeTrue($"Generated MAC '{mac}' should be valid");
        }
    }

    [Fact]
    public void GenerateRandomMac_First_Octet_Is_Locally_Administered_Unicast()
    {
        for (int i = 0; i < 50; i++)
        {
            var mac   = MacSpoofer.GenerateRandomMac();
            var first = Convert.ToByte(mac[..2], 16);

            // Bit 0 (LSB) = 0 → unicast
            (first & 0x01).Should().Be(0, $"MAC {mac} must not have multicast bit set");
            // Bit 1       = 1 → locally administered
            (first & 0x02).Should().Be(2, $"MAC {mac} must have locally-administered bit set");
        }
    }

    [Fact]
    public void GenerateRandomMac_Produces_Unique_Values()
    {
        var macs = Enumerable.Range(0, 20)
            .Select(_ => MacSpoofer.GenerateRandomMac())
            .ToHashSet();
        macs.Count.Should().BeGreaterThan(10, "random MACs should not all collide");
    }

    // ──────────────── NicInfo.FormatMac ────────────────

    [Theory]
    [InlineData("AABBCCDDEEFF",      "AA:BB:CC:DD:EE:FF")]
    [InlineData("AA:BB:CC:DD:EE:FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aa-bb-cc-dd-ee-ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("",                  "")]
    [InlineData("SHORT",             "SHORT")]  // too short — returned as-is
    public void FormatMac_Returns_Expected(string input, string expected)
    {
        NicInfo.FormatMac(input).Should().Be(expected);
    }
}
