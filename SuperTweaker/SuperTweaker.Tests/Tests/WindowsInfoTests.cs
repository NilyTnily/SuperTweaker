using FluentAssertions;
using SuperTweaker.Core;
using Xunit;

namespace SuperTweaker.Tests;

/// <summary>
/// Read-only tests for WindowsInfo — queries system info but makes no changes.
/// </summary>
public class WindowsInfoTests
{
    private static readonly WindowsInfo Info = WindowsInfo.Get();

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void WindowsInfo_Caption_Is_Not_Empty()
    {
        Info.Caption.Should().NotBeNullOrWhiteSpace(
            "OS caption (e.g. 'Microsoft Windows 11 Home') should always be populated");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void WindowsInfo_Build_Is_Numeric()
    {
        int.TryParse(Info.Build, out _).Should().BeTrue(
            $"Build number '{Info.Build}' should be a valid integer");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void WindowsInfo_IsWindows10_Or_11_Or_Unknown()
    {
        // At most one of these can be true
        (Info.IsWindows10 && Info.IsWindows11).Should().BeFalse(
            "cannot be both Win10 and Win11 simultaneously");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void WindowsInfo_Architecture_Is_x64()
    {
        Info.Architecture.Should().Be("x64",
            "tests are configured for x64 only; the app.manifest targets x64");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void WindowsInfo_CpuName_Is_Not_Empty()
    {
        Info.CpuName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void WindowsInfo_TotalRamMb_Is_Positive()
    {
        Info.TotalRamMb.Should().BeGreaterThan(0, "system must have some RAM");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void WindowsInfo_Edition_Is_Not_Empty()
    {
        Info.Edition.Should().NotBeNullOrWhiteSpace(
            "edition should be readable from registry (e.g. 'Professional')");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void WindowsInfo_Get_Does_Not_Throw()
    {
        var act = WindowsInfo.Get;
        act.Should().NotThrow("WindowsInfo.Get() must handle any WMI/registry errors internally");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void WindowsInfo_Build_Corresponds_To_IsWindows_Flag()
    {
        if (int.TryParse(Info.Build, out int b))
        {
            if (b >= 22000)
                Info.IsWindows11.Should().BeTrue($"Build {b} >= 22000 must be Windows 11");
            else if (b >= 10240)
                Info.IsWindows10.Should().BeTrue($"Build {b} in [10240, 21999] must be Windows 10");
        }
    }
}
