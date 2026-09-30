using System.Runtime.InteropServices;
using System.Text;

namespace CaptionTranslator.Speech.Piper
{
    /// <summary>
    /// English text → IPA phonemes with espeak-ng. espeak-ng has global state, so there is one instance per process
    /// and all calls are serialized.
    /// </summary>
    public sealed class EspeakPhonemizer
    {
        private static readonly object loadLock = new object();
        private static EspeakPhonemizer? shared;
        private static string? loadError;

        private readonly object phonemizeLock = new object();
        private string? currentVoice;

        private EspeakPhonemizer()
        {
        }

        /// <summary>The process-wide instance, or null if espeak-ng could not be loaded (see <see cref="LoadError"/>).</summary>
        public static EspeakPhonemizer? Shared
        {
            get
            {
                EnsureLoaded();
                return shared;
            }
        }

        public static string? LoadError
        {
            get
            {
                EnsureLoaded();
                return loadError;
            }
        }

        /// <exception cref="InvalidOperationException">espeak-ng does not know <paramref name="voice"/>.</exception>
        public string Phonemize(string text, string voice)
        {
            lock (this.phonemizeLock)
            {
                if (voice != this.currentVoice)
                {
                    int result = EspeakNativeMethods.espeak_SetVoiceByName(voice);
                    if (result != EspeakNativeMethods.ResultOk)
                        throw new InvalidOperationException($"espeak-ng voice '{voice}' not found (code {result}).");
                    this.currentVoice = voice;
                }

                StringBuilder phonemes = new StringBuilder();
                IntPtr buffer = Marshal.StringToCoTaskMemUTF8(text);
                try
                {
                    IntPtr position = buffer;
                    while (position != IntPtr.Zero)
                    {
                        string? part = Marshal.PtrToStringUTF8(EspeakNativeMethods.espeak_TextToPhonemes(ref position, EspeakNativeMethods.CharsUtf8, EspeakNativeMethods.PhonemesIpa));
                        if (string.IsNullOrEmpty(part))
                            continue;
                        if (phonemes.Length > 0)
                            phonemes.Append(' ');
                        phonemes.Append(part);
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(buffer);
                }

                return phonemes.ToString();
            }
        }

        private static void EnsureLoaded()
        {
            lock (loadLock)
            {
                if (shared != null || loadError != null)
                    return;

                string dataParent = AppContext.BaseDirectory;
                if (!Directory.Exists(Path.Combine(dataParent, "espeak-ng-data")))
                {
                    loadError = "espeak-ng-data folder is missing next to the app.";
                }
                else
                {
                    try
                    {
                        int sampleRate = EspeakNativeMethods.espeak_Initialize(EspeakNativeMethods.AudioOutputSynchronous, 0, dataParent, 0);
                        if (sampleRate > 0)
                            shared = new EspeakPhonemizer();
                        else
                            loadError = $"espeak-ng could not start (code {sampleRate}).";
                    }
                    catch (Exception exception) when (exception is DllNotFoundException || exception is BadImageFormatException || exception is EntryPointNotFoundException)
                    {
                        loadError = "libespeak-ng.dll could not be loaded: " + exception.Message;
                    }
                }

                if (loadError != null)
                    Log.Info("Natural voices unavailable: " + loadError);
            }
        }
    }
}
