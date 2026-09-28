using System.Threading.Channels;
using CaptionTranslator.Captions;
using CaptionTranslator.Translation;

namespace CaptionTranslator
{
    /// <summary>
    /// Background loop: poll the caption source, detect finished lines, translate them in order.
    /// Events are raised on background threads.
    /// </summary>
    public sealed class CaptionPipeline : IDisposable
    {
        private const int initialBacklogLines = 3;

        private static readonly TimeSpan pollInterval = TimeSpan.FromMilliseconds(400);

        private readonly TimeSpan quietPeriod;
        private readonly Channel<CaptionLine> translationQueue = Channel.CreateUnbounded<CaptionLine>(new UnboundedChannelOptions { SingleReader = true });
        private readonly TaskCompletionSource<ITranslator?> translatorReady = new TaskCompletionSource<ITranslator?>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private CancellationTokenSource? pollCancellation;
        private string lastLoggedStatus = string.Empty;

        public CaptionPipeline(TimeSpan quietPeriod)
        {
            this.quietPeriod = quietPeriod;
            _ = Task.Run(() => TranslateLoopAsync(this.shutdown.Token));
        }

        public event Action<string>? StatusChanged;

        public event Action<CaptionLine>? LineAdded;

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
            this.translationQueue.Writer.TryComplete();
            if (this.translatorReady.Task.IsCompletedSuccessfully)
                this.translatorReady.Task.Result?.Dispose();
        }

        private async Task PollLoopAsync(ICaptionSource source, CancellationToken token)
        {
            CaptionStabilizer stabilizer = new CaptionStabilizer(this.quietPeriod);
            string status = string.Empty;
            bool backlogSkipped = false;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    CaptionReadResult result = source.Read();
                    if (token.IsCancellationRequested)
                        break;

                    if (result.IsReading)
                    {
                        if (!backlogSkipped && result.Segments.Count > 0)
                        {
                            // Teams keeps a long caption history; translate only the newest few so live lines are not delayed.
                            stabilizer.MarkAsSeen(result.Segments.Take(Math.Max(0, result.Segments.Count - initialBacklogLines)));
                            backlogSkipped = true;
                        }

                        foreach (CaptionSegment segment in stabilizer.Process(result.Segments, DateTimeOffset.UtcNow))
                        {
                            CaptionLine line = new CaptionLine(segment.Speaker, segment.Text);
                            this.LineAdded?.Invoke(line);
                            this.translationQueue.Writer.TryWrite(line);
                        }

                        this.PendingChanged?.Invoke(stabilizer.Pending);
                    }
                    else
                    {
                        this.PendingChanged?.Invoke(null);
                    }

                    ReportStatus(result.Status, ref status);
                }
                catch (Exception exception)
                {
                    // Boundary of a long-running loop: one failed poll must not stop reading captions.
                    ReportStatus("Error while reading captions: " + exception.Message, ref status, exception);
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

        private async Task TranslateLoopAsync(CancellationToken token)
        {
            try
            {
                ITranslator? translator = await this.translatorReady.Task.WaitAsync(token).ConfigureAwait(false);

                await foreach (CaptionLine line in this.translationQueue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    if (translator == null)
                    {
                        line.SetTranslationFailed();
                        continue;
                    }

                    try
                    {
                        line.SetEnglish(translator.Translate(line.GermanText));
                    }
                    catch (Exception exception)
                    {
                        // Boundary: one failed line must not stop the translation queue.
                        Log.Error($"Translation failed for: {line.GermanText}", exception);
                        line.SetTranslationFailed();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
        }

        private void ReportStatus(string newStatus, ref string currentStatus, Exception? exception = null)
        {
            if (newStatus == currentStatus)
                return;

            currentStatus = newStatus;
            this.StatusChanged?.Invoke(newStatus);

            if (newStatus != this.lastLoggedStatus)
            {
                this.lastLoggedStatus = newStatus;
                if (exception != null)
                    Log.Error(newStatus, exception);
                else
                    Log.Info("Status: " + newStatus);
            }
        }
    }
}
