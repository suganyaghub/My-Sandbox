using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class PiperPhonemeEncoderTest
    {
        private static readonly IReadOnlyDictionary<string, long[]> map = new Dictionary<string, long[]>
        {
            ["_"] = new long[] { 0 },
            ["^"] = new long[] { 1 },
            ["$"] = new long[] { 2 },
            [" "] = new long[] { 3 },
            ["a"] = new long[] { 14 },
            ["b"] = new long[] { 15 },
            ["ˈ"] = new long[] { 120 },
            ["ə"] = new long[] { 59, 60 },
        };

        [TestMethod]
        public void Encode_Phonemes_AddsStartPadAfterEachAndEnd()
        {
            long[] ids = PiperPhonemeEncoder.Encode("ˈa b", map);

            CollectionAssert.AreEqual(new long[] { 1, 0, 120, 0, 14, 0, 3, 0, 15, 0, 2 }, ids);
        }

        [TestMethod]
        public void Encode_PhonemeWithSeveralIds_AddsAllIds()
        {
            long[] ids = PiperPhonemeEncoder.Encode("ə", map);

            CollectionAssert.AreEqual(new long[] { 1, 0, 59, 60, 0, 2 }, ids);
        }

        [TestMethod]
        public void Encode_UnknownPhoneme_IsSkippedAndReported()
        {
            List<string> missing = new List<string>();

            long[] ids = PiperPhonemeEncoder.Encode("axb", map, missing);

            CollectionAssert.AreEqual(new long[] { 1, 0, 14, 0, 15, 0, 2 }, ids);
            CollectionAssert.AreEqual(new[] { "x" }, missing);
        }

        [TestMethod]
        public void Encode_Empty_ReturnsOnlyMarkers()
        {
            CollectionAssert.AreEqual(new long[] { 1, 0, 2 }, PiperPhonemeEncoder.Encode("", map));
        }
    }
}
