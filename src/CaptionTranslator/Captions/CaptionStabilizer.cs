namespace CaptionTranslator.Captions
{
    /// <summary>
    /// Caption sources rewrite the newest line while the person is still speaking. This class turns a stream of
    /// snapshots into finished lines, each emitted once:
    /// - every segment except the last is final (a newer one exists after it);
    /// - the last segment is final once its text has not changed for the quiet period.
    /// If a finished line later grows (the speaker continued), only the new remainder is emitted.
    /// </summary>
    public sealed class CaptionStabilizer
    {
        private const int rememberedLineCount = 200;

        private readonly TimeSpan quietPeriod;
        private readonly LinkedList<CaptionSegment> emitted = new LinkedList<CaptionSegment>();
        private CaptionSegment? pending;
        private DateTimeOffset pendingSince;

        public CaptionStabilizer(TimeSpan quietPeriod)
        {
            this.quietPeriod = quietPeriod;
        }

        /// <summary>The newest segment that is not final yet, for showing live progress. Null if none.</summary>
        public CaptionSegment? Pending { get; private set; }

        /// <summary>Treats these lines as already emitted, e.g. old captions that were on screen before the app started.</summary>
        public void MarkAsSeen(IEnumerable<CaptionSegment> segments)
        {
            foreach (CaptionSegment segment in segments)
                Remember(Normalize(segment));
        }

        public IReadOnlyList<CaptionSegment> Process(IReadOnlyList<CaptionSegment> snapshot, DateTimeOffset now)
        {
            List<CaptionSegment> finished = new List<CaptionSegment>();
            this.Pending = null;

            for (int index = 0; index < snapshot.Count; index++)
            {
                CaptionSegment segment = Normalize(snapshot[index]);
                if (segment.Text.Length == 0)
                    continue;

                bool isLast = index == snapshot.Count - 1;
                if (isLast)
                {
                    if (segment != this.pending)
                    {
                        this.pending = segment;
                        this.pendingSince = now;
                    }

                    if (now - this.pendingSince < this.quietPeriod)
                    {
                        if (!IsCovered(segment, false))
                            this.Pending = segment;
                        continue;
                    }
                }

                CaptionSegment? result = TryEmit(segment, index == 0);
                if (result != null)
                    finished.Add(result);
            }

            return finished;
        }

        private CaptionSegment? TryEmit(CaptionSegment segment, bool mayBeTruncated)
        {
            if (IsCovered(segment, mayBeTruncated))
                return null;

            // The speaker continued a line that was already emitted: emit only the new part.
            // Only the newest few lines are candidates, so an unrelated old line cannot swallow a new one.
            foreach (CaptionSegment previous in this.emitted.Take(5))
            {
                if (previous.Speaker == segment.Speaker && segment.Text.StartsWith(previous.Text, StringComparison.Ordinal))
                {
                    string remainder = segment.Text.Substring(previous.Text.Length).Trim();
                    Remember(segment);
                    return remainder.Length == 0 ? null : new CaptionSegment(segment.Speaker, remainder);
                }
            }

            Remember(segment);
            return segment;
        }

        /// <summary>
        /// True if this exact line was already emitted. The first visible segment may be cut off at the start
        /// because older text scrolled away, so for it any emitted line that contains it counts too.
        /// </summary>
        private bool IsCovered(CaptionSegment segment, bool mayBeTruncated)
        {
            foreach (CaptionSegment previous in this.emitted)
            {
                if (previous.Speaker != segment.Speaker)
                    continue;

                if (previous.Text == segment.Text)
                    return true;

                if (mayBeTruncated && previous.Text.EndsWith(segment.Text, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private void Remember(CaptionSegment segment)
        {
            this.emitted.AddFirst(segment);
            while (this.emitted.Count > rememberedLineCount)
                this.emitted.RemoveLast();
        }

        private static CaptionSegment Normalize(CaptionSegment segment)
            => new CaptionSegment(TeamsCaptionParser.NormalizeWhitespace(segment.Speaker), TeamsCaptionParser.NormalizeWhitespace(segment.Text));
    }
}
