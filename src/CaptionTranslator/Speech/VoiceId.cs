namespace CaptionTranslator.Speech
{
    /// <summary>A voice as stored in the settings: "piper:&lt;id&gt;" or "windows:&lt;SAPI name&gt;".</summary>
    public sealed record VoiceId(bool IsNatural, string Name)
    {
        public const string NaturalPrefix = "piper:";
        public const string WindowsPrefix = "windows:";

        public static VoiceId Natural(string id) => new VoiceId(true, id);

        public static VoiceId Windows(string name) => new VoiceId(false, name);

        /// <summary>Null for empty values. A value without prefix is a Windows voice (settings saved before natural voices existed).</summary>
        public static VoiceId? Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            VoiceId id = value.StartsWith(NaturalPrefix, StringComparison.Ordinal) ? Natural(value[NaturalPrefix.Length..])
                       : value.StartsWith(WindowsPrefix, StringComparison.Ordinal) ? Windows(value[WindowsPrefix.Length..])
                       : Windows(value);
            return id.Name.Length > 0 ? id : null;
        }

        public override string ToString() => (this.IsNatural ? NaturalPrefix : WindowsPrefix) + this.Name;
    }
}
