using System.Text.RegularExpressions;

namespace CaptionTranslator.Captions
{
    /// <summary>Word-level text comparison that ignores case and punctuation (Teams changes both while it revises captions).</summary>
    public static class CaptionText
    {
        private static readonly Regex nonWord = new Regex(@"[^\p{L}\p{N}]", RegexOptions.Compiled);

        /// <summary>Lower-case words without punctuation, e.g. "Die Tests laufen, damit." → ["die", "tests", "laufen", "damit"].</summary>
        public static string[] Words(string text)
            => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(NormalizeToken).Where(word => word.Length > 0).ToArray();

        public static bool EndsWithSentencePunctuation(string text)
        {
            string trimmed = text.TrimEnd();
            return trimmed.Length > 0 && ".!?…".Contains(trimmed[^1]);
        }

        /// <summary>True if <paramref name="words"/> begins with all of <paramref name="prefix"/>.</summary>
        public static bool StartsWith(string[] words, string[] prefix)
            => prefix.Length <= words.Length && words.Take(prefix.Length).SequenceEqual(prefix);

        /// <summary>Share of <paramref name="words"/> that also occur in <paramref name="known"/> (0..1).</summary>
        public static double Coverage(string[] words, HashSet<string> known)
            => words.Length == 0 ? 1 : words.Count(known.Contains) / (double)words.Length;

        /// <summary>
        /// Longest common word sequence (in order) divided by the longer sentence's length (0..1). A corrected sentence
        /// keeps most words in the same order (high); a different sentence that shares common words does not.
        /// </summary>
        public static double OrderedSimilarity(string[] first, string[] second)
        {
            if (first.Length == 0 || second.Length == 0)
                return 0;

            int[] previousRow = new int[second.Length + 1];
            int[] currentRow = new int[second.Length + 1];
            for (int row = 1; row <= first.Length; row++)
            {
                for (int column = 1; column <= second.Length; column++)
                {
                    currentRow[column] = first[row - 1] == second[column - 1]
                        ? previousRow[column - 1] + 1
                        : Math.Max(previousRow[column], currentRow[column - 1]);
                }

                (previousRow, currentRow) = (currentRow, previousRow);
            }

            return previousRow[second.Length] / (double)Math.Max(first.Length, second.Length);
        }

        /// <summary>The original text after the first <paramref name="wordCount"/> words (punctuation kept).</summary>
        public static string TextAfterWords(string text, int wordCount)
        {
            string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int seen = 0;
            for (int index = 0; index < tokens.Length; index++)
            {
                if (seen == wordCount)
                    return string.Join(" ", tokens.Skip(index));

                if (NormalizeToken(tokens[index]).Length > 0)
                    seen++;
            }

            return string.Empty;
        }

        private static string NormalizeToken(string token) => nonWord.Replace(token, string.Empty).ToLowerInvariant();
    }
}
