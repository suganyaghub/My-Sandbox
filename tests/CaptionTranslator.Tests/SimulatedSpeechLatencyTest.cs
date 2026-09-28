using System.Collections.Concurrent;
using System.Diagnostics;
using CaptionTranslator.Captions;
using CaptionTranslator.Translation;

namespace CaptionTranslator.Tests
{
    /// <summary>
    /// End-to-end latency with the real pipeline and translation model, fed by a simulated Teams caption bubble that grows
    /// word by word at speaking pace (like the 17 s, 4-sentence captions measured in a real meeting).
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class SimulatedSpeechLatencyTest
    {
        private const int wordIntervalMs = 400;

        private static readonly string[] sentences =
        {
            "Wir haben heute drei Punkte auf der Agenda.",
            "Zuerst schauen wir uns die Ergebnisse vom letzten Test an.",
            "Danach besprechen wir kurz die offenen Fragen aus dem Team.",
            "Am Ende planen wir die nächsten Schritte.",
        };

        [TestMethod]
        public void Pipeline_BubbleGrowingAtSpeakingPace_ShowsAndReadsEachSentenceShortlyAfterItEnds()
        {
            if (ModelFiles.MissingFiles().Count > 0)
                Assert.Inconclusive("Model not downloaded.");

            using OpusMtTranslator translator = new OpusMtTranslator(ModelFiles.Directory);
            translator.Translate("Guten Morgen, wir fangen jetzt an.");

            string[] words = string.Join(" ", sentences).Split(' ');
            Stopwatch clock = new Stopwatch();
            ConcurrentDictionary<int, double> shownAt = new ConcurrentDictionary<int, double>();
            ConcurrentDictionary<string, double> readyToReadAt = new ConcurrentDictionary<string, double>();
            int cards = 0;

            using CaptionPipeline pipeline = new CaptionPipeline(TimeSpan.FromMilliseconds(1500));
            pipeline.LineAdded += line => Interlocked.Increment(ref cards);
            pipeline.LineTranslated += line =>
            {
                // A sentence counts as shown once the card's German contains it and the English was recomputed after that.
                for (int index = 0; index < sentences.Length; index++)
                {
                    if (line.GermanText.Contains(sentences[index], StringComparison.Ordinal))
                        shownAt.TryAdd(index, clock.Elapsed.TotalSeconds);
                }
            };
            pipeline.SentenceReady += (line, german, english) => readyToReadAt.TryAdd(german, clock.Elapsed.TotalSeconds);
            pipeline.SetTranslator(translator);
            clock.Start();
            pipeline.Start(new GrowingBubbleSource(words, clock));

            double speechEnds = words.Length * wordIntervalMs / 1000.0;
            SpinWait.SpinUntil(() => readyToReadAt.Count >= sentences.Length, TimeSpan.FromSeconds(speechEnds + 10));

            List<double> shown = new List<double>();
            List<double> read = new List<double>();
            int wordsSoFar = 0;
            for (int index = 0; index < sentences.Length; index++)
            {
                wordsSoFar += sentences[index].Split(' ').Length;

                // Complete on screen when its last word appears.
                double completeAt = (wordsSoFar - 1) * wordIntervalMs / 1000.0;
                Assert.IsTrue(shownAt.TryGetValue(index, out double shownTime), $"Sentence {index + 1} never shown.");
                Assert.IsTrue(readyToReadAt.TryGetValue(sentences[index], out double readTime), $"Sentence {index + 1} never ready to read aloud.");
                shown.Add(shownTime - completeAt);
                read.Add(readTime - completeAt);
                Console.WriteLine($"Sentence {index + 1}: English on screen {shownTime - completeAt:F1} s, read-aloud ready {readTime - completeAt:F1} s after it ended");
            }

            Console.WriteLine($"Cards created: {cards}");
            Assert.AreEqual(1, cards, "One Teams bubble must stay one card.");
            Assert.AreEqual(sentences.Length, readyToReadAt.Count, "Each sentence must be read exactly once.");
            Assert.IsTrue(shown.Max() < 1.5, "English should be on screen within 1.5 s.");
            Assert.IsTrue(read.Max() < 2.5, "Sentences should be ready to read aloud within 2.5 s.");
        }

        /// <summary>One Teams caption bubble (fixed id) that reveals one more word every 400 ms, then stays (speaker pauses).</summary>
        private sealed class GrowingBubbleSource : ICaptionSource
        {
            private readonly string[] words;
            private readonly Stopwatch clock;

            public GrowingBubbleSource(string[] words, Stopwatch clock)
            {
                this.words = words;
                this.clock = clock;
            }

            public string DisplayName => "Simulated";

            public CaptionReadResult Read()
            {
                int visible = Math.Min(this.words.Length, 1 + (int)(this.clock.ElapsedMilliseconds / wordIntervalMs));
                string text = string.Join(" ", this.words.Take(visible));
                return CaptionReadResult.Read(new[] { new CaptionSegment("Anna Muster", text, "bubble-1") }, "Reading");
            }

            public string DumpTree() => string.Empty;
        }
    }
}
