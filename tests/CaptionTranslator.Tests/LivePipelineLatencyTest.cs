using System.Collections.Concurrent;
using CaptionTranslator.Captions;
using CaptionTranslator.Translation;

namespace CaptionTranslator.Tests
{
    /// <summary>
    /// End-to-end latency against a running Teams meeting: for each translated sentence, the time from when the
    /// sentence was first complete on screen (the next sentence had started) until its English text was ready.
    /// Prints numbers only; no caption text.
    /// </summary>
    [TestClass]
    [TestCategory("Live")]
    [DoNotParallelize]
    public class LivePipelineLatencyTest
    {
        [TestMethod]
        public void Pipeline_LiveMeeting_ReportsSentenceLatency()
        {
            TeamsCaptionSource probe = new TeamsCaptionSource(new AppSettings().TeamsCaptionContainerPattern);
            if (probe.Read().Segments.Count == 0)
                Assert.Inconclusive("No Teams meeting with live captions open.");

            // Watch the screen independently: remember when each sentence text first appeared complete.
            ConcurrentDictionary<string, DateTime> completeSince = new ConcurrentDictionary<string, DateTime>();
            ConcurrentBag<double> latencies = new ConcurrentBag<double>();
            HashSet<string> alreadyOnScreen = new HashSet<string>(probe.Read().Segments.SelectMany(segment => SentenceSplitter.Split(segment.Text)));
            int linesAdded = 0;
            ConcurrentBag<double> translationTimes = new ConcurrentBag<double>();
            using CancellationTokenSource stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Task watcher = Task.Run(() =>
            {
                TeamsCaptionSource source = new TeamsCaptionSource(new AppSettings().TeamsCaptionContainerPattern);
                while (!stop.IsCancellationRequested)
                {
                    foreach (CaptionSegment segment in source.Read().Segments)
                    {
                        IReadOnlyList<string> sentences = SentenceSplitter.Split(segment.Text);
                        for (int index = 0; index < sentences.Count - 1; index++)
                        {
                            if (!alreadyOnScreen.Contains(sentences[index]))
                                completeSince.TryAdd(sentences[index], DateTime.UtcNow);
                        }
                    }

                    Thread.Sleep(100);
                }
            });

            using CaptionPipeline pipeline = new CaptionPipeline(TimeSpan.FromMilliseconds(1500));
            pipeline.LineAdded += line => Interlocked.Increment(ref linesAdded);
            pipeline.LineTranslated += line =>
            {
                translationTimes.Add((DateTime.Now - line.Time).TotalSeconds);
                if (completeSince.TryGetValue(line.GermanText, out DateTime since))
                    latencies.Add((DateTime.UtcNow - since).TotalSeconds);
            };
            pipeline.SetTranslator(new OpusMtTranslator(ModelFiles.Directory));
            pipeline.Start(new TeamsCaptionSource(new AppSettings().TeamsCaptionContainerPattern));

            watcher.Wait();

            Console.WriteLine($"Lines added: {linesAdded}, emit-to-English: median {translationTimes.OrderBy(value => value).ElementAtOrDefault(translationTimes.Count / 2):F2} s, max {(translationTimes.IsEmpty ? 0 : translationTimes.Max()):F2} s");
            if (latencies.IsEmpty)
                Assert.Inconclusive("Nobody finished a sentence mid-caption during the measurement.");

            List<double> sorted = latencies.OrderBy(value => value).ToList();
            Console.WriteLine($"Sentences measured: {sorted.Count}");
            Console.WriteLine($"Latency after sentence complete on screen: median {sorted[sorted.Count / 2]:F1} s, max {sorted[^1]:F1} s, min {sorted[0]:F1} s");
        }
    }
}
