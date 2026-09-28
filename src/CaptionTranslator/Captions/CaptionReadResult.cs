namespace CaptionTranslator.Captions
{
    /// <summary>Result of one poll of a caption source.</summary>
    public sealed class CaptionReadResult
    {
        private CaptionReadResult(bool isReading, IReadOnlyList<CaptionSegment> segments, string status)
        {
            this.IsReading = isReading;
            this.Segments = segments;
            this.Status = status;
        }

        /// <summary>True when the caption area was found and read (it may still be empty).</summary>
        public bool IsReading { get; }

        /// <summary>Visible caption segments in display order, oldest first. The last one may still be in progress.</summary>
        public IReadOnlyList<CaptionSegment> Segments { get; }

        public string Status { get; }

        public static CaptionReadResult Read(IReadOnlyList<CaptionSegment> segments, string status)
            => new CaptionReadResult(true, segments, status);

        public static CaptionReadResult NotAvailable(string status)
            => new CaptionReadResult(false, Array.Empty<CaptionSegment>(), status);
    }
}
