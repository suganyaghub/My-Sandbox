using System.Diagnostics;
using System.Windows.Threading;

namespace CaptionTranslator
{
    /// <summary>
    /// Diagnostics: logs when the UI thread does not respond for more than <see cref="thresholdMilliseconds"/>
    /// (and how long it was blocked), so "the window hangs" reports can be matched with the log.
    /// </summary>
    public sealed class UiWatchdog : IDisposable
    {
        private const int thresholdMilliseconds = 2000;

        private readonly DispatcherTimer heartbeat;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private long lastBeat;

        public UiWatchdog(Dispatcher dispatcher)
        {
            this.heartbeat = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (sender, e) => Interlocked.Exchange(ref this.lastBeat, this.clock.ElapsedMilliseconds), dispatcher);
        }

        public void Start()
        {
            Interlocked.Exchange(ref this.lastBeat, this.clock.ElapsedMilliseconds);
            this.heartbeat.Start();
            _ = Task.Run(() => WatchAsync(this.shutdown.Token));
        }

        public void Dispose()
        {
            this.shutdown.Cancel();
            this.heartbeat.Stop();
        }

        private async Task WatchAsync(CancellationToken cancellationToken)
        {
            bool blocked = false;
            long blockedSince = 0;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    long beat = Interlocked.Read(ref this.lastBeat);
                    long gap = this.clock.ElapsedMilliseconds - beat;
                    if (!blocked && gap > thresholdMilliseconds)
                    {
                        blocked = true;
                        blockedSince = beat;
                        Log.Info($"UI watchdog: window not responding for {gap} ms.");
                    }
                    else if (blocked && gap < thresholdMilliseconds)
                    {
                        blocked = false;
                        Log.Info($"UI watchdog: window responding again after about {beat - blockedSince} ms.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // App is closing.
            }
        }
    }
}
