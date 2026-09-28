using CaptionTranslator.Speech;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class SpeechQueueTest
    {
        [TestMethod]
        public async Task DequeueAsync_LinesEnqueued_ReturnsThemInOrder()
        {
            SpeechQueue queue = new SpeechQueue(2);
            queue.Enqueue("one");
            queue.Enqueue("two");

            Assert.AreEqual("one", await queue.DequeueAsync(CancellationToken.None));
            Assert.AreEqual("two", await queue.DequeueAsync(CancellationToken.None));
        }

        [TestMethod]
        public async Task Enqueue_MoreThanMaxPending_DropsOldestToStayLive()
        {
            SpeechQueue queue = new SpeechQueue(2);
            queue.Enqueue("one");
            queue.Enqueue("two");

            int dropped = queue.Enqueue("three");

            Assert.AreEqual(1, dropped);
            Assert.AreEqual(2, queue.Count);
            Assert.AreEqual("two", await queue.DequeueAsync(CancellationToken.None));
            Assert.AreEqual("three", await queue.DequeueAsync(CancellationToken.None));
        }

        [TestMethod]
        public async Task DequeueAsync_AfterClear_ReturnsNullThenWaitsForNewLine()
        {
            SpeechQueue queue = new SpeechQueue(2);
            queue.Enqueue("old");
            queue.Clear();

            Assert.IsNull(await queue.DequeueAsync(CancellationToken.None));

            Task<string?> next = queue.DequeueAsync(CancellationToken.None);
            Assert.IsFalse(next.IsCompleted);

            queue.Enqueue("new");
            Assert.AreEqual("new", await next.WaitAsync(TimeSpan.FromSeconds(2)));
        }
    }
}
