namespace CaptionTranslator.Captions
{
    /// <summary>
    /// Caption sources keep rewriting the newest caption while the person is still speaking, and Teams keeps one
    /// caption growing for many sentences. This class turns a stream of snapshots into finished <b>sentences</b>,
    /// each emitted once, as early as it is safe:
    /// - sentences in an older segment (a newer segment exists after it) are final immediately;
    /// - finished sentences inside the newest segment are final once unchanged for the stable period
    ///   (the source may still correct the last words for a moment);
    /// - the unfinished last sentence is final once unchanged for the quiet period (the speaker paused).
    /// If an emitted sentence later grows (the speaker continued it), only the new remainder is emitted.
    /// </summary>
    public sealed class CaptionStabilizer
    {
        private const int rememberedSentenceCount = 400;
        private const char keySeparator = '\u0001';

        private readonly TimeSpan quietPeriod;
        private readonly TimeSpan stablePeriod;
        private readonly LinkedList<CaptionSegment> emitted = new LinkedList<CaptionSegment>();
        private Dictionary<string, DateTimeOffset> firstSeen = new Dictionary<string, DateTimeOffset>();

        public CaptionStabilizer(TimeSpan quietPeriod)
            : this(quietPeriod, TimeSpan.FromMilliseconds(600))
        {
        }

        public CaptionStabilizer(TimeSpan quietPeriod, TimeSpan stablePeriod)
        {
            this.quietPeriod = quietPeriod;
            this.stablePeriod = stablePeriod;
        }

        /// <summary>The part of the newest segment that is not emitted yet, for showing live progress. Null if none.</summary>
        public CaptionSegment? Pending { get; private set; }

        /// <summary>Treats these lines as already emitted, e.g. old captions that were on screen before the app started.</summary>
        public void MarkAsSeen(IEnumerable<CaptionSegment> segments)
        {
            foreach (CaptionSegment segment in segments)
            {
                CaptionSegment normalized = Normalize(segment);
                foreach (string sentence in SentenceSplitter.Split(normalized.Text))
                    Remember(new CaptionSegment(normalized.Speaker, sentence));
            }
        }

        public IReadOnlyList<CaptionSegment> Process(IReadOnlyList<CaptionSegment> snapshot, DateTimeOffset now)
        {
            List<CaptionSegment> finished = new List<CaptionSegment>();
            Dictionary<string, DateTimeOffset> seenNow = new Dictionary<string, DateTimeOffset>();
            List<string> pendingParts = new List<string>();
            string pendingSpeaker = string.Empty;

            for (int segmentIndex = 0; segmentIndex < snapshot.Count; segmentIndex++)
            {
                CaptionSegment segment = Normalize(snapshot[segmentIndex]);
                IReadOnlyList<string> sentences = SentenceSplitter.Split(segment.Text);
                bool isNewestSegment = segmentIndex == snapshot.Count - 1;

                for (int sentenceIndex = 0; sentenceIndex < sentences.Count; sentenceIndex++)
                {
                    CaptionSegment sentence = new CaptionSegment(segment.Speaker, sentences[sentenceIndex]);
                    DateTimeOffset since = TrackFirstSeen(sentence, now, seenNow);
                    // The newest sentence counts as finished once it ends with punctuation (Teams adds it when the sentence is done);
                    // if the speaker continues it anyway, only the new remainder is emitted later.
                    bool isOpen = isNewestSegment && sentenceIndex == sentences.Count - 1 && !EndsWithSentencePunctuation(sentence.Text);
                    bool mayBeTruncated = segmentIndex == 0 && sentenceIndex == 0;
                    TimeSpan required = !isNewestSegment ? TimeSpan.Zero : isOpen ? this.quietPeriod : this.stablePeriod;

                    if (now - since < required)
                    {
                        if (!IsCovered(sentence, mayBeTruncated))
                        {
                            pendingParts.Add(sentence.Text);
                            pendingSpeaker = sentence.Speaker;
                        }

                        continue;
                    }

                    CaptionSegment? result = TryEmit(sentence, mayBeTruncated);
                    if (result != null)
                        finished.Add(result);
                }
            }

            this.firstSeen = seenNow;
            this.Pending = pendingParts.Count > 0 ? new CaptionSegment(pendingSpeaker, string.Join(" ", pendingParts)) : null;
            return finished;
        }

        /// <summary>When this exact sentence text was first seen in consecutive snapshots; resets as soon as the text changes.</summary>
        private DateTimeOffset TrackFirstSeen(CaptionSegment sentence, DateTimeOffset now, Dictionary<string, DateTimeOffset> seenNow)
        {
            string key = sentence.Speaker + keySeparator + sentence.Text;
            if (!seenNow.TryGetValue(key, out DateTimeOffset since))
            {
                since = this.firstSeen.TryGetValue(key, out DateTimeOffset previous) ? previous : now;
                seenNow[key] = since;
            }

            return since;
        }

        private CaptionSegment? TryEmit(CaptionSegment sentence, bool mayBeTruncated)
        {
            if (IsCovered(sentence, mayBeTruncated))
                return null;

            // The speaker continued a sentence that was already emitted (after a pause): emit only the new part.
            // Only the newest few sentences are candidates, so an unrelated old one cannot swallow a new one.
            foreach (CaptionSegment previous in this.emitted.Take(5))
            {
                if (previous.Speaker == sentence.Speaker && sentence.Text.StartsWith(previous.Text, StringComparison.Ordinal))
                {
                    string remainder = sentence.Text.Substring(previous.Text.Length).Trim();
                    Remember(sentence);
                    return remainder.Length == 0 ? null : new CaptionSegment(sentence.Speaker, remainder);
                }
            }

            Remember(sentence);
            return sentence;
        }

        /// <summary>
        /// True if this exact sentence was already emitted. The first visible sentence may be cut off at the start
        /// because older text scrolled away, so for it any emitted sentence that ends with it counts too.
        /// </summary>
        private bool IsCovered(CaptionSegment sentence, bool mayBeTruncated)
        {
            foreach (CaptionSegment previous in this.emitted)
            {
                if (previous.Speaker != sentence.Speaker)
                    continue;

                if (previous.Text == sentence.Text)
                    return true;

                if (mayBeTruncated && previous.Text.EndsWith(sentence.Text, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private void Remember(CaptionSegment sentence)
        {
            this.emitted.AddFirst(sentence);
            while (this.emitted.Count > rememberedSentenceCount)
                this.emitted.RemoveLast();
        }

        private static bool EndsWithSentencePunctuation(string text)
            => text.Length > 0 && ".!?…".Contains(text[^1]);

        private static CaptionSegment Normalize(CaptionSegment segment)
            => new CaptionSegment(TeamsCaptionParser.NormalizeWhitespace(segment.Speaker), TeamsCaptionParser.NormalizeWhitespace(segment.Text));
    }
}
