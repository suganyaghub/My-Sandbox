using System.Collections.Concurrent;
using CaptionTranslator.Captions;
using CaptionTranslator.Translation;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class CaptionPipelineTest
    {
        [TestMethod]
        public void Start_SourceWithHistory_TranslatesOnlyNewestLinesAndSetsEnglish()
        {
            List<CaptionSegment> history = Enumerable.Range(1, 10).Select(number => new CaptionSegment("Anna", $"Satz {number}.")).ToList();
            history.Add(new CaptionSegment("Ben", "Noch nicht fertig"));
            ConcurrentQueue<CaptionLine> added = new ConcurrentQueue<CaptionLine>();

            using CaptionPipeline pipeline = new CaptionPipeline(TimeSpan.FromSeconds(30));
            pipeline.LineAdded += line => added.Enqueue(line);
            pipeline.SetTranslator(new UpperCaseTranslator());
            pipeline.Start(new FixedSource(history));

            SpinWait.SpinUntil(() => added.Count >= 2 && added.All(line => line.EnglishText != null), TimeSpan.FromSeconds(5));

            // 3 newest lines are kept from the backlog; the last one is still in progress, so 2 are emitted.
            CollectionAssert.AreEqual(new[] { "Satz 9.", "Satz 10." }, added.Select(line => line.GermanText).ToArray());
            CollectionAssert.AreEqual(new[] { "SATZ 9.", "SATZ 10." }, added.Select(line => line.EnglishText).ToArray());
        }

        private sealed class FixedSource : ICaptionSource
        {
            private readonly IReadOnlyList<CaptionSegment> segments;

            public FixedSource(IReadOnlyList<CaptionSegment> segments) => this.segments = segments;

            public string DisplayName => "Fake";

            public CaptionReadResult Read() => CaptionReadResult.Read(this.segments, "Reading");

            public string DumpTree() => string.Empty;
        }

        private sealed class UpperCaseTranslator : ITranslator
        {
            public string Translate(string germanText) => germanText.ToUpperInvariant();

            public void Dispose() { }
        }
    }
}
