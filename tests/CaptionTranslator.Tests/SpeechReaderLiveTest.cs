using System.Diagnostics;
using CaptionTranslator.Speech;
using CaptionTranslator.Speech.Piper;
using NAudio.Wave;

namespace CaptionTranslator.Tests
{
    /// <summary>
    /// Uses the real Windows voices and audio devices. Playback runs at volume 0, so nothing is audible
    /// (safe during a meeting), but the full path — synthesize, convert, play on the device — is exercised.
    /// </summary>
    [TestClass]
    [TestCategory("Live")]
    [DoNotParallelize]
    public class SpeechReaderLiveTest
    {
        [TestMethod]
        public void Synthesize_EnglishSentence_ProducesAudibleWave()
        {
            IReadOnlyList<string> voices = WindowsVoice.GetInstalledNames();
            if (voices.Count == 0)
                Assert.Inconclusive("No Windows voices installed.");
            using SpeechReader reader = new SpeechReader();
            reader.SetVoice(new WindowsVoice(voices[0]));

            byte[] wav = reader.SynthesizeAll("This is a test of the caption translator voice.")[0];

            using WaveFileReader wave = new WaveFileReader(new MemoryStream(wav));
            ISampleProvider samples = wave.ToSampleProvider();
            float[] buffer = new float[wave.WaveFormat.SampleRate * wave.WaveFormat.Channels];
            double sumOfSquares = 0;
            long count = 0;
            int read;
            while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int index = 0; index < read; index++)
                    sumOfSquares += buffer[index] * buffer[index];
                count += read;
            }

            double rms = Math.Sqrt(sumOfSquares / count);
            Console.WriteLine($"Duration {wave.TotalTime.TotalSeconds:F2} s, RMS {rms:F4}, format {wave.WaveFormat}");
            Assert.IsTrue(wave.TotalTime > TimeSpan.FromSeconds(1), "Speech is too short.");
            Assert.IsTrue(rms > 0.01, "Speech is silent.");
        }

        [TestMethod]
        public async Task SpeakNowAsync_EachConnectedDevice_PlaysForTheLengthOfTheAudio()
        {
            IReadOnlyList<string> voices = WindowsVoice.GetInstalledNames();
            if (voices.Count == 0)
                Assert.Inconclusive("No Windows voices installed.");
            using SpeechReader reader = new SpeechReader();
            reader.SetVoice(new WindowsVoice(voices[0]));

            const string sentence = "Testing the output device.";
            TimeSpan expected;
            using (WaveFileReader wave = new WaveFileReader(new MemoryStream(reader.SynthesizeAll(sentence)[0])))
                expected = wave.TotalTime;

            IReadOnlyList<AudioOutputDevice> devices = AudioOutputDevice.GetConnected();
            Assert.AreEqual(AudioOutputDevice.DefaultName, devices[0].Name);

            foreach (AudioOutputDevice device in devices)
            {
                reader.SetDevice(device.Id);
                Stopwatch stopwatch = Stopwatch.StartNew();

                await reader.SpeakNowAsync(sentence, 0f, CancellationToken.None).WaitAsync(expected + TimeSpan.FromSeconds(5));

                Console.WriteLine($"{device.Name}: played {stopwatch.Elapsed.TotalSeconds:F2} s (audio {expected.TotalSeconds:F2} s)");
                Assert.IsTrue(stopwatch.Elapsed >= expected * 0.8, $"Playback on {device.Name} ended too early.");
            }
        }

        [TestMethod]
        public async Task SpeakNowAsync_AnyVolume_DoesNotChangeWindowsDeviceVolume()
        {
            IReadOnlyList<string> voices = WindowsVoice.GetInstalledNames();
            if (voices.Count == 0)
                Assert.Inconclusive("No Windows voices installed.");
            using SpeechReader reader = new SpeechReader();
            reader.SetVoice(new WindowsVoice(voices[0]));

            using NAudio.CoreAudioApi.MMDeviceEnumerator enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using NAudio.CoreAudioApi.MMDevice device = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            float before = device.AudioEndpointVolume.MasterVolumeLevelScalar;

            reader.SetDevice(null);
            await reader.SpeakNowAsync("Volume check.", 0f, CancellationToken.None);

            float after = device.AudioEndpointVolume.MasterVolumeLevelScalar;
            Console.WriteLine($"{device.FriendlyName}: Windows volume before {before:P0}, after {after:P0}");
            Assert.AreEqual(before, after, 0.001f, "Playback changed the Windows device volume.");
        }

        [TestMethod]
        public async Task SpeakNowAsync_Cancelled_StopsEarly()
        {
            IReadOnlyList<string> voices = WindowsVoice.GetInstalledNames();
            if (voices.Count == 0)
                Assert.Inconclusive("No Windows voices installed.");
            using SpeechReader reader = new SpeechReader();
            reader.SetVoice(new WindowsVoice(voices[0]));

            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            Stopwatch stopwatch = Stopwatch.StartNew();

            await reader.SpeakNowAsync("This is a long sentence that would take several seconds to read out completely.", 0f, cancellation.Token);

            Console.WriteLine($"Stopped after {stopwatch.Elapsed.TotalSeconds:F2} s");
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(2.5));
        }

        [TestMethod]
        public async Task SpeakNowAsync_PiperVoiceTwoClauses_PlaysAllPartsWithoutLongGaps()
        {
            PiperVoiceInfo kristin = PiperVoiceCatalog.Find("en_US-kristin-medium")!;
            VoiceFiles files = new VoiceFiles();
            if (!files.IsDownloaded(kristin) || EspeakPhonemizer.Shared == null)
                Assert.Inconclusive("Kristin voice or espeak-ng not available.");
            const string sentence = "Good morning everyone, let's start with the status of the project.";
            using SpeechReader reader = new SpeechReader();
            reader.SetVoice(PiperVoice.Load(files.ModelPath(kristin), files.ConfigPath(kristin), EspeakPhonemizer.Shared));
            TimeSpan audio = TimeSpan.Zero;
            foreach (byte[] part in reader.SynthesizeAll(sentence))
            {
                using WaveFileReader wave = new WaveFileReader(new MemoryStream(part));
                audio += wave.TotalTime;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            await reader.SpeakNowAsync(sentence, 0f, CancellationToken.None);

            // First part is synthesized before playback starts; later parts are made while the previous one plays.
            Console.WriteLine($"Audio {audio.TotalSeconds:F2} s, played in {stopwatch.Elapsed.TotalSeconds:F2} s");
            Assert.IsGreaterThan(audio * 0.9, stopwatch.Elapsed);
            Assert.IsLessThan(audio + TimeSpan.FromSeconds(1.5), stopwatch.Elapsed);
        }
    }
}
