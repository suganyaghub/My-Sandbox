namespace CaptionTranslator.Captions
{
    public sealed class CaptionTrackerResult
    {
        public CaptionTrackerResult(IReadOnlyList<CaptionUpdate> updates, IReadOnlyList<SettledSentence> settled)
        {
            this.Updates = updates;
            this.Settled = settled;
        }

        /// <summary>Bubbles that are new, changed text, or stopped being live — in display order.</summary>
        public IReadOnlyList<CaptionUpdate> Updates { get; }

        /// <summary>Sentences to read aloud, each once.</summary>
        public IReadOnlyList<SettledSentence> Settled { get; }
    }
}
