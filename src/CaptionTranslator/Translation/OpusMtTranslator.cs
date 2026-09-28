using CaptionTranslator.Captions;
using Microsoft.ML.OnnxRuntime;

namespace CaptionTranslator.Translation
{
    /// <summary>
    /// German→English translation with Helsinki-NLP opus-mt-de-en (Marian) exported to ONNX.
    /// Runs fully offline on the CPU. Greedy decoding with the key/value cache of the merged decoder.
    /// </summary>
    public sealed class OpusMtTranslator : ITranslator
    {
        private const int maxInputTokens = 512;

        // Measured on a 12-thread laptop CPU: 4 threads 327 ms, 6 threads 361 ms, 8 threads 490 ms, 12 threads 755 ms per sentence.
        // More threads spill onto hyperthreads / efficiency cores and get slower.
        private const int defaultThreads = 4;
        private const long decoderStartTokenId = 58100;
        private const int attentionHeads = 8;
        private const int headSize = 64;
        private const string pastPrefix = "past_key_values.";
        private const string presentPrefix = "present.";

        private readonly MarianTokenizer tokenizer;
        private readonly InferenceSession encoder;
        private readonly InferenceSession decoder;
        private readonly RunOptions runOptions = new RunOptions();
        private readonly object runLock = new object();
        private readonly IReadOnlyList<string> pastInputNames;
        private readonly IReadOnlyList<string> decoderOutputNames;
        private readonly Dictionary<string, int> decoderOutputIndex;

        public OpusMtTranslator(string modelDirectory)
            : this(modelDirectory, ModelFiles.EncoderFile, ModelFiles.DecoderFile, Math.Min(defaultThreads, Environment.ProcessorCount))
        {
        }

        /// <param name="encoderFile">File name inside the "onnx" folder, e.g. "encoder_model.onnx" or the int8 variant.</param>
        /// <param name="decoderFile">File name inside the "onnx" folder of the merged (key/value cache) decoder.</param>
        /// <param name="threads">CPU threads per inference.</param>
        public OpusMtTranslator(string modelDirectory, string encoderFile, string decoderFile, int threads)
        {
            this.tokenizer = MarianTokenizer.Load(Path.Combine(modelDirectory, "source.spm"), Path.Combine(modelDirectory, "vocab.json"));

            SessionOptions options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Max(1, threads),
            };

            this.encoder = new InferenceSession(Path.Combine(modelDirectory, "onnx", encoderFile), options);
            this.decoder = new InferenceSession(Path.Combine(modelDirectory, "onnx", decoderFile), options);
            this.pastInputNames = this.decoder.InputNames.Where(name => name.StartsWith(pastPrefix, StringComparison.Ordinal)).ToList();
            this.decoderOutputNames = this.decoder.OutputNames.ToList();
            this.decoderOutputIndex = this.decoderOutputNames.Select((name, index) => (name, index)).ToDictionary(pair => pair.name, pair => pair.index);
        }

        public string Translate(string germanText)
        {
            IEnumerable<string> english = SentenceSplitter.Split(germanText).Select(TranslateSentence).Where(sentence => sentence.Length > 0);
            return string.Join(" ", english);
        }

        public void Dispose()
        {
            this.encoder.Dispose();
            this.decoder.Dispose();
            this.runOptions.Dispose();
        }

        private string TranslateSentence(string sentence)
        {
            long[] inputIds = this.tokenizer.Encode(sentence);
            if (inputIds.Length <= 1)
                return string.Empty;

            if (inputIds.Length > maxInputTokens)
                inputIds = inputIds.Take(maxInputTokens - 1).Append(this.tokenizer.EndOfSentenceId).ToArray();

            int inputLength = inputIds.Length;
            long[] shape = { 1, inputLength };

            lock (this.runLock)
            {
                using OrtValue inputValue = OrtValue.CreateTensorValueFromMemory(inputIds, shape);
                using OrtValue maskValue = OrtValue.CreateTensorValueFromMemory(Enumerable.Repeat(1L, inputLength).ToArray(), shape);
                using IDisposableReadOnlyCollection<OrtValue> encoderResults = this.encoder.Run(
                    this.runOptions, new[] { "input_ids", "attention_mask" }, new[] { inputValue, maskValue }, new[] { "last_hidden_state" });

                return this.tokenizer.Decode(Decode(encoderResults[0], maskValue, inputLength));
            }
        }

