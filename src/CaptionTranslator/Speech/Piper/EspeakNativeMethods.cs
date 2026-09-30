using System.Runtime.InteropServices;

namespace CaptionTranslator.Speech.Piper
{
    /// <summary>espeak-ng C API (speak_lib.h), from libespeak-ng.dll next to the exe.</summary>
    internal static class EspeakNativeMethods
    {
        public const int AudioOutputSynchronous = 2;
        public const int CharsUtf8 = 1;
        public const int PhonemesIpa = 0x02;
        public const int ResultOk = 0;

        private const string library = "libespeak-ng.dll";

        /// <returns>Sample rate, or a negative error code.</returns>
        [DllImport(library, CallingConvention = CallingConvention.Cdecl)]
        public static extern int espeak_Initialize(int output, int bufferLength, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int options);

        [DllImport(library, CallingConvention = CallingConvention.Cdecl)]
        public static extern int espeak_SetVoiceByName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        /// <summary>Phonemes of the next clause; advances <paramref name="textPointer"/> and sets it to zero at the end of the text.</summary>
        [DllImport(library, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr espeak_TextToPhonemes(ref IntPtr textPointer, int textMode, int phonemeMode);
    }
}
