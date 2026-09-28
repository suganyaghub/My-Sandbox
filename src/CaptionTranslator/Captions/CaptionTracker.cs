namespace CaptionTranslator.Captions
{
    /// <summary>
    /// Follows caption bubbles by their id (Teams keeps it while it revises the text), so each bubble is one card that is
    /// updated in place instead of producing a new, near-duplicate line for every revision.
    /// Also decides which sentences are settled enough to read aloud, and never reads the same content twice in a bubble,
    /// even when Teams changes words, punctuation or sentence boundaries afterwards.
    /// </summary>
    public sealed class CaptionTracker
    {
        /// <summary>Share of a sentence's words already spoken in the bubble above which it counts as a revision, not new content.</summary>
        private const double alreadySpokenCoverage = 0.7;

        /// <summary>Ordered word similarity to one already spoken sentence above which it counts as a corrected version of it.</summary>
        private const double revisionSimilarity = 0.6;

        /// <summary>A corrected beginning is only recognised for spoken sentences at least this long (short ones match too easily).</summary>
        private const int minimumCorrectedPrefixWords = 4;

        /// <summary>How many words longer or shorter a corrected beginning may be than the sentence that was read.</summary>
        private const int prefixLengthTolerance = 3;

        /// <summary>Extensions shorter than this ("damit." → "damit das klappt.") are not worth reading separately.</summary>
        private const int minimumExtensionWords = 3;

        private static readonly TimeSpan forgetAfter = TimeSpan.FromMinutes(3);

        private readonly TimeSpan quietPeriod;
        private readonly TimeSpan settlePeriod;
        private readonly Dictionary<string, BubbleState> bubbles = new Dictionary<string, BubbleState>();
        private readonly HashSet<string> ignored = new HashSet<string>();

        /// <param name="quietPeriod">A bubble stops being live when unchanged this long (or when a newer bubble exists).</param>
        /// <param name="settlePeriod">A sentence is read aloud once unchanged this long.</param>
        public CaptionTracker(TimeSpan quietPeriod, TimeSpan settlePeriod)
        {
            this.quietPeriod = quietPeriod;
            this.settlePeriod = settlePeriod;
        }

        /// <summary>Never shows or reads these bubbles, e.g. old captions that were on screen before the app started.</summary>
        public void Ignore(IEnumerable<string> ids)
        {
            foreach (string id in ids)
                this.ignored.Add(id);
        }

        public CaptionTrackerResult Process(IReadOnlyList<CaptionSegment> snapshot, DateTimeOffset now)
        {
            List<CaptionUpdate> updates = new List<CaptionUpdate>();
            List<SettledSentence> settled = new List<SettledSentence>();
            int newestIndex = snapshot.Count - 1;

            for (int index = 0; index < snapshot.Count; index++)
            {
                CaptionSegment segment = snapshot[index];
                if (segment.Id == null || this.ignored.Contains(segment.Id))
                    continue;

                string speaker = TeamsCaptionParser.NormalizeWhitespace(segment.Speaker);
                string text = TeamsCaptionParser.NormalizeWhitespace(segment.Text);
                if (text.Length == 0)
                    continue;

                bool isNew = !this.bubbles.TryGetValue(segment.Id, out BubbleState? state);
                bool changed = false;
                if (state == null)
                {
                    state = new BubbleState(speaker, text, now);
                    this.bubbles[segment.Id] = state;
                }
                else if (state.Text != text)
                {
                    state.Text = text;
                    state.LastChanged = now;
                    changed = true;
                }

                state.LastSeen = now;
                bool isLive = index == newestIndex && now - state.LastChanged < this.quietPeriod;
                if (isNew || changed || isLive != state.IsLive)
                {
                    state.IsLive = isLive;
                    updates.Add(new CaptionUpdate(segment.Id, speaker, text, isNew, isLive));
                }

                CollectSettled(segment.Id, state, now, settled);
            }

            ForgetOldBubbles(now);
            return new CaptionTrackerResult(updates, settled);
        }

        private void CollectSettled(string id, BubbleState state, DateTimeOffset now, List<SettledSentence> settled)
        {
            IReadOnlyList<string> sentences = SentenceSplitter.Split(state.Text);
            Dictionary<string, DateTimeOffset> seenNow = new Dictionary<string, DateTimeOffset>();

            for (int index = 0; index < sentences.Count; index++)
            {
                string sentence = sentences[index];
                if (!seenNow.ContainsKey(sentence))
                    seenNow[sentence] = state.SentenceSince.TryGetValue(sentence, out DateTimeOffset since) ? since : now;

                // The last sentence of a live bubble may still be growing; it only counts once it has its full stop.
                bool mayStillGrow = index == sentences.Count - 1 && state.IsLive && !CaptionText.EndsWithSentencePunctuation(sentence);
                if (mayStillGrow || now - seenNow[sentence] < this.settlePeriod)
                    continue;

                string? toSpeak = state.TakeUnspoken(sentence);
                if (toSpeak != null)
                    settled.Add(new SettledSentence(id, state.Speaker, toSpeak));
            }

            state.SentenceSince = seenNow;
        }

        private void ForgetOldBubbles(DateTimeOffset now)
        {
            foreach (string id in this.bubbles.Where(pair => now - pair.Value.LastSeen > forgetAfter).Select(pair => pair.Key).ToList())
                this.bubbles.Remove(id);
        }

        private sealed class BubbleState
        {
            private readonly List<string[]> spoken = new List<string[]>();
            private readonly HashSet<string> spokenWords = new HashSet<string>();

            public BubbleState(string speaker, string text, DateTimeOffset now)
            {
                this.Speaker = speaker;
                this.Text = text;
                this.LastChanged = now;
                this.LastSeen = now;
            }

            public string Speaker { get; }

            public string Text { get; set; }

            public DateTimeOffset LastChanged { get; set; }

            public DateTimeOffset LastSeen { get; set; }

            public bool IsLive { get; set; }

            public Dictionary<string, DateTimeOffset> SentenceSince { get; set; } = new Dictionary<string, DateTimeOffset>();

            /// <summary>
            /// Returns what of this sentence has not been read aloud yet in this bubble: the whole sentence, only the new
            /// end of an extended sentence, or null if it is a revision of what was already said.
            /// </summary>
            public string? TakeUnspoken(string sentence)
            {
                string[] words = CaptionText.Words(sentence);
                if (words.Length == 0)
                    return null;

                // Compare with the LONGEST version already spoken that this sentence starts with. The shortest match would
                // report the same "new end" again on every poll once the sentence has been extended.
                string[]? longestPrefix = this.spoken.Where(previous => CaptionText.StartsWith(words, previous))
                                                     .OrderByDescending(previous => previous.Length)
                                                     .FirstOrDefault();
                if (longestPrefix != null)
                {
                    int extra = words.Length - longestPrefix.Length;
                    if (extra == 0)
                        return null;

                    string? extension = extra >= minimumExtensionWords ? NewEnd(sentence, longestPrefix.Length) : null;
                    Remember(words);
                    return extension;
                }

                // Teams often corrects a spoken sentence AND continues it: its beginning is then a corrected version of
                // what was read. Read only the words after that beginning.
                int correctedPrefixLength = FindCorrectedPrefixLength(words);
                if (correctedPrefixLength > 0)
                {
                    int extra = words.Length - correctedPrefixLength;
                    string? extension = extra >= minimumExtensionWords ? NewEnd(sentence, correctedPrefixLength) : null;
                    Remember(words);
                    return extension;
                }

                bool isRevision = CaptionText.Coverage(words, this.spokenWords) >= alreadySpokenCoverage ||
                                  this.spoken.Any(previous => CaptionText.OrderedSimilarity(words, previous) >= revisionSimilarity);
                Remember(words);
                return isRevision ? null : sentence;
            }

            /// <summary>
            /// The text after the first <paramref name="skipWords"/> words, or null if that end was itself already read
            /// (e.g. two read sentences merged into one: the "new" end is the second one again).
            /// </summary>
            private string? NewEnd(string sentence, int skipWords)
            {
                string end = CaptionText.TextAfterWords(sentence, skipWords);
                string[] endWords = CaptionText.Words(end);
                if (endWords.Length == 0)
                    return null;

                bool alreadyRead = this.spoken.Any(previous => CaptionText.OrderedSimilarity(endWords, previous.Skip(Math.Max(0, previous.Length - endWords.Length - 1)).ToArray()) >= revisionSimilarity);
                return alreadyRead ? null : end;
            }

            /// <summary>
            /// Number of leading words of <paramref name="words"/> that are a corrected version of one already spoken
            /// sentence (ordered similarity at least <see cref="revisionSimilarity"/>), or 0. The prefix may be a few
            /// words shorter or longer than the spoken sentence, because corrections add or drop words.
            /// </summary>
            private int FindCorrectedPrefixLength(string[] words)
            {
                int bestLength = 0;
                double bestSimilarity = 0;

                foreach (string[] previous in this.spoken.Where(previous => previous.Length >= minimumCorrectedPrefixWords))
                {
                    int shortest = Math.Max(1, previous.Length - prefixLengthTolerance);
                    int longest = Math.Min(words.Length, previous.Length + prefixLengthTolerance);
                    for (int length = shortest; length <= longest; length++)
                    {
                        double similarity = CaptionText.OrderedSimilarity(words.Take(length).ToArray(), previous);
                        if (similarity >= revisionSimilarity && (similarity > bestSimilarity || (similarity == bestSimilarity && length > bestLength)))
                        {
                            bestSimilarity = similarity;
                            bestLength = length;
                        }
                    }
                }

                return bestLength;
            }

            private void Remember(string[] words)
            {
                this.spoken.Add(words);
                this.spokenWords.UnionWith(words);
            }
        }
    }
}
