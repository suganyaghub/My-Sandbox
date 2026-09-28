using System.Diagnostics;
using CaptionTranslator.Captions;
using CaptionTranslator.Translation;

namespace CaptionTranslator.Tests
{
    /// <summary>Measures where caption latency comes from, against a running Teams meeting. Prints numbers only.</summary>
    [TestClass]
    [TestCategory("Live")]
    [DoNotParallelize]
    public class LatencyProbeTest
    {
        [TestMethod]
        public void Probe_LiveMeeting_ReportsReadTimeBubbleLengthAndTranslationSpeed()
        {
            TeamsCaptionSource source = new TeamsCaptionSource(new AppSettings().TeamsCaptionContainerPattern);
            if (source.Read().Segments.Count == 0)
                Assert.Inconclusive("No Teams meeting with live captions open.");

            List<long> readTimes = new List<long>();
            Dictionary<string, (DateTime FirstSeen, DateTime LastChanged, int Sentences)> bubbles = new Dictionary<string, (DateTime, DateTime, int)>();
            string? lastKey = null;
            string lastText = string.Empty;
            Stopwatch total = Stopwatch.StartNew();

            while (total.Elapsed < TimeSpan.FromSeconds(30))
            {
                Stopwatch read = Stopwatch.StartNew();
                CaptionReadResult result = source.Read();
                readTimes.Add(read.ElapsedMilliseconds);

                if (result.Segments.Count > 0)
                {
                    CaptionSegment last = result.Segments[^1];
                    // Identify a bubble by its first 25 characters (the start does not change while it grows).
                    string key = last.Speaker + "|" + new string(last.Text.Take(25).ToArray());
                    DateTime now = DateTime.UtcNow;
                    if (!bubbles.ContainsKey(key))
                        bubbles[key] = (now, now, 0);
                    if (key != lastKey || last.Text != lastText)
                        bubbles[key] = (bubbles[key].FirstSeen, now, SentenceSplitter.Split(last.Text).Count);
                    lastKey = key;
                    lastText = last.Text;
                }

                Thread.Sleep(250);
            }

            Console.WriteLine($"UI read: avg {readTimes.Average():F0} ms, max {readTimes.Max()} ms, polls {readTimes.Count}, visible segments {source.Read().Segments.Count}");
            foreach ((string key, (DateTime firstSeen, DateTime lastChanged, int sentences)) in bubbles)
                Console.WriteLine($"Bubble grew for {(lastChanged - firstSeen).TotalSeconds:F1} s, {sentences} sentence(s)");

            using OpusMtTranslator translator = new OpusMtTranslator(ModelFiles.Directory);
            translator.Translate("Hallo.");
            string sample = "Also das ist jetzt eigentlich schon ganz nett für Leute, die das jetzt verwenden, und dann kann man einfach weitermachen.";
            Stopwatch translate = Stopwatch.StartNew();
            for (int index = 0; index < 5; index++)
                translator.Translate(sample);
            Console.WriteLine($"Translation: {translate.ElapsedMilliseconds / 5} ms per 20-word sentence, {Environment.ProcessorCount} logical CPUs");
        }
    }
}
