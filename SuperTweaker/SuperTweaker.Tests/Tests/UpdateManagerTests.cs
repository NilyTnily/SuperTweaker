using FluentAssertions;
using SuperTweaker.Core;
using SuperTweaker.Modules.UpdateControl;
using Xunit;

namespace SuperTweaker.Tests;

/// <summary>
/// Read-only tests for UpdateManager — only reads current state, no changes made.
/// </summary>
public class UpdateManagerTests
{
    [Fact]
    [Trait("Category", "ReadOnly")]
    public void GetStatus_Does_Not_Throw()
    {
        var log     = new TestLogger();
        var manager = new UpdateManager(log, MakeFakeOsInfo());

        var act = manager.GetStatus;
        act.Should().NotThrow("GetStatus reads services and registry without modifying anything");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void GetStatus_Returns_Valid_Status_Object()
    {
        var log     = new TestLogger();
        var manager = new UpdateManager(log, MakeFakeOsInfo());
        var status  = manager.GetStatus();

        status.Should().NotBeNull();
        // ServicesDisabled and PolicyDisabled are booleans — any combination is valid
        _ = status.ServicesDisabled;
        _ = status.PolicyDisabled;
        _ = status.IsFullyDisabled;
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void IsFullyDisabled_Is_Both_Fields_Combined()
    {
        var s1 = new UpdateStatus(ServicesDisabled: true,  PolicyDisabled: true);
        var s2 = new UpdateStatus(ServicesDisabled: true,  PolicyDisabled: false);
        var s3 = new UpdateStatus(ServicesDisabled: false, PolicyDisabled: false);

        s1.IsFullyDisabled.Should().BeTrue();
        s2.IsFullyDisabled.Should().BeFalse();
        s3.IsFullyDisabled.Should().BeFalse();
    }

    private static WindowsInfo MakeFakeOsInfo() =>
        new(
            Caption: "Microsoft Windows 11 Pro",
            Version: "10.0.22621",
            Build: "22621",
            IsWindows11: true,
            IsWindows10: false,
            IsPro: true,
            Edition: "Professional",
            Architecture: "x64",
            CpuName: "Test CPU",
            MotherboardVendor: "TestVendor",
            MotherboardModel: "TestBoard",
            MotherboardKind: "Desktop",
            MotherboardVersion: "Rev 1.0",
            MotherboardSerialNumber: "TEST-SN",
            MotherboardPartNumber: "TEST-PART",
            CpuPhysicalCores: 8,
            CpuLogicalCores: 16,
            CpuMaxClockMhz: 4800,
            TotalRamMb: 16384,
            GpuName: "Test GPU",
            GpuVramMb: 8192,
            GpuDriverVersion: "1.0-test",
            DiskInfo: "C: 50GB/500GB",
            SecureBootEnabled: true,
            VbsEnabled: false,
            TamperProtectionEnabled: false,
            IsElevated: true
        );
}
