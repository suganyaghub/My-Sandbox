using System.Collections.Concurrent;
using CaptionTranslator.Captions;
using CaptionTranslator.Translation;

namespace CaptionTranslator
{
    /// <summary>
    /// Background loops: poll the caption source and keep one <see cref="CaptionLine"/> per caption bubble up to date
    /// (Teams revises bubbles; they are updated in place, never duplicated), and translate changed lines sentence by
    /// sentence with a cache. Sources without bubble ids (Windows Live Captions) use <see cref="CaptionStabilizer"/>
    /// and produce one finished line per sentence. Events are raised on background threads.
    /// </summary>
    public sealed class CaptionPipeline : IDisposable
    {
        private const int initialBacklogLines = 3;
        private const int provisionalMinimumNewWords = 3;
        private const int maxTrackedLines = 600;

        private static readonly TimeSpan pollInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan settlePeriod = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan provisionalInterval = TimeSpan.FromMilliseconds(1200);

        private readonly TimeSpan quietPeriod;
        private readonly TaskCompletionSource<ITranslator?> translatorReady = new TaskCompletionSource<ITranslator?>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly ConcurrentDictionary<CaptionLine, byte> dirtyLines = new ConcurrentDictionary<CaptionLine, byte>();
        private readonly ConcurrentQueue<(CaptionLine Line, string German)> settledSentences = new ConcurrentQueue<(CaptionLine Line, string German)>();
        private readonly SemaphoreSlim workSignal = new SemaphoreSlim(0);
        private readonly Dictionary<CaptionLine, ProvisionalTranslation> provisional = new Dictionary<CaptionLine, ProvisionalTranslation>();
        private CancellationTokenSource? pollCancellation;
        private string lastLoggedStatus = string.Empty;

        public CaptionPipeline(TimeSpan quietPeriod)
        {
            this.quietPeriod = quietPeriod;
            _ = Task.Run(() => TranslateLoopAsync(this.shutdown.Token));
        }

        /// <summary>Status text and state of the caption source.</summary>
        public event Action<string, SourceState>? StatusChanged;

        /// <summary>A new card; its German and English text may change afterwards.</summary>
        public event Action<CaptionLine>? LineAdded;

        /// <summary>Raised after a line's English text was (re)computed.</summary>
        public event Action<CaptionLine>? LineTranslated;

        /// <summary>A settled sentence (German, English) that was not said before in its bubble — for reading aloud, raised once.</summary>
        public event Action<CaptionLine, string, string>? SentenceReady;

        /// <summary>Unfinished text of sources without bubble ids (Windows Live Captions); null when nothing is pending.</summary>
        public event Action<CaptionSegment?>? PendingChanged;

        /// <summary>Call once the translator is loaded, or with null if loading failed (lines are then shown in German only).</summary>
        public void SetTranslator(ITranslator? translator) => this.translatorReady.TrySetResult(translator);

        public void Start(ICaptionSource source)
        {
            this.pollCancellation?.Cancel();
            this.pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.shutdown.Token);
            CancellationToken token = this.pollCancellation.Token;
            _ = Task.Run(() => PollLoopAsync(source, token));
        }

        public void Dispose()
        {
            this.shutdown.Cancel();
            this.workSignal.Release();
            if (this.translatorReady.Task.IsCompletedSuccessfully)
                this.translatorReady.Task.Result?.Dispose();
        }

        // ---------- Reading captions ----------

        private async Task PollLoopAsync(ICaptionSource source, CancellationToken token)
        {
            CaptionStabilizer stabilizer = new CaptionStabilizer(this.quietPeriod);
            CaptionTracker tracker = new CaptionTracker(this.quietPeriod, settlePeriod);
            Dictionary<string, CaptionLine> linesById = new Dictionary<string, CaptionLine>();
            string status = string.Empty;
            bool backlogSkipped = false;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    CaptionReadResult result = source.Read();
                    if (token.IsCancellationRequested)
                        break;

                    if (result.IsReading && result.Segments.Count > 0)
                    {
                        bool hasBubbleIds = result.Segments.All(segment => segment.Id != null);
                        if (!backlogSkipped)
                        {
                            // Teams keeps a long caption history; show only the newest few so live lines are not delayed.
                            IEnumerable<CaptionSegment> backlog = result.Segments.Take(Math.Max(0, result.Segments.Count - initialBacklogLines));
                            if (hasBubbleIds)
                                tracker.Ignore(backlog.Select(segment => segment.Id!));
                            else
                                stabilizer.MarkAsSeen(backlog);
                            backlogSkipped = true;
                        }

                        if (hasBubbleIds)
                        {
                            ProcessTracked(tracker, result.Segments, linesById);
                            this.PendingChanged?.Invoke(null);
                        }
                        else
                        {
                            ProcessStabilized(stabilizer, result.Segments);
                        }
                    }
                    else
                    {
                        this.PendingChanged?.Invoke(null);
                    }

                    SourceState state = !result.IsReading ? SourceState.NotFound : result.Segments.Count > 0 ? SourceState.Live : SourceState.Waiting;
                    ReportStatus(result.Status, state, ref status);
                }
                catch (Exception exception)
                {
                    // Boundary of a long-running loop: one failed poll must not stop reading captions.
                    ReportStatus("Error while reading captions: " + exception.Message, SourceState.Error, ref status, exception);
                }

