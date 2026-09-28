using System.Text.Json;

namespace CaptionTranslator
{
    /// <summary>User settings, stored in %LocalAppData%\CaptionTranslator\settings.json.</summary>
    public sealed class AppSettings
    {
        public const string TeamsSource = "Teams";
        public const string LiveCaptionsSource = "LiveCaptions";

        public string Source { get; set; } = TeamsSource;

        public bool ShowGerman { get; set; } = true;

        public bool Topmost { get; set; } = true;

        public double FontSize { get; set; } = 18;

        /// <summary>Regex matched against Name/AutomationId of Teams UI elements to find the caption area. Edit if Teams changes.</summary>
        public string TeamsCaptionContainerPattern { get; set; } = "caption|untertitel";

        /// <summary>How long the newest caption line must stay unchanged before it is translated.</summary>
        public int QuietPeriodMilliseconds { get; set; } = 1500;

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new AppSettings();
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException)
            {
                Log.Error("Could not read settings, using defaults.", exception);
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (IOException exception)
            {
                Log.Error("Could not save settings.", exception);
            }
        }
    }
}
