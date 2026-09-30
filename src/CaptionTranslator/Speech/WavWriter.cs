namespace CaptionTranslator.Speech
{
    /// <summary>Builds 16-bit mono PCM WAV data from float samples.</summary>
    public static class WavWriter
    {
        /// <summary>Loudness below this peak is treated as this peak, so near-silence is not boosted to full volume (as Piper does).</summary>
        private const float minimumPeak = 0.01f;

        /// <summary>Scales the samples so the loudest one uses the full 16-bit range, then writes a WAV file in memory.</summary>
        public static byte[] FromNormalizedFloats(float[] samples, int sampleRate)
        {
            float peak = minimumPeak;
            foreach (float sample in samples)
                peak = Math.Max(peak, Math.Abs(sample));
            float scale = short.MaxValue / peak;

            int dataLength = samples.Length * 2;
            using MemoryStream stream = new MemoryStream(44 + dataLength);
            using (BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                writer.Write("RIFF"u8);
                writer.Write(36 + dataLength);
                writer.Write("WAVE"u8);
                writer.Write("fmt "u8);
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write("data"u8);
                writer.Write(dataLength);
                foreach (float sample in samples)
                    writer.Write((short)Math.Clamp(sample * scale, short.MinValue, short.MaxValue));
            }

            return stream.ToArray();
        }
    }
}
