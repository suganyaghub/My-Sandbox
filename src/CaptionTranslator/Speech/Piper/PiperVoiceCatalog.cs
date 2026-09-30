namespace CaptionTranslator.Speech.Piper
{
    /// <summary>
    /// Piper voices offered for download. Only voices whose training data license allows free use
    /// (public domain, CC0, CC-BY, CC-BY-SA); non-commercial and unclear licenses are left out.
    /// Sizes and licenses from voices.json and each MODEL_CARD, checked 2026-09-30.
    /// </summary>
    public static class PiperVoiceCatalog
    {
        public static IReadOnlyList<PiperVoiceInfo> All { get; } = new[]
        {
            new PiperVoiceInfo("en_US-kristin-medium", "Kristin – US", "en/en_US/kristin/medium", 64, "Public domain (LibriVox)"),
            new PiperVoiceInfo("en_US-ljspeech-medium", "Linda – US", "en/en_US/ljspeech/medium", 64, "Public domain (LJ Speech)"),
            new PiperVoiceInfo("en_US-john-medium", "John – US", "en/en_US/john/medium", 64, "Public domain (LibriVox)"),
            new PiperVoiceInfo("en_US-norman-medium", "Norman – US", "en/en_US/norman/medium", 64, "Public domain (LibriVox)"),
            new PiperVoiceInfo("en_US-bryce-medium", "Bryce – US", "en/en_US/bryce/medium", 64, "Public domain"),
            new PiperVoiceInfo("en_US-joe-medium", "Joe – US", "en/en_US/joe/medium", 63, "CC0"),
            new PiperVoiceInfo("en_US-mike-medium", "Mike – US", "en/en_US/mike/medium", 63, "CC0"),
            new PiperVoiceInfo("en_GB-cori-medium", "Cori – UK", "en/en_GB/cori/medium", 64, "Public domain (LibriVox)"),
            new PiperVoiceInfo("en_GB-alba-medium", "Alba – UK (Scottish)", "en/en_GB/alba/medium", 63, "CC BY 4.0"),
            new PiperVoiceInfo("en_GB-northern_english_male-medium", "Northern English – UK", "en/en_GB/northern_english_male/medium", 63, "CC BY-SA 4.0"),
        };

        public static PiperVoiceInfo? Find(string id) => All.FirstOrDefault(voice => voice.Id == id);
    }
}
