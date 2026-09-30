# Natural Voices Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add offline Piper neural voices (downloaded on selection) next to the Windows voices for read aloud, played clause by clause.

**Architecture:** `SpeechReader` talks to an `IVoice` (`WindowsVoice` = today's SAPI code, `PiperVoice` = Piper ONNX model on the existing ONNX Runtime + espeak-ng phonemes via P/Invoke). `IVoice` yields one WAV per clause; `SpeechReader` plays part *n* while part *n+1* is synthesized. Voices are listed from a fixed catalog and downloaded by `VoiceFiles`.

**Tech Stack:** .NET 8 WPF, Microsoft.ML.OnnxRuntime 1.30, System.Speech, NAudio, espeak-ng 1.52.0 (native x64 DLL), MSTest 4.

**Spec:** `docs/superpowers/specs/2026-09-30-natural-voices-design.md`

**Conventions (match existing code):** explicit types (no `var`), `this.` for fields, private fields camelCase, one top-level type per file, file header none (repo has none), `.ConfigureAwait(false)` in non-UI code, 4-space indent, CRLF.

**Commands** (run from repo root `C:\Users\PANDIYANS\source\repos\CaptionTranslator`):
- Unit tests: `dotnet test tests/CaptionTranslator.Tests --filter "TestCategory!=Live"`
- One class: `dotnet test tests/CaptionTranslator.Tests --filter "FullyQualifiedName~ClauseSplitterTest"`
- Before building/publishing the app: make sure `CaptionTranslator.exe` from `src\...\bin` is not running (`tasklist | grep -i CaptionTranslator`), otherwise the build fails with a locked file.

---

## File structure

| File | Responsibility |
|---|---|
| `third_party/espeak-ng/libespeak-ng.dll`, `espeak-ng-data/` (English only), `README.md` | Bundled espeak-ng 1.52.0 |
| `src/CaptionTranslator/Speech/IVoice.cs` | Voice contract: text → WAV parts |
| `src/CaptionTranslator/Speech/WindowsVoice.cs` | SAPI voice (moved from `SpeechReader`) |
| `src/CaptionTranslator/Speech/WavWriter.cs` | float samples → 16-bit mono WAV |
| `src/CaptionTranslator/Speech/VoiceId.cs` | `piper:`/`windows:` setting value |
| `src/CaptionTranslator/Speech/VoiceOption.cs` | One entry of the voice list |
| `src/CaptionTranslator/Speech/VoiceOptions.cs` | Build list + resolve saved setting |
| `src/CaptionTranslator/Speech/Piper/Clause.cs` | Clause text + trailing punctuation |
| `src/CaptionTranslator/Speech/Piper/ClauseSplitter.cs` | Split text into clauses |
| `src/CaptionTranslator/Speech/Piper/PiperPhonemeEncoder.cs` | IPA → Piper ids |
| `src/CaptionTranslator/Speech/Piper/PiperVoiceConfig.cs` | Parse `.onnx.json` |
| `src/CaptionTranslator/Speech/Piper/PiperVoiceInfo.cs` | Catalog entry |
| `src/CaptionTranslator/Speech/Piper/PiperVoiceCatalog.cs` | The 10 licensed voices |
| `src/CaptionTranslator/Speech/Piper/VoiceFiles.cs` | Download/locate voice files |
| `src/CaptionTranslator/Speech/Piper/EspeakNativeMethods.cs` | P/Invoke declarations |
| `src/CaptionTranslator/Speech/Piper/EspeakPhonemizer.cs` | Shared espeak-ng instance, text → IPA |
| `src/CaptionTranslator/Speech/Piper/PiperVoice.cs` | Piper `IVoice` |
| `src/CaptionTranslator/Speech/SpeechReader.cs` | Modify: holds `IVoice`, pipelined playback |
| `src/CaptionTranslator/MainWindow.xaml(.cs)` | Modify: voice list, download status |
| `LICENSE`, `THIRD-PARTY-NOTICES.txt`, `README.md` | Licensing |

---

### Task 1: Bundle espeak-ng (English data only)

**Files:**
- Create: `third_party/espeak-ng/libespeak-ng.dll`, `third_party/espeak-ng/espeak-ng-data/**`, `third_party/espeak-ng/README.md`
- Modify: `src/CaptionTranslator/CaptionTranslator.csproj`

Source files were extracted (no install) from the official 1.52.0 MSI to `%TEMP%\espeak-extract\eSpeak NG\` during step 0.

- [ ] **Step 1: Copy DLL and English-only data**

```bash
SRC="$TEMP/espeak-extract/eSpeak NG"
DST="third_party/espeak-ng"
mkdir -p "$DST/espeak-ng-data/lang/gmw"
cp "$SRC/libespeak-ng.dll" "$DST/"
for f in phontab phonindex phondata phondata-manifest intonations en_dict; do cp "$SRC/espeak-ng-data/$f" "$DST/espeak-ng-data/"; done
cp "$SRC"/espeak-ng-data/lang/gmw/en* "$DST/espeak-ng-data/lang/gmw/"
cp -r "$SRC/espeak-ng-data/voices" "$DST/espeak-ng-data/"
du -sh "$DST"
```
Expected: about 1.5 MB.

- [ ] **Step 2: Verify the trimmed data still phonemizes** — run the step-0 spike against the trimmed folder:

```bash
cp -r third_party/espeak-ng "$TEMP/espeak-trim-test"
sed -i 's|Path.Combine(Path.GetTempPath(), "espeak-extract", "eSpeak NG")|Path.Combine(Path.GetTempPath(), "espeak-trim-test")|' "$TEMP/piper-spike/Program.cs"
cd "$TEMP/piper-spike" && dotnet run -c Release 2>&1 | grep -E "espeak init|run 0"
```
Expected: `espeak init 22050, setvoice(en)=0` and a `run 0` line with audio > 4 s. If `setvoice` is not 0, copy the missing file named in espeak's error from `$SRC/espeak-ng-data` and repeat.

- [ ] **Step 3: Write `third_party/espeak-ng/README.md`**

```markdown
# espeak-ng 1.52.0 (bundled)

- Source: https://github.com/espeak-ng/espeak-ng/releases/tag/1.52.0 (`espeak-ng.msi`, x64, extracted with `msiexec /a`, not installed)
- MSI SHA-256: 7f673c709ea5dd579d3b5ebb98688cc575328a6ab7438d2bc405b88cedaeafb9 (the MSI is not Authenticode-signed)
- License: GPL-3.0-or-later — see ../../LICENSE. Source code: https://github.com/espeak-ng/espeak-ng
- Only English data is kept: phontab, phonindex, phondata, phondata-manifest, intonations, en_dict, lang/gmw/en*, voices/.
- Used by Caption Translator to turn English text into IPA phonemes for Piper voices.
```

- [ ] **Step 4: Copy to output and publish** — add to `CaptionTranslator.csproj` inside a new `ItemGroup`:

```xml
  <ItemGroup>
    <None Include="..\..\third_party\espeak-ng\libespeak-ng.dll" Link="libespeak-ng.dll" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
    <None Include="..\..\third_party\espeak-ng\espeak-ng-data\**\*" LinkBase="espeak-ng-data" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 5: Build and check the output**

```bash
dotnet build src/CaptionTranslator -c Debug -v q && ls src/CaptionTranslator/bin/Debug/net8.0-windows/libespeak-ng.dll src/CaptionTranslator/bin/Debug/net8.0-windows/espeak-ng-data/en_dict src/CaptionTranslator/bin/Debug/net8.0-windows/espeak-ng-data/lang/gmw/en
```
Expected: all three paths listed. Then check the test output got them too: `dotnet build tests/CaptionTranslator.Tests -v q && ls tests/CaptionTranslator.Tests/bin/Debug/net8.0-windows/libespeak-ng.dll tests/CaptionTranslator.Tests/bin/Debug/net8.0-windows/espeak-ng-data/en_dict`. If the test output lacks them, add the same two `None` items (paths `..\..	hird_party\...`) to `tests/CaptionTranslator.Tests/CaptionTranslator.Tests.csproj`.

- [ ] **Step 6: Commit**

```bash
git add third_party src/CaptionTranslator/CaptionTranslator.csproj
git commit -m "Bundle espeak-ng 1.52.0 (English data) for natural voices"
```

---

### Task 2: WavWriter

**Files:**
- Create: `src/CaptionTranslator/Speech/WavWriter.cs`
- Test: `tests/CaptionTranslator.Tests/WavWriterTest.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using CaptionTranslator.Speech;
using NAudio.Wave;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class WavWriterTest
    {
        [TestMethod]
        public void FromNormalizedFloats_Samples_WritesMono16BitWaveAtSampleRate()
        {
            byte[] wav = WavWriter.FromNormalizedFloats(new[] { 0f, 0.5f, -0.25f }, 22050);

            using WaveFileReader reader = new WaveFileReader(new MemoryStream(wav));
            Assert.AreEqual(22050, reader.WaveFormat.SampleRate);
            Assert.AreEqual(1, reader.WaveFormat.Channels);
            Assert.AreEqual(16, reader.WaveFormat.BitsPerSample);
            Assert.AreEqual(3, reader.SampleCount);
        }

        [TestMethod]
        public void FromNormalizedFloats_LoudestSample_IsScaledToFullRange()
        {
            short[] samples = ReadSamples(WavWriter.FromNormalizedFloats(new[] { 0f, 0.5f, -0.25f }, 22050));

            CollectionAssert.AreEqual(new short[] { 0, 32767, -16383 }, samples);
        }

        [TestMethod]
        public void FromNormalizedFloats_VeryQuietSignal_IsNotBoostedAboveMinimumPeak()
        {
            short[] samples = ReadSamples(WavWriter.FromNormalizedFloats(new[] { 0.001f }, 22050));

            Assert.AreEqual((short)3276, samples[0]);
        }

        [TestMethod]
        public void FromNormalizedFloats_Silence_StaysSilent()
        {
            short[] samples = ReadSamples(WavWriter.FromNormalizedFloats(new float[4], 16000));

            CollectionAssert.AreEqual(new short[4], samples);
        }

        private static short[] ReadSamples(byte[] wav)
        {
            using WaveFileReader reader = new WaveFileReader(new MemoryStream(wav));
            byte[] data = new byte[reader.Length];
            int read = reader.Read(data, 0, data.Length);
            short[] samples = new short[read / 2];
            Buffer.BlockCopy(data, 0, samples, 0, read);
            return samples;
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/CaptionTranslator.Tests --filter "FullyQualifiedName~WavWriterTest"` → compile error "WavWriter does not exist".

- [ ] **Step 3: Implement**

```csharp
namespace CaptionTranslator.Speech
{
    /// <summary>Builds 16-bit mono PCM WAV data from float samples.</summary>
    public static class WavWriter
    {
        /// <summary>Loudness below this peak is treated as this peak, so near-silence is not boosted to full volume (as Piper does).</summary>
        private const float minimumPeak = 0.01f;

        /// <summary>Scales the samples so the loudest one uses the full 16-bit range, then writes a WAV file in memory.</summary>
        public static byte[] FromNormalizedFloats(float[] samples, int sampleRate)
        {
            float peak = minimumPeak;
            foreach (float sample in samples)
                peak = Math.Max(peak, Math.Abs(sample));
            float scale = short.MaxValue / peak;

            int dataLength = samples.Length * 2;
            using MemoryStream stream = new MemoryStream(44 + dataLength);
            using (BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                writer.Write("RIFF"u8);
                writer.Write(36 + dataLength);
                writer.Write("WAVE"u8);
                writer.Write("fmt "u8);
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write("data"u8);
                writer.Write(dataLength);
                foreach (float sample in samples)
                    writer.Write((short)Math.Clamp(sample * scale, short.MinValue, short.MaxValue));
            }

            return stream.ToArray();
        }
    }
}
```
Note: `(short)(-0.25f * 65534)` = `(short)-16383.5f` truncates toward zero → `-16383` (matches the test).

- [ ] **Step 4: Run the test** — same command → 4 passed.

- [ ] **Step 5: Commit** — `git add src/CaptionTranslator/Speech/WavWriter.cs tests/CaptionTranslator.Tests/WavWriterTest.cs && git commit -m "Add WavWriter for synthesized float audio"`

---

### Task 3: Clause and ClauseSplitter

**Files:**
- Create: `src/CaptionTranslator/Speech/Piper/Clause.cs`, `src/CaptionTranslator/Speech/Piper/ClauseSplitter.cs`
- Test: `tests/CaptionTranslator.Tests/ClauseSplitterTest.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class ClauseSplitterTest
    {
        [TestMethod]
        public void Split_CommaAndFullStop_ReturnsClausesWithPunctuation()
        {
            IReadOnlyList<Clause> clauses = ClauseSplitter.Split("Good morning everyone, let's start.");

            CollectionAssert.AreEqual(new[] { new Clause("Good morning everyone", ','), new Clause("let's start", '.') }, clauses.ToArray());
        }

        [TestMethod]
        public void Split_DecimalNumber_DoesNotSplitInsideTheNumber()
        {
            IReadOnlyList<Clause> clauses = ClauseSplitter.Split("It costs 3.5 million euros.");

            CollectionAssert.AreEqual(new[] { new Clause("It costs 3.5 million euros", '.') }, clauses.ToArray());
        }

        [TestMethod]
        public void Split_SeveralMarks_KeepsTheLastMark()
        {
            IReadOnlyList<Clause> clauses = ClauseSplitter.Split("Really?! Yes");

            CollectionAssert.AreEqual(new[] { new Clause("Really", '!'), new Clause("Yes", null) }, clauses.ToArray());
        }

        [TestMethod]
        public void Split_TextWithoutWords_ReturnsNothing()
        {
            Assert.AreEqual(0, ClauseSplitter.Split("").Count);
            Assert.AreEqual(0, ClauseSplitter.Split(" ... ").Count);
        }

        [TestMethod]
        public void Split_SurroundingSpaces_AreTrimmed()
        {
            IReadOnlyList<Clause> clauses = ClauseSplitter.Split("  Hello  ");

            CollectionAssert.AreEqual(new[] { new Clause("Hello", null) }, clauses.ToArray());
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~ClauseSplitterTest"` → compile error.

- [ ] **Step 3: Implement** `Clause.cs`:

```csharp
namespace CaptionTranslator.Speech.Piper
{
    /// <summary>Part of a sentence that is spoken in one go, and the punctuation mark that ended it (null if none).</summary>
    public sealed record Clause(string Text, char? Punctuation);
}
```

`ClauseSplitter.cs`:

```csharp
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
```

- [ ] **Step 4: Run the test** → 5 passed.

- [ ] **Step 5: Commit** — `git add src/CaptionTranslator/Speech/Piper tests/CaptionTranslator.Tests/ClauseSplitterTest.cs && git commit -m "Add ClauseSplitter for clause-by-clause speech"`

---

### Task 4: PiperPhonemeEncoder

**Files:**
- Create: `src/CaptionTranslator/Speech/Piper/PiperPhonemeEncoder.cs`
- Test: `tests/CaptionTranslator.Tests/PiperPhonemeEncoderTest.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class PiperPhonemeEncoderTest
    {
        private static readonly IReadOnlyDictionary<string, long[]> map = new Dictionary<string, long[]>
        {
            ["_"] = new long[] { 0 },
            ["^"] = new long[] { 1 },
            ["$"] = new long[] { 2 },
            [" "] = new long[] { 3 },
            ["a"] = new long[] { 14 },
            ["b"] = new long[] { 15 },
            ["ˈ"] = new long[] { 120 },
            ["ə"] = new long[] { 59, 60 },
        };

        [TestMethod]
        public void Encode_Phonemes_AddsStartPadAfterEachAndEnd()
        {
            long[] ids = PiperPhonemeEncoder.Encode("ˈa b", map);

            CollectionAssert.AreEqual(new long[] { 1, 0, 120, 0, 14, 0, 3, 0, 15, 0, 2 }, ids);
        }

        [TestMethod]
        public void Encode_PhonemeWithSeveralIds_AddsAllIds()
        {
            long[] ids = PiperPhonemeEncoder.Encode("ə", map);

            CollectionAssert.AreEqual(new long[] { 1, 0, 59, 60, 0, 2 }, ids);
        }

        [TestMethod]
        public void Encode_UnknownPhoneme_IsSkippedAndReported()
        {
            List<string> missing = new List<string>();

            long[] ids = PiperPhonemeEncoder.Encode("axb", map, missing);

            CollectionAssert.AreEqual(new long[] { 1, 0, 14, 0, 15, 0, 2 }, ids);
            CollectionAssert.AreEqual(new[] { "x" }, missing);
        }

        [TestMethod]
        public void Encode_Empty_ReturnsOnlyMarkers()
        {
            CollectionAssert.AreEqual(new long[] { 1, 0, 2 }, PiperPhonemeEncoder.Encode("", map));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~PiperPhonemeEncoderTest"` → compile error.

- [ ] **Step 3: Implement**

```csharp
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
```

- [ ] **Step 4: Run the test** → 4 passed.

- [ ] **Step 5: Commit** — `git add src/CaptionTranslator/Speech/Piper/PiperPhonemeEncoder.cs tests/CaptionTranslator.Tests/PiperPhonemeEncoderTest.cs && git commit -m "Add Piper phoneme id encoder"`

---

### Task 5: PiperVoiceConfig

**Files:**
- Create: `src/CaptionTranslator/Speech/Piper/PiperVoiceConfig.cs`
- Test: `tests/CaptionTranslator.Tests/PiperVoiceConfigTest.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class PiperVoiceConfigTest
    {
        private const string json = """
            {
              "audio": { "sample_rate": 22050, "quality": "medium" },
              "espeak": { "voice": "en-us" },
              "inference": { "noise_scale": 0.667, "length_scale": 1.1, "noise_w": 0.8 },
              "phoneme_type": "espeak",
              "num_speakers": 1,
              "phoneme_id_map": { "_": [0], "^": [1], "$": [2], "a": [14] }
            }
            """;

        [TestMethod]
        public void Parse_PiperConfig_ReadsAllValues()
        {
            PiperVoiceConfig config = PiperVoiceConfig.Parse(json);

            Assert.AreEqual(22050, config.SampleRate);
            Assert.AreEqual("en-us", config.EspeakVoice);
            Assert.AreEqual(0.667f, config.NoiseScale, 0.0001f);
            Assert.AreEqual(1.1f, config.LengthScale, 0.0001f);
            Assert.AreEqual(0.8f, config.NoiseW, 0.0001f);
            Assert.AreEqual(1, config.NumSpeakers);
            CollectionAssert.AreEqual(new long[] { 14 }, config.PhonemeIdMap["a"]);
            Assert.AreEqual(4, config.PhonemeIdMap.Count);
        }

        [TestMethod]
        public void Parse_TextPhonemeType_ThrowsNotSupported()
        {
            string textVoice = json.Replace("\"phoneme_type\": \"espeak\"", "\"phoneme_type\": \"text\"");

            Assert.ThrowsExactly<NotSupportedException>(() => PiperVoiceConfig.Parse(textVoice));
        }

        [TestMethod]
        public void Parse_MissingPhonemeMap_ThrowsKeyNotFound()
        {
            string broken = """{ "audio": { "sample_rate": 22050 }, "espeak": { "voice": "en" }, "inference": { "noise_scale": 0.6, "length_scale": 1, "noise_w": 0.8 } }""";

            Assert.ThrowsExactly<KeyNotFoundException>(() => PiperVoiceConfig.Parse(broken));
        }

        [TestMethod]
        public void Parse_InvalidJson_ThrowsJsonException()
        {
            Assert.Throws<JsonException>(() => PiperVoiceConfig.Parse("{ not json"));
        }
    }
}
```
Note: `System.Text.Json` throws a subclass of `JsonException`, hence `Assert.Throws` (allows derived types) instead of `ThrowsExactly` in the last test. (MSTest 4 removed the old `Assert.ThrowsException`.)

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~PiperVoiceConfigTest"` → compile error.

- [ ] **Step 3: Implement**

```csharp
using System.Text.Json;

namespace CaptionTranslator.Speech.Piper
{
    /// <summary>Settings of a Piper voice, read from its "&lt;voice&gt;.onnx.json" file.</summary>
    public sealed class PiperVoiceConfig
    {
        public int SampleRate { get; init; }

        /// <summary>espeak-ng voice used when the model was trained, e.g. "en-us"; phonemes must be made with the same one.</summary>
        public string EspeakVoice { get; init; } = "en-us";

        public float NoiseScale { get; init; }

        public float LengthScale { get; init; }

        public float NoiseW { get; init; }

        public int NumSpeakers { get; init; } = 1;

        public IReadOnlyDictionary<string, long[]> PhonemeIdMap { get; init; } = new Dictionary<string, long[]>();

        /// <exception cref="JsonException">The text is not JSON.</exception>
        /// <exception cref="KeyNotFoundException">A required value is missing.</exception>
        /// <exception cref="NotSupportedException">The voice does not use espeak phonemes.</exception>
        public static PiperVoiceConfig Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string phonemeType = root.TryGetProperty("phoneme_type", out JsonElement type) ? type.GetString() ?? "espeak" : "espeak";
            if (phonemeType != "espeak")
                throw new NotSupportedException($"Piper phoneme type '{phonemeType}' is not supported.");

            JsonElement inference = root.GetProperty("inference");
            return new PiperVoiceConfig
            {
                SampleRate = root.GetProperty("audio").GetProperty("sample_rate").GetInt32(),
                EspeakVoice = root.GetProperty("espeak").GetProperty("voice").GetString() ?? "en-us",
                NoiseScale = inference.GetProperty("noise_scale").GetSingle(),
                LengthScale = inference.GetProperty("length_scale").GetSingle(),
                NoiseW = inference.GetProperty("noise_w").GetSingle(),
                NumSpeakers = root.TryGetProperty("num_speakers", out JsonElement speakers) ? speakers.GetInt32() : 1,
                PhonemeIdMap = root.GetProperty("phoneme_id_map")
                                   .EnumerateObject()
                                   .ToDictionary(entry => entry.Name, entry => entry.Value.EnumerateArray().Select(id => id.GetInt64()).ToArray()),
            };
        }
    }
}
```

- [ ] **Step 4: Run the test** → 4 passed.

- [ ] **Step 5: Commit** — `git add src/CaptionTranslator/Speech/Piper/PiperVoiceConfig.cs tests/CaptionTranslator.Tests/PiperVoiceConfigTest.cs && git commit -m "Add Piper voice config parser"`

---

### Task 6: Voice catalog (PiperVoiceInfo, PiperVoiceCatalog)

**Files:**
- Create: `src/CaptionTranslator/Speech/Piper/PiperVoiceInfo.cs`, `src/CaptionTranslator/Speech/Piper/PiperVoiceCatalog.cs`
- Test: `tests/CaptionTranslator.Tests/PiperVoiceCatalogTest.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class PiperVoiceCatalogTest
    {
        [TestMethod]
        public void All_Entries_AreCompleteAndConsistent()
        {
            Assert.AreEqual(10, PiperVoiceCatalog.All.Count);
            foreach (PiperVoiceInfo voice in PiperVoiceCatalog.All)
            {
                string[] parts = voice.Id.Split('-');   // en_US-kristin-medium
                Assert.AreEqual($"en/{parts[0]}/{parts[1]}/{parts[2]}", voice.Folder, voice.Id);
                Assert.IsTrue(voice.SizeMegabytes > 0, voice.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(voice.DisplayName), voice.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(voice.License), voice.Id);
            }
        }

        [TestMethod]
        public void All_Ids_AreUnique()
        {
            Assert.AreEqual(PiperVoiceCatalog.All.Count, PiperVoiceCatalog.All.Select(voice => voice.Id).Distinct().Count());
        }

        [TestMethod]
        public void Find_KnownAndUnknownId_ReturnsVoiceOrNull()
        {
            Assert.AreEqual("Kristin – US", PiperVoiceCatalog.Find("en_US-kristin-medium")?.DisplayName);
            Assert.IsNull(PiperVoiceCatalog.Find("en_US-ryan-medium"));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~PiperVoiceCatalogTest"` → compile error.

- [ ] **Step 3: Implement** `PiperVoiceInfo.cs`:

```csharp
namespace CaptionTranslator.Speech.Piper
{
    /// <summary>A downloadable Piper voice.</summary>
    /// <param name="Id">Piper voice name, e.g. "en_US-kristin-medium"; also the file name without ".onnx".</param>
    /// <param name="Folder">Folder in the rhasspy/piper-voices repository, e.g. "en/en_US/kristin/medium".</param>
    /// <param name="License">License of the training data, from the voice's MODEL_CARD.</param>
    public sealed record PiperVoiceInfo(string Id, string DisplayName, string Folder, int SizeMegabytes, string License);
}
```

`PiperVoiceCatalog.cs` (sizes and licenses from `voices.json` and each `MODEL_CARD`, checked 2026-09-30):

```csharp
namespace CaptionTranslator.Speech.Piper
{
    /// <summary>
    /// Piper voices offered for download. Only voices whose training data license allows free use
    /// (public domain, CC0, CC-BY, CC-BY-SA); non-commercial and unclear licenses are left out.
    /// </summary>
    public static class PiperVoiceCatalog
    {
        public static IReadOnlyList<PiperVoiceInfo> All { get; } = new[]
        {
            new PiperVoiceInfo("en_US-kristin-medium", "Kristin – US", "en/en_US/kristin/medium", 64, "Public domain (LibriVox)"),
            new PiperVoiceInfo("en_US-ljspeech-medium", "Linda – US", "en/en_US/ljspeech/medium", 64, "Public domain (LJ Speech)"),
            new PiperVoiceInfo("en_US-john-medium", "John – US", "en/en_US/john/medium", 64, "Public domain (LibriVox)"),
            new PiperVoiceInfo("en_US-norman-medium", "Norman – US", "en/en_US/norman/medium", 64, "Public domain (LibriVox)"),
            new PiperVoiceInfo("en_US-bryce-medium", "Bryce – US", "en/en_US/bryce/medium", 64, "Public domain"),
            new PiperVoiceInfo("en_US-joe-medium", "Joe – US", "en/en_US/joe/medium", 63, "CC0"),
            new PiperVoiceInfo("en_US-mike-medium", "Mike – US", "en/en_US/mike/medium", 63, "CC0"),
            new PiperVoiceInfo("en_GB-cori-medium", "Cori – UK", "en/en_GB/cori/medium", 64, "Public domain (LibriVox)"),
            new PiperVoiceInfo("en_GB-alba-medium", "Alba – UK (Scottish)", "en/en_GB/alba/medium", 63, "CC BY 4.0"),
            new PiperVoiceInfo("en_GB-northern_english_male-medium", "Northern English – UK", "en/en_GB/northern_english_male/medium", 63, "CC BY-SA 4.0"),
        };

        public static PiperVoiceInfo? Find(string id) => All.FirstOrDefault(voice => voice.Id == id);
    }
}
```

- [ ] **Step 4: Run the test** → 3 passed.

- [ ] **Step 5: Commit** — `git add src/CaptionTranslator/Speech/Piper/PiperVoiceInfo.cs src/CaptionTranslator/Speech/Piper/PiperVoiceCatalog.cs tests/CaptionTranslator.Tests/PiperVoiceCatalogTest.cs && git commit -m "Add catalog of freely licensed Piper voices"`

---

### Task 7: VoiceId, VoiceOption, VoiceOptions

**Files:**
- Create: `src/CaptionTranslator/Speech/VoiceId.cs`, `src/CaptionTranslator/Speech/VoiceOption.cs`, `src/CaptionTranslator/Speech/VoiceOptions.cs`
- Test: `tests/CaptionTranslator.Tests/VoiceOptionsTest.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using CaptionTranslator.Speech;
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class VoiceOptionsTest
    {
        private static readonly PiperVoiceInfo kristin = new PiperVoiceInfo("en_US-kristin-medium", "Kristin – US", "en/en_US/kristin/medium", 64, "PD");

        [TestMethod]
        public void Parse_Prefixes_AndPlainOldName()
        {
            Assert.AreEqual(VoiceId.Natural("en_US-kristin-medium"), VoiceId.Parse("piper:en_US-kristin-medium"));
            Assert.AreEqual(VoiceId.Windows("Microsoft Zira Desktop"), VoiceId.Parse("windows:Microsoft Zira Desktop"));
            Assert.AreEqual(VoiceId.Windows("Microsoft Zira Desktop"), VoiceId.Parse("Microsoft Zira Desktop"));
            Assert.IsNull(VoiceId.Parse(null));
            Assert.IsNull(VoiceId.Parse("  "));
            Assert.IsNull(VoiceId.Parse("piper:"));
        }

        [TestMethod]
        public void ToString_RoundTripsThroughParse()
        {
            VoiceId id = VoiceId.Natural("en_US-kristin-medium");

            Assert.AreEqual("piper:en_US-kristin-medium", id.ToString());
            Assert.AreEqual(id, VoiceId.Parse(id.ToString()));
        }

        [TestMethod]
        public void Build_NaturalAvailable_ListsNaturalVoicesFirst()
        {
            IReadOnlyList<VoiceOption> options = VoiceOptions.Build(new[] { "Microsoft Zira Desktop" }, new[] { kristin }, naturalAvailable: true);

            CollectionAssert.AreEqual(new[] { "piper:en_US-kristin-medium", "windows:Microsoft Zira Desktop" }, options.Select(option => option.Id.ToString()).ToArray());
            Assert.AreSame(kristin, options[0].Natural);
            Assert.IsNull(options[1].Natural);
        }

        [TestMethod]
        public void Build_NaturalUnavailable_ListsOnlyWindowsVoices()
        {
            IReadOnlyList<VoiceOption> options = VoiceOptions.Build(new[] { "Microsoft Zira Desktop" }, new[] { kristin }, naturalAvailable: false);

            Assert.AreEqual(1, options.Count);
            Assert.IsFalse(options[0].Id.IsNatural);
        }

        [TestMethod]
        public void Resolve_SavedVoiceInList_ReturnsIt()
        {
            IReadOnlyList<VoiceOption> options = VoiceOptions.Build(new[] { "Microsoft David Desktop", "Microsoft Zira Desktop" }, new[] { kristin }, true);

            Assert.AreEqual("piper:en_US-kristin-medium", VoiceOptions.Resolve(options, "piper:en_US-kristin-medium")?.Id.ToString());
            Assert.AreEqual("windows:Microsoft Zira Desktop", VoiceOptions.Resolve(options, "Microsoft Zira Desktop")?.Id.ToString());
        }

        [TestMethod]
        public void Resolve_UnknownOrEmpty_ReturnsFirstWindowsVoice()
        {
            IReadOnlyList<VoiceOption> options = VoiceOptions.Build(new[] { "Microsoft David Desktop" }, new[] { kristin }, true);

            Assert.AreEqual("windows:Microsoft David Desktop", VoiceOptions.Resolve(options, "piper:en_US-ryan-medium")?.Id.ToString());
            Assert.AreEqual("windows:Microsoft David Desktop", VoiceOptions.Resolve(options, null)?.Id.ToString());
        }

        [TestMethod]
        public void Resolve_NoWindowsVoices_ReturnsFirstOptionOrNull()
        {
            Assert.AreEqual("piper:en_US-kristin-medium", VoiceOptions.Resolve(VoiceOptions.Build(Array.Empty<string>(), new[] { kristin }, true), null)?.Id.ToString());
            Assert.IsNull(VoiceOptions.Resolve(Array.Empty<VoiceOption>(), null));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~VoiceOptionsTest"` → compile error.

- [ ] **Step 3: Implement** `VoiceId.cs`:

```csharp
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
```

`VoiceOption.cs`:

```csharp
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Speech
{
    /// <summary>One entry of the voice list. <see cref="Natural"/> is set for Piper voices, null for Windows voices.</summary>
    public sealed record VoiceOption(VoiceId Id, string DisplayName, PiperVoiceInfo? Natural);
}
```

`VoiceOptions.cs`:

```csharp
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Speech
{
    public static class VoiceOptions
    {
        /// <summary>Natural voices first (only if espeak-ng is available), then the installed Windows voices.</summary>
        public static IReadOnlyList<VoiceOption> Build(IReadOnlyList<string> windowsVoices, IReadOnlyList<PiperVoiceInfo> naturalVoices, bool naturalAvailable)
        {
            List<VoiceOption> options = new List<VoiceOption>();
            if (naturalAvailable)
                options.AddRange(naturalVoices.Select(voice => new VoiceOption(VoiceId.Natural(voice.Id), voice.DisplayName, voice)));
            options.AddRange(windowsVoices.Select(name => new VoiceOption(VoiceId.Windows(name), name, null)));
            return options;
        }

        /// <summary>The saved voice if it is in the list, otherwise the first Windows voice, otherwise the first voice; null if the list is empty.</summary>
        public static VoiceOption? Resolve(IReadOnlyList<VoiceOption> options, string? savedVoice)
        {
            VoiceId? saved = VoiceId.Parse(savedVoice);
            return options.FirstOrDefault(option => option.Id == saved)
                ?? options.FirstOrDefault(option => !option.Id.IsNatural)
                ?? options.FirstOrDefault();
        }
    }
}
```

- [ ] **Step 4: Run the test** → 7 passed.

- [ ] **Step 5: Commit** — `git add src/CaptionTranslator/Speech/VoiceId.cs src/CaptionTranslator/Speech/VoiceOption.cs src/CaptionTranslator/Speech/VoiceOptions.cs tests/CaptionTranslator.Tests/VoiceOptionsTest.cs && git commit -m "Add voice ids and voice list resolution"`

---

### Task 8: VoiceFiles (download)

**Files:**
- Create: `src/CaptionTranslator/Speech/Piper/VoiceFiles.cs`
- Test: `tests/CaptionTranslator.Tests/VoiceFilesTest.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class VoiceFilesTest
    {
        private static readonly PiperVoiceInfo voice = new PiperVoiceInfo("en_US-test-medium", "Test", "en/en_US/test/medium", 1, "CC0");
        private string root = string.Empty;

        [TestInitialize]
        public void CreateRoot() => this.root = Path.Combine(Path.GetTempPath(), "ct-voices-" + Guid.NewGuid().ToString("N"));

        [TestCleanup]
        public void DeleteRoot()
        {
            if (Directory.Exists(this.root))
                Directory.Delete(this.root, true);
        }

        [TestMethod]
        public async Task DownloadAsync_Success_StoresBothFilesAndReportsProgress()
        {
            FakeHandler handler = new FakeHandler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1000]) });
            VoiceFiles files = new VoiceFiles(this.root, handler, "https://example.test/");
            List<int> reported = new List<int>();

            await files.DownloadAsync(voice, new SynchronousProgress(reported.Add), CancellationToken.None);

            Assert.IsTrue(files.IsDownloaded(voice));
            CollectionAssert.AreEqual(
                new[] { "https://example.test/en/en_US/test/medium/en_US-test-medium.onnx.json", "https://example.test/en/en_US/test/medium/en_US-test-medium.onnx" },
                handler.Requests);
            Assert.AreEqual(100, reported[^1]);
            Assert.AreEqual(0, Directory.GetFiles(this.root, "*.part", SearchOption.AllDirectories).Length);
        }

        [TestMethod]
        public async Task DownloadAsync_ModelNotFound_LeavesNoFiles()
        {
            FakeHandler handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith(".onnx")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) });
            VoiceFiles files = new VoiceFiles(this.root, handler, "https://example.test/");

            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => files.DownloadAsync(voice, new SynchronousProgress(_ => { }), CancellationToken.None));

            Assert.IsFalse(files.IsDownloaded(voice));
            Assert.AreEqual(0, Directory.GetFiles(this.root, "*", SearchOption.AllDirectories).Length);
        }

        [TestMethod]
        public async Task DownloadAsync_StreamBreaks_LeavesNoFiles()
        {
            FakeHandler handler = new FakeHandler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) });
            VoiceFiles files = new VoiceFiles(this.root, handler, "https://example.test/");

            await Assert.ThrowsExactlyAsync<IOException>(() => files.DownloadAsync(voice, new SynchronousProgress(_ => { }), CancellationToken.None));

            Assert.IsFalse(files.IsDownloaded(voice));
            Assert.AreEqual(0, Directory.GetFiles(this.root, "*", SearchOption.AllDirectories).Length);
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => this.respond = respond;

            public List<string> Requests { get; } = new List<string>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                this.Requests.Add(request.RequestUri!.ToString());
                return Task.FromResult(this.respond(request));
            }
        }

        /// <summary>Returns some bytes, then fails like a dropped connection.</summary>
        private sealed class BrokenStream : MemoryStream
        {
            private int calls;

            public BrokenStream() : base(new byte[100]) { }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                => ++this.calls > 1 ? throw new IOException("Connection reset.") : base.ReadAsync(buffer, cancellationToken);

            public override int Read(byte[] buffer, int offset, int count)
                => ++this.calls > 1 ? throw new IOException("Connection reset.") : base.Read(buffer, offset, count);
        }

        /// <summary><see cref="Progress{T}"/> posts to a synchronization context; this one reports immediately.</summary>
        private sealed class SynchronousProgress : IProgress<int>
        {
            private readonly Action<int> report;

            public SynchronousProgress(Action<int> report) => this.report = report;

            public void Report(int value) => this.report(value);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~VoiceFilesTest"` → compile error.

- [ ] **Step 3: Implement**

```csharp
using System.Net.Http;

namespace CaptionTranslator.Speech.Piper
{
    /// <summary>
    /// Location and one-time download of Piper voice files: &lt;root&gt;\&lt;id&gt;\&lt;id&gt;.onnx and .onnx.json.
    /// A voice counts as downloaded only when both files are complete (files arrive as *.part and are renamed at the end).
    /// </summary>
    public sealed class VoiceFiles
    {
        public const string DefaultBaseUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/main/";

        private readonly string rootDirectory;
        private readonly HttpMessageHandler? handler;
        private readonly string baseUrl;

        public VoiceFiles()
            : this(Path.Combine(AppPaths.DataDirectory, "voices"), null, DefaultBaseUrl)
        {
        }

        /// <param name="handler">For tests; null uses the normal network stack.</param>
        public VoiceFiles(string rootDirectory, HttpMessageHandler? handler, string baseUrl)
        {
            this.rootDirectory = rootDirectory;
            this.handler = handler;
            this.baseUrl = baseUrl;
        }

        public string ModelPath(PiperVoiceInfo voice) => Path.Combine(this.rootDirectory, voice.Id, voice.Id + ".onnx");

        public string ConfigPath(PiperVoiceInfo voice) => ModelPath(voice) + ".json";

        public bool IsDownloaded(PiperVoiceInfo voice) => File.Exists(ModelPath(voice)) && File.Exists(ConfigPath(voice));

        /// <summary>Downloads the config, then the model. On failure or cancel nothing of this voice is left behind.</summary>
        /// <param name="progress">Percent of the model file (0..100).</param>
        /// <exception cref="HttpRequestException">Server not reachable or file not found.</exception>
        /// <exception cref="IOException">Connection dropped or disk error.</exception>
        /// <exception cref="OperationCanceledException">Cancelled.</exception>
        public async Task DownloadAsync(PiperVoiceInfo voice, IProgress<int> progress, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath(voice))!);
            using HttpClient client = this.handler != null ? new HttpClient(this.handler, disposeHandler: false) : new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(30);

            try
            {
                await DownloadFileAsync(client, $"{voice.Folder}/{voice.Id}.onnx.json", ConfigPath(voice), null, cancellationToken).ConfigureAwait(false);
                await DownloadFileAsync(client, $"{voice.Folder}/{voice.Id}.onnx", ModelPath(voice), progress, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Cleanup only; the exception is passed on to the caller.
                DeleteIfExists(ConfigPath(voice));
                DeleteIfExists(ConfigPath(voice) + ".part");
                DeleteIfExists(ModelPath(voice) + ".part");
                DeleteEmptyDirectory(Path.GetDirectoryName(ModelPath(voice))!);
                throw;
            }
        }

        private async Task DownloadFileAsync(HttpClient client, string relativeUrl, string target, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            string temporary = target + ".part";
            using HttpResponseMessage response = await client.GetAsync(this.baseUrl + relativeUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;

            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream destination = File.Create(temporary))
            {
                byte[] buffer = new byte[1 << 16];
                long written = 0;
                int lastPercent = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;
                    int percent = total > 0 ? (int)(written * 100 / total.Value) : 0;
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(percent);
                    }
                }
            }

            File.Move(temporary, target, true);
            progress?.Report(100);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static void DeleteEmptyDirectory(string path)
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
    }
}
```

- [ ] **Step 4: Run the test** → 3 passed. (The "no files" asserts count files only; an empty `root` folder may remain — that is fine.)

- [ ] **Step 5: Commit** — `git add src/CaptionTranslator/Speech/Piper/VoiceFiles.cs tests/CaptionTranslator.Tests/VoiceFilesTest.cs && git commit -m "Add one-time download of Piper voice files"`

---

### Task 9: EspeakPhonemizer

**Files:**
- Create: `src/CaptionTranslator/Speech/Piper/EspeakNativeMethods.cs`, `src/CaptionTranslator/Speech/Piper/EspeakPhonemizer.cs`
- Test: `tests/CaptionTranslator.Tests/EspeakPhonemizerLiveTest.cs`

- [ ] **Step 1: Write the failing live test** (needs the bundled DLL from Task 1 in the test output; no network)

```csharp
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    [TestCategory("Live")]
    [DoNotParallelize]
    public class EspeakPhonemizerLiveTest
    {
        [TestMethod]
        public void Phonemize_EnglishWords_ReturnsIpaWithStressMarks()
        {
            EspeakPhonemizer? phonemizer = EspeakPhonemizer.Shared;
            Assert.IsNotNull(phonemizer, EspeakPhonemizer.LoadError);

            string phonemes = phonemizer.Phonemize("Good morning everyone", "en-us");

            Console.WriteLine(phonemes);
            StringAssert.Contains(phonemes, "ˈ");
            StringAssert.Contains(phonemes, "ɡ");
            Assert.AreEqual(2, phonemes.Count(character => character == ' '), "Three words expected.");
        }

        [TestMethod]
        public void Phonemize_UnknownVoice_ThrowsInvalidOperation()
        {
            EspeakPhonemizer? phonemizer = EspeakPhonemizer.Shared;
            Assert.IsNotNull(phonemizer, EspeakPhonemizer.LoadError);

            Assert.ThrowsExactly<InvalidOperationException>(() => phonemizer.Phonemize("Hello", "xx-notalanguage"));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/CaptionTranslator.Tests --filter "FullyQualifiedName~EspeakPhonemizerLiveTest"` → compile error.

- [ ] **Step 3: Implement** `EspeakNativeMethods.cs`:

```csharp
using System.Runtime.InteropServices;

namespace CaptionTranslator.Speech.Piper
{
    /// <summary>espeak-ng C API (speak_lib.h), from libespeak-ng.dll next to the exe.</summary>
    internal static class EspeakNativeMethods
    {
        public const int AudioOutputSynchronous = 2;
        public const int CharsUtf8 = 1;
        public const int PhonemesIpa = 0x02;
        public const int ResultOk = 0;

        private const string library = "libespeak-ng.dll";

        /// <returns>Sample rate, or a negative error code.</returns>
        [DllImport(library, CallingConvention = CallingConvention.Cdecl)]
        public static extern int espeak_Initialize(int output, int bufferLength, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int options);

        [DllImport(library, CallingConvention = CallingConvention.Cdecl)]
        public static extern int espeak_SetVoiceByName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        /// <summary>Phonemes of the next clause; advances <paramref name="textPointer"/> and sets it to zero at the end of the text.</summary>
        [DllImport(library, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr espeak_TextToPhonemes(ref IntPtr textPointer, int textMode, int phonemeMode);
    }
}
```

`EspeakPhonemizer.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace CaptionTranslator.Speech.Piper
{
    /// <summary>
    /// English text → IPA phonemes with espeak-ng. espeak-ng has global state, so there is one instance per process
    /// and all calls are serialized.
    /// </summary>
    public sealed class EspeakPhonemizer
    {
        private static readonly object loadLock = new object();
        private static EspeakPhonemizer? shared;
        private static string? loadError;

        private readonly object phonemizeLock = new object();
        private string? currentVoice;

        private EspeakPhonemizer()
        {
        }

        /// <summary>The process-wide instance, or null if espeak-ng could not be loaded (see <see cref="LoadError"/>).</summary>
        public static EspeakPhonemizer? Shared
        {
            get
            {
                EnsureLoaded();
                return shared;
            }
        }

        public static string? LoadError
        {
            get
            {
                EnsureLoaded();
                return loadError;
            }
        }

        /// <exception cref="InvalidOperationException">espeak-ng does not know <paramref name="voice"/>.</exception>
        public string Phonemize(string text, string voice)
        {
            lock (this.phonemizeLock)
            {
                if (voice != this.currentVoice)
                {
                    int result = EspeakNativeMethods.espeak_SetVoiceByName(voice);
                    if (result != EspeakNativeMethods.ResultOk)
                        throw new InvalidOperationException($"espeak-ng voice '{voice}' not found (code {result}).");
                    this.currentVoice = voice;
                }

                StringBuilder phonemes = new StringBuilder();
                IntPtr buffer = Marshal.StringToCoTaskMemUTF8(text);
                try
                {
                    IntPtr position = buffer;
                    while (position != IntPtr.Zero)
                    {
                        string? part = Marshal.PtrToStringUTF8(EspeakNativeMethods.espeak_TextToPhonemes(ref position, EspeakNativeMethods.CharsUtf8, EspeakNativeMethods.PhonemesIpa));
                        if (string.IsNullOrEmpty(part))
                            continue;
                        if (phonemes.Length > 0)
                            phonemes.Append(' ');
                        phonemes.Append(part);
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(buffer);
                }

                return phonemes.ToString();
            }
        }

        private static void EnsureLoaded()
        {
            lock (loadLock)
            {
                if (shared != null || loadError != null)
                    return;

                string dataParent = AppContext.BaseDirectory;
                if (!Directory.Exists(Path.Combine(dataParent, "espeak-ng-data")))
                {
                    loadError = "espeak-ng-data folder is missing next to the app.";
                }
                else
                {
                    try
                    {
                        int sampleRate = EspeakNativeMethods.espeak_Initialize(EspeakNativeMethods.AudioOutputSynchronous, 0, dataParent, 0);
                        if (sampleRate > 0)
                            shared = new EspeakPhonemizer();
                        else
                            loadError = $"espeak-ng could not start (code {sampleRate}).";
                    }
                    catch (Exception exception) when (exception is DllNotFoundException || exception is BadImageFormatException || exception is EntryPointNotFoundException)
                    {
                        loadError = "libespeak-ng.dll could not be loaded: " + exception.Message;
                    }
                }

                if (loadError != null)
                    Log.Info("Natural voices unavailable: " + loadError);
            }
        }
    }
}
```

- [ ] **Step 4: Run the test** → 2 passed; the console output shows IPA like `ɡˈʊd mˈɔːɹnɪŋ ˈɛvɹɪwˌʌn`. If `Phonemize_UnknownVoice` fails because espeak-ng accepts the name, change the name to `"zz"` and rerun; if it still passes silently, delete that test and note it in the commit message.

- [ ] **Step 5: Commit** — `git add src/CaptionTranslator/Speech/Piper/EspeakNativeMethods.cs src/CaptionTranslator/Speech/Piper/EspeakPhonemizer.cs tests/CaptionTranslator.Tests/EspeakPhonemizerLiveTest.cs && git commit -m "Add espeak-ng phonemizer for Piper voices"`

---

### Task 10: IVoice, WindowsVoice, PiperVoice

**Files:**
- Create: `src/CaptionTranslator/Speech/IVoice.cs`, `src/CaptionTranslator/Speech/WindowsVoice.cs`, `src/CaptionTranslator/Speech/Piper/PiperVoice.cs`
- Test: `tests/CaptionTranslator.Tests/PiperVoiceTest.cs`, `tests/CaptionTranslator.Tests/PiperVoiceLiveTest.cs`

- [ ] **Step 1: Write the failing tests** — `PiperVoiceTest.cs`:

```csharp
using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class PiperVoiceTest
    {
        [TestMethod]
        [DataRow(0, 1f)]
        [DataRow(10, 0.5f)]
        [DataRow(-10, 2f)]
        [DataRow(20, 0.5f)]
        [DataRow(2, 0.8706f)]
        public void RateToLengthScale_Rate_ReturnsLengthScale(int rate, float expected)
        {
            Assert.AreEqual(expected, PiperVoice.RateToLengthScale(rate), 0.001f);
        }
    }
}
```

`PiperVoiceLiveTest.cs` (uses the Kristin voice downloaded in step 0; inconclusive if missing):

```csharp
using CaptionTranslator.Speech.Piper;
using NAudio.Wave;

namespace CaptionTranslator.Tests
{
    [TestClass]
    [TestCategory("Live")]
    [DoNotParallelize]
    public class PiperVoiceLiveTest
    {
        [TestMethod]
        public void SynthesizeWavChunks_TwoClauses_ReturnsOneAudibleWavPerClause()
        {
            PiperVoiceInfo kristin = PiperVoiceCatalog.Find("en_US-kristin-medium")!;
            VoiceFiles files = new VoiceFiles();
            if (!files.IsDownloaded(kristin))
                Assert.Inconclusive("Kristin voice not downloaded.");
            EspeakPhonemizer? phonemizer = EspeakPhonemizer.Shared;
            Assert.IsNotNull(phonemizer, EspeakPhonemizer.LoadError);

            using PiperVoice voice = PiperVoice.Load(files.ModelPath(kristin), files.ConfigPath(kristin), phonemizer);
            List<byte[]> parts = voice.SynthesizeWavChunks("Good morning everyone, let's start with the status.", 0).ToList();

            Assert.AreEqual(2, parts.Count);
            foreach (byte[] part in parts)
            {
                using WaveFileReader reader = new WaveFileReader(new MemoryStream(part));
                Console.WriteLine($"{reader.TotalTime.TotalSeconds:F2} s");
                Assert.AreEqual(22050, reader.WaveFormat.SampleRate);
                Assert.IsTrue(reader.TotalTime > TimeSpan.FromSeconds(0.5));
            }
        }

        [TestMethod]
        public void SynthesizeWavChunks_AfterDispose_ThrowsObjectDisposed()
        {
            PiperVoiceInfo kristin = PiperVoiceCatalog.Find("en_US-kristin-medium")!;
            VoiceFiles files = new VoiceFiles();
            if (!files.IsDownloaded(kristin))
                Assert.Inconclusive("Kristin voice not downloaded.");

            PiperVoice voice = PiperVoice.Load(files.ModelPath(kristin), files.ConfigPath(kristin), EspeakPhonemizer.Shared!);
            voice.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(() => voice.SynthesizeWavChunks("Hello.", 0).ToList());
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~PiperVoice"` → compile error.

- [ ] **Step 3: Implement** `IVoice.cs`:

```csharp
namespace CaptionTranslator.Speech
{
    /// <summary>A voice that turns text into speech audio.</summary>
    public interface IVoice : IDisposable
    {
        /// <summary>
        /// Lazily yields the speech as WAV data, one part per clause (or one part for voices that cannot split),
        /// so playback can start before the whole text is synthesized.
        /// </summary>
        /// <param name="rate">Speed from -10 (slowest) to 10 (fastest); 0 is normal.</param>
        /// <exception cref="ObjectDisposedException">The voice was disposed (e.g. replaced by another voice).</exception>
        IEnumerable<byte[]> SynthesizeWavChunks(string text, int rate);
    }
}
```

`WindowsVoice.cs` (the SAPI code moved out of `SpeechReader`):

```csharp
using System.Speech.Synthesis;

namespace CaptionTranslator.Speech
{
    /// <summary>An offline Windows (SAPI) voice.</summary>
    public sealed class WindowsVoice : IVoice
    {
        private readonly SpeechSynthesizer synthesizer = new SpeechSynthesizer();
        private readonly object synthesizerLock = new object();
        private bool disposed;

        /// <exception cref="ArgumentException">No enabled Windows voice has this name.</exception>
        public WindowsVoice(string name)
        {
            this.synthesizer.SelectVoice(name);
        }

        /// <summary>Installed and enabled Windows voices, English first.</summary>
        public static IReadOnlyList<string> GetInstalledNames()
        {
            using SpeechSynthesizer synthesizer = new SpeechSynthesizer();
            return synthesizer.GetInstalledVoices()
                              .Where(voice => voice.Enabled)
                              .Select(voice => voice.VoiceInfo)
                              .OrderBy(voice => voice.Culture.TwoLetterISOLanguageName == "en" ? 0 : 1)
                              .ThenBy(voice => voice.Name)
                              .Select(voice => voice.Name)
                              .ToList();
        }

        public IEnumerable<byte[]> SynthesizeWavChunks(string text, int rate)
        {
            yield return Synthesize(text, rate);
        }

        public void Dispose()
        {
            lock (this.synthesizerLock)
            {
                this.disposed = true;
                this.synthesizer.Dispose();
            }
        }

        private byte[] Synthesize(string text, int rate)
        {
            using MemoryStream wav = new MemoryStream();
            lock (this.synthesizerLock)
            {
                ObjectDisposedException.ThrowIf(this.disposed, this);
                this.synthesizer.Rate = Math.Clamp(rate, -10, 10);
                this.synthesizer.SetOutputToWaveStream(wav);
                try
                {
                    this.synthesizer.Speak(text);
                }
                finally
                {
                    this.synthesizer.SetOutputToNull();
                }
            }

            return wav.ToArray();
        }
    }
}
```

`Piper/PiperVoice.cs`:

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CaptionTranslator.Speech.Piper
{
    /// <summary>An offline neural Piper voice: espeak-ng phonemes → ONNX model → audio, clause by clause.</summary>
    public sealed class PiperVoice : IVoice
    {
        private const int defaultThreads = 4;

        private readonly InferenceSession session;
        private readonly PiperVoiceConfig config;
        private readonly EspeakPhonemizer phonemizer;
        private readonly object runLock = new object();
        private readonly HashSet<string> reportedMissing = new HashSet<string>();
        private bool disposed;

        public PiperVoice(string modelPath, PiperVoiceConfig config, EspeakPhonemizer phonemizer, int threads)
        {
            this.config = config;
            this.phonemizer = phonemizer;
            SessionOptions options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Max(1, threads),
            };
            this.session = new InferenceSession(modelPath, options);
        }

        /// <summary>Loads a voice (takes a few seconds; call off the UI thread).</summary>
        /// <exception cref="OnnxRuntimeException">The model file is damaged.</exception>
        /// <exception cref="System.Text.Json.JsonException">The config file is damaged.</exception>
        public static PiperVoice Load(string modelPath, string configPath, EspeakPhonemizer phonemizer)
            => new PiperVoice(modelPath, PiperVoiceConfig.Parse(File.ReadAllText(configPath)), phonemizer, Math.Min(defaultThreads, Environment.ProcessorCount));

        /// <summary>Speed -10..10 → Piper length scale (0 → 1, +10 → 0.5 = twice as fast, -10 → 2).</summary>
        public static float RateToLengthScale(int rate) => (float)Math.Pow(2, -Math.Clamp(rate, -10, 10) / 10.0);

        public IEnumerable<byte[]> SynthesizeWavChunks(string text, int rate)
        {
            float lengthScale = this.config.LengthScale * RateToLengthScale(rate);
            foreach (Clause clause in ClauseSplitter.Split(text))
            {
                string phonemes = this.phonemizer.Phonemize(clause.Text, this.config.EspeakVoice);
                if (clause.Punctuation is char mark)
                    phonemes += mark;

                List<string> missing = new List<string>();
                long[] ids = PiperPhonemeEncoder.Encode(phonemes, this.config.PhonemeIdMap, missing);
                ReportMissing(missing);
                if (ids.Length <= 3)
                    continue;

                yield return WavWriter.FromNormalizedFloats(Run(ids, lengthScale), this.config.SampleRate);
            }
        }

        public void Dispose()
        {
            lock (this.runLock)
            {
                this.disposed = true;
                this.session.Dispose();
            }
        }

        private float[] Run(long[] ids, float lengthScale)
        {
            List<NamedOnnxValue> inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", new DenseTensor<long>(ids, new[] { 1, ids.Length })),
                NamedOnnxValue.CreateFromTensor("input_lengths", new DenseTensor<long>(new long[] { ids.Length }, new[] { 1 })),
                NamedOnnxValue.CreateFromTensor("scales", new DenseTensor<float>(new[] { this.config.NoiseScale, lengthScale, this.config.NoiseW }, new[] { 3 })),
            };

            lock (this.runLock)
            {
                ObjectDisposedException.ThrowIf(this.disposed, this);
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = this.session.Run(inputs);
                return results[0].AsEnumerable<float>().ToArray();
            }
        }

        private void ReportMissing(List<string> missing)
        {
            foreach (string phoneme in missing)
            {
                if (this.reportedMissing.Add(phoneme))
                    Log.Info($"Piper voice: phoneme U+{char.ConvertToUtf32(phoneme, 0):X4} is not in the voice and is skipped.");
            }
        }
    }
}
```
Note: `SynthesizeWavChunks` is an iterator; the dispose check runs at the first `Run` (live test calls `.ToList()`, which enumerates).

- [ ] **Step 4: Run the tests** — `--filter "FullyQualifiedName~PiperVoice"` → 5 unit + 2 live passed.

- [ ] **Step 5: Commit** — `git add src/CaptionTranslator/Speech/IVoice.cs src/CaptionTranslator/Speech/WindowsVoice.cs src/CaptionTranslator/Speech/Piper/PiperVoice.cs tests/CaptionTranslator.Tests/PiperVoiceTest.cs tests/CaptionTranslator.Tests/PiperVoiceLiveTest.cs && git commit -m "Add IVoice with Windows and Piper voices"`

---

### Task 11: SpeechReader uses IVoice and plays part n while synthesizing part n+1

**Files:**
- Modify: `src/CaptionTranslator/Speech/SpeechReader.cs` (whole file)
- Modify: `tests/CaptionTranslator.Tests/SpeechReaderLiveTest.cs` (uses of `reader.Voices` / `reader.Synthesize`)

- [ ] **Step 1: Replace `SpeechReader.cs`** with:

```csharp
namespace CaptionTranslator.Speech
{
    /// <summary>
    /// Reads translated lines aloud with the current <see cref="IVoice"/>, one line after another, on the selected
    /// output device. Says the speaker's name when the speaker changes. Part n+1 of a line is synthesized while part n plays.
    /// </summary>
    public sealed class SpeechReader : IDisposable
    {
        private const int maxPendingLines = 2;

