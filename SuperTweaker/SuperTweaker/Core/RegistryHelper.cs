using Microsoft.Win32;

namespace SuperTweaker.Core;

/// <summary>Safe wrappers around registry operations with undo tracking.</summary>
public static class RegistryHelper
{
    public static bool SetValue(string fullPath, string name, object value, RegistryValueKind kind)
    {
        try
        {
            var (hive, subKey) = SplitPath(fullPath);
            using var key = hive.CreateSubKey(subKey, writable: true);
            if (key == null) return false;
            key.SetValue(name, value, kind);
            return true;
        }
        catch { return false; }
    }

    public static object? GetValue(string fullPath, string name)
    {
        try
        {
            var (hive, subKey) = SplitPath(fullPath);
            using var key = hive.OpenSubKey(subKey, false);
            return key?.GetValue(name);
        }
        catch { return null; }
    }

    public static bool DeleteValue(string fullPath, string name)
    {
        try
        {
            var (hive, subKey) = SplitPath(fullPath);
            using var key = hive.OpenSubKey(subKey, true);
            key?.DeleteValue(name, false);
            return true;
        }
        catch { return false; }
    }

    public static bool EnsureKeyExists(string fullPath)
    {
        try
        {
            var (hive, subKey) = SplitPath(fullPath);
            using var key = hive.CreateSubKey(subKey, true);
            return key != null;
        }
        catch { return false; }
    }

    // Returns the previous value so callers can record undo data
    public static object? GetCurrentValue(string fullPath, string name)
        => GetValue(fullPath, name);

    private static (RegistryKey hive, string subKey) SplitPath(string fullPath)
    {
        var idx = fullPath.IndexOf('\\');
        if (idx < 0) throw new ArgumentException($"Invalid registry path: {fullPath}");

        var hiveStr = fullPath[..idx];
        var sub     = fullPath[(idx + 1)..];

        RegistryKey hive = hiveStr.ToUpper() switch
        {
            "HKEY_LOCAL_MACHINE"  or "HKLM" => Registry.LocalMachine,
            "HKEY_CURRENT_USER"   or "HKCU" => Registry.CurrentUser,
            "HKEY_CLASSES_ROOT"   or "HKCR" => Registry.ClassesRoot,
            "HKEY_USERS"          or "HKU"  => Registry.Users,
            "HKEY_CURRENT_CONFIG" or "HKCC" => Registry.CurrentConfig,
            _ => throw new ArgumentException($"Unknown hive: {hiveStr}")
        };

        return (hive, sub);
    }
}
