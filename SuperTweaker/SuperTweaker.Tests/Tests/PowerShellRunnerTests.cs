using FluentAssertions;
using SuperTweaker.Core;
using Xunit;

namespace SuperTweaker.Tests;

/// <summary>
/// Tests for PowerShellRunner — runs harmless scripts, no system changes.
/// </summary>
public class PowerShellRunnerTests
{
    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task Run_Simple_Echo_Returns_Output()
    {
        var log = new TestLogger();
        var runner    = new PowerShellRunner(log);

        var (ok, output) = await runner.RunAsync("Write-Output 'hello_test'");

        ok.Should().BeTrue("simple echo script should succeed");
        output.Should().Contain("hello_test");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task Run_Empty_Script_Returns_Success()
    {
        var runner       = new PowerShellRunner();
        var (ok, output) = await runner.RunAsync("");

        ok.Should().BeTrue("empty script is a no-op and should succeed");
        output.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task Run_Arithmetic_Returns_Correct_Result()
    {
        var runner       = new PowerShellRunner();
        var (ok, output) = await runner.RunAsync("Write-Output (2 + 2)");

        ok.Should().BeTrue();
        output.Should().Contain("4");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task Run_Failing_Script_Returns_NonSuccess()
    {
        var runner       = new PowerShellRunner();
        // Exit code 1 — PowerShell will treat exit 1 as failure
        var (ok, _) = await runner.RunAsync("exit 1");

        ok.Should().BeFalse("exit code 1 should result in success=false");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task Run_GetVariable_Returns_Value()
    {
        var runner = new PowerShellRunner();
        var (ok, output) = await runner.RunAsync(
            "$env:COMPUTERNAME | Write-Output");

        ok.Should().BeTrue();
        output.Should().NotBeNullOrWhiteSpace("COMPUTERNAME should be accessible");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task Run_Can_Be_Cancelled()
    {
        var runner = new PowerShellRunner();
        // Pre-cancel the token — RunAsync checks at entry
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var (ok, output) = await runner.RunAsync("Write-Output 'should_not_run'", cts.Token);

        ok.Should().BeFalse("pre-cancelled token should result in success=false");
        output.Should().Contain("Cancel", "should report cancellation");
    }
}

/// <summary>Helper logger that captures lines for assertion.</summary>
internal sealed class TestLogger : Logger
{
    public List<string> Lines { get; } = new();

    public TestLogger() : base("test-" + Guid.NewGuid().ToString("N")[..6])
    {
        OnLine += l => Lines.Add(l);
    }
}