        private readonly SpeechQueue queue = new SpeechQueue(maxPendingLines);
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly object voiceLock = new object();
        private IVoice? voice;
        private volatile int rate;
        private string lastSpeaker = string.Empty;
        private volatile string? deviceId;
        private float volume = 1f;
        private CancellationTokenSource currentLine = new CancellationTokenSource();

        public SpeechReader()
        {
            _ = Task.Run(() => SpeakLoopAsync(this.shutdown.Token));
        }

        /// <summary>
        /// Uses <paramref name="newVoice"/> from the next part on (null = silent). The reader owns the voice: the previous
        /// one is disposed in the background after its current part (never on the calling UI thread).
        /// </summary>
        public void SetVoice(IVoice? newVoice)
        {
            IVoice? old;
            lock (this.voiceLock)
            {
                old = this.voice;
                this.voice = newVoice;
            }

            if (old != null)
                _ = Task.Run(old.Dispose);
        }

        /// <summary>Speed from -10 (slowest) to 10 (fastest); 0 is normal.</summary>
        public void SetRate(int value) => this.rate = Math.Clamp(value, -10, 10);

        /// <summary>Output device id from <see cref="AudioOutputDevice"/>; null = Windows default device.</summary>
        public void SetDevice(string? outputDeviceId) => this.deviceId = outputDeviceId;

