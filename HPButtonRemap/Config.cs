using System.Text.Json.Serialization;

namespace HPButtonRemap;

/// <summary>
/// Configuration model for HP button remap application
/// </summary>
public class Config
{
    public bool ShowStartupNotification { get; set; } = true;
    public bool RunAtStartup { get; set; } = true;
    public int? EventID { get; set; } = 29;
    public int? EventData { get; set; } = 8616;
    public ButtonAction? Action { get; set; } = ButtonAction.CreateDefault();

    [JsonIgnore]
    public int EffectiveEventID => EventID ?? 29;

    [JsonIgnore]
    public int EffectiveEventData => EventData ?? 8616;
}

/// <summary>
/// Represents an action to perform when a button is pressed
/// </summary>
public class ButtonAction
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ActionType Type { get; set; } = ActionType.RunCommand;

    public string? Command { get; set; }
    public bool? CreateNewWindow { get; set; } = true;
    public string? WorkingDirectory { get; set; }
    public int? DelayMs { get; set; } = 0;
    public string? FilePath { get; set; }
    public string? OpenWebsite { get; set; }

    [JsonIgnore]
    public bool EffectiveCreateNewWindow => CreateNewWindow ?? true;

    [JsonIgnore]
    public int EffectiveDelayMs => DelayMs ?? 0;

    internal static bool TryGetWebsite(string? value, out Uri? website) =>
        Uri.TryCreate(value, UriKind.Absolute, out website) &&
        website.Scheme is "http" or "https" && website.Host.Length > 0 &&
        website.UserInfo.Length == 0;

    public static ButtonAction CreateDefault() => new()
    {
        Type = ActionType.RunCommand,
        Command = "%localappdata%\\Microsoft\\WindowsApps\\wt.exe",
        CreateNewWindow = true
    };
}

/// <summary>
/// Types of actions that can be performed
/// </summary>
public enum ActionType
{
    RunCommand,
    OpenWebsite,
    OpenFile
}