        private List<long> Decode(OrtValue hiddenStates, OrtValue maskValue, int inputLength)
        {
            List<long> outputIds = new List<long>();
            int maxSteps = Math.Min(maxInputTokens, (inputLength * 2) + 10);
            long lastId = decoderStartTokenId;

            // Step 0 computes the encoder key/values once and they stay valid; later steps only return new decoder key/values.
            IDisposableReadOnlyCollection<OrtValue>? firstResults = null;
            IDisposableReadOnlyCollection<OrtValue>? latestResults = null;

            try
            {
                for (int step = 0; step < maxSteps; step++)
                {
                    IDisposableReadOnlyCollection<OrtValue> results = RunDecoderStep(lastId, hiddenStates, maskValue, firstResults, latestResults);

                    if (firstResults == null)
                        firstResults = results;
                    else if (latestResults != null && !ReferenceEquals(latestResults, firstResults))
                        latestResults.Dispose();

                    latestResults = results;

                    lastId = ArgMax(results[this.decoderOutputIndex["logits"]]);
                    if (lastId == this.tokenizer.EndOfSentenceId)
                        break;

                    outputIds.Add(lastId);
                }
            }
            finally
            {
                if (latestResults != null && !ReferenceEquals(latestResults, firstResults))
                    latestResults.Dispose();
                firstResults?.Dispose();
            }

            return outputIds;
        }

        private IDisposableReadOnlyCollection<OrtValue> RunDecoderStep(long tokenId, OrtValue hiddenStates, OrtValue maskValue,
                                                                      IDisposableReadOnlyCollection<OrtValue>? firstResults,
                                                                      IDisposableReadOnlyCollection<OrtValue>? latestResults)
        {
            bool useCache = firstResults != null;
            List<OrtValue> ownedValues = new List<OrtValue>();
            List<string> names = new List<string> { "input_ids", "encoder_hidden_states", "encoder_attention_mask", "use_cache_branch" };
            List<OrtValue> values = new List<OrtValue>();

            try
            {
                OrtValue tokenValue = OrtValue.CreateTensorValueFromMemory(new[] { tokenId }, new long[] { 1, 1 });
                OrtValue useCacheValue = OrtValue.CreateTensorValueFromMemory(new[] { useCache }, new long[] { 1 });
                ownedValues.Add(tokenValue);
                ownedValues.Add(useCacheValue);
                values.Add(tokenValue);
                values.Add(hiddenStates);
                values.Add(maskValue);
                values.Add(useCacheValue);

                foreach (string pastName in this.pastInputNames)
                {
                    names.Add(pastName);
                    if (!useCache)
                    {
                        OrtValue empty = OrtValue.CreateTensorValueFromMemory(Array.Empty<float>(), new long[] { 1, attentionHeads, 0, headSize });
                        ownedValues.Add(empty);
                        values.Add(empty);
                        continue;
                    }

                    string presentName = presentPrefix + pastName.Substring(pastPrefix.Length);
                    IDisposableReadOnlyCollection<OrtValue> source = presentName.Contains(".encoder.", StringComparison.Ordinal) ? firstResults! : latestResults!;
                    values.Add(source[this.decoderOutputIndex[presentName]]);
                }

                return this.decoder.Run(this.runOptions, names, values, this.decoderOutputNames);
            }
            finally
            {
                foreach (OrtValue value in ownedValues)
                    value.Dispose();
            }
        }

        private long ArgMax(OrtValue logits)
        {
            long[] shape = logits.GetTensorTypeAndShape().Shape;
            int vocabularySize = (int)shape[2];
            int offset = (int)(shape[1] - 1) * vocabularySize;
            ReadOnlySpan<float> lastRow = logits.GetTensorDataAsSpan<float>().Slice(offset, vocabularySize);

            long bestId = this.tokenizer.EndOfSentenceId;
            float bestScore = float.NegativeInfinity;
            for (int id = 0; id < vocabularySize; id++)
            {
                if (id == this.tokenizer.PadId)
                    continue;

                if (lastRow[id] > bestScore)
                {
                    bestScore = lastRow[id];
                    bestId = id;
                }
            }

            return bestId;
        }
    }
}
