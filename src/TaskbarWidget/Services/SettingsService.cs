using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarWidget.Services;

public enum NicMode
{
    Primary,
    Selected,
    All
}

public enum RefreshInterval
{
    OneSecond,
    TwoSeconds
}

public enum WheelMode
{
    Toggle,
    Directional
}

public enum WidgetPosition
{
    Right,
    Left
}

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; } = false;
    public WidgetPosition Position { get; set; } = WidgetPosition.Right;
    public RefreshInterval Refresh { get; set; } = RefreshInterval.OneSecond;
    public NicMode NicMode { get; set; } = NicMode.Primary;
    public string SelectedNicAlias { get; set; } = string.Empty;
    public WheelMode WheelMode { get; set; } = WheelMode.Toggle;

    [JsonIgnore]
    public int RefreshMilliseconds => Refresh == RefreshInterval.TwoSeconds ? 2000 : 1000;
}

public sealed class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarWidget");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch
        {
            // Settings write failure should not crash the app.
        }

        SettingsChanged?.Invoke(this, Current);
    }
}
