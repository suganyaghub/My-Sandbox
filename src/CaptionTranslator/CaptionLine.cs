using System.ComponentModel;
using System.Windows.Media;

namespace CaptionTranslator
{
    /// <summary>One finished caption line shown as a card. English text arrives later from the translation queue.</summary>
    public sealed class CaptionLine : INotifyPropertyChanged
    {
        private static readonly Color[] avatarPalette =
        {
            Color.FromRgb(0x5B, 0x8C, 0xFF), Color.FromRgb(0x8B, 0x6C, 0xFF), Color.FromRgb(0x2F, 0xB8, 0x80), Color.FromRgb(0xE0, 0x9A, 0x2E),
            Color.FromRgb(0xE8, 0x5A, 0x8A), Color.FromRgb(0x2A, 0xA8, 0xD8), Color.FromRgb(0xB0, 0x6A, 0xE8), Color.FromRgb(0xE8, 0x74, 0x4A),
        };

        private string germanText;
        private string? englishText;
        private bool translationFailed;
        private bool isMe;
        private bool isLive;

        public CaptionLine(string speaker, string germanText)
        {
            this.Speaker = speaker;
            this.germanText = germanText;
            this.Time = DateTime.Now;
            this.Initials = GetInitials(speaker);
            this.AvatarColor = GetAvatarColor(speaker);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Speaker { get; }

        /// <summary>The caption text; Teams revises it while the person speaks, so it can change after the card was added.</summary>
        public string GermanText => this.germanText;

        /// <summary>True while the person is still speaking in this caption bubble (text may still change).</summary>
        public bool IsLive
        {
            get => this.isLive;
            set
            {
                if (this.isLive == value)
                    return;

                this.isLive = value;
                OnPropertyChanged(nameof(this.IsLive));
            }
        }

        public void UpdateGerman(string text)
        {
            if (this.germanText == text)
                return;

            this.germanText = text;
            OnPropertyChanged(nameof(this.GermanText));
        }

        public DateTime Time { get; }

        public string TimeLabel => this.Time.ToString("HH:mm");

        public bool HasSpeaker => this.Speaker.Length > 0;

        /// <summary>
        /// True if the previous line is from the same speaker shortly before; the card then continues it
        /// (no avatar, no name) like a chat. Set before the line is added to the list.
        /// </summary>
        public bool IsContinuation { get; set; }

        /// <summary>True if this line was spoken by the user (their name matches "My name in Teams captions").</summary>
        public bool IsMe
        {
            get => this.isMe;
            set
            {
                if (this.isMe == value)
                    return;

                this.isMe = value;
                OnPropertyChanged(nameof(this.IsMe));
            }
        }

        public string Initials { get; }

        public Color AvatarColor { get; }

        public string SpeakerLabel => this.Speaker.Length > 0 ? this.Speaker + ":  " : string.Empty;

        public string? EnglishText => this.englishText;

        public bool IsTranslating => this.englishText == null && !this.translationFailed;

        public string EnglishDisplay => this.translationFailed ? "[translation failed] " + this.GermanText : this.englishText ?? "Translating…";

        public void SetEnglish(string english)
        {
            this.englishText = english;
            OnPropertyChanged(nameof(this.EnglishText));
            OnPropertyChanged(nameof(this.EnglishDisplay));
            OnPropertyChanged(nameof(this.IsTranslating));
        }

        public void SetTranslationFailed()
        {
            this.translationFailed = true;
            OnPropertyChanged(nameof(this.EnglishDisplay));
            OnPropertyChanged(nameof(this.IsTranslating));
        }

        public static string GetInitials(string speaker)
        {
            string[] parts = speaker.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return "CC";

            return parts.Length == 1
                ? parts[0].Substring(0, 1).ToUpperInvariant()
                : (parts[0].Substring(0, 1) + parts[^1].Substring(0, 1)).ToUpperInvariant();
        }

        /// <summary>Stable colour per speaker name, so the same person always has the same avatar colour.</summary>
        public static Color GetAvatarColor(string speaker)
        {
            int hash = 17;
            foreach (char character in speaker)
                hash = unchecked((hash * 31) + character);

            return avatarPalette[(hash & int.MaxValue) % avatarPalette.Length];
        }

        private void OnPropertyChanged(string propertyName) => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
