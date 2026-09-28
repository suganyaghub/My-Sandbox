using System.Text;

namespace CaptionTranslator.Translation
{
    /// <summary>
    /// Minimal SentencePiece unigram segmenter: reads the pieces and scores from a .spm model file
    /// (protobuf ModelProto) and splits text with the Viterbi algorithm. Normalization approximates
    /// "nmt_nfkc": NFKC, collapsed whitespace, "▁" as word marker with a leading dummy prefix.
    /// </summary>
    public sealed class SentencePieceModel
    {
        public const char WordMarker = '▁';

        private const int pieceTypeNormal = 1;
        private const int pieceTypeUserDefined = 4;

        private readonly Dictionary<string, float> scores;
        private readonly int maxPieceLength;
        private readonly float unknownScore;

        private SentencePieceModel(Dictionary<string, float> scores)
        {
            this.scores = scores;
            this.maxPieceLength = scores.Count == 0 ? 1 : scores.Keys.Max(piece => piece.Length);
            this.unknownScore = (scores.Count == 0 ? 0f : scores.Values.Min()) - 10f;
        }

        public int PieceCount => this.scores.Count;

        public static SentencePieceModel Load(string path) => Parse(File.ReadAllBytes(path));

        public static SentencePieceModel Parse(byte[] modelProto)
        {
            Dictionary<string, float> scores = new Dictionary<string, float>(StringComparer.Ordinal);
            ProtoReader reader = new ProtoReader(modelProto, 0, modelProto.Length);

            while (reader.HasMore)
            {
                (int field, int wireType) = reader.ReadTag();
                if (field == 1 && wireType == 2)
                {
                    (int start, int length) = reader.ReadLengthDelimited();
                    ReadPiece(modelProto, start, length, scores);
                }
                else
                {
                    reader.Skip(wireType);
                }
            }

            return new SentencePieceModel(scores);
        }

        public static string Normalize(string text)
        {
            string normalized = text.Normalize(NormalizationForm.FormKC);
            StringBuilder builder = new StringBuilder(normalized.Length + 1);
            builder.Append(WordMarker);
            bool lastWasSpace = true;

            foreach (char character in normalized)
            {
                if (char.IsWhiteSpace(character))
                {
                    if (!lastWasSpace)
                        builder.Append(WordMarker);
                    lastWasSpace = true;
                }
                else if (!char.IsControl(character))
                {
                    builder.Append(character);
                    lastWasSpace = false;
                }
            }

            if (builder.Length > 1 && builder[^1] == WordMarker)
                builder.Length--;

            return builder.ToString();
        }

        public IReadOnlyList<string> Encode(string text)
        {
            string normalized = Normalize(text);
            if (normalized.Length <= 1)
                return Array.Empty<string>();

            int length = normalized.Length;
            double[] best = new double[length + 1];
            int[] previousPosition = new int[length + 1];
            Array.Fill(best, double.NegativeInfinity);
            best[0] = 0;

            for (int start = 0; start < length; start++)
            {
                if (double.IsNegativeInfinity(best[start]))
                    continue;

                int maxLength = Math.Min(this.maxPieceLength, length - start);
                bool singleCharacterFound = false;
                for (int pieceLength = 1; pieceLength <= maxLength; pieceLength++)
                {
                    if (this.scores.TryGetValue(normalized.Substring(start, pieceLength), out float score))
                    {
                        singleCharacterFound |= pieceLength == 1;
                        Relax(best, previousPosition, start, start + pieceLength, best[start] + score);
                    }
                }

                if (!singleCharacterFound)
                    Relax(best, previousPosition, start, start + 1, best[start] + this.unknownScore);
            }

            List<string> pieces = new List<string>();
            for (int end = length; end > 0; end = previousPosition[end])
                pieces.Add(normalized.Substring(previousPosition[end], end - previousPosition[end]));

            pieces.Reverse();
            return pieces;
        }

        private static void Relax(double[] best, int[] previousPosition, int start, int end, double score)
        {
            if (score > best[end])
            {
                best[end] = score;
                previousPosition[end] = start;
            }
        }

        private static void ReadPiece(byte[] buffer, int start, int length, Dictionary<string, float> scores)
        {
            ProtoReader reader = new ProtoReader(buffer, start, start + length);
            string? piece = null;
            float score = 0f;
            int type = pieceTypeNormal;

            while (reader.HasMore)
            {
                (int field, int wireType) = reader.ReadTag();
                if (field == 1 && wireType == 2)
                {
                    (int textStart, int textLength) = reader.ReadLengthDelimited();
                    piece = Encoding.UTF8.GetString(buffer, textStart, textLength);
                }
                else if (field == 2 && wireType == 5)
                {
                    score = reader.ReadFloat();
                }
                else if (field == 3 && wireType == 0)
                {
                    type = (int)reader.ReadVarint();
                }
                else
                {
                    reader.Skip(wireType);
                }
            }

            if (piece != null && (type == pieceTypeNormal || type == pieceTypeUserDefined))
                scores[piece] = score;
        }

        private struct ProtoReader
        {
            private readonly byte[] buffer;
            private readonly int end;
            private int position;

            public ProtoReader(byte[] buffer, int start, int end)
            {
                this.buffer = buffer;
                this.position = start;
                this.end = end;
            }

            public bool HasMore => this.position < this.end;

            public (int Field, int WireType) ReadTag()
            {
                ulong tag = ReadVarint();
                return ((int)(tag >> 3), (int)(tag & 7));
            }

            public ulong ReadVarint()
            {
                ulong result = 0;
                int shift = 0;
                while (true)
                {
                    byte current = this.buffer[this.position++];
                    result |= (ulong)(current & 0x7F) << shift;
                    if ((current & 0x80) == 0)
                        return result;
                    shift += 7;
                }
            }

            public (int Start, int Length) ReadLengthDelimited()
            {
                int length = (int)ReadVarint();
                int start = this.position;
                this.position += length;
                return (start, length);
            }

            public float ReadFloat()
            {
                float value = BitConverter.ToSingle(this.buffer, this.position);
                this.position += 4;
                return value;
            }

            public void Skip(int wireType)
            {
                switch (wireType)
                {
                    case 0:
                        ReadVarint();
                        break;
                    case 1:
                        this.position += 8;
                        break;
                    case 2:
                        ReadLengthDelimited();
                        break;
                    case 5:
                        this.position += 4;
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported protobuf wire type {wireType} in SentencePiece model.");
                }
            }
        }
    }
}
