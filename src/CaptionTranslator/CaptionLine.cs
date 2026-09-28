using System.ComponentModel;

namespace CaptionTranslator
{
    /// <summary>One finished caption line shown in the window. English text arrives later from the translation queue.</summary>
    public sealed class CaptionLine : INotifyPropertyChanged
    {
        private string? englishText;
        private bool translationFailed;

        public CaptionLine(string speaker, string germanText)
        {
            this.Speaker = speaker;
            this.GermanText = germanText;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Speaker { get; }

        public string GermanText { get; }

        public string SpeakerLabel => this.Speaker.Length > 0 ? this.Speaker + ":  " : string.Empty;

        public string? EnglishText => this.englishText;

        public string EnglishDisplay => this.translationFailed ? "[translation failed] " + this.GermanText : this.englishText ?? "…";

        public void SetEnglish(string english)
        {
            this.englishText = english;
            OnPropertyChanged(nameof(this.EnglishText));
            OnPropertyChanged(nameof(this.EnglishDisplay));
        }

        public void SetTranslationFailed()
        {
            this.translationFailed = true;
            OnPropertyChanged(nameof(this.EnglishDisplay));
        }

        private void OnPropertyChanged(string propertyName) => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
