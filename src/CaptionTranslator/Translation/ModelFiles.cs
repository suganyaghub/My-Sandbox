using System.Net.Http;

namespace CaptionTranslator.Translation
{
    /// <summary>Location of the translation model and download of missing files (one time, about 420 MB).</summary>
    public static class ModelFiles
    {
        public const string EncoderFile = "encoder_model.onnx";
        public const string DecoderFile = "decoder_model_merged.onnx";

        private const string baseUrl = "https://huggingface.co/Xenova/opus-mt-de-en/resolve/main/";

        private static readonly string[] requiredFiles =
        {
            "config.json",
            "source.spm",
            "vocab.json",
            "onnx/" + EncoderFile,
            "onnx/" + DecoderFile,
        };

        public static string Directory { get; } = Path.Combine(AppPaths.DataDirectory, "models", "opus-mt-de-en");

        public static IReadOnlyList<string> MissingFiles()
            => requiredFiles.Where(file => !File.Exists(Path.Combine(Directory, file))).ToList();

        public static async Task DownloadMissingAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            using HttpClient client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

            foreach (string file in MissingFiles())
            {
                string target = Path.Combine(Directory, file);
                string temporary = target + ".part";
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                using HttpResponseMessage response = await client.GetAsync(baseUrl + file, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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
                            progress.Report($"Downloading translation model: {file} {percent}% (one time only)");
                        }
                    }
                }

                File.Move(temporary, target, true);
            }
        }
    }
}
