using System.Net.Http;

namespace CaptionTranslator.Speech.Piper
{
    /// <summary>
    /// Location and one-time download of Piper voice files: &lt;root&gt;\&lt;id&gt;\&lt;id&gt;.onnx and .onnx.json.
    /// A voice counts as downloaded only when both files are complete (files arrive as *.part and are renamed at the end).
    /// </summary>
    public sealed class VoiceFiles
    {
        public const string DefaultBaseUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/main/";

        private readonly string rootDirectory;
        private readonly HttpMessageHandler? handler;
        private readonly string baseUrl;

        public VoiceFiles()
            : this(Path.Combine(AppPaths.DataDirectory, "voices"), null, DefaultBaseUrl)
        {
        }

        /// <param name="handler">For tests; null uses the normal network stack.</param>
        public VoiceFiles(string rootDirectory, HttpMessageHandler? handler, string baseUrl)
        {
            this.rootDirectory = rootDirectory;
            this.handler = handler;
            this.baseUrl = baseUrl;
        }

        public string ModelPath(PiperVoiceInfo voice) => Path.Combine(this.rootDirectory, voice.Id, voice.Id + ".onnx");

        public string ConfigPath(PiperVoiceInfo voice) => ModelPath(voice) + ".json";

        public bool IsDownloaded(PiperVoiceInfo voice) => File.Exists(ModelPath(voice)) && File.Exists(ConfigPath(voice));

        /// <summary>Downloads the config, then the model. On failure or cancel nothing of this voice is left behind.</summary>
        /// <param name="progress">Percent of the model file (0..100).</param>
        /// <exception cref="HttpRequestException">Server not reachable or file not found.</exception>
        /// <exception cref="IOException">Connection dropped or disk error.</exception>
        /// <exception cref="OperationCanceledException">Cancelled.</exception>
        public async Task DownloadAsync(PiperVoiceInfo voice, IProgress<int> progress, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath(voice))!);
            using HttpClient client = this.handler != null ? new HttpClient(this.handler, disposeHandler: false) : new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(30);

            try
            {
                await DownloadFileAsync(client, $"{voice.Folder}/{voice.Id}.onnx.json", ConfigPath(voice), null, cancellationToken).ConfigureAwait(false);
                await DownloadFileAsync(client, $"{voice.Folder}/{voice.Id}.onnx", ModelPath(voice), progress, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Cleanup only; the exception is passed on to the caller.
                DeleteIfExists(ConfigPath(voice));
                DeleteIfExists(ConfigPath(voice) + ".part");
                DeleteIfExists(ModelPath(voice) + ".part");
                DeleteEmptyDirectory(Path.GetDirectoryName(ModelPath(voice))!);
                throw;
            }
        }

        private async Task DownloadFileAsync(HttpClient client, string relativeUrl, string target, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            string temporary = target + ".part";
            using HttpResponseMessage response = await client.GetAsync(this.baseUrl + relativeUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;

            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream destination = File.Create(temporary))
            {
                byte[] buffer = new byte[1 << 16];
                long written = 0;
                int lastPercent = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;
                    int percent = total > 0 ? (int)(written * 100 / total.Value) : 0;
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(percent);
                    }
                }
            }

            File.Move(temporary, target, true);
            progress?.Report(100);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static void DeleteEmptyDirectory(string path)
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
    }
}
