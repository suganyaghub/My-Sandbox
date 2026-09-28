namespace CaptionTranslator.Captions
{
    /// <summary>Reads the currently visible captions from another application. Called from a background thread.</summary>
    public interface ICaptionSource
    {
        string DisplayName { get; }

        CaptionReadResult Read();

        /// <summary>Writes the UI Automation tree of the source application(s), for diagnosing detection problems.</summary>
        string DumpTree();
    }
}
