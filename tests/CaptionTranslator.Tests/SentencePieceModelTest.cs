using CaptionTranslator.Captions;
using CaptionTranslator.Translation;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class SentencePieceModelTest
    {
        [TestMethod]
        public void Normalize_CollapsesWhitespaceAndAddsWordMarkers()
        {
            Assert.AreEqual("▁Guten▁Morgen", SentencePieceModel.Normalize("  Guten \t Morgen  "));
        }

        [TestMethod]
        public void Encode_RealModel_PiecesJoinBackToNormalizedText()
        {
            string path = Path.Combine(ModelFiles.Directory, "source.spm");
            if (!File.Exists(path))
                Assert.Inconclusive("Model not downloaded.");

            SentencePieceModel model = SentencePieceModel.Load(path);
            IReadOnlyList<string> pieces = model.Encode("Können wir das Meeting verschieben?");

            Assert.IsTrue(model.PieceCount > 10000);
            Assert.AreEqual(SentencePieceModel.Normalize("Können wir das Meeting verschieben?"), string.Concat(pieces));
        }

        [TestMethod]
        public void Split_SentencesAtPunctuation_LastPartMayBeUnfinished()
        {
            CollectionAssert.AreEqual(new[] { "Das ist gut.", "Wirklich?", "Und dann" }, SentenceSplitter.Split("Das ist gut. Wirklich?  Und dann").ToArray());
        }
    }
}
