using System.Text.Json;
using System.Text.RegularExpressions;
using CaptionTranslator.Captions;

namespace CaptionTranslator.Tests
{
    /// <summary>Replays a local caption recording through the stabilizer and reports duplicate sentences by kind.</summary>
    [TestClass]
    [TestCategory("Live")]
    public class RecordingReplayTest
    {
        [TestMethod]
        public void Replay_Recording_ReportsDuplicates()
        {
            if (!File.Exists(CaptionRecorderTest.RecordingPath))
                Assert.Inconclusive("No recording.");

            List<CaptionRecorderTest.RecordedSnapshot> snapshots =
                JsonSerializer.Deserialize<List<CaptionRecorderTest.RecordedSnapshot>>(File.ReadAllText(CaptionRecorderTest.RecordingPath))!;

            CaptionStabilizer stabilizer = new CaptionStabilizer(TimeSpan.FromMilliseconds(1500));
            DateTimeOffset start = DateTimeOffset.UtcNow;
            List<CaptionSegment> emitted = new List<CaptionSegment>();
            bool first = true;
            foreach (CaptionRecorderTest.RecordedSnapshot snapshot in snapshots)
            {
                List<CaptionSegment> segments = snapshot.Segments.Select(pair => new CaptionSegment(pair[0], pair[1])).ToList();
                if (first)
                {
                    stabilizer.MarkAsSeen(segments.Take(Math.Max(0, segments.Count - 3)));
                    first = false;
                }

                emitted.AddRange(stabilizer.Process(segments, start.AddMilliseconds(snapshot.Milliseconds)));
            }

            Dictionary<string, int> kinds = new Dictionary<string, int>();
            for (int later = 0; later < emitted.Count; later++)
            {
                for (int earlier = Math.Max(0, later - 12); earlier < later; earlier++)
                {
                    if (emitted[earlier].Speaker != emitted[later].Speaker)
                        continue;

                    string kind = Classify(emitted[earlier].Text, emitted[later].Text);
                    if (kind.Length == 0)
                        continue;

                    kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                    Console.WriteLine($"[{kind}]\n   A: {Short(emitted[earlier].Text)}\n   B: {Short(emitted[later].Text)}");
                    break;
                }
            }

            Console.WriteLine($"Snapshots {snapshots.Count}, emitted {emitted.Count}");
            foreach ((string kind, int count) in kinds)
                Console.WriteLine($"{kind}: {count}");
        }

        [TestMethod]
        public void Replay_RecordingWithIds_ThroughTracker_ReportsCardsAndDuplicateReads()
        {
            if (!File.Exists(CaptionRecorderTest.RecordingPath))
                Assert.Inconclusive("No recording.");

            List<CaptionRecorderTest.RecordedSnapshot> snapshots =
                JsonSerializer.Deserialize<List<CaptionRecorderTest.RecordedSnapshot>>(File.ReadAllText(CaptionRecorderTest.RecordingPath))!;
            if (snapshots.Any(snapshot => snapshot.Segments.Any(segment => segment.Length < 3 || segment[2].Length == 0)))
                Assert.Inconclusive("Recording has no bubble ids.");

            CaptionTracker tracker = new CaptionTracker(TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(1));
            DateTimeOffset start = DateTimeOffset.UtcNow;
            List<SettledSentence> settled = new List<SettledSentence>();
            HashSet<string> cards = new HashSet<string>();
            int updates = 0;
            bool first = true;
            foreach (CaptionRecorderTest.RecordedSnapshot snapshot in snapshots)
            {
                List<CaptionSegment> segments = snapshot.Segments.Select(item => new CaptionSegment(item[0], item[1], item[2])).ToList();
                if (first)
                {
                    tracker.Ignore(segments.Take(Math.Max(0, segments.Count - 3)).Select(segment => segment.Id!));
                    first = false;
                }

                CaptionTrackerResult result = tracker.Process(segments, start.AddMilliseconds(snapshot.Milliseconds));
                updates += result.Updates.Count;
                cards.UnionWith(result.Updates.Where(update => update.IsNew).Select(update => update.Id));
                settled.AddRange(result.Settled);
            }

            int duplicateReads = 0;
            for (int later = 0; later < settled.Count; later++)
            {
                for (int earlier = Math.Max(0, later - 12); earlier < later; earlier++)
                {
                    if (settled[earlier].Speaker != settled[later].Speaker)
                        continue;

                    string kind = Classify(settled[earlier].Text, settled[later].Text);
                    if (kind.Length == 0)
                        continue;

                    duplicateReads++;
                    string[] earlierWords = CaptionText.Words(settled[earlier].Text);
                    string[] laterWords = CaptionText.Words(settled[later].Text);
                    Console.WriteLine($"[{kind}] similarity {CaptionText.OrderedSimilarity(laterWords, earlierWords):F2}, words {earlierWords.Length}/{laterWords.Length}, same bubble {settled[earlier].Id == settled[later].Id}\n   A: {settled[earlier].Text}\n   B: {settled[later].Text}");
                    break;
                }
            }

            Console.WriteLine($"Snapshots {snapshots.Count}, cards {cards.Count}, card updates {updates}, sentences read aloud {settled.Count}, near-duplicate reads {duplicateReads}");
            foreach (IGrouping<string, SettledSentence> bubble in settled.GroupBy(sentence => sentence.Id))
            {
                Console.WriteLine($"-- bubble ..{bubble.Key.Substring(Math.Max(0, bubble.Key.Length - 6))}");
                foreach (SettledSentence sentence in bubble)
                    Console.WriteLine("     " + Short(sentence.Text));
            }
        }

        private static string Classify(string earlier, string later)
        {
            string[] a = Words(earlier);
            string[] b = Words(later);
            if (a.Length == 0 || b.Length == 0)
                return string.Empty;
            if (string.Join(" ", a) == string.Join(" ", b))
                return "same words, other punctuation/case";
            if (Contains(a, b))
                return "later is part of earlier";
            if (Contains(b, a))
                return "later contains earlier";

            double overlap = a.Intersect(b).Count() / (double)a.Union(b).Count();
            return overlap >= 0.5 ? $"word overlap {Math.Floor(overlap * 10) * 10:0}%+" : string.Empty;
        }

        private static bool Contains(string[] outer, string[] inner)
            => inner.Length <= outer.Length && Enumerable.Range(0, outer.Length - inner.Length + 1).Any(offset => outer.Skip(offset).Take(inner.Length).SequenceEqual(inner));

        private static string[] Words(string text)
            => Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}\s]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);

        private static string Short(string text) => text.Length > 70 ? text.Substring(0, 70) + "…" : text;
    }
}
