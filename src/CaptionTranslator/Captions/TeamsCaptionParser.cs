using System.Text.RegularExpressions;

namespace CaptionTranslator.Captions
{
    /// <summary>
    /// Turns a snapshot of the Teams caption area into caption segments.
    /// Expected shape: a container (possibly wrapped) whose children are caption items; each item holds
    /// text elements where the first is the speaker name and the rest is the spoken text.
    /// </summary>
    public static class TeamsCaptionParser
    {
        private static readonly HashSet<string> excludedControlTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Button", "MenuItem", "CheckBox", "ComboBox", "Menu", "MenuBar", "ToolBar", "ToolTip", "SplitButton", "Hyperlink",
        };

        private static readonly Regex whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        public static IReadOnlyList<CaptionSegment> Parse(UiNode container)
        {
            UiNode node = FindItemList(container);

            List<CaptionSegment> segments = new List<CaptionSegment>();
            foreach (UiNode item in node.Children)
            {
                if (excludedControlTypes.Contains(item.ControlType))
                    continue;

                List<string> texts = CollectTexts(item);
                if (texts.Count == 0)
                    continue;

                if (texts.Count == 1)
                    segments.Add(new CaptionSegment(string.Empty, texts[0]));
                else
                    segments.Add(new CaptionSegment(texts[0], string.Join(" ", texts.Skip(1))));
            }

            return segments;
        }

        public static int CountTexts(UiNode node) => CollectTexts(node).Count;

        /// <summary>
        /// The caption pane also holds a header text and buttons, and the list sits several wrapper groups deep.
        /// The list is the node with the most non-text children that contain text (the caption items).
        /// On a tie the deeper node wins, so wrappers around a single item are skipped.
        /// </summary>
        private static UiNode FindItemList(UiNode container)
        {
            UiNode best = container;
            int bestScore = -1;

            foreach (UiNode node in container.DescendantsAndSelf())
            {
                if (excludedControlTypes.Contains(node.ControlType))
                    continue;

                int score = node.Children.Count(child => !IsText(child) && !excludedControlTypes.Contains(child.ControlType) && CountTexts(child) > 0);
                if (score > 0 && score >= bestScore)
                {
                    best = node;
                    bestScore = score;
                }
            }

            return best;
        }

        private static bool IsText(UiNode node) => string.Equals(node.ControlType, "Text", StringComparison.OrdinalIgnoreCase);

        public static string NormalizeWhitespace(string text) => whitespace.Replace(text, " ").Trim();

        private static List<string> CollectTexts(UiNode root)
        {
            List<string> texts = new List<string>();
            Collect(root, texts);
            return texts;
        }

        private static void Collect(UiNode node, List<string> texts)
        {
            if (excludedControlTypes.Contains(node.ControlType))
                return;

            if (string.Equals(node.ControlType, "Text", StringComparison.OrdinalIgnoreCase))
            {
                string text = NormalizeWhitespace(node.Name);

                // Chromium often nests a text element with the same name inside another one.
                if (text.Length > 0 && (texts.Count == 0 || texts[^1] != text))
                    texts.Add(text);
            }

            foreach (UiNode child in node.Children)
                Collect(child, texts);
        }
    }
}
