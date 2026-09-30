using System.Text.Json;

namespace CaptionTranslator.Speech.Piper
{
    /// <summary>Settings of a Piper voice, read from its "&lt;voice&gt;.onnx.json" file.</summary>
    public sealed class PiperVoiceConfig
    {
        public int SampleRate { get; init; }

        /// <summary>espeak-ng voice used when the model was trained, e.g. "en-us"; phonemes must be made with the same one.</summary>
        public string EspeakVoice { get; init; } = "en-us";

        public float NoiseScale { get; init; }

        public float LengthScale { get; init; }

        public float NoiseW { get; init; }

        public int NumSpeakers { get; init; } = 1;

        public IReadOnlyDictionary<string, long[]> PhonemeIdMap { get; init; } = new Dictionary<string, long[]>();

        /// <exception cref="JsonException">The text is not JSON.</exception>
        /// <exception cref="KeyNotFoundException">A required value is missing.</exception>
        /// <exception cref="NotSupportedException">The voice does not use espeak phonemes.</exception>
        public static PiperVoiceConfig Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string phonemeType = root.TryGetProperty("phoneme_type", out JsonElement type) ? type.GetString() ?? "espeak" : "espeak";
            if (phonemeType != "espeak")
                throw new NotSupportedException($"Piper phoneme type '{phonemeType}' is not supported.");

            JsonElement inference = root.GetProperty("inference");
            return new PiperVoiceConfig
            {
                SampleRate = root.GetProperty("audio").GetProperty("sample_rate").GetInt32(),
                EspeakVoice = root.GetProperty("espeak").GetProperty("voice").GetString() ?? "en-us",
                NoiseScale = inference.GetProperty("noise_scale").GetSingle(),
                LengthScale = inference.GetProperty("length_scale").GetSingle(),
                NoiseW = inference.GetProperty("noise_w").GetSingle(),
                NumSpeakers = root.TryGetProperty("num_speakers", out JsonElement speakers) ? speakers.GetInt32() : 1,
                PhonemeIdMap = root.GetProperty("phoneme_id_map")
                                   .EnumerateObject()
                                   .ToDictionary(entry => entry.Name, entry => entry.Value.EnumerateArray().Select(id => id.GetInt64()).ToArray()),
            };
        }
    }
}
