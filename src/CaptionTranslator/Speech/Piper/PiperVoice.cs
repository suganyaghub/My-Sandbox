using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CaptionTranslator.Speech.Piper
{
    /// <summary>An offline neural Piper voice: espeak-ng phonemes → ONNX model → audio, clause by clause.</summary>
    public sealed class PiperVoice : IVoice
    {
        private const int defaultThreads = 4;

        private readonly InferenceSession session;
        private readonly PiperVoiceConfig config;
        private readonly EspeakPhonemizer phonemizer;
        private readonly object runLock = new object();
        private readonly HashSet<string> reportedMissing = new HashSet<string>();
        private bool disposed;

        public PiperVoice(string modelPath, PiperVoiceConfig config, EspeakPhonemizer phonemizer, int threads)
        {
            this.config = config;
            this.phonemizer = phonemizer;
            SessionOptions options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Max(1, threads),
            };
            this.session = new InferenceSession(modelPath, options);
        }

        /// <summary>Loads a voice (takes a few seconds; call off the UI thread).</summary>
        /// <exception cref="OnnxRuntimeException">The model file is damaged.</exception>
        /// <exception cref="System.Text.Json.JsonException">The config file is damaged.</exception>
        public static PiperVoice Load(string modelPath, string configPath, EspeakPhonemizer phonemizer)
            => new PiperVoice(modelPath, PiperVoiceConfig.Parse(File.ReadAllText(configPath)), phonemizer, Math.Min(defaultThreads, Environment.ProcessorCount));

        /// <summary>Speed -10..10 → Piper length scale (0 → 1, +10 → 0.5 = twice as fast, -10 → 2).</summary>
        public static float RateToLengthScale(int rate) => (float)Math.Pow(2, -Math.Clamp(rate, -10, 10) / 10.0);

        public IEnumerable<byte[]> SynthesizeWavChunks(string text, int rate)
        {
            float lengthScale = this.config.LengthScale * RateToLengthScale(rate);
            foreach (Clause clause in ClauseSplitter.Split(text))
            {
                string phonemes = this.phonemizer.Phonemize(clause.Text, this.config.EspeakVoice);
                if (clause.Punctuation is char mark)
                    phonemes += mark;

                List<string> missing = new List<string>();
                long[] ids = PiperPhonemeEncoder.Encode(phonemes, this.config.PhonemeIdMap, missing);
                ReportMissing(missing);
                if (ids.Length <= 3)
                    continue;

                yield return WavWriter.FromNormalizedFloats(Run(ids, lengthScale), this.config.SampleRate);
            }
        }

        public void Dispose()
        {
            lock (this.runLock)
            {
                this.disposed = true;
                this.session.Dispose();
            }
        }

        private float[] Run(long[] ids, float lengthScale)
        {
            List<NamedOnnxValue> inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", new DenseTensor<long>(ids, new[] { 1, ids.Length })),
                NamedOnnxValue.CreateFromTensor("input_lengths", new DenseTensor<long>(new long[] { ids.Length }, new[] { 1 })),
                NamedOnnxValue.CreateFromTensor("scales", new DenseTensor<float>(new[] { this.config.NoiseScale, lengthScale, this.config.NoiseW }, new[] { 3 })),
            };

            lock (this.runLock)
            {
                ObjectDisposedException.ThrowIf(this.disposed, this);
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = this.session.Run(inputs);
                return results[0].AsEnumerable<float>().ToArray();
            }
        }

        private void ReportMissing(List<string> missing)
        {
            foreach (string phoneme in missing)
            {
                if (this.reportedMissing.Add(phoneme))
                    Log.Info($"Piper voice: phoneme U+{char.ConvertToUtf32(phoneme, 0):X4} is not in the voice and is skipped.");
            }
        }
    }
}
