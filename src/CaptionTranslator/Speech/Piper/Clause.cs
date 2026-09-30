namespace CaptionTranslator.Speech.Piper
{
    /// <summary>Part of a sentence that is spoken in one go, and the punctuation mark that ended it (null if none).</summary>
    public sealed record Clause(string Text, char? Punctuation);
}
