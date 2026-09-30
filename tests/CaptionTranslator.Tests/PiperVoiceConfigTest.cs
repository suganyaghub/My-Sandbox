using System.Text.Json;
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class PiperVoiceConfigTest
    {
        private const string json = """
            {
              "audio": { "sample_rate": 22050, "quality": "medium" },
              "espeak": { "voice": "en-us" },
              "inference": { "noise_scale": 0.667, "length_scale": 1.1, "noise_w": 0.8 },
              "phoneme_type": "espeak",
              "num_speakers": 1,
              "phoneme_id_map": { "_": [0], "^": [1], "$": [2], "a": [14] }
            }
            """;

        [TestMethod]
        public void Parse_PiperConfig_ReadsAllValues()
        {
            PiperVoiceConfig config = PiperVoiceConfig.Parse(json);

            Assert.AreEqual(22050, config.SampleRate);
            Assert.AreEqual("en-us", config.EspeakVoice);
            Assert.AreEqual(0.667f, config.NoiseScale, 0.0001f);
            Assert.AreEqual(1.1f, config.LengthScale, 0.0001f);
            Assert.AreEqual(0.8f, config.NoiseW, 0.0001f);
            Assert.AreEqual(1, config.NumSpeakers);
            CollectionAssert.AreEqual(new long[] { 14 }, config.PhonemeIdMap["a"]);
            Assert.HasCount(4, config.PhonemeIdMap);
        }

        [TestMethod]
        public void Parse_TextPhonemeType_ThrowsNotSupported()
        {
            string textVoice = json.Replace("\"phoneme_type\": \"espeak\"", "\"phoneme_type\": \"text\"");

            Assert.ThrowsExactly<NotSupportedException>(() => PiperVoiceConfig.Parse(textVoice));
        }

        [TestMethod]
        public void Parse_MissingPhonemeMap_ThrowsKeyNotFound()
        {
            string broken = """{ "audio": { "sample_rate": 22050 }, "espeak": { "voice": "en" }, "inference": { "noise_scale": 0.6, "length_scale": 1, "noise_w": 0.8 } }""";

            Assert.ThrowsExactly<KeyNotFoundException>(() => PiperVoiceConfig.Parse(broken));
        }

        [TestMethod]
        public void Parse_InvalidJson_ThrowsJsonException()
        {
            Assert.Throws<JsonException>(() => PiperVoiceConfig.Parse("{ not json"));
        }
    }
}
