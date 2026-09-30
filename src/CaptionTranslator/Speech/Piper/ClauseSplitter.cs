using System.Text.RegularExpressions;

namespace CaptionTranslator.Speech.Piper
{
    /// <summary>
    /// Splits text into clauses at , ; : . ! ? followed by a space (so "3.5" stays together).
    /// Speaking clause by clause lets audio start early and keeps the pause of each punctuation mark.
    /// </summary>
    public static class ClauseSplitter
    {
        private const string marks = ",;:.!?";

        private static readonly Regex boundary = new Regex(@"(?<=[,;:.!?])\s+", RegexOptions.Compiled);

        public static IReadOnlyList<Clause> Split(string text)
        {
            List<Clause> clauses = new List<Clause>();
            foreach (string part in boundary.Split(text.Trim()))
            {
                string trimmed = part.Trim();
                int end = trimmed.Length;
                while (end > 0 && marks.IndexOf(trimmed[end - 1]) >= 0)
                    end--;

                string words = trimmed[..end].Trim();
                if (!words.Any(char.IsLetterOrDigit))
                    continue;

                char? mark = end < trimmed.Length ? trimmed[^1] : null;
                clauses.Add(new Clause(words, mark));
            }

            return clauses;
        }
    }
}
