namespace CaptionTranslator
{
    public static class AppPaths
    {
        public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaptionTranslator");

        public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

        public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");
    }
}
