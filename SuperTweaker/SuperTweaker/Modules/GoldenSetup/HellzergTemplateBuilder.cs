using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SuperTweaker.Core;

namespace SuperTweaker.Modules.GoldenSetup;

/// <summary>
/// Builds patched Hellzerg Optimizer JSON from upstream base templates (read-only file operations; no <c>Optimizer.exe</c>).
/// </summary>
public static class HellzergTemplateBuilder
{
    /// <summary>
    /// Loads a Hellzerg base template JSON, applies SuperTweaker patches (HPET, restart, performance flags).
    /// </summary>
    public static bool TryBuildPatchedHellzergTemplate(
        string baseJsonPath,
        WindowsInfo os,
        out string? patchedJson,
        out string? error)
    {
        patchedJson = null;
        error       = null;
        try
        {
            var jsonText = File.ReadAllText(baseJsonPath);
            var root     = JsonNode.Parse(jsonText) ?? throw new InvalidDataException("Empty JSON");
            ApplyHellzergPatch(root, os);
            patchedJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static void ApplyHellzergPatch(JsonNode root, WindowsInfo os)
    {
        var ramGb = Math.Clamp((int)(os.TotalRamMb / 1024UL), 4, 512);

        var post = root["PostAction"] ??= new JsonObject();
        post["Restart"]      = true;
        post["RestartType"] = "Normal";

        var adv = root["AdvancedTweaks"] ??= new JsonObject();
        adv["DisableHPET"] = true;

        var svchost = adv["SvchostProcessSplitting"] as JsonObject ?? new JsonObject();
        svchost["Disable"] = true;
        svchost["RAM"]     = ramGb;
        adv["SvchostProcessSplitting"] = svchost;

        var tweaks = root["Tweaks"] ??= new JsonObject();
        tweaks["EnablePerformanceTweaks"]   = true;
        tweaks["DisableNetworkThrottling"] = true;
    }
}
