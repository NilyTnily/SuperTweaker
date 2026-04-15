using System.Text.Json;
using FluentAssertions;
using SuperTweaker.Modules.GoldenSetup;
using Xunit;
using Microsoft.Win32;

namespace SuperTweaker.Tests;

/// <summary>
/// Unit tests for TweakApplier logic — no system changes made.
/// All registry/service/PS operations are tested in dry-run or via mock data only.
/// </summary>
public class TweakApplierTests
{
    // ──────────────── ParseKind ────────────────

    [Theory]
    [InlineData("DWORD",        RegistryValueKind.DWord)]
    [InlineData("dword",        RegistryValueKind.DWord)]
    [InlineData("QWORD",        RegistryValueKind.QWord)]
    [InlineData("STRING",       RegistryValueKind.String)]
    [InlineData("EXPANDSTRING", RegistryValueKind.ExpandString)]
    [InlineData("BINARY",       RegistryValueKind.Binary)]
    [InlineData("MULTISTRING",  RegistryValueKind.MultiString)]
    [InlineData("unknown",      RegistryValueKind.DWord)]  // defaults to DWord
    public void ParseKind_Returns_Correct_Kind(string input, RegistryValueKind expected)
    {
        TweakApplier.ParseKind(input).Should().Be(expected);
    }

    // ──────────────── ResolveValue ────────────────

    [Fact]
    public void ResolveValue_DWord_FromJsonNumber_Returns_Int()
    {
        var el     = JsonDocument.Parse("1").RootElement;
        var result = TweakApplier.ResolveValue(el, RegistryValueKind.DWord);
        result.Should().Be(1).And.BeOfType<int>();
    }

    [Fact]
    public void ResolveValue_DWord_FromJsonString_Returns_Int()
    {
        var el     = JsonDocument.Parse("\"42\"").RootElement;
        var result = TweakApplier.ResolveValue(el, RegistryValueKind.DWord);
        result.Should().Be(42).And.BeOfType<int>();
    }

    [Fact]
    public void ResolveValue_DWord_Zero_From_Null_Json()
    {
        var el     = JsonDocument.Parse("null").RootElement;
        var result = TweakApplier.ResolveValue(el, RegistryValueKind.DWord);
        result.Should().Be(0);
    }

    [Fact]
    public void ResolveValue_String_FromJsonString_Returns_String()
    {
        var el     = JsonDocument.Parse("\"hello\"").RootElement;
        var result = TweakApplier.ResolveValue(el, RegistryValueKind.String);
        result.Should().Be("hello").And.BeOfType<string>();
    }

    [Fact]
    public void ResolveValue_QWord_FromJsonNumber_Returns_Long()
    {
        var el     = JsonDocument.Parse("9999999999").RootElement;
        var result = TweakApplier.ResolveValue(el, RegistryValueKind.QWord);
        result.Should().Be(9999999999L).And.BeOfType<long>();
    }

    // ──────────────── ValidateTweak ────────────────

    [Fact]
    public void ValidateTweak_Valid_Minimal_Tweak_Passes()
    {
        var tweak = new Tweak { Id = "t1", Name = "Test" };
        var (valid, reason) = TweakApplier.ValidateTweak(tweak);
        valid.Should().BeTrue(reason);
    }

    [Fact]
    public void ValidateTweak_Missing_Id_Fails()
    {
        var tweak = new Tweak { Id = "", Name = "Test" };
        var (valid, _) = TweakApplier.ValidateTweak(tweak);
        valid.Should().BeFalse();
    }

    [Fact]
    public void ValidateTweak_Missing_Name_Fails()
    {
        var tweak = new Tweak { Id = "t1", Name = "" };
        var (valid, _) = TweakApplier.ValidateTweak(tweak);
        valid.Should().BeFalse();
    }

    [Fact]
    public void ValidateTweak_Invalid_Registry_Hive_Fails()
    {
        var json  = """{"Path":"HKEY_INVALID\\Some\\Key","Name":"Val","Value":0,"ValueKind":"DWORD"}""";
        var entry = JsonSerializer.Deserialize<RegistryEntry>(json, GoldenProfile.JsonOptions)!;
        var tweak = new Tweak { Id = "t1", Name = "Test",
            Registry = new List<RegistryEntry> { entry } };

        var (valid, reason) = TweakApplier.ValidateTweak(tweak);
        valid.Should().BeFalse(reason);
    }

    [Fact]
    public void ValidateTweak_Invalid_ValueKind_Fails()
    {
        var json  = """{"Path":"HKEY_LOCAL_MACHINE\\Test","Name":"X","Value":1,"ValueKind":"INVALID"}""";
        var entry = JsonSerializer.Deserialize<RegistryEntry>(json, GoldenProfile.JsonOptions)!;

        var tweak = new Tweak { Id = "t1", Name = "Test",
            Registry = new List<RegistryEntry> { entry } };

        var (valid, reason) = TweakApplier.ValidateTweak(tweak);
        valid.Should().BeFalse(reason);
    }

    [Fact]
    public void ValidateTweak_Valid_Registry_Entry_Passes()
    {
        var json  = """{"Path":"HKEY_LOCAL_MACHINE\\SOFTWARE\\Test","Name":"MyVal","Value":0,"ValueKind":"DWORD"}""";
        var entry = JsonSerializer.Deserialize<RegistryEntry>(json, GoldenProfile.JsonOptions)!;

        var tweak = new Tweak { Id = "t1", Name = "Test",
            Registry = new List<RegistryEntry> { entry } };

        var (valid, reason) = TweakApplier.ValidateTweak(tweak);
        valid.Should().BeTrue(reason);
    }

    // ──────────────── ValidateUndo ────────────────

    [Fact]
    public void ValidateUndo_Apply_Without_Undo_PS_Fails()
    {
        var tweak = new Tweak
        {
            Id   = "t1", Name = "Test",
            PowerShellApply = "Write-Host 'hello'",
            PowerShellUndo  = null
        };
        var (valid, _) = TweakApplier.ValidateUndo(tweak);
        valid.Should().BeFalse("a PowerShellApply without PowerShellUndo is not fully revertible");
    }

    [Fact]
    public void ValidateUndo_Apply_With_Undo_PS_Passes()
    {
        var tweak = new Tweak
        {
            Id = "t1", Name = "Test",
            PowerShellApply = "Write-Host 'apply'",
            PowerShellUndo  = "Write-Host 'undo'"
        };
        var (valid, reason) = TweakApplier.ValidateUndo(tweak);
        valid.Should().BeTrue(reason);
    }

    [Fact]
    public void ValidateUndo_Registry_With_Null_UndoValue_Is_Valid()
    {
        // null UndoValue = delete key on undo — that IS a valid undo strategy
        var json = """{"Path":"HKLM\\SOFTWARE\\Test","Name":"X","Value":1,"ValueKind":"DWORD","UndoValue":null}""";
        var entry = JsonSerializer.Deserialize<RegistryEntry>(json, GoldenProfile.JsonOptions)!;
        var tweak = new Tweak { Id = "t1", Name = "Test", Registry = new() { entry } };

        var (valid, reason) = TweakApplier.ValidateUndo(tweak);
        valid.Should().BeTrue(reason);
    }
}