        /// <summary>Loudness of the reading voice, 0..1, relative to the device's own Windows volume (which is never changed).</summary>
        public float Volume
        {
            get => Volatile.Read(ref this.volume);
            set => Volatile.Write(ref this.volume, Math.Clamp(value, 0f, 1f));
        }

        public void Enqueue(string speaker, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            string spoken = speaker.Length > 0 && speaker != this.lastSpeaker ? $"{speaker}: {text}" : text;
            this.lastSpeaker = speaker;

            int dropped = this.queue.Enqueue(spoken);
            if (dropped > 0)
                Log.Info($"Read aloud: skipped {dropped} line(s) to stay live.");
        }

        /// <summary>Stops the current line and forgets everything waiting.</summary>
        public void StopAll()
        {
            this.queue.Clear();
            this.lastSpeaker = string.Empty;
            this.currentLine.Cancel();
        }

        /// <summary>All WAV parts of a text with the current voice (used by tests). Empty if there is no voice.</summary>
        public IReadOnlyList<byte[]> SynthesizeAll(string text)
        {
            IVoice? speaking;
            lock (this.voiceLock)
                speaking = this.voice;
            return speaking?.SynthesizeWavChunks(text, this.rate).ToList() ?? new List<byte[]>();
        }

        /// <summary>Speaks one text now on the selected device (also used by the "Test" button).</summary>
        public async Task SpeakNowAsync(string text, float volume, CancellationToken cancellationToken)
        {
            IVoice? speaking;
            lock (this.voiceLock)
                speaking = this.voice;
            if (speaking == null)
                return;

            using IEnumerator<byte[]> parts = speaking.SynthesizeWavChunks(text, this.rate).GetEnumerator();
            Task<byte[]?> next = Task.Run(() => NextPart(speaking, parts), CancellationToken.None);
            try
            {
                while (await next.ConfigureAwait(false) is byte[] part)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    next = Task.Run(() => NextPart(speaking, parts), CancellationToken.None);
                    await AudioPlayer.PlayAsync(part, this.deviceId, volume, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                // The worker may still use the enumerator; wait for it before the enumerator is disposed.
                await WaitForPendingPartAsync(next).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            this.shutdown.Cancel();
            this.currentLine.Cancel();
            IVoice? old;
            lock (this.voiceLock)
            {
                old = this.voice;
                this.voice = null;
            }

            old?.Dispose();
        }

        /// <summary>Next WAV part, or null at the end of the text or when the voice was replaced meanwhile.</summary>
        private byte[]? NextPart(IVoice speaking, IEnumerator<byte[]> parts)
        {
            lock (this.voiceLock)
            {
                if (!ReferenceEquals(speaking, this.voice))
                    return null;
            }

            try
            {
                return parts.MoveNext() ? parts.Current : null;
            }
            catch (ObjectDisposedException)
            {
                // The voice was replaced (and disposed) while this part was being made: end the line quietly.
                return null;
            }
        }

        private static async Task WaitForPendingPartAsync(Task<byte[]?> pending)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The line was stopped or has already failed; this part is not played. The reason stays in the log.
                Log.Info("Read aloud: discarded a part: " + exception.Message);
            }
        }

