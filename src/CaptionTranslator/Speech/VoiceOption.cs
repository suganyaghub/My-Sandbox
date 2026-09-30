using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Speech
{
    /// <summary>One entry of the voice list. <see cref="Natural"/> is set for Piper voices, null for Windows voices.</summary>
    public sealed record VoiceOption(VoiceId Id, string DisplayName, PiperVoiceInfo? Natural);
}