                try
                {
                    await Task.Delay(pollInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void ProcessTracked(CaptionTracker tracker, IReadOnlyList<CaptionSegment> segments, Dictionary<string, CaptionLine> linesById)
        {
            CaptionTrackerResult result = tracker.Process(segments, DateTimeOffset.UtcNow);

            foreach (CaptionUpdate update in result.Updates)
            {
                if (update.IsNew || !linesById.TryGetValue(update.Id, out CaptionLine? line))
                {
                    line = new CaptionLine(update.Speaker, update.Text) { IsLive = update.IsLive };
                    linesById[update.Id] = line;
                    this.LineAdded?.Invoke(line);
                }
                else
                {
                    line.UpdateGerman(update.Text);
                    line.IsLive = update.IsLive;
                }

                this.dirtyLines.TryAdd(line, 0);
            }

            foreach (SettledSentence sentence in result.Settled)
            {
                if (linesById.TryGetValue(sentence.Id, out CaptionLine? line))
                    this.settledSentences.Enqueue((line, sentence.Text));
            }

            if (linesById.Count > maxTrackedLines)
            {
                HashSet<string> visible = segments.Where(segment => segment.Id != null).Select(segment => segment.Id!).ToHashSet();
                foreach (string id in linesById.Keys.Where(id => !visible.Contains(id)).ToList())
                    linesById.Remove(id);
            }

            if (result.Updates.Count > 0 || result.Settled.Count > 0)
                SignalWork();
        }

        private void ProcessStabilized(CaptionStabilizer stabilizer, IReadOnlyList<CaptionSegment> segments)
        {
            foreach (CaptionSegment segment in stabilizer.Process(segments, DateTimeOffset.UtcNow))
            {
                CaptionLine line = new CaptionLine(segment.Speaker, segment.Text);
                this.LineAdded?.Invoke(line);
                this.dirtyLines.TryAdd(line, 0);
                this.settledSentences.Enqueue((line, segment.Text));
                SignalWork();
            }

            this.PendingChanged?.Invoke(stabilizer.Pending);
        }

        private void SignalWork()
        {
            if (this.workSignal.CurrentCount == 0)
                this.workSignal.Release();
        }

        // ---------- Translating ----------

        private async Task TranslateLoopAsync(CancellationToken token)
        {
            try
            {
                ITranslator? translator = await this.translatorReady.Task.WaitAsync(token).ConfigureAwait(false);
                CachedTranslator? cached = translator != null ? new CachedTranslator(translator) : null;

                while (!token.IsCancellationRequested)
                {
                    await this.workSignal.WaitAsync(token).ConfigureAwait(false);
                    while (!token.IsCancellationRequested && DoNextWork(cached)) { }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
        }

        /// <summary>Does one piece of work; false when nothing is left. Settled sentences first (read aloud), then the newest changed card.</summary>
        private bool DoNextWork(CachedTranslator? cached)
        {
            if (this.settledSentences.TryDequeue(out (CaptionLine Line, string German) settled))
            {
                if (cached != null)
                {
                    string? english = TryTranslate(cached, settled.German);
                    if (english != null)
                        this.SentenceReady?.Invoke(settled.Line, settled.German, english);
                }

                return true;
            }

            CaptionLine? line = this.dirtyLines.Keys.OrderByDescending(candidate => candidate.Time).FirstOrDefault();
            if (line == null || !this.dirtyLines.TryRemove(line, out _))
                return line != null;

            TranslateLine(cached, line);
            return true;
        }

        private void TranslateLine(CachedTranslator? cached, CaptionLine line)
        {
            if (cached == null)
            {
                line.SetTranslationFailed();
                return;
            }

            bool isLive = line.IsLive;
            IReadOnlyList<string> sentences = SentenceSplitter.Split(line.GermanText);
            List<string> english = new List<string>();

            for (int index = 0; index < sentences.Count; index++)
            {
                bool isOpen = isLive && index == sentences.Count - 1 && !CaptionText.EndsWithSentencePunctuation(sentences[index]);
                string? translated = isOpen ? TranslateOpenSentence(cached, line, sentences[index]) : TryTranslate(cached, sentences[index]);
                if (!string.IsNullOrEmpty(translated))
                    english.Add(translated);
            }

            if (!isLive)
                this.provisional.Remove(line);

            line.SetEnglish(string.Join(" ", english));
            this.LineTranslated?.Invoke(line);
        }

        /// <summary>
        /// The sentence the person is still speaking: translated again only after a few new words or a short time,
        /// so the model is not busy re-translating every single new word.
        /// </summary>
        private string? TranslateOpenSentence(CachedTranslator cached, CaptionLine line, string sentence)
        {
            int words = CaptionText.Words(sentence).Length;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (this.provisional.TryGetValue(line, out ProvisionalTranslation? previous) &&
                words - previous.WordCount < provisionalMinimumNewWords && now - previous.At < provisionalInterval)
            {
                return previous.English;
            }

            string? english = TryTranslate(cached, sentence);
            if (english != null)
                this.provisional[line] = new ProvisionalTranslation(words, english, now);

            return english;
        }

        private static string? TryTranslate(CachedTranslator cached, string sentence)
        {
            try
            {
                return cached.TranslateSentence(sentence);
            }
            catch (Exception exception)
            {
                // Boundary: one failed sentence must not stop the translation worker.
                Log.Error("Translation failed.", exception);
                return null;
            }
        }

        private void ReportStatus(string newStatus, SourceState state, ref string currentStatus, Exception? exception = null)
        {
            if (newStatus == currentStatus)
                return;

            currentStatus = newStatus;
            this.StatusChanged?.Invoke(newStatus, state);

            if (newStatus != this.lastLoggedStatus)
            {
                this.lastLoggedStatus = newStatus;
                if (exception != null)
                    Log.Error(newStatus, exception);
                else
                    Log.Info("Status: " + newStatus);
            }
        }

        private sealed record ProvisionalTranslation(int WordCount, string English, DateTimeOffset At);
    }
}
