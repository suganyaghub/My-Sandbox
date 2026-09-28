namespace CaptionTranslator.Translation
{
    /// <summary>
    /// Remembers translations of single sentences. Caption bubbles are re-translated whenever Teams changes them;
    /// unchanged sentences then come from the cache and only the changed ones cost model time. Not thread-safe
    /// (used by the single translation worker).
    /// </summary>
    public sealed class CachedTranslator
    {
        private const int capacity = 3000;

        private readonly ITranslator translator;
        private readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.Ordinal);

        public CachedTranslator(ITranslator translator)
        {
            this.translator = translator;
        }

        public string TranslateSentence(string germanSentence)
        {
            if (this.cache.TryGetValue(germanSentence, out string? english))
                return english;

            english = this.translator.Translate(germanSentence);
            if (this.cache.Count >= capacity)
                this.cache.Clear();

            this.cache[germanSentence] = english;
            return english;
        }
    }
}
