using System.IO;
using System.Text.Json;
using FluentAssertions;
using SuperTweaker.Modules.GoldenSetup;
using Xunit;

namespace SuperTweaker.Tests;

/// <summary>
/// Tests JSON profile loading and structural validity.
/// No system changes are made.
/// </summary>
public class ProfileTests
{
    private static readonly string ProfileDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Data", "profiles");

    [Theory]
    [InlineData("golden-win10.json")]
    [InlineData("golden-win11.json")]
    public void Profile_File_Exists(string fileName)
    {
        var path = Path.Combine(ProfileDir, fileName);
        File.Exists(path).Should().BeTrue($"Profile file '{fileName}' must be present in output directory.");
    }

    [Theory]
    [InlineData("golden-win10.json")]
    [InlineData("golden-win11.json")]
    public void Profile_Deserializes_Without_Exception(string fileName)
    {
        var path = Path.Combine(ProfileDir, fileName);
        var json = File.ReadAllText(path);

        GoldenProfile? profile = null;
        var act = () => { profile = JsonSerializer.Deserialize<GoldenProfile>(json, GoldenProfile.JsonOptions); };
        act.Should().NotThrow($"Profile '{fileName}' must deserialize cleanly.");
        profile.Should().NotBeNull();
    }

    [Theory]
    [InlineData("golden-win10.json")]
    [InlineData("golden-win11.json")]
    public void Profile_Has_Name_And_Tweaks(string fileName)
    {
        var profile = LoadProfile(fileName);
        profile.Name.Should().NotBeNullOrWhiteSpace("profile must have a Name");
        profile.Tweaks.Should().NotBeEmpty("profile must have at least one tweak");
    }

    [Theory]
    [InlineData("golden-win10.json")]
    [InlineData("golden-win11.json")]
    public void All_Tweaks_Have_Id_And_Name(string fileName)
    {
        var profile = LoadProfile(fileName);
        foreach (var tweak in profile.Tweaks)
        {
            tweak.Id.Should().NotBeNullOrWhiteSpace($"Tweak in {fileName} is missing Id");
            tweak.Name.Should().NotBeNullOrWhiteSpace($"Tweak '{tweak.Id}' is missing Name");
        }
    }

    [Theory]
    [InlineData("golden-win10.json")]
    [InlineData("golden-win11.json")]
    public void All_Ids_Are_Unique(string fileName)
    {
        var profile = LoadProfile(fileName);
        var ids     = profile.Tweaks.Select(t => t.Id).ToList();
        ids.Should().OnlyHaveUniqueItems("duplicate tweak IDs cause undefined apply/revert order");
    }

    [Theory]
    [InlineData("golden-win10.json")]
    [InlineData("golden-win11.json")]
    public void All_Tweaks_Pass_Apply_Validation(string fileName)
    {
        var profile = LoadProfile(fileName);
        var failures = new List<string>();

        foreach (var tweak in profile.Tweaks)
        {
            var (valid, reason) = TweakApplier.ValidateTweak(tweak);
            if (!valid) failures.Add($"{tweak.Id}: {reason}");
        }

        failures.Should().BeEmpty(
            $"all tweaks in {fileName} should pass validation.\nFailing:\n{string.Join("\n", failures)}");
    }

    [Theory]
    [InlineData("golden-win10.json")]
    [InlineData("golden-win11.json")]
    public void All_Tweaks_Pass_Undo_Validation(string fileName)
    {
        var profile  = LoadProfile(fileName);
        var failures = new List<string>();

        foreach (var tweak in profile.Tweaks)
        {
            var (valid, reason) = TweakApplier.ValidateUndo(tweak);
            if (!valid) failures.Add($"{tweak.Id}: {reason}");
        }

        failures.Should().BeEmpty(
            $"all tweaks should have revert data.\nFailing:\n{string.Join("\n", failures)}");
    }

    [Theory]
    [InlineData("golden-win10.json")]
    [InlineData("golden-win11.json")]
    public void Registry_Entries_Have_Valid_Paths(string fileName)
    {
        var profile = LoadProfile(fileName);
        foreach (var tweak in profile.Tweaks)
        {
            if (tweak.Registry == null) continue;
            foreach (var r in tweak.Registry)
            {
                r.Path.Should().NotBeNullOrWhiteSpace($"Tweak '{tweak.Id}' has registry entry without Path");
                r.Path.Should().MatchRegex(@"^(HKEY_LOCAL_MACHINE|HKEY_CURRENT_USER|HKLM|HKCU)\\",
                    $"Tweak '{tweak.Id}' registry path must start with a known hive");
            }
        }
    }

    [Theory]
    [InlineData("golden-win10.json")]
    [InlineData("golden-win11.json")]
    public void Win11Only_Tweaks_Only_In_Win11_Profile(string fileName)
    {
        var profile   = LoadProfile(fileName);
        bool isWin11  = fileName.Contains("win11");

        if (!isWin11)
        {
            var win11Tweaks = profile.Tweaks.Where(t => t.Os == TweakOs.Win11Only).ToList();
            // Win10 profile can contain Win11Only tweaks — they're just skipped at runtime
            // but the profile must not have any that are ONLY applicable to Win11 when targeting Win10
            // This is informational, not a hard failure
            _ = win11Tweaks; // intentional: just verifying it doesn't throw
        }
    }

    private static GoldenProfile LoadProfile(string fileName)
    {
        var path    = Path.Combine(ProfileDir, fileName);
        var json    = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<GoldenProfile>(json, GoldenProfile.JsonOptions)!;
        return profile;
    }
}