        private async Task SpeakLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    string? text = await this.queue.DequeueAsync(cancellationToken).ConfigureAwait(false);
                    if (text == null)
                        continue;

                    this.currentLine = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    try
                    {
                        await SpeakNowAsync(text, this.Volume, this.currentLine.Token).ConfigureAwait(false);
                    }
                    catch (IOException exception) when (IsDeviceInvalidated(exception))
                    {
                        // Bluetooth headsets briefly disappear when they switch profile (e.g. Teams starts using their microphone).
                        // Wait for the device to come back and play the line once more.
                        Log.Info("Audio device changed while reading aloud; retrying the line.");
                        await Task.Delay(700, this.currentLine.Token).ConfigureAwait(false);
                        await SpeakNowAsync(text, this.Volume, this.currentLine.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    // StopAll cancelled the current line.
                }
                catch (Exception exception)
                {
                    // Boundary of a long-running loop: a failing line (e.g. device unplugged) must not stop reading.
                    Log.Error("Read aloud failed.", exception);
                    await Task.Delay(1000, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        /// <summary>AUDCLNT_E_DEVICE_INVALIDATED: the output device was removed or reconfigured during playback.</summary>
        private static bool IsDeviceInvalidated(IOException exception)
            => exception.InnerException is System.Runtime.InteropServices.COMException com && com.HResult == unchecked((int)0x88890004);
    }
}
```

- [ ] **Step 2: Update `SpeechReaderLiveTest.cs`** — in every test replace the setup

```csharp
            using SpeechReader reader = new SpeechReader();
            if (reader.Voices.Count == 0)
                Assert.Inconclusive("No Windows voices installed.");
```
with
```csharp
            IReadOnlyList<string> voices = WindowsVoice.GetInstalledNames();
            if (voices.Count == 0)
                Assert.Inconclusive("No Windows voices installed.");
            using SpeechReader reader = new SpeechReader();
            reader.SetVoice(new WindowsVoice(voices[0]));
```
and every `reader.Synthesize(x)` with `reader.SynthesizeAll(x)[0]`. Then check nothing else uses the removed members:

```bash
grep -rn "\.Voices\b\|\.Synthesize(" src tests --include=*.cs | grep -v "SynthesizeAll\|SynthesizeWavChunks"
```
Expected: only lines in `MainWindow.xaml.cs` (fixed in Task 12).

- [ ] **Step 3: Build the test project alone is not possible yet** (MainWindow still uses the old API) — continue directly with Task 12, then run tests. Commit after Task 12.

---

### Task 12: Settings drawer — voice list, download status, fallback

**Files:**
- Modify: `src/CaptionTranslator/MainWindow.xaml` (below `VoiceBox`, line ~306)
- Modify: `src/CaptionTranslator/MainWindow.xaml.cs` (fields near line 31; `InitializeSpeech` ~432–472; `UpdateReadAloud` ~481–497; `OnVoiceChanged` ~553–560)

- [ ] **Step 1: XAML** — replace

```xml
                                <ComboBox x:Name="VoiceBox" SelectionChanged="OnVoiceChanged" />
```
with
```xml
                                <ComboBox x:Name="VoiceBox" SelectionChanged="OnVoiceChanged" />
                                <Grid x:Name="VoiceStatusPanel" Margin="0,4,0,0" Visibility="Collapsed">
                                    <TextBlock x:Name="VoiceStatusText" FontSize="11.5" Foreground="{StaticResource FaintBrush}" TextWrapping="Wrap" Margin="0,0,56,0" />
                                    <TextBlock x:Name="VoiceCancelLink" FontSize="11.5" HorizontalAlignment="Right" VerticalAlignment="Top">
                                        <Hyperlink Click="OnCancelVoiceDownloadClick" Foreground="{StaticResource AccentBrush}">Cancel</Hyperlink>
                                    </TextBlock>
                                </Grid>
```

- [ ] **Step 2: Usings and fields** — add `using CaptionTranslator.Speech.Piper;` and `using System.Net.Http;` and `using Microsoft.ML.OnnxRuntime;` to the usings of `MainWindow.xaml.cs`; next to `private SpeechReader? speechReader;` add:

```csharp
        private readonly VoiceFiles voiceFiles = new VoiceFiles();
        private IReadOnlyList<VoiceOption> voiceOptions = Array.Empty<VoiceOption>();
        private VoiceOption? activeVoice;
        private CancellationTokenSource? voiceSelection;
        private bool updatingVoiceBox;
```

- [ ] **Step 3: Replace the start of `InitializeSpeech`** — from `try { this.speechReader = new SpeechReader(); }` down to and including the line `: this.speechReader.Voices[0];` with:

```csharp
            IReadOnlyList<string> windowsVoices = Array.Empty<string>();
            try
            {
                this.speechReader = new SpeechReader();
                windowsVoices = WindowsVoice.GetInstalledNames();
            }
            catch (Exception exception)
            {
                // No speech engine on this PC: the app works without reading aloud (or with natural voices only).
                Log.Error("Windows voices not available.", exception);
            }

            bool naturalAvailable = EspeakPhonemizer.Shared != null;
            this.voiceOptions = VoiceOptions.Build(windowsVoices, PiperVoiceCatalog.All, naturalAvailable);

            if (this.speechReader == null || this.voiceOptions.Count == 0)
            {
                this.ReadAloudToggle.IsEnabled = false;
                this.VoiceBox.IsEnabled = false;
                this.SpeedBox.IsEnabled = false;
                this.DeviceBox.IsEnabled = false;
                this.TestVoiceButton.IsEnabled = false;
                this.VolumeSlider.IsEnabled = false;
                this.SkipMyLinesSwitch.IsEnabled = false;
                this.MyNameBox.IsEnabled = false;
                this.ReadAloudToggle.ToolTip = "No voices available on this PC.";
                return;
            }

            FillVoiceBox();
            if (!naturalAvailable)
                SetVoiceStatus("Natural voices unavailable – see log", false);

            // Start with a Windows voice so reading works at once; a saved natural voice replaces it when loaded (a few seconds).
            VoiceOption? saved = VoiceOptions.Resolve(this.voiceOptions, this.settings.Voice);
            VoiceOption? start = saved != null && !saved.Id.IsNatural ? saved : this.voiceOptions.FirstOrDefault(option => !option.Id.IsNatural);
            if (start != null)
                ActivateWindowsVoice(start, save: false);

            if (saved?.Natural != null)
            {
                if (this.voiceFiles.IsDownloaded(saved.Natural))
                {
                    SelectVoiceBoxItem(saved);
                    _ = UseNaturalVoiceAsync(saved);
                }
                else
                {
                    Log.Info($"Saved natural voice {saved.Id} is not downloaded; using {start?.Id}.");
                    SetVoiceStatus($"{saved.DisplayName} is not downloaded – select it to download", false);
                }
            }
```
Keep the rest of `InitializeSpeech` (speed box, toggle, volume, name, devices) unchanged.

- [ ] **Step 4: `UpdateReadAloud`** — replace `if (this.speechReader != null && this.speechReader.Voices.Count > 0)` with `if (this.speechReader != null && this.voiceOptions.Count > 0)`.

- [ ] **Step 5: Replace `OnVoiceChanged`** with these members:

```csharp
        private async void OnVoiceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.updatingVoiceBox || (this.VoiceBox.SelectedItem as ComboBoxItem)?.Tag is not VoiceOption option || option == this.activeVoice)
                return;

