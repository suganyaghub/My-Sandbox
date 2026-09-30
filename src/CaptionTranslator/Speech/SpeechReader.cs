namespace CaptionTranslator.Speech
{
    /// <summary>
    /// Reads translated lines aloud with the current <see cref="IVoice"/>, one line after another, on the selected
    /// output device. Says the speaker's name when the speaker changes. Part n+1 of a line is synthesized while part n plays.
    /// </summary>
    public sealed class SpeechReader : IDisposable
    {
        private const int maxPendingLines = 2;
        private const int slowPartMilliseconds = 1500;

        private readonly SpeechQueue queue = new SpeechQueue(maxPendingLines);
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly object voiceLock = new object();
        private IVoice? voice;
        private volatile int rate;
        private string lastSpeaker = string.Empty;
        private volatile string? deviceId;
        private float volume = 1f;
        private CancellationTokenSource currentLine = new CancellationTokenSource();

        public SpeechReader()
        {
            _ = Task.Run(() => SpeakLoopAsync(this.shutdown.Token));
        }

        /// <summary>
        /// Uses <paramref name="newVoice"/> from the next part on (null = silent). The reader owns the voice: the previous
        /// one is disposed in the background after its current part (never on the calling UI thread).
        /// </summary>
        public void SetVoice(IVoice? newVoice)
        {
            IVoice? old;
            lock (this.voiceLock)
            {
                old = this.voice;
                this.voice = newVoice;
            }

            if (old != null)
                _ = Task.Run(() => DisposeVoice(old));
        }

        /// <summary>Speed from -10 (slowest) to 10 (fastest); 0 is normal.</summary>
        public void SetRate(int value) => this.rate = Math.Clamp(value, -10, 10);

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

        /// <summary>All WAV parts of a text with the current voice (used by tests). Empty if there is no voice.</summary>
        public IReadOnlyList<byte[]> SynthesizeAll(string text)
        {
            IVoice? speaking;
            lock (this.voiceLock)
                speaking = this.voice;
            return speaking?.SynthesizeWavChunks(text, this.rate).ToList() ?? new List<byte[]>();
        }

        /// <summary>Speaks one text now on the selected device (also used by the "Test" button).</summary>
        public async Task SpeakNowAsync(string text, float volume, CancellationToken cancellationToken)
        {
            IVoice? speaking;
            lock (this.voiceLock)
                speaking = this.voice;
            if (speaking == null)
                return;

            using IEnumerator<byte[]> parts = speaking.SynthesizeWavChunks(text, this.rate).GetEnumerator();
            Task<byte[]?> next = Task.Run(() => NextPart(speaking, parts), CancellationToken.None);
            try
            {
                while (await next.ConfigureAwait(false) is byte[] part)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    next = Task.Run(() => NextPart(speaking, parts), CancellationToken.None);
                    await AudioPlayer.PlayAsync(part, this.deviceId, volume, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                // The worker may still use the enumerator; wait for it before the enumerator is disposed.
                await WaitForPendingPartAsync(next).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            this.shutdown.Cancel();
            this.currentLine.Cancel();
            IVoice? old;
            lock (this.voiceLock)
            {
                old = this.voice;
                this.voice = null;
            }

            old?.Dispose();
        }

        /// <summary>Next WAV part, or null at the end of the text or when the voice was replaced meanwhile.</summary>
        private byte[]? NextPart(IVoice speaking, IEnumerator<byte[]> parts)
        {
            lock (this.voiceLock)
            {
                if (!ReferenceEquals(speaking, this.voice))
                    return null;
            }

            try
            {
                System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                byte[]? part = parts.MoveNext() ? parts.Current : null;
                if (stopwatch.ElapsedMilliseconds > slowPartMilliseconds)
                    Log.Info($"Read aloud: slow part ({speaking.GetType().Name}, {stopwatch.ElapsedMilliseconds} ms).");
                return part;
            }
            catch (ObjectDisposedException)
            {
                // The voice was replaced (and disposed) while this part was being made: end the line quietly.
                return null;
            }
        }

        private static void DisposeVoice(IVoice old)
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            old.Dispose();
            Log.Info($"Read aloud: previous voice ({old.GetType().Name}) released after {stopwatch.ElapsedMilliseconds} ms.");
        }

        private static async Task WaitForPendingPartAsync(Task<byte[]?> pending)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The line was stopped or has already failed; this part is not played. The reason stays in the log.
                Log.Info("Read aloud: discarded a part: " + exception.Message);
            }
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
