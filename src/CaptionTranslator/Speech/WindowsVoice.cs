using System.Speech.Synthesis;

namespace CaptionTranslator.Speech
{
    /// <summary>An offline Windows (SAPI) voice.</summary>
    public sealed class WindowsVoice : IVoice
    {
        private readonly SpeechSynthesizer synthesizer = new SpeechSynthesizer();
        private readonly object synthesizerLock = new object();
        private bool disposed;

        /// <exception cref="ArgumentException">No enabled Windows voice has this name.</exception>
        public WindowsVoice(string name)
        {
            this.synthesizer.SelectVoice(name);
        }

        /// <summary>Installed and enabled Windows voices, English first.</summary>
        public static IReadOnlyList<string> GetInstalledNames()
        {
            using SpeechSynthesizer synthesizer = new SpeechSynthesizer();
            return synthesizer.GetInstalledVoices()
                              .Where(voice => voice.Enabled)
                              .Select(voice => voice.VoiceInfo)
                              .OrderBy(voice => voice.Culture.TwoLetterISOLanguageName == "en" ? 0 : 1)
                              .ThenBy(voice => voice.Name)
                              .Select(voice => voice.Name)
                              .ToList();
        }

        public IEnumerable<byte[]> SynthesizeWavChunks(string text, int rate)
        {
            yield return Synthesize(text, rate);
        }

        public void Dispose()
        {
            lock (this.synthesizerLock)
            {
                this.disposed = true;
                this.synthesizer.Dispose();
            }
        }

        private byte[] Synthesize(string text, int rate)
        {
            using MemoryStream wav = new MemoryStream();
            lock (this.synthesizerLock)
            {
                ObjectDisposedException.ThrowIf(this.disposed, this);
                this.synthesizer.Rate = Math.Clamp(rate, -10, 10);
                this.synthesizer.SetOutputToWaveStream(wav);
                try
                {
                    this.synthesizer.Speak(text);
                }
                finally
                {
                    this.synthesizer.SetOutputToNull();
                }
            }

            return wav.ToArray();
        }
    }
}
