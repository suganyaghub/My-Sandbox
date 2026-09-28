namespace CaptionTranslator.Captions
{
    /// <summary>One caption line as shown by the source: who spoke (may be empty) and what was said.</summary>
    public sealed record CaptionSegment(string Speaker, string Text);
}