            this.voiceSelection?.Cancel();
            if (option.Natural == null)
            {
                SetVoiceStatus(null, false);
                ActivateWindowsVoice(option, save: true);
                return;
            }

            await UseNaturalVoiceAsync(option);
        }

        private void OnCancelVoiceDownloadClick(object sender, RoutedEventArgs e) => this.voiceSelection?.Cancel();

        private void ActivateWindowsVoice(VoiceOption option, bool save)
        {
            try
            {
                this.speechReader?.SetVoice(new WindowsVoice(option.Id.Name));
                this.activeVoice = option;
                if (save)
                    this.settings.Voice = option.Id.ToString();
                SelectVoiceBoxItem(option);
            }
            catch (ArgumentException exception)
            {
                Log.Error($"Windows voice '{option.Id.Name}' could not be selected.", exception);
                SetVoiceStatus("This Windows voice is not available", false);
            }
        }

        /// <summary>Downloads the voice if needed, loads it and switches to it. The previous voice keeps reading meanwhile.</summary>
        private async Task UseNaturalVoiceAsync(VoiceOption option)
        {
            PiperVoiceInfo info = option.Natural!;
            EspeakPhonemizer? phonemizer = EspeakPhonemizer.Shared;
            if (phonemizer == null || this.speechReader == null)
                return;

            CancellationTokenSource selection = new CancellationTokenSource();
            this.voiceSelection = selection;
            try
            {
                if (!this.voiceFiles.IsDownloaded(info))
                {
                    SetVoiceStatus($"Downloading {option.DisplayName}… 0 %", true);
                    Progress<int> progress = new Progress<int>(percent =>
                    {
                        if (!selection.IsCancellationRequested)
                            SetVoiceStatus($"Downloading {option.DisplayName}… {percent} %", true);
                    });
                    await Task.Run(() => this.voiceFiles.DownloadAsync(info, progress, selection.Token));
                    FillVoiceBox();
                    SelectVoiceBoxItem(option);
                }

                SetVoiceStatus($"Loading {option.DisplayName}…", false);
                PiperVoice voice = await Task.Run(() => PiperVoice.Load(this.voiceFiles.ModelPath(info), this.voiceFiles.ConfigPath(info), phonemizer));
                if (selection.IsCancellationRequested)
                {
                    // Another voice was chosen while this one was loading.
                    voice.Dispose();
                    return;
                }

                this.speechReader.SetVoice(voice);
                this.activeVoice = option;
                this.settings.Voice = option.Id.ToString();
                SetVoiceStatus(null, false);
                Log.Info($"Natural voice {info.Id} in use.");
            }
            catch (OperationCanceledException) when (selection.IsCancellationRequested)
            {
                if (this.voiceSelection == selection)
                {
                    SetVoiceStatus(null, false);
                    SelectVoiceBoxItem(this.activeVoice);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is IOException
                                              || exception is OnnxRuntimeException || exception is System.Text.Json.JsonException
                                              || exception is KeyNotFoundException || exception is NotSupportedException || exception is InvalidOperationException)
            {
                Log.Error($"Natural voice {info.Id} could not be used.", exception);
                bool network = exception is HttpRequestException || exception is TaskCanceledException;
                SetVoiceStatus(network ? "Download failed – check internet connection" : "Voice could not be loaded – see log", false);
                SelectVoiceBoxItem(this.activeVoice);
            }
        }

        private void FillVoiceBox()
        {
            this.updatingVoiceBox = true;
            this.VoiceBox.Items.Clear();
            AddVoiceGroup("NATURAL VOICES", this.voiceOptions.Where(option => option.Natural != null));
            AddVoiceGroup("WINDOWS VOICES", this.voiceOptions.Where(option => option.Natural == null));
            this.updatingVoiceBox = false;
        }

        private void AddVoiceGroup(string header, IEnumerable<VoiceOption> options)
        {
            List<VoiceOption> group = options.ToList();
            if (group.Count == 0)
                return;

            this.VoiceBox.Items.Add(new ComboBoxItem
            {
                Content = header,
                IsEnabled = false,
                FontSize = 11,
                Foreground = (Brush)FindResource("FaintBrush"),
            });
            foreach (VoiceOption option in group)
                this.VoiceBox.Items.Add(new ComboBoxItem { Content = VoiceLabel(option), Tag = option });
        }

        private string VoiceLabel(VoiceOption option)
        {
            if (option.Natural == null)
                return option.DisplayName;

            return this.voiceFiles.IsDownloaded(option.Natural)
                ? $"{option.DisplayName}  ✓"
                : $"{option.DisplayName} · {option.Natural.SizeMegabytes} MB";
        }

        private void SelectVoiceBoxItem(VoiceOption? option)
        {
            this.updatingVoiceBox = true;
            this.VoiceBox.SelectedItem = this.VoiceBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, option));
            this.updatingVoiceBox = false;
        }

        /// <param name="message">Null hides the status line.</param>
        private void SetVoiceStatus(string? message, bool canCancel)
        {
            this.VoiceStatusPanel.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
            this.VoiceStatusText.Text = message ?? string.Empty;
            this.VoiceCancelLink.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        }
