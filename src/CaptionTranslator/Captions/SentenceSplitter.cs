using System.Text.RegularExpressions;

namespace CaptionTranslator.Captions
{
    public static class SentenceSplitter
    {
        private static readonly Regex boundary = new Regex(@"(?<=[.!?…])\s+", RegexOptions.Compiled);

        /// <summary>Splits text into sentences at ". ", "! ", "? ". The last part may be an unfinished sentence.</summary>
        public static IReadOnlyList<string> Split(string text)
        {
            string normalized = TeamsCaptionParser.NormalizeWhitespace(text);
            if (normalized.Length == 0)
                return Array.Empty<string>();

            return boundary.Split(normalized).Where(part => part.Length > 0).ToList();
        }
    }
}
