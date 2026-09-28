using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CaptionTranslator.Speech
{
    /// <summary>Plays a WAV buffer on a chosen output device (WASAPI shared mode), converting to the device's format.</summary>
    public static class AudioPlayer
    {
        /// <summary>
        /// Plays the WAV data and completes when playback has finished or was cancelled.
        /// If the device is not connected any more, the Windows default device is used.
        /// </summary>
        /// <param name="volume">
        /// Loudness of this app's audio only, 0..1 (0 plays silently, used by tests). Applied to the samples;
        /// the Windows volume of the device is never changed.
        /// </param>
        public static async Task PlayAsync(byte[] wav, string? deviceId, float volume, CancellationToken cancellationToken)
        {
            using MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
            using MMDevice device = ResolveDevice(enumerator, deviceId);
            WaveFormat mixFormat = device.AudioClient.MixFormat;

            using MemoryStream stream = new MemoryStream(wav);
            using WaveFileReader reader = new WaveFileReader(stream);
            ISampleProvider samples = reader.ToSampleProvider();

            if (samples.WaveFormat.Channels == 1 && mixFormat.Channels >= 2)
                samples = new MonoToStereoSampleProvider(samples);
            if (samples.WaveFormat.SampleRate != mixFormat.SampleRate)
                samples = new WdlResamplingSampleProvider(samples, mixFormat.SampleRate);

            // Do NOT use WasapiOut.Volume: in NAudio it sets the device's Windows master volume.
            samples = new VolumeSampleProvider(samples) { Volume = Math.Clamp(volume, 0f, 1f) };

            using WasapiOut output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            TaskCompletionSource<Exception?> stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (sender, e) => stopped.TrySetResult(e.Exception);
            output.Init(samples);
            output.Play();

            using (cancellationToken.Register(() => output.Stop()))
            {
                Exception? error = await stopped.Task.ConfigureAwait(false);
                if (error != null)
                    throw new IOException($"Playback on '{device.FriendlyName}' failed.", error);
            }
        }

        private static MMDevice ResolveDevice(MMDeviceEnumerator enumerator, string? deviceId)
        {
            if (deviceId != null)
            {
                foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    if (device.ID == deviceId)
                        return device;
                    device.Dispose();
                }

                Log.Info("Selected audio device is not connected; using the Windows default device.");
            }

            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
    }
}
