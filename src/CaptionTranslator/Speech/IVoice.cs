namespace CaptionTranslator.Speech
{
    /// <summary>A voice that turns text into speech audio.</summary>
    public interface IVoice : IDisposable
    {
        /// <summary>
        /// Lazily yields the speech as WAV data, one part per clause (or one part for voices that cannot split),
        /// so playback can start before the whole text is synthesized.
        /// </summary>
        /// <param name="rate">Speed from -10 (slowest) to 10 (fastest); 0 is normal.</param>
        /// <exception cref="ObjectDisposedException">The voice was disposed (e.g. replaced by another voice).</exception>
        IEnumerable<byte[]> SynthesizeWavChunks(string text, int rate);
    }
}