```

- [ ] **Step 6: Build and run all unit tests**

```bash
tasklist | grep -i CaptionTranslator   # must be empty for the bin\Debug exe
dotnet build src/CaptionTranslator -v q 2>&1 | grep -E "error|warn|Build succeeded"
dotnet test tests/CaptionTranslator.Tests --filter "TestCategory!=Live"
```
Expected: build succeeded, 0 warnings from the new files; unit tests = 53 old + 4 + 5 + 4 + 4 + 3 + 7 + 3 + 5 = **88 passed**. Compare the total with this number (not just "passed").

- [ ] **Step 7: Run the speech live tests** (Windows voice path must still work; plays at volume 0)

```bash
dotnet test tests/CaptionTranslator.Tests --filter "FullyQualifiedName~SpeechReaderLiveTest|FullyQualifiedName~PiperVoiceLiveTest|FullyQualifiedName~EspeakPhonemizerLiveTest"
```
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/CaptionTranslator/Speech/SpeechReader.cs src/CaptionTranslator/MainWindow.xaml src/CaptionTranslator/MainWindow.xaml.cs tests/CaptionTranslator.Tests/SpeechReaderLiveTest.cs
git commit -m "Offer natural Piper voices in read aloud, downloaded on selection"
```

---

### Task 13: Manual check in the running app

