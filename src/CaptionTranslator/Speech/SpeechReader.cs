using System.Speech.Synthesis;

namespace CaptionTranslator.Speech
{
    /// <summary>
    /// Reads translated lines aloud with the offline Windows (SAPI) voices, one line after another,
    /// on the selected output device. Says the speaker's name when the speaker changes.
    /// </summary>
    public sealed class SpeechReader : IDisposable
    {
        private const int maxPendingLines = 2;

        private readonly SpeechSynthesizer synthesizer = new SpeechSynthesizer();
        private readonly object synthesizerLock = new object();
        private readonly SpeechQueue queue = new SpeechQueue(maxPendingLines);
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private string lastSpeaker = string.Empty;
        private volatile string? deviceId;
        private float volume = 1f;
        private CancellationTokenSource currentLine = new CancellationTokenSource();

        public SpeechReader()
        {
            this.Voices = this.synthesizer.GetInstalledVoices()
                                          .Where(voice => voice.Enabled)
                                          .Select(voice => voice.VoiceInfo)
                                          .OrderBy(voice => voice.Culture.TwoLetterISOLanguageName == "en" ? 0 : 1)
                                          .ThenBy(voice => voice.Name)
                                          .Select(voice => voice.Name)
                                          .ToList();
            _ = Task.Run(() => SpeakLoopAsync(this.shutdown.Token));
        }

        /// <summary>Installed voices, English first.</summary>
        public IReadOnlyList<string> Voices { get; }

        public void SetVoice(string? voiceName)
        {
            string? name = voiceName != null && this.Voices.Contains(voiceName) ? voiceName : this.Voices.FirstOrDefault();
            if (name == null)
                return;

            lock (this.synthesizerLock)
                this.synthesizer.SelectVoice(name);
        }

        /// <summary>Speed from -10 (slowest) to 10 (fastest); 0 is normal.</summary>
        public void SetRate(int rate)
        {
            lock (this.synthesizerLock)
                this.synthesizer.Rate = Math.Clamp(rate, -10, 10);
        }

        /// <summary>Output device id from <see cref="AudioOutputDevice"/>; null = Windows default device.</summary>
        public void SetDevice(string? outputDeviceId) => this.deviceId = outputDeviceId;

        /// <summary>Loudness of the reading voice, 0..1, relative to the device's own Windows volume (which is never changed).</summary>
        public float Volume
        {
            get => Volatile.Read(ref this.volume);
            set => Volatile.Write(ref this.volume, Math.Clamp(value, 0f, 1f));
        }

        public void Enqueue(string speaker, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            string spoken = speaker.Length > 0 && speaker != this.lastSpeaker ? $"{speaker}: {text}" : text;
            this.lastSpeaker = speaker;

            int dropped = this.queue.Enqueue(spoken);
            if (dropped > 0)
                Log.Info($"Read aloud: skipped {dropped} line(s) to stay live.");
        }

        /// <summary>Stops the current line and forgets everything waiting.</summary>
        public void StopAll()
        {
            this.queue.Clear();
            this.lastSpeaker = string.Empty;
            this.currentLine.Cancel();
        }

        /// <summary>Renders text to WAV in memory with the current voice and speed.</summary>
        public byte[] Synthesize(string text)
        {
            using MemoryStream wav = new MemoryStream();
            lock (this.synthesizerLock)
            {
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

        /// <summary>Speaks one text immediately on the selected device (used by the "Test" button).</summary>
        public Task SpeakNowAsync(string text, float volume, CancellationToken cancellationToken)
            => AudioPlayer.PlayAsync(Synthesize(text), this.deviceId, volume, cancellationToken);

        public void Dispose()
        {
            this.shutdown.Cancel();
            this.currentLine.Cancel();
            lock (this.synthesizerLock)
                this.synthesizer.Dispose();
        }

        private async Task SpeakLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    string? text = await this.queue.DequeueAsync(cancellationToken).ConfigureAwait(false);
                    if (text == null)
                        continue;

                    this.currentLine = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    try
                    {
                        await SpeakNowAsync(text, this.Volume, this.currentLine.Token).ConfigureAwait(false);
                    }
                    catch (IOException exception) when (IsDeviceInvalidated(exception))
                    {
                        // Bluetooth headsets briefly disappear when they switch profile (e.g. Teams starts using their microphone).
                        // Wait for the device to come back and play the line once more.
                        Log.Info("Audio device changed while reading aloud; retrying the line.");
                        await Task.Delay(700, this.currentLine.Token).ConfigureAwait(false);
                        await SpeakNowAsync(text, this.Volume, this.currentLine.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    // StopAll cancelled the current line.
                }
                catch (Exception exception)
                {
                    // Boundary of a long-running loop: a failing line (e.g. device unplugged) must not stop reading.
                    Log.Error("Read aloud failed.", exception);
                    await Task.Delay(1000, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        /// <summary>AUDCLNT_E_DEVICE_INVALIDATED: the output device was removed or reconfigured during playback.</summary>
        private static bool IsDeviceInvalidated(IOException exception)
            => exception.InnerException is System.Runtime.InteropServices.COMException com && com.HResult == unchecked((int)0x88890004);
    }
}
