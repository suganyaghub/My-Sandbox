using CaptionTranslator.Speech;
using NAudio.Wave;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class WavWriterTest
    {
        [TestMethod]
        public void FromNormalizedFloats_Samples_WritesMono16BitWaveAtSampleRate()
        {
            byte[] wav = WavWriter.FromNormalizedFloats(new[] { 0f, 0.5f, -0.25f }, 22050);

            using WaveFileReader reader = new WaveFileReader(new MemoryStream(wav));
            Assert.AreEqual(22050, reader.WaveFormat.SampleRate);
            Assert.AreEqual(1, reader.WaveFormat.Channels);
            Assert.AreEqual(16, reader.WaveFormat.BitsPerSample);
            Assert.AreEqual(3, reader.SampleCount);
        }

        [TestMethod]
        public void FromNormalizedFloats_LoudestSample_IsScaledToFullRange()
        {
            short[] samples = ReadSamples(WavWriter.FromNormalizedFloats(new[] { 0f, 0.5f, -0.25f }, 22050));

            CollectionAssert.AreEqual(new short[] { 0, 32767, -16383 }, samples);
        }

        [TestMethod]
        public void FromNormalizedFloats_VeryQuietSignal_IsNotBoostedAboveMinimumPeak()
        {
            short[] samples = ReadSamples(WavWriter.FromNormalizedFloats(new[] { 0.001f }, 22050));

            Assert.AreEqual((short)3276, samples[0]);
        }

        [TestMethod]
        public void FromNormalizedFloats_Silence_StaysSilent()
        {
            short[] samples = ReadSamples(WavWriter.FromNormalizedFloats(new float[4], 16000));

            CollectionAssert.AreEqual(new short[4], samples);
        }

        private static short[] ReadSamples(byte[] wav)
        {
            using WaveFileReader reader = new WaveFileReader(new MemoryStream(wav));
            byte[] data = new byte[reader.Length];
            int read = reader.Read(data, 0, data.Length);
            short[] samples = new short[read / 2];
            Buffer.BlockCopy(data, 0, samples, 0, read);
            return samples;
        }
    }
}
