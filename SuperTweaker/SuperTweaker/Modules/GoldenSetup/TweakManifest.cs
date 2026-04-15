using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuperTweaker.Modules.GoldenSetup;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TweakRisk { Safe, Moderate, Advanced }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TweakOs { Both, Win10Only, Win11Only }

/// <summary>
/// A single registry entry. Value / UndoValue are kept as JsonElement from deserialization
/// so we can safely convert them at apply-time.
/// Use <see cref="TweakApplier.ResolveValue"/> to convert to a .NET type.
/// </summary>
public sealed class RegistryEntry
{
    [JsonPropertyName("Path")]      public string Path      { get; set; } = "";
    [JsonPropertyName("Name")]      public string Name      { get; set; } = "";
    /// <summary>Raw JSON value — number or string.</summary>
    [JsonPropertyName("Value")]     public JsonElement Value    { get; set; }
    [JsonPropertyName("ValueKind")] public string ValueKind { get; set; } = "DWORD";
    /// <summary>null = delete key on undo; otherwise restore this value.</summary>
    [JsonPropertyName("UndoValue")] public JsonElement? UndoValue { get; set; }
}

public sealed class ServiceEntry
{
    [JsonPropertyName("ServiceName")]    public string ServiceName    { get; set; } = "";
    [JsonPropertyName("ApplyStartMode")] public string ApplyStartMode { get; set; } = "demand";
    [JsonPropertyName("UndoStartMode")]  public string UndoStartMode  { get; set; } = "auto";
}

public sealed class ScheduledTaskEntry
{
    [JsonPropertyName("TaskPath")] public string TaskPath { get; set; } = "";
    /// <summary>true = Disable on apply, Enable on undo.</summary>
    [JsonPropertyName("Disable")]  public bool   Disable  { get; set; } = true;
}

public sealed class Tweak
{
    [JsonPropertyName("Id")]              public string Id              { get; set; } = "";
    [JsonPropertyName("Name")]            public string Name            { get; set; } = "";
    [JsonPropertyName("Description")]     public string Description     { get; set; } = "";
    [JsonPropertyName("Risk")]            public TweakRisk Risk         { get; set; } = TweakRisk.Safe;
    [JsonPropertyName("Os")]              public TweakOs Os             { get; set; } = TweakOs.Both;
    [JsonPropertyName("Registry")]        public List<RegistryEntry>?  Registry { get; set; }
    [JsonPropertyName("Services")]        public List<ServiceEntry>?   Services { get; set; }
    [JsonPropertyName("Tasks")]           public List<ScheduledTaskEntry>? Tasks { get; set; }
    [JsonPropertyName("PowerShellApply")] public string? PowerShellApply { get; set; }
    [JsonPropertyName("PowerShellUndo")]  public string? PowerShellUndo  { get; set; }
}

public sealed class GoldenProfile
{
    [JsonPropertyName("Name")]      public string Name      { get; set; } = "";
    [JsonPropertyName("OsTarget")]  public string OsTarget  { get; set; } = "";
    [JsonPropertyName("Tweaks")]    public List<Tweak> Tweaks { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive  = true,
        ReadCommentHandling          = JsonCommentHandling.Skip,
        AllowTrailingCommas          = true,
        Converters                   = { new JsonStringEnumConverter() }
    };
}

/// <summary>Result of a single tweak action (for dry-run and reporting).</summary>
public record TweakResult(
    string TweakId,
    string TweakName,
    bool   Success,
    string Message,
    bool   IsDryRun
);
