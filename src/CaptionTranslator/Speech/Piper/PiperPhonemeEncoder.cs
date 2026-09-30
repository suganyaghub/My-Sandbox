namespace CaptionTranslator.Speech.Piper
{
    /// <summary>Turns IPA phonemes into the id sequence Piper models expect: ^ _ (phoneme _)* $.</summary>
    public static class PiperPhonemeEncoder
    {
        public const string Pad = "_";
        public const string Start = "^";
        public const string End = "$";

        /// <param name="missing">Receives each phoneme (one Unicode code point) that is not in the map; those are skipped.</param>
        public static long[] Encode(string phonemes, IReadOnlyDictionary<string, long[]> idMap, ICollection<string>? missing = null)
        {
            long[] pad = idMap[Pad];
            List<long> ids = new List<long>(phonemes.Length * 2 + 3);
            ids.AddRange(idMap[Start]);
            ids.AddRange(pad);

            for (int index = 0; index < phonemes.Length; index += char.IsSurrogatePair(phonemes, index) ? 2 : 1)
            {
                string phoneme = char.ConvertFromUtf32(char.ConvertToUtf32(phonemes, index));
                if (idMap.TryGetValue(phoneme, out long[]? phonemeIds))
                {
                    ids.AddRange(phonemeIds);
                    ids.AddRange(pad);
                }
                else
                {
                    missing?.Add(phoneme);
                }
            }

            ids.AddRange(idMap[End]);
            return ids.ToArray();
        }
    }
}
