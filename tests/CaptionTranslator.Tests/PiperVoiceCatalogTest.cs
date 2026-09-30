using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class PiperVoiceCatalogTest
    {
        [TestMethod]
        public void All_Entries_AreCompleteAndConsistent()
        {
            Assert.HasCount(10, PiperVoiceCatalog.All);
            foreach (PiperVoiceInfo voice in PiperVoiceCatalog.All)
            {
                string[] parts = voice.Id.Split('-');   // en_US-kristin-medium
                Assert.AreEqual($"en/{parts[0]}/{parts[1]}/{parts[2]}", voice.Folder, voice.Id);
                Assert.IsGreaterThan(0, voice.SizeMegabytes, voice.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(voice.DisplayName), voice.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(voice.License), voice.Id);
            }
        }

        [TestMethod]
        public void All_Ids_AreUnique()
        {
            Assert.AreEqual(PiperVoiceCatalog.All.Count, PiperVoiceCatalog.All.Select(voice => voice.Id).Distinct().Count());
        }

        [TestMethod]
        public void Find_KnownAndUnknownId_ReturnsVoiceOrNull()
        {
            Assert.AreEqual("Kristin – US", PiperVoiceCatalog.Find("en_US-kristin-medium")?.DisplayName);
            Assert.IsNull(PiperVoiceCatalog.Find("en_US-ryan-medium"));
        }
    }
}
