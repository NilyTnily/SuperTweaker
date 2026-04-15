using FluentAssertions;
using SuperTweaker.Core;
using Xunit;

namespace SuperTweaker.Tests;

/// <summary>
/// Tests for RegistryHelper path parsing — no actual registry reads/writes.
/// </summary>
public class RegistryHelperTests
{
    // ──────────────── GetValue (safe reads of known public keys) ────────────────

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void GetValue_Returns_Value_For_Known_Key()
    {
        // HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProductName always exists on Windows
        var val = RegistryHelper.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "ProductName");

        val.Should().NotBeNull("ProductName always exists in Windows NT CurrentVersion key");
        val!.ToString().Should().StartWith("Windows",
            "ProductName should start with 'Windows'");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void GetValue_Returns_Null_For_Missing_Key()
    {
        var val = RegistryHelper.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\SuperTweaker_NonExistent_Key_ABCDEF",
            "SomeMissingValue");
        val.Should().BeNull("non-existent key should return null, not throw");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public void GetValue_Returns_Null_For_Missing_Value_Name()
    {
        var val = RegistryHelper.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "ThisValueDefinitelyDoesNotExist_ZZZZZ");
        val.Should().BeNull();
    }

    // ──────────────── Path Parsing via GetValue (indirect) ────────────────

    [Theory]
    [Trait("Category", "ReadOnly")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion",              "ProductName")]
    public void GetValue_Accepts_Both_Full_And_Short_Hive_Names(string path, string name)
    {
        var full  = RegistryHelper.GetValue(path, name);
        full.Should().NotBeNull();
    }

    // ──────────────── Write roundtrip (uses safe test subkey) ────────────────

    private const string TestKey = @"HKEY_CURRENT_USER\SOFTWARE\SuperTweaker_Tests";

    [Fact]
    [Trait("Category", "WriteSafe")]
    public void SetValue_And_GetValue_Roundtrip_DWord()
    {
        try
        {
            RegistryHelper.EnsureKeyExists(TestKey);
            RegistryHelper.SetValue(TestKey, "TestDWord", 42, Microsoft.Win32.RegistryValueKind.DWord);
            var result = RegistryHelper.GetValue(TestKey, "TestDWord");
            result.Should().NotBeNull();
            Convert.ToInt32(result).Should().Be(42);
        }
        finally
        {
            RegistryHelper.DeleteValue(TestKey, "TestDWord");
        }
    }

    [Fact]
    [Trait("Category", "WriteSafe")]
    public void SetValue_And_GetValue_Roundtrip_String()
    {
        try
        {
            RegistryHelper.EnsureKeyExists(TestKey);
            RegistryHelper.SetValue(TestKey, "TestString", "hello_world",
                Microsoft.Win32.RegistryValueKind.String);
            var result = RegistryHelper.GetValue(TestKey, "TestString");
            result.Should().Be("hello_world");
        }
        finally
        {
            RegistryHelper.DeleteValue(TestKey, "TestString");
        }
    }

    [Fact]
    [Trait("Category", "WriteSafe")]
    public void DeleteValue_Removes_Value_Silently()
    {
        RegistryHelper.EnsureKeyExists(TestKey);
        RegistryHelper.SetValue(TestKey, "ToDelete", 1,
            Microsoft.Win32.RegistryValueKind.DWord);

        RegistryHelper.DeleteValue(TestKey, "ToDelete");

        var result = RegistryHelper.GetValue(TestKey, "ToDelete");
        result.Should().BeNull("deleted value should return null");
    }

    [Fact]
    [Trait("Category", "WriteSafe")]
    public void DeleteValue_On_Missing_Key_Does_Not_Throw()
    {
        var act = () => RegistryHelper.DeleteValue(
            @"HKEY_CURRENT_USER\SOFTWARE\SuperTweaker_Tests_MISSING_KEY_ZZZ",
            "NonExistent");
        act.Should().NotThrow();
    }
}
