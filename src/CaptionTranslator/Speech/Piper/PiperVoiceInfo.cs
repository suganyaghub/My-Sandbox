namespace CaptionTranslator.Speech.Piper
{
    /// <summary>A downloadable Piper voice.</summary>
    /// <param name="Id">Piper voice name, e.g. "en_US-kristin-medium"; also the file name without ".onnx".</param>
    /// <param name="Folder">Folder in the rhasspy/piper-voices repository, e.g. "en/en_US/kristin/medium".</param>
    /// <param name="License">License of the training data, from the voice's MODEL_CARD.</param>
    public sealed record PiperVoiceInfo(string Id, string DisplayName, string Folder, int SizeMegabytes, string License);
}
