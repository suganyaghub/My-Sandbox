using System.Diagnostics;
using CaptionTranslator.Translation;

namespace CaptionTranslator.Tests
{
    /// <summary>Compares speed and output of the full-precision and int8 model, and thread counts. Prints results only.</summary>
    [TestClass]
    [TestCategory("Live")]
    [DoNotParallelize]
    public class ModelVariantBenchmarkTest
    {
        private static readonly string[] sentences =
        {
            "Guten Morgen zusammen.",
            "Können wir das Meeting auf morgen verschieben?",
            "Also das ist jetzt eigentlich schon ganz nett für Leute, die das jetzt verwenden, und dann kann man einfach weitermachen.",
            "Ich würde keinen Hook schreiben, der bei jedem Befehl alle Unit Tests durchlaufen lässt, das dauert viel zu lang.",
            "Dann machen wir einfach ein paar Minuten früher Pause.",
            "Wir schauen uns das nach der Pause noch einmal genauer an.",
        };

        [TestMethod]
        [DataRow("encoder_model.onnx", "decoder_model_merged.onnx", 6)]
        [DataRow("encoder_model.onnx", "decoder_model_merged.onnx", 4)]
        [DataRow("encoder_model.onnx", "decoder_model_merged.onnx", 8)]
        [DataRow("encoder_model.onnx", "decoder_model_merged.onnx", 12)]
        public void Translate_Variant_ReportsSpeedAndOutput(string encoder, string decoder, int threads)
        {
            if (!File.Exists(Path.Combine(ModelFiles.Directory, "onnx", encoder)) || !File.Exists(Path.Combine(ModelFiles.Directory, "onnx", decoder)))
                Assert.Inconclusive("Model variant not downloaded.");

            using OpusMtTranslator translator = new OpusMtTranslator(ModelFiles.Directory, encoder, decoder, threads);
            translator.Translate("Hallo.");

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<string> outputs = sentences.Select(translator.Translate).ToList();
            double averageMs = stopwatch.Elapsed.TotalMilliseconds / sentences.Length;

            Console.WriteLine($"=== {encoder} / {threads} threads: {averageMs:F0} ms per sentence");
            foreach (string output in outputs)
                Console.WriteLine("  " + output);
        }
    }
}
