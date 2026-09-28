using CaptionTranslator.Captions;

namespace CaptionTranslator.Tests
{
    /// <summary>Runs against the real Teams client; inconclusive unless a meeting with live captions is open.</summary>
    [TestClass]
    [TestCategory("Live")]
    public class LiveTeamsCaptionSourceTest
    {
        [TestMethod]
        public void Read_MeetingWithCaptionsOpen_ReturnsSegmentsWithSpeakers()
        {
            TeamsCaptionSource source = new TeamsCaptionSource(new AppSettings().TeamsCaptionContainerPattern);

            CaptionReadResult result = source.Read();
            if (!result.IsReading || result.Segments.Count == 0)
                Assert.Inconclusive("No Teams meeting with live captions open: " + result.Status);

            Console.WriteLine($"Segments: {result.Segments.Count}, speakers: {result.Segments.Select(segment => segment.Speaker).Distinct().Count()}");
            // Teams also shows system lines without a speaker (e.g. "Transcription has started…"), so not every line has one.
            Assert.IsTrue(result.Segments.All(segment => segment.Text.Length > 0));
            Assert.IsTrue(result.Segments.Any(segment => segment.Speaker.Length > 0));
            Assert.IsFalse(result.Segments.Any(segment => segment.Text.Contains("Live Captions")));
        }
    }
}
