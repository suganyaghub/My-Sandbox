namespace CaptionTranslator
{
    /// <summary>What the caption source is doing right now; drives the status dot and the hint text.</summary>
    public enum SourceState
    {
        NotFound,
        Waiting,
        Live,
        Error,
    }
}
