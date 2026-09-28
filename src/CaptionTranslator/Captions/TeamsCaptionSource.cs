using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace CaptionTranslator.Captions
{
    /// <summary>Reads Teams live captions (new Teams "ms-teams" and classic "Teams") through UI Automation.</summary>
    public sealed class TeamsCaptionSource : ICaptionSource
    {
        private static readonly string[] processNames = { "ms-teams", "Teams" };
        private static readonly TimeSpan locateRetryInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan relocateWhenEmptyInterval = TimeSpan.FromSeconds(10);

        private static readonly HashSet<ControlType> excludedCandidateTypes = new HashSet<ControlType>
        {
            ControlType.Button, ControlType.MenuItem, ControlType.CheckBox, ControlType.Text, ControlType.ToolTip,
            ControlType.SplitButton, ControlType.Hyperlink, ControlType.Menu, ControlType.ComboBox, ControlType.Window,
        };

        private readonly Regex containerPattern;
        private AutomationElement? container;
        private DateTime nextLocateAttempt = DateTime.MinValue;
        private DateTime lastNonEmptyRead = DateTime.MinValue;
        private string lastStatus = "Looking for Teams captions…";
        private CaptionReadResult? lastResult;

        public TeamsCaptionSource(string containerPattern)
        {
            this.containerPattern = new Regex(containerPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public string DisplayName => "Teams captions";

        public CaptionReadResult Read()
        {
            DateTime now = DateTime.UtcNow;

            if (this.container == null)
            {
                if (now < this.nextLocateAttempt)
                    return CaptionReadResult.NotAvailable(this.lastStatus);

                this.nextLocateAttempt = now + locateRetryInterval;
                this.lastStatus = Locate();
                if (this.container == null)
                    return CaptionReadResult.NotAvailable(this.lastStatus);

                this.lastNonEmptyRead = now;
            }

            try
            {
                UiNode snapshot = UiAutomationHelper.Snapshot(this.container);
                IReadOnlyList<CaptionSegment> segments = TeamsCaptionParser.Parse(snapshot);

                if (segments.Count > 0)
                {
                    this.lastNonEmptyRead = now;
                }
                else if (now - this.lastNonEmptyRead > relocateWhenEmptyInterval)
                {
                    // Possibly the wrong element was picked, or Teams rebuilt the caption area. Search again.
                    this.container = null;
                }

                this.lastResult = CaptionReadResult.Read(segments, segments.Count > 0 ? "Reading Teams captions" : "Teams captions found – waiting for speech…");
                return this.lastResult;
            }
            catch (ElementNotAvailableException)
            {
                this.container = null;
                this.lastResult = null;
                this.lastStatus = "Teams caption area closed – searching again…";
                return CaptionReadResult.NotAvailable(this.lastStatus);
            }
            catch (COMException)
            {
                // Teams changed the caption list while it was being read; the next poll reads it again.
                return this.lastResult ?? CaptionReadResult.NotAvailable(this.lastStatus);
            }
        }

        public static bool IsTransient(Exception exception) => exception is ElementNotAvailableException || exception is COMException;

        public string DumpTree()
        {
            IReadOnlyList<AutomationElement> windows = UiAutomationHelper.FindTopLevelWindows(processNames);
            if (windows.Count == 0)
                return "Teams is not running.";

            StringBuilder builder = new StringBuilder();
            foreach (AutomationElement window in windows)
            {
                try
                {
                    builder.AppendLine("=== Window ===");
                    builder.Append(UiAutomationHelper.Format(UiAutomationHelper.Snapshot(window)));
                }
                catch (Exception exception) when (IsTransient(exception))
                {
                    builder.AppendLine("(window closed)");
                }
            }

            return builder.ToString();
        }

        private string Locate()
        {
            IReadOnlyList<AutomationElement> windows = UiAutomationHelper.FindTopLevelWindows(processNames);
            if (windows.Count == 0)
                return "Teams is not running.";

            AutomationElement? best = null;
            int bestTextCount = -1;

            foreach (AutomationElement candidate in FindCandidates(windows))
            {
                int textCount;
                try
                {
                    textCount = TeamsCaptionParser.CountTexts(UiAutomationHelper.Snapshot(candidate));
                }
                catch (Exception exception) when (IsTransient(exception))
                {
                    continue;
                }

                if (textCount > bestTextCount)
                {
                    best = candidate;
                    bestTextCount = textCount;
                }
            }

            this.container = best;
            if (best == null)
                return "Teams is running, but no captions found. In the meeting: More (…) → Language and speech → Turn on live captions, and set the spoken language to German.";

            Log.Info($"Teams caption container selected: '{SafeName(best)}' ({bestTextCount} text elements).");
            return "Teams captions found – waiting for speech…";
        }

        private IEnumerable<AutomationElement> FindCandidates(IReadOnlyList<AutomationElement> windows)
        {
            CacheRequest request = new CacheRequest { AutomationElementMode = AutomationElementMode.Full };
            request.Add(AutomationElement.NameProperty);
            request.Add(AutomationElement.AutomationIdProperty);
            request.Add(AutomationElement.ControlTypeProperty);

            List<AutomationElement> candidates = new List<AutomationElement>();
            foreach (AutomationElement window in windows)
            {
                AutomationElementCollection elements;
                try
                {
                    using (request.Activate())
                        elements = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                }
                catch (Exception exception) when (IsTransient(exception))
                {
                    continue;
                }

                foreach (AutomationElement element in elements)
                {
                    if (excludedCandidateTypes.Contains(element.Cached.ControlType))
                        continue;

                    string name = element.Cached.Name ?? string.Empty;
                    string automationId = element.Cached.AutomationId ?? string.Empty;
                    if ((name.Length < 80 && this.containerPattern.IsMatch(name)) || this.containerPattern.IsMatch(automationId))
                        candidates.Add(element);
                }
            }

            return candidates;
        }

        private static string SafeName(AutomationElement element)
        {
            try
            {
                return element.Current.Name;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                return "?";
            }
        }
    }
}
