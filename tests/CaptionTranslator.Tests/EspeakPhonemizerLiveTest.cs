using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    [TestCategory("Live")]
    [DoNotParallelize]
    public class EspeakPhonemizerLiveTest
    {
        [TestMethod]
        public void Phonemize_EnglishWords_ReturnsIpaWithStressMarks()
        {
            EspeakPhonemizer? phonemizer = EspeakPhonemizer.Shared;
            Assert.IsNotNull(phonemizer, EspeakPhonemizer.LoadError);

            string phonemes = phonemizer.Phonemize("Good morning everyone", "en-us");

            Console.WriteLine(phonemes);
            Assert.Contains("ˈ", phonemes);
            Assert.Contains("ɡ", phonemes);
            Assert.AreEqual(2, phonemes.Count(character => character == ' '), "Three words expected.");
        }

        [TestMethod]
        public void Phonemize_UnknownVoice_ThrowsInvalidOperation()
        {
            EspeakPhonemizer? phonemizer = EspeakPhonemizer.Shared;
            Assert.IsNotNull(phonemizer, EspeakPhonemizer.LoadError);

            Assert.ThrowsExactly<InvalidOperationException>(() => phonemizer.Phonemize("Hello", "xx-notalanguage"));
        }
    }
}