- [ ] **Step 1:** Stop the running app, start `src/CaptionTranslator/bin/Debug/net8.0-windows/CaptionTranslator.exe`, open settings.
- [ ] **Step 2:** Voice list shows "NATURAL VOICES" with Kristin marked ✓ (downloaded in step 0), others with "· 63 MB"/"· 64 MB", then "WINDOWS VOICES".
- [ ] **Step 3:** Select Kristin → "Loading Kristin – US…" → status disappears; press "Test voice" (user listens). Check the log: `Natural voice en_US-kristin-medium in use.`
- [ ] **Step 4:** Select John → download status with percent; press Cancel → status disappears, selection returns to Kristin, no `voices\en_US-john-medium\*.part` left.
- [ ] **Step 5:** Restart the app → Kristin is selected again and loads (settings value `piper:en_US-kristin-medium`).
- [ ] **Step 6:** Select a Windows voice → works immediately, as before.

Record results (and anything the user heard) in the spec's "Changes during implementation" section.

---

### Task 14: Licensing, notices, docs, release zip

**Files:**
- Create: `LICENSE`, `THIRD-PARTY-NOTICES.txt`
- Modify: `README.md`, `src/CaptionTranslator/CaptionTranslator.csproj`, `release\CaptionTranslator\How to start.txt` (not in git)

