namespace Devlight;

internal sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public string? MonitorIdentity { get; init; }
    public string? MonitorName { get; init; }
    public int Brightness { get; init; } = 30;
    public bool StartWithWindows { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Version == 1 && !string.IsNullOrWhiteSpace(MonitorIdentity)
        && Brightness is >= 0 and <= 100;
}
