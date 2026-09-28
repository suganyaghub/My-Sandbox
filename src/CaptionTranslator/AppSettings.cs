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

        public bool ReadAloud { get; set; }

        public string? Voice { get; set; }

        /// <summary>Speech rate from -10 to 10; 0 is normal.</summary>
        public int SpeechRate { get; set; }

        /// <summary>Output device for reading aloud; null = Windows default device.</summary>
        public string? AudioDeviceId { get; set; }

        /// <summary>Reading voice loudness in percent of the device volume (the Windows volume itself is never changed).</summary>
        public int ReadAloudVolume { get; set; } = 80;

        /// <summary>Do not read aloud lines spoken by <see cref="MyName"/>.</summary>
        public bool SkipMyLines { get; set; } = true;

        /// <summary>The user's name as Teams shows it in the captions. Prefilled from the Windows display name.</summary>
        public string? MyName { get; set; }

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
