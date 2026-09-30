using CaptionTranslator.Speech.Piper;
using NAudio.Wave;

namespace CaptionTranslator.Tests
{
    [TestClass]
    [TestCategory("Live")]
    [DoNotParallelize]
    public class PiperVoiceLiveTest
    {
        [TestMethod]
        public void SynthesizeWavChunks_TwoClauses_ReturnsOneAudibleWavPerClause()
        {
            PiperVoiceInfo kristin = PiperVoiceCatalog.Find("en_US-kristin-medium")!;
            VoiceFiles files = new VoiceFiles();
            if (!files.IsDownloaded(kristin))
                Assert.Inconclusive("Kristin voice not downloaded.");
            EspeakPhonemizer? phonemizer = EspeakPhonemizer.Shared;
            Assert.IsNotNull(phonemizer, EspeakPhonemizer.LoadError);

            using PiperVoice voice = PiperVoice.Load(files.ModelPath(kristin), files.ConfigPath(kristin), phonemizer);
            List<byte[]> parts = voice.SynthesizeWavChunks("Good morning everyone, let's start with the status.", 0).ToList();

            Assert.HasCount(2, parts);
            foreach (byte[] part in parts)
            {
                using WaveFileReader reader = new WaveFileReader(new MemoryStream(part));
                Console.WriteLine($"{reader.TotalTime.TotalSeconds:F2} s");
                Assert.AreEqual(22050, reader.WaveFormat.SampleRate);
                Assert.IsGreaterThan(TimeSpan.FromSeconds(0.5), reader.TotalTime);
            }
        }

        [TestMethod]
        public void SynthesizeWavChunks_AfterDispose_ThrowsObjectDisposed()
        {
            PiperVoiceInfo kristin = PiperVoiceCatalog.Find("en_US-kristin-medium")!;
            VoiceFiles files = new VoiceFiles();
            if (!files.IsDownloaded(kristin))
                Assert.Inconclusive("Kristin voice not downloaded.");

            PiperVoice voice = PiperVoice.Load(files.ModelPath(kristin), files.ConfigPath(kristin), EspeakPhonemizer.Shared!);
            voice.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(() => voice.SynthesizeWavChunks("Hello.", 0).ToList());
        }
    }
}
