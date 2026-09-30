using System.Net;
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class VoiceFilesTest
    {
        private static readonly PiperVoiceInfo voice = new PiperVoiceInfo("en_US-test-medium", "Test", "en/en_US/test/medium", 1, "CC0");
        private string root = string.Empty;

        [TestInitialize]
        public void CreateRoot() => this.root = Path.Combine(Path.GetTempPath(), "ct-voices-" + Guid.NewGuid().ToString("N"));

        [TestCleanup]
        public void DeleteRoot()
        {
            if (Directory.Exists(this.root))
                Directory.Delete(this.root, true);
        }

        [TestMethod]
        public async Task DownloadAsync_Success_StoresBothFilesAndReportsProgress()
        {
            FakeHandler handler = new FakeHandler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1000]) });
            VoiceFiles files = new VoiceFiles(this.root, handler, "https://example.test/");
            List<int> reported = new List<int>();

            await files.DownloadAsync(voice, new SynchronousProgress(reported.Add), CancellationToken.None);

            Assert.IsTrue(files.IsDownloaded(voice));
            CollectionAssert.AreEqual(
                new[] { "https://example.test/en/en_US/test/medium/en_US-test-medium.onnx.json", "https://example.test/en/en_US/test/medium/en_US-test-medium.onnx" },
                handler.Requests);
            Assert.AreEqual(100, reported[^1]);
            Assert.IsEmpty(Directory.GetFiles(this.root, "*.part", SearchOption.AllDirectories));
        }

        [TestMethod]
        public async Task DownloadAsync_ModelNotFound_LeavesNoFiles()
        {
            FakeHandler handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith(".onnx")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) });
            VoiceFiles files = new VoiceFiles(this.root, handler, "https://example.test/");

            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => files.DownloadAsync(voice, new SynchronousProgress(_ => { }), CancellationToken.None));

            Assert.IsFalse(files.IsDownloaded(voice));
            Assert.IsEmpty(Directory.GetFiles(this.root, "*", SearchOption.AllDirectories));
        }

        [TestMethod]
        public async Task DownloadAsync_StreamBreaks_LeavesNoFiles()
        {
            FakeHandler handler = new FakeHandler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) });
            VoiceFiles files = new VoiceFiles(this.root, handler, "https://example.test/");

            await Assert.ThrowsExactlyAsync<IOException>(() => files.DownloadAsync(voice, new SynchronousProgress(_ => { }), CancellationToken.None));

            Assert.IsFalse(files.IsDownloaded(voice));
            Assert.IsEmpty(Directory.GetFiles(this.root, "*", SearchOption.AllDirectories));
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => this.respond = respond;

            public List<string> Requests { get; } = new List<string>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                this.Requests.Add(request.RequestUri!.ToString());
                return Task.FromResult(this.respond(request));
            }
        }

        /// <summary>Returns some bytes, then fails like a dropped connection.</summary>
        private sealed class BrokenStream : MemoryStream
        {
            private int calls;

            public BrokenStream() : base(new byte[100]) { }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                => ++this.calls > 1 ? throw new IOException("Connection reset.") : base.ReadAsync(buffer, cancellationToken);

            public override int Read(byte[] buffer, int offset, int count)
                => ++this.calls > 1 ? throw new IOException("Connection reset.") : base.Read(buffer, offset, count);
        }

        /// <summary><see cref="Progress{T}"/> posts to a synchronization context; this one reports immediately.</summary>
        private sealed class SynchronousProgress : IProgress<int>
        {
            private readonly Action<int> report;

            public SynchronousProgress(Action<int> report) => this.report = report;

            public void Report(int value) => this.report(value);
        }
    }
}
