using System.Text;
using System.Text.Json;

namespace CaptionTranslator.Translation
{
    /// <summary>
    /// Same scheme as Hugging Face MarianTokenizer: split with the source SentencePiece model,
    /// then map pieces to ids with the shared vocab.json, and append the end-of-sentence id.
    /// </summary>
    public sealed class MarianTokenizer
    {
        private readonly SentencePieceModel sourceModel;
        private readonly Dictionary<string, long> vocabulary;
        private readonly Dictionary<long, string> reverseVocabulary;

        public MarianTokenizer(SentencePieceModel sourceModel, Dictionary<string, long> vocabulary)
        {
            this.sourceModel = sourceModel;
            this.vocabulary = vocabulary;
            this.reverseVocabulary = vocabulary.ToDictionary(pair => pair.Value, pair => pair.Key);
            this.EndOfSentenceId = vocabulary["</s>"];
            this.UnknownId = vocabulary["<unk>"];
            this.PadId = vocabulary["<pad>"];
        }

        public long EndOfSentenceId { get; }

        public long UnknownId { get; }

        public long PadId { get; }

        public static MarianTokenizer Load(string sourceSpmPath, string vocabJsonPath)
        {
            Dictionary<string, long> vocabulary = JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(vocabJsonPath))
                                                  ?? throw new InvalidDataException("vocab.json is empty.");
            return new MarianTokenizer(SentencePieceModel.Load(sourceSpmPath), vocabulary);
        }

        public long[] Encode(string text)
        {
            List<long> ids = this.sourceModel.Encode(text)
                                             .Select(piece => this.vocabulary.TryGetValue(piece, out long id) ? id : this.UnknownId)
                                             .ToList();
            ids.Add(this.EndOfSentenceId);
            return ids.ToArray();
        }

        public string Decode(IEnumerable<long> ids)
        {
            StringBuilder builder = new StringBuilder();
            foreach (long id in ids)
            {
                if (id == this.EndOfSentenceId || id == this.PadId || id == this.UnknownId)
                    continue;

                if (this.reverseVocabulary.TryGetValue(id, out string? piece))
                    builder.Append(piece);
            }

            return builder.ToString().Replace(SentencePieceModel.WordMarker, ' ').Trim();
        }
    }
}
