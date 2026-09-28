namespace CaptionTranslator.Captions
{
    /// <summary>
    /// One caption line as shown by the source: who spoke (may be empty) and what was said.
    /// <paramref name="Id"/> identifies the same caption bubble across snapshots while its text changes (null if the source cannot tell).
    /// </summary>
    public sealed record CaptionSegment(string Speaker, string Text, string? Id = null);
}
