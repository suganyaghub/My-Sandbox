using CaptionTranslator.Speech;
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class VoiceOptionsTest
    {
        private static readonly PiperVoiceInfo kristin = new PiperVoiceInfo("en_US-kristin-medium", "Kristin – US", "en/en_US/kristin/medium", 64, "PD");

        [TestMethod]
        public void Parse_Prefixes_AndPlainOldName()
        {
            Assert.AreEqual(VoiceId.Natural("en_US-kristin-medium"), VoiceId.Parse("piper:en_US-kristin-medium"));
            Assert.AreEqual(VoiceId.Windows("Microsoft Zira Desktop"), VoiceId.Parse("windows:Microsoft Zira Desktop"));
            Assert.AreEqual(VoiceId.Windows("Microsoft Zira Desktop"), VoiceId.Parse("Microsoft Zira Desktop"));
            Assert.IsNull(VoiceId.Parse(null));
            Assert.IsNull(VoiceId.Parse("  "));
            Assert.IsNull(VoiceId.Parse("piper:"));
        }

        [TestMethod]
        public void ToString_RoundTripsThroughParse()
        {
            VoiceId id = VoiceId.Natural("en_US-kristin-medium");

            Assert.AreEqual("piper:en_US-kristin-medium", id.ToString());
            Assert.AreEqual(id, VoiceId.Parse(id.ToString()));
        }

        [TestMethod]
        public void Build_NaturalAvailable_ListsNaturalVoicesFirst()
        {
            IReadOnlyList<VoiceOption> options = VoiceOptions.Build(new[] { "Microsoft Zira Desktop" }, new[] { kristin }, naturalAvailable: true);

            CollectionAssert.AreEqual(new[] { "piper:en_US-kristin-medium", "windows:Microsoft Zira Desktop" }, options.Select(option => option.Id.ToString()).ToArray());
            Assert.AreSame(kristin, options[0].Natural);
            Assert.IsNull(options[1].Natural);
        }

        [TestMethod]
        public void Build_NaturalUnavailable_ListsOnlyWindowsVoices()
        {
            IReadOnlyList<VoiceOption> options = VoiceOptions.Build(new[] { "Microsoft Zira Desktop" }, new[] { kristin }, naturalAvailable: false);

            Assert.HasCount(1, options);
            Assert.IsFalse(options[0].Id.IsNatural);
        }

        [TestMethod]
        public void Resolve_SavedVoiceInList_ReturnsIt()
        {
            IReadOnlyList<VoiceOption> options = VoiceOptions.Build(new[] { "Microsoft David Desktop", "Microsoft Zira Desktop" }, new[] { kristin }, true);

            Assert.AreEqual("piper:en_US-kristin-medium", VoiceOptions.Resolve(options, "piper:en_US-kristin-medium")?.Id.ToString());
            Assert.AreEqual("windows:Microsoft Zira Desktop", VoiceOptions.Resolve(options, "Microsoft Zira Desktop")?.Id.ToString());
        }

        [TestMethod]
        public void Resolve_UnknownOrEmpty_ReturnsFirstWindowsVoice()
        {
            IReadOnlyList<VoiceOption> options = VoiceOptions.Build(new[] { "Microsoft David Desktop" }, new[] { kristin }, true);

            Assert.AreEqual("windows:Microsoft David Desktop", VoiceOptions.Resolve(options, "piper:en_US-ryan-medium")?.Id.ToString());
            Assert.AreEqual("windows:Microsoft David Desktop", VoiceOptions.Resolve(options, null)?.Id.ToString());
        }

        [TestMethod]
        public void Resolve_NoWindowsVoices_ReturnsFirstOptionOrNull()
        {
            Assert.AreEqual("piper:en_US-kristin-medium", VoiceOptions.Resolve(VoiceOptions.Build(Array.Empty<string>(), new[] { kristin }, true), null)?.Id.ToString());
            Assert.IsNull(VoiceOptions.Resolve(Array.Empty<VoiceOption>(), null));
        }
    }
}