- [ ] **Step 1: `LICENSE`** — full GPL-3.0 text from `https://www.gnu.org/licenses/gpl-3.0.txt` (**ask the user before downloading**), saved verbatim.

- [ ] **Step 2: `THIRD-PARTY-NOTICES.txt`**

```text
Caption Translator is licensed under the GNU General Public License v3.0 (see LICENSE),
because it includes espeak-ng. Source code: https://github.com/suganyaghub/My-Sandbox

Included in the download
- espeak-ng 1.52.0 — GPL-3.0-or-later — https://github.com/espeak-ng/espeak-ng
- ONNX Runtime — MIT — https://github.com/microsoft/onnxruntime
- NAudio — MIT — https://github.com/naudio/NAudio

Downloaded by the app on first use
- OPUS-MT German→English model (Helsinki-NLP, ONNX export by Xenova) — CC-BY 4.0
  https://huggingface.co/Xenova/opus-mt-de-en
- Piper voices (rhasspy/piper-voices, MIT) — https://huggingface.co/rhasspy/piper-voices
  Training data licenses:
  - Kristin, John, Norman, Cori (LibriVox recordings) — public domain
  - Linda (LJ Speech, https://keithito.com/LJ-Speech-Dataset/) — public domain
  - Bryce — public domain
  - Joe, Mike (https://github.com/OHF-Voice/voice-datasets) — CC0
  - Alba (https://datashare.ed.ac.uk/handle/10283/3270) — CC BY 4.0
  - Northern English (OpenSLR 83, http://www.openslr.org/83/) — CC BY-SA 4.0
```

- [ ] **Step 3: Ship both files** — add to the `ItemGroup` from Task 1:

```xml
    <None Include="..\..\LICENSE" Link="LICENSE.txt" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
    <None Include="..\..\THIRD-PARTY-NOTICES.txt" Link="THIRD-PARTY-NOTICES.txt" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
```

- [ ] **Step 4: README** — in the feature list add: "Read aloud with Windows voices or natural offline Piper voices (downloaded once when selected, ~63 MB each)." and a "License" section: "GPL-3.0 (includes espeak-ng). See THIRD-PARTY-NOTICES.txt."

- [ ] **Step 5: Commit** — `git add LICENSE THIRD-PARTY-NOTICES.txt README.md src/CaptionTranslator/CaptionTranslator.csproj && git commit -m "License under GPL-3.0 and list third-party notices"`

- [ ] **Step 6: Rebuild the release zip** (app must not be running from `release\`):

```bash
rm -rf release/CaptionTranslator release/CaptionTranslator-win-x64.zip
dotnet publish src/CaptionTranslator -c Release -r win-x64 --self-contained true -o release/CaptionTranslator
ls release/CaptionTranslator/libespeak-ng.dll release/CaptionTranslator/espeak-ng-data/en_dict release/CaptionTranslator/LICENSE.txt release/CaptionTranslator/THIRD-PARTY-NOTICES.txt
```
Re-create `release/CaptionTranslator/How to start.txt` (previous content + this line after step 4):
`   Natural-sounding voices: Settings -> Voice -> "Natural voices". Each is downloaded once (~63 MB) when you select it.`
Then zip:
```bash
powershell -NoProfile -Command "Compress-Archive -Path 'release\CaptionTranslator' -DestinationPath 'release\CaptionTranslator-win-x64.zip' -Force"
```
Smoke test: start `release\CaptionTranslator\CaptionTranslator.exe`, check the log for "Translation model loaded." and no "Natural voices unavailable", then stop that instance only.

- [ ] **Step 7: Push?** — do not push; ask the user.
