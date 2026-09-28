using System.Diagnostics;
using CaptionTranslator.Translation;

namespace CaptionTranslator.Tests
{
    [TestClass]
    [TestCategory("Live")]
    public class TranslatorTimingTest
    {
        [TestMethod]
        public void Translate_LongCaption_Timing()
        {
            if (ModelFiles.MissingFiles().Count > 0)
                Assert.Inconclusive("Model not downloaded.");

            using OpusMtTranslator translator = new OpusMtTranslator(ModelFiles.Directory);
            translator.Translate("Hallo.");
            string german = "Wir sollten klären, welches Werkzeug wir für die Spracherkennung verwenden und ob man das auch lokal auf dem eigenen Rechner laufen lassen kann, ohne dass die Daten irgendwo hingeschickt werden";

            Stopwatch stopwatch = Stopwatch.StartNew();
            string english = translator.Translate(german);
            Console.WriteLine($"{stopwatch.ElapsedMilliseconds} ms: {english}");
        }
    }
}
