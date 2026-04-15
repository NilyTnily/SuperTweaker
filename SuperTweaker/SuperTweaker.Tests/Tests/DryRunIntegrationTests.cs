using System.IO;
using System.Text.Json;
using FluentAssertions;
using SuperTweaker.Core;
using SuperTweaker.Modules.GoldenSetup;
using Xunit;

namespace SuperTweaker.Tests;

/// <summary>
/// Integration dry-run: loads actual profiles, runs the full apply+revert pipeline
/// in dry-run mode. ZERO system changes are made.
/// </summary>
public class DryRunIntegrationTests
{
    private static readonly string ProfileDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Data", "profiles");

    [Theory]
    [Trait("Category", "DryRun")]
    [InlineData("golden-win11.json", true,  false)]  // Win11 OS
    [InlineData("golden-win11.json", false, false)]  // Win10 OS (Win11Only tweaks filtered out)
    [InlineData("golden-win10.json", true,  false)]  // Win11 OS (Win10Only tweaks filtered out)
    [InlineData("golden-win10.json", false, false)]  // Win10 OS
    public async Task DryRun_Apply_Passes_All_Tweaks(
        string fileName, bool simulateWin11, bool simulateWin10)
    {
        var profile = LoadProfile(fileName);
        var info    = MakeFakeOsInfo(isWin11: simulateWin11, isWin10: simulateWin10 || !simulateWin11);
        var log     = new TestLogger();
        var applier = new TweakApplier(log, info);

        var results = await applier.ApplyAsync(profile, dryRun: true);

        var failures = results.Where(r => !r.Success).ToList();
        failures.Should().BeEmpty(
            $"All tweaks in {fileName} should pass dry-run validation.\n" +
            string.Join("\n", failures.Select(f => $"  [{f.TweakId}] {f.Message}")));
    }

    [Theory]
    [Trait("Category", "DryRun")]
    [InlineData("golden-win11.json", true)]
    [InlineData("golden-win10.json", false)]
    public async Task DryRun_Revert_Passes_All_Tweaks(string fileName, bool isWin11)
    {
        var profile = LoadProfile(fileName);
        var info    = MakeFakeOsInfo(isWin11: isWin11, isWin10: !isWin11);
        var log     = new TestLogger();
        var applier = new TweakApplier(log, info);

        var results = await applier.RevertAsync(profile, dryRun: true);

        var failures = results.Where(r => !r.Success).ToList();
        failures.Should().BeEmpty(
            $"All tweaks in {fileName} should have revert data.\n" +
            string.Join("\n", failures.Select(f => $"  [{f.TweakId}] {f.Message}")));
    }

    [Fact]
    [Trait("Category", "DryRun")]
    public async Task DryRun_Reports_Correct_Count()
    {
        var profile = LoadProfile("golden-win11.json");
        var info    = MakeFakeOsInfo(isWin11: true, isWin10: false);
        var log     = new TestLogger();
        var applier = new TweakApplier(log, info);

        var results = await applier.ApplyAsync(profile, dryRun: true);

        results.Should().HaveCount(
            profile.Tweaks.Count(t => t.Os == TweakOs.Both || t.Os == TweakOs.Win11Only),
            "dry-run results count should match filtered-for-Win11 tweak count");
    }

    [Fact]
    [Trait("Category", "DryRun")]
    public async Task DryRun_Results_Are_Marked_IsDryRun()
    {
        var profile = LoadProfile("golden-win11.json");
        var info    = MakeFakeOsInfo(isWin11: true, isWin10: false);
        var log     = new TestLogger();
        var applier = new TweakApplier(log, info);

        var results = await applier.ApplyAsync(profile, dryRun: true);
        results.Should().AllSatisfy(r => r.IsDryRun.Should().BeTrue());
    }

    [Fact]
    [Trait("Category", "DryRun")]
    public async Task DryRun_Cancellation_Stops_Processing()
    {
        var profile = LoadProfile("golden-win11.json");
        var info    = MakeFakeOsInfo(isWin11: true, isWin10: false);
        var log     = new TestLogger();
        var applier = new TweakApplier(log, info);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately

        var results = await applier.ApplyAsync(profile, dryRun: true, cts.Token);
        results.Should().BeEmpty("no tweaks should process when already-cancelled token is passed");
    }

    // ──────────────── Helpers ────────────────

    private static GoldenProfile LoadProfile(string fileName)
    {
        var path = Path.Combine(ProfileDir, fileName);
        path.Should().Match(p => File.Exists(p), $"profile {fileName} must exist in output dir");
        return JsonSerializer.Deserialize<GoldenProfile>(
            File.ReadAllText(path), GoldenProfile.JsonOptions)!;
    }

    /// <summary>Creates a WindowsInfo stub without touching WMI.</summary>
    private static WindowsInfo MakeFakeOsInfo(bool isWin11, bool isWin10) =>
        new(
            Caption:                 isWin11 ? "Microsoft Windows 11 Pro" : "Microsoft Windows 10 Pro",
            Version:                 isWin11 ? "10.0.22621" : "10.0.19045",
            Build:                   isWin11 ? "22621" : "19045",
            IsWindows11:             isWin11,
            IsWindows10:             isWin10,
            IsPro:                   true,
            Edition:                 "Professional",
            Architecture:            "x64",
            CpuName:                 "Fake CPU for test",
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
            GpuName:                 "Fake GPU",
            GpuVramMb:               8192,
            GpuDriverVersion:        "1.0.0-test",
            DiskInfo:                "C: 50GB/500GB",
            SecureBootEnabled:       true,
            VbsEnabled:              false,
            TamperProtectionEnabled: false,
            IsElevated:              true
        );
}
