using CaptionTranslator.Speech;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class SpeakerMatcherTest
    {
        [TestMethod]
        [DataRow("Muster Anna", "Muster Anna")]
        [DataRow("muster  anna", "Muster Anna")]
        [DataRow(" Muster Anna ", "Muster Anna")]
        [DataRow("Muster Anna (You)", "Muster Anna")]
        [DataRow("Muster Anna (Guest)", "Muster Anna")]
        [DataRow("Muster Anna", "Muster Anna (EXAMPLE)")]
        public void IsSameSpeaker_SameNameWithVariations_ReturnsTrue(string speaker, string myName)
        {
            Assert.IsTrue(SpeakerMatcher.IsSameSpeaker(speaker, myName));
        }

        [TestMethod]
        [DataRow("Beispiel Max", "Muster Anna")]
        [DataRow("Muster Annabelle", "Muster Anna")]
        [DataRow("", "Muster Anna")]
        [DataRow("Muster Anna", "")]
        [DataRow("Muster Anna", null)]
        public void IsSameSpeaker_DifferentOrMissingName_ReturnsFalse(string speaker, string? myName)
        {
            Assert.IsFalse(SpeakerMatcher.IsSameSpeaker(speaker, myName));
        }
    }
}
