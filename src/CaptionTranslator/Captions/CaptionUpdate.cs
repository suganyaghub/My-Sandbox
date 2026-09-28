namespace CaptionTranslator.Captions
{
    /// <summary>A caption bubble that appeared or changed. <paramref name="IsLive"/>: the person is still speaking in it.</summary>
    public sealed record CaptionUpdate(string Id, string Speaker, string Text, bool IsNew, bool IsLive);
}
