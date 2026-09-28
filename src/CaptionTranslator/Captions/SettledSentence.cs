namespace CaptionTranslator.Captions
{
    /// <summary>
    /// A sentence of a bubble that stopped changing and was not said before in that bubble; read aloud exactly once.
    /// <paramref name="Text"/> may be only the new end of a sentence that Teams extended.
    /// </summary>
    public sealed record SettledSentence(string Id, string Speaker, string Text);
}
