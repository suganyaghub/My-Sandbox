using System.Diagnostics;
using CaptionTranslator.Speech;
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
            using SpeechReader reader = new SpeechReader();
            if (reader.Voices.Count == 0)
                Assert.Inconclusive("No Windows voices installed.");

            byte[] wav = reader.Synthesize("This is a test of the caption translator voice.");

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
            using SpeechReader reader = new SpeechReader();
            if (reader.Voices.Count == 0)
                Assert.Inconclusive("No Windows voices installed.");

            const string sentence = "Testing the output device.";
            TimeSpan expected;
            using (WaveFileReader wave = new WaveFileReader(new MemoryStream(reader.Synthesize(sentence))))
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
            using SpeechReader reader = new SpeechReader();
            if (reader.Voices.Count == 0)
                Assert.Inconclusive("No Windows voices installed.");

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
            using SpeechReader reader = new SpeechReader();
            if (reader.Voices.Count == 0)
                Assert.Inconclusive("No Windows voices installed.");

            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            Stopwatch stopwatch = Stopwatch.StartNew();

            await reader.SpeakNowAsync("This is a long sentence that would take several seconds to read out completely.", 0f, cancellation.Token);

            Console.WriteLine($"Stopped after {stopwatch.Elapsed.TotalSeconds:F2} s");
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(2.5));
        }
    }
}
