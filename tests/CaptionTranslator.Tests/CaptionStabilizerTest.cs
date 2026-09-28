using CaptionTranslator.Captions;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class CaptionStabilizerTest
    {
        private static readonly DateTimeOffset start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
        private readonly CaptionStabilizer stabilizer = new CaptionStabilizer(TimeSpan.FromSeconds(1.5));

        [TestMethod]
        public void Process_LastSegmentStillChanging_EmitsNothingAndReportsPending()
        {
            this.stabilizer.Process(Snapshot(("Anna", "Guten")), start);
            IReadOnlyList<CaptionSegment> result = this.stabilizer.Process(Snapshot(("Anna", "Guten Morgen")), start.AddSeconds(1));

            Assert.AreEqual(0, result.Count);
            Assert.AreEqual("Guten Morgen", this.stabilizer.Pending?.Text);
        }

        [TestMethod]
        public void Process_LastSegmentUnchangedForQuietPeriod_EmitsItOnce()
        {
            this.stabilizer.Process(Snapshot(("Anna", "Guten Morgen.")), start);
            IReadOnlyList<CaptionSegment> first = this.stabilizer.Process(Snapshot(("Anna", "Guten Morgen.")), start.AddSeconds(2));
            IReadOnlyList<CaptionSegment> second = this.stabilizer.Process(Snapshot(("Anna", "Guten Morgen.")), start.AddSeconds(3));

            Assert.AreEqual(1, first.Count);
            Assert.AreEqual(new CaptionSegment("Anna", "Guten Morgen."), first[0]);
            Assert.AreEqual(0, second.Count);
        }

        [TestMethod]
        public void Process_NewerSegmentAppears_EmitsPreviousImmediately()
        {
            this.stabilizer.Process(Snapshot(("Anna", "Guten Morgen.")), start);
            IReadOnlyList<CaptionSegment> result = this.stabilizer.Process(Snapshot(("Anna", "Guten Morgen."), ("Ben", "Hallo")), start.AddMilliseconds(400));

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Anna", result[0].Speaker);
        }

        [TestMethod]
        public void Process_EmittedLineGrowsLater_EmitsOnlyRemainder()
        {
            this.stabilizer.Process(Snapshot(("Anna", "Wir starten")), start);
            this.stabilizer.Process(Snapshot(("Anna", "Wir starten")), start.AddSeconds(2));

            this.stabilizer.Process(Snapshot(("Anna", "Wir starten jetzt mit dem Meeting.")), start.AddSeconds(3));
            IReadOnlyList<CaptionSegment> result = this.stabilizer.Process(Snapshot(("Anna", "Wir starten jetzt mit dem Meeting.")), start.AddSeconds(5));

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("jetzt mit dem Meeting.", result[0].Text);
        }

        [TestMethod]
        public void Process_FirstSegmentCutOffByScrolling_IsNotEmittedAgain()
        {
            this.stabilizer.Process(Snapshot(("", "Das ist der erste Satz."), ("", "Zweiter")), start);
            this.stabilizer.Process(Snapshot(("", "ist der erste Satz."), ("", "Zweiter Satz.")), start.AddMilliseconds(400));
            IReadOnlyList<CaptionSegment> result = this.stabilizer.Process(Snapshot(("", "ist der erste Satz."), ("", "Zweiter Satz."), ("", "Dritter")), start.AddMilliseconds(800));

            CollectionAssert.AreEqual(new[] { new CaptionSegment("", "Zweiter Satz.") }, result.ToArray());
        }

        [TestMethod]
        public void Process_WhitespaceDiffers_TreatedAsSameLine()
        {
            this.stabilizer.Process(Snapshot(("Anna", "Guten  Morgen.")), start);
            this.stabilizer.Process(Snapshot(("Anna", "Guten Morgen. ")), start.AddSeconds(2));
            IReadOnlyList<CaptionSegment> result = this.stabilizer.Process(Snapshot(("Anna", "Guten Morgen."), ("Ben", "Hallo")), start.AddSeconds(3));

            Assert.AreEqual(0, result.Count);
        }

        private static IReadOnlyList<CaptionSegment> Snapshot(params (string Speaker, string Text)[] segments)
            => segments.Select(segment => new CaptionSegment(segment.Speaker, segment.Text)).ToList();
    }
}
