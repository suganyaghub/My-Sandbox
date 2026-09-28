namespace CaptionTranslator.Translation
{
    public interface ITranslator : IDisposable
    {
        /// <summary>Translates German text (one or more sentences) to English.</summary>
        string Translate(string germanText);
    }
}
