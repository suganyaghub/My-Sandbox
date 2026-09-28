using System.Diagnostics;
using CaptionTranslator.Captions;

namespace CaptionTranslator.Tests
{
    /// <summary>
    /// Checks against a running meeting that Teams keeps the same UI Automation id for a caption bubble while its text
    /// changes, and gives a new bubble a new id. Prints numbers only.
    /// </summary>
    [TestClass]
    [TestCategory("Live")]
    public class BubbleIdStabilityTest
    {
        [TestMethod]
        public void Read_LiveMeeting_BubbleIdsStayStableWhileTextChanges()
        {
            TeamsCaptionSource source = new TeamsCaptionSource(new AppSettings().TeamsCaptionContainerPattern);
            if (source.Read().Segments.Count == 0)
                Assert.Inconclusive("No Teams meeting with live captions open.");

            Dictionary<string, HashSet<string>> textsById = new Dictionary<string, HashSet<string>>();
            Dictionary<string, HashSet<string>> idsByText = new Dictionary<string, HashSet<string>>();
            int missingIds = 0;
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(30))
            {
                foreach (CaptionSegment segment in source.Read().Segments)
                {
                    if (segment.Id == null)
                    {
                        missingIds++;
                        continue;
                    }

                    textsById.TryAdd(segment.Id, new HashSet<string>());
                    textsById[segment.Id].Add(segment.Text);
                    idsByText.TryAdd(segment.Text, new HashSet<string>());
                    idsByText[segment.Text].Add(segment.Id);
                }

                Thread.Sleep(250);
            }

            int changedBubbles = textsById.Count(pair => pair.Value.Count > 1);
            int textsUnderSeveralIds = idsByText.Count(pair => pair.Value.Count > 1);
            Console.WriteLine($"Bubble ids: {textsById.Count}, bubbles whose text changed under the same id: {changedBubbles}, " +
                              $"max versions of one bubble: {textsById.Values.Max(texts => texts.Count)}, identical text under several ids: {textsUnderSeveralIds}, segments without id: {missingIds}");

            Assert.AreEqual(0, missingIds, "Teams caption items have no runtime id.");
            Assert.AreEqual(0, textsUnderSeveralIds, "The same caption text appeared under different ids (ids are not stable).");
        }
    }
}
