using System.IO;
using System.Text.Json.Nodes;
using FluentAssertions;
using SuperTweaker.Core;
using SuperTweaker.Modules.GoldenSetup;
using Xunit;

namespace SuperTweaker.Tests;

/// <summary>
/// Read-only validation of the Performance pipeline pieces that can be checked without running
/// PowerShell (Sophia) or Optimizer.exe. Golden profiles use <see cref="DryRunIntegrationTests"/>.
/// </summary>
public class PerformanceGoldenSetupReadOnlyTests
{
    private static readonly string OptimizerDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Data", "optimizer");

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void Hellzerg_Needs_Separate_Win10_And_Win11_Base_Templates()
    {
        // Upstream templates differ: WindowsVersion, Win11-only tweak keys (taskbar, Copilot, …).
        // Optimizer's automation validates WindowsVersion — wrong file can fail or ignore options.
        var p10 = Path.Combine(OptimizerDir, "hellzerg-base-win10.json");
        var p11 = Path.Combine(OptimizerDir, "hellzerg-base-win11.json");
        File.Exists(p10).Should().BeTrue();
        File.Exists(p11).Should().BeTrue();

        var j10 = JsonNode.Parse(File.ReadAllText(p10))!;
        var j11 = JsonNode.Parse(File.ReadAllText(p11))!;
        j10["WindowsVersion"]!.GetValue<int>().Should().Be(10);
        j11["WindowsVersion"]!.GetValue<int>().Should().Be(11);

        // Win11 upstream template adds Win11-only tweak keys (taskbar, Copilot, …); Win10 file omits them.
        var s10 = File.ReadAllText(p10);
        var s11 = File.ReadAllText(p11);
        s11.Should().Contain("TaskbarToLeft");
        s10.Should().NotContain("TaskbarToLeft");
        s11.Should().Contain("DisableCoPilotAI");
        s10.Should().NotContain("DisableCoPilotAI");
    }

    [Theory]
    [Trait("Category", "ReadOnly")]
    [InlineData(true)]
    [InlineData(false)]
    public void Hellzerg_Patched_Template_Has_Restart_And_HPET(bool isWin11)
    {
        var baseName = isWin11 ? "hellzerg-base-win11.json" : "hellzerg-base-win10.json";
        var path     = Path.Combine(OptimizerDir, baseName);
        var os       = MakeFakeOs(isWin11);

        var ok = HellzergTemplateBuilder.TryBuildPatchedHellzergTemplate(path, os, out var json, out var err);
        ok.Should().BeTrue($"patch failed: {err}");
        json.Should().NotBeNullOrWhiteSpace();

        var root = JsonNode.Parse(json!)!;
        root["PostAction"]!["Restart"]!.GetValue<bool>().Should().BeTrue("only Hellzerg step should request restart");
        root["AdvancedTweaks"]!["DisableHPET"]!.GetValue<bool>().Should().BeTrue();
        root["Tweaks"]!["EnablePerformanceTweaks"]!.GetValue<bool>().Should().BeTrue();
        root["Tweaks"]!["DisableNetworkThrottling"]!.GetValue<bool>().Should().BeTrue();
        root["AdvancedTweaks"]!["SvchostProcessSplitting"]!["Disable"]!.GetValue<bool>().Should().BeTrue();
        root["AdvancedTweaks"]!["SvchostProcessSplitting"]!["RAM"]!.GetValue<int>().Should().Be(16);
    }

    private static WindowsInfo MakeFakeOs(bool isWin11) =>
        new(
            Caption:                 isWin11 ? "Microsoft Windows 11 Pro" : "Microsoft Windows 10 Pro",
            Version:                 isWin11 ? "10.0.22621" : "10.0.19045",
            Build:                   isWin11 ? "22621" : "19045",
            IsWindows11:             isWin11,
            IsWindows10:             !isWin11,
            IsPro:                   true,
            Edition:                 "Professional",
            Architecture:            "x64",
            CpuName:                 "Test CPU",
            MotherboardVendor:       "TestVendor",
            MotherboardModel:        "TestBoard",
            MotherboardKind:         "Desktop",
            MotherboardVersion:      "Rev 1.0",
            MotherboardSerialNumber: "TEST-SN",
            MotherboardPartNumber:   "TEST-PART",
            CpuPhysicalCores:        8,
            CpuLogicalCores:         16,
            CpuMaxClockMhz:          4800,
            TotalRamMb:              16384,
            GpuName:                 "Test GPU",
            GpuVramMb:               8192,
            GpuDriverVersion:        "1.0-test",
            DiskInfo:                "C: 50GB/500GB",
            SecureBootEnabled:       true,
            VbsEnabled:              false,
            TamperProtectionEnabled: false,
            IsElevated:              true
        );
}
