using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Speech
{
    public static class VoiceOptions
    {
        /// <summary>Natural voices first (only if espeak-ng is available), then the installed Windows voices.</summary>
        public static IReadOnlyList<VoiceOption> Build(IReadOnlyList<string> windowsVoices, IReadOnlyList<PiperVoiceInfo> naturalVoices, bool naturalAvailable)
        {
            List<VoiceOption> options = new List<VoiceOption>();
            if (naturalAvailable)
                options.AddRange(naturalVoices.Select(voice => new VoiceOption(VoiceId.Natural(voice.Id), voice.DisplayName, voice)));
            options.AddRange(windowsVoices.Select(name => new VoiceOption(VoiceId.Windows(name), name, null)));
            return options;
        }

        /// <summary>The saved voice if it is in the list, otherwise the first Windows voice, otherwise the first voice; null if the list is empty.</summary>
        public static VoiceOption? Resolve(IReadOnlyList<VoiceOption> options, string? savedVoice)
        {
            VoiceId? saved = VoiceId.Parse(savedVoice);
            return options.FirstOrDefault(option => option.Id == saved)
                ?? options.FirstOrDefault(option => !option.Id.IsNatural)
                ?? options.FirstOrDefault();
        }
    }
}
