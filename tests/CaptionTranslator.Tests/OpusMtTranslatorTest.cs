using CaptionTranslator.Translation;

namespace CaptionTranslator.Tests
{
    /// <summary>Integration test with the real model; inconclusive when the model is not downloaded.</summary>
    [TestClass]
    [DoNotParallelize]
    public class OpusMtTranslatorTest
    {
        private static OpusMtTranslator? translator;

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            if (ModelFiles.MissingFiles().Count == 0)
                translator = new OpusMtTranslator(ModelFiles.Directory);
        }

        [ClassCleanup]
        public static void ClassCleanup() => translator?.Dispose();

        [TestMethod]
        [DataRow("Guten Morgen zusammen.", "morning")]
        [DataRow("Können wir das Meeting auf morgen verschieben?", "tomorrow")]
        [DataRow("Der Kran hat einen Fehler im Winkelgeber.", "crane")]
        public void Translate_GermanSentence_ContainsExpectedEnglishWord(string german, string expectedWord)
        {
            if (translator == null)
                Assert.Inconclusive("Model not downloaded.");

            string english = translator.Translate(german);

            Console.WriteLine($"{german} -> {english}");
            StringAssert.Contains(english.ToLowerInvariant(), expectedWord);
        }

        [TestMethod]
        public void Translate_TwoSentences_TranslatesBoth()
        {
            if (translator == null)
                Assert.Inconclusive("Model not downloaded.");

            string english = translator.Translate("Das ist gut. Wir fangen jetzt an.");

            Console.WriteLine(english);
            StringAssert.Contains(english.ToLowerInvariant(), "good");
            StringAssert.Contains(english.ToLowerInvariant(), "start");
        }
    }
}
