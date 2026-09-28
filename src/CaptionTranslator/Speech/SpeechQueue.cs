namespace CaptionTranslator.Speech
{
    /// <summary>
    /// Lines waiting to be read aloud. Keeps the reader close to live: when more than
    /// <see cref="MaxPending"/> lines wait, the oldest are dropped. Thread-safe.
    /// </summary>
    public sealed class SpeechQueue
    {
        private readonly object queueLock = new object();
        private readonly LinkedList<string> pending = new LinkedList<string>();
        private readonly SemaphoreSlim available = new SemaphoreSlim(0);

        public SpeechQueue(int maxPending)
        {
            this.MaxPending = maxPending;
        }

        public int MaxPending { get; }

        public int Count
        {
            get
            {
                lock (this.queueLock)
                    return this.pending.Count;
            }
        }

        /// <summary>Adds a line. Returns the number of older lines that were dropped to stay close to live.</summary>
        public int Enqueue(string text)
        {
            int dropped = 0;
            lock (this.queueLock)
            {
                this.pending.AddLast(text);
                while (this.pending.Count > this.MaxPending)
                {
                    this.pending.RemoveFirst();
                    dropped++;
                }
            }

            if (dropped == 0)
                this.available.Release();

            return dropped;
        }

        public void Clear()
        {
            lock (this.queueLock)
                this.pending.Clear();
        }

        /// <summary>Waits for the next line. Returns null if the queue was cleared while waiting.</summary>
        public async Task<string?> DequeueAsync(CancellationToken cancellationToken)
        {
            await this.available.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (this.queueLock)
            {
                if (this.pending.Count == 0)
                    return null;

                string text = this.pending.First!.Value;
                this.pending.RemoveFirst();
                return text;
            }
        }
    }
}
