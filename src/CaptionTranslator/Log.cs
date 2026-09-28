namespace CaptionTranslator
{
    /// <summary>Tiny file log in %LocalAppData%\CaptionTranslator\logs, one file per day.</summary>
    public static class Log
    {
        private static readonly object fileLock = new object();

        public static void Info(string message) => Write("INFO ", message);

        public static void Error(string message, Exception exception) => Write("ERROR", message + Environment.NewLine + exception);

        public static string WriteDump(string name, string content)
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            string path = Path.Combine(AppPaths.LogDirectory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, content);
            return path;
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (fileLock)
                {
                    Directory.CreateDirectory(AppPaths.LogDirectory);
                    string path = Path.Combine(AppPaths.LogDirectory, $"log-{DateTime.Now:yyyyMMdd}.txt");
                    File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
                }
            }
            catch (IOException)
            {
                // Logging must never break the app.
            }
        }
    }
}
