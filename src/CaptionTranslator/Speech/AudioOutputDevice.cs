using NAudio.CoreAudioApi;

namespace CaptionTranslator.Speech
{
    /// <summary>An audio output (speakers, headset, Bluetooth). <see cref="Id"/> null means "Windows default device".</summary>
    public sealed record AudioOutputDevice(string? Id, string Name)
    {
        public const string DefaultName = "Default output device";

        public static AudioOutputDevice Default { get; } = new AudioOutputDevice(null, DefaultName);

        public override string ToString() => this.Name;

        /// <summary>Currently connected output devices, with "Default output device" first.</summary>
        public static IReadOnlyList<AudioOutputDevice> GetConnected()
        {
            List<AudioOutputDevice> devices = new List<AudioOutputDevice> { Default };
            using MMDeviceEnumerator enumerator = new MMDeviceEnumerator();

            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                    devices.Add(new AudioOutputDevice(device.ID, device.FriendlyName));
            }

            return devices;
        }
    }
}
