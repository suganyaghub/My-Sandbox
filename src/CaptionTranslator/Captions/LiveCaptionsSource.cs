using System.Text;
using System.Windows.Automation;

namespace CaptionTranslator.Captions
{
    /// <summary>
    /// Reads the built-in Windows 11 Live Captions window (LiveCaptions.exe). Works for any audio on the PC,
    /// transcribed offline by Windows. Requires the German speech language to be set in Live Captions.
    /// </summary>
    public sealed class LiveCaptionsSource : ICaptionSource
    {
        private const string processName = "LiveCaptions";
        private const string captionsTextBlockId = "CaptionsTextBlock";

        private AutomationElement? textBlock;

        public string DisplayName => "Windows Live Captions";

        public CaptionReadResult Read()
        {
            try
            {
                if (this.textBlock == null)
                {
                    IReadOnlyList<AutomationElement> windows = UiAutomationHelper.FindTopLevelWindows(processName);
                    if (windows.Count == 0)
                        return CaptionReadResult.NotAvailable("Windows Live Captions is not running. Press Win+Ctrl+L and set the language to German.");

                    Condition condition = new PropertyCondition(AutomationElement.AutomationIdProperty, captionsTextBlockId);
                    this.textBlock = windows.Select(window => window.FindFirst(TreeScope.Descendants, condition)).FirstOrDefault(element => element != null);
                    if (this.textBlock == null)
                        return CaptionReadResult.NotAvailable("Windows Live Captions is running, but its caption text was not found.");
                }

                string text = this.textBlock.Current.Name ?? string.Empty;
                // One segment: CaptionStabilizer splits it into sentences and waits until each is stable,
                // because Live Captions keeps correcting the most recent words.
                string normalized = TeamsCaptionParser.NormalizeWhitespace(text);
                List<CaptionSegment> segments = normalized.Length > 0 ? new List<CaptionSegment> { new CaptionSegment(string.Empty, normalized) } : new List<CaptionSegment>();
                return CaptionReadResult.Read(segments, segments.Count > 0 ? "Reading Windows Live Captions" : "Windows Live Captions found – waiting for speech…");
            }
            catch (Exception exception) when (TeamsCaptionSource.IsTransient(exception))
            {
                this.textBlock = null;
                return CaptionReadResult.NotAvailable("Windows Live Captions was closed.");
            }
        }

        public string DumpTree()
        {
            IReadOnlyList<AutomationElement> windows = UiAutomationHelper.FindTopLevelWindows(processName);
            if (windows.Count == 0)
                return "Windows Live Captions is not running.";

            StringBuilder builder = new StringBuilder();
            foreach (AutomationElement window in windows)
            {
                builder.AppendLine("=== Window ===");
                builder.Append(UiAutomationHelper.Format(UiAutomationHelper.Snapshot(window)));
            }

            return builder.ToString();
        }
    }
}
