# Natural Voices for Read Aloud — Design

Date: 2026-09-30

## Goal

Offer more, and more natural-sounding, English voices for read aloud. Today only the Windows SAPI voices
(David, Zira, Hedda) are available and they sound robotic.

## Decisions (from brainstorming)

| Question | Decision |
|---|---|
| Where does speech synthesis run? | Offline only. Translated meeting text never leaves the PC. |
| Quality vs. speed | Fast and light: Piper (VITS) voices, medium quality, ~63 MB each. |
| Pronunciation component | espeak-ng (GPL-3). The app becomes GPL-3; `LICENSE` + notices added. |
| How voices get onto the PC | Downloaded once from Hugging Face when the user selects them. |
| Windows voices | Stay in the list (no download, fallback). Default stays a Windows voice. |
| Integration | In-process: Piper model on the existing ONNX Runtime, espeak-ng via P/Invoke. |

## Step 0 — Speed spike (before production code)

A test speaks one ~15-word sentence with `en_US-kristin-medium` while the translation model is loaded.
Measure model load time and time-to-audio per sentence.

- **Pass:** time-to-audio < 500 ms per sentence on the user's laptop. Otherwise stop and discuss.
- Also confirms: espeak-ng 1.52.0 `libespeak-ng.dll` (x64, from the official `espeak-ng.msi`, extracted with
  `msiexec /a` — no system install) loads, and the voice's ONNX inputs are as expected
  (`input` int64 [1,N], `input_lengths` int64 [1], `scales` float [3], no `sid` for single-speaker voices).
- Record measured sizes of `libespeak-ng.dll` + `espeak-ng-data`.

### Step 0 results (2026-09-30, en_US-kristin-medium, 19-word sentence, 5.4 s audio)

| Mode | Idle | Translator busy nonstop |
|---|---|---|
| Whole sentence at once | 410–750 ms | 670–1200 ms (criterion missed) |
| Clause by clause: first audio | 105–141 ms | 189–279 ms |
| Clause by clause: all clauses | 440–520 ms | 735–1241 ms |
| Windows SAPI (reference) | ~25 ms | – |

Decision: **synthesize and play clause by clause** (user choice). The next clause is synthesized while the current one
plays; in the test it was always ready before the first clause finished (no gap). Voice load ≈ 6 s (once).
espeak-ng: `libespeak-ng.dll` 468 KB, x64; full `espeak-ng-data` 24 MB (trim to English). The 1.52.0 MSI is
**not Authenticode-signed** (SHA-256 `7f673c70…eafb9`). Input/output names confirmed: `input`, `input_lengths`,
`scales` → `output` [1,1,1,T] float at 22050 Hz. All phonemes of the test sentence were in `phoneme_id_map`.
The voice's `espeak.voice` value (`en` for Kristin) is used as-is.

## Voice catalog

Only voices whose dataset license allows free use (checked on each `MODEL_CARD`, 2026-09-30):

| Id | Display name | License |
|---|---|---|
| en_US-kristin-medium | Kristin – US | public domain (LibriVox) |
| en_US-ljspeech-medium | Linda – US | public domain (LJ Speech) |
| en_US-john-medium | John – US | public domain (LibriVox) |
| en_US-norman-medium | Norman – US | public domain (LibriVox) |
| en_US-bryce-medium | Bryce – US | public domain |
| en_US-joe-medium | Joe – US | CC0 |
| en_US-mike-medium | Mike – US | CC0 |
| en_GB-cori-medium | Cori – UK | public domain (LibriVox) |
| en_GB-alba-medium | Alba – UK (Scottish) | CC-BY 4.0 |
| en_GB-northern_english_male-medium | Northern English – UK | CC-BY-SA 4.0 |

Excluded: ryan, hfc_male, hfc_female (non-commercial), lessac (research license), amy, alan, jenny_dioco,
kusal (license "see URL", unclear). Multi-speaker voices (vctk, libritts_r, arctic, …) are out of scope.
Gender is not in the voice index; it is added to the display name only after listening (user confirms).

Files per voice: `<id>.onnx` + `<id>.onnx.json` from
`https://huggingface.co/rhasspy/piper-voices/resolve/main/en/<locale>/<name>/medium/`.

## Architecture

```
SpeechReader (queue, device, volume, Bluetooth retry — unchanged)
   └── IVoice  ── WindowsVoice   (today's SAPI code, moved)
               └─ PiperVoice     (ONNX session + PiperVoiceConfig)
                      └── EspeakPhonemizer (libespeak-ng.dll, shared, locked)
PiperVoiceCatalog (fixed list above)     VoiceFiles (download to %LOCALAPPDATA%\CaptionTranslator\voices\<id>\)
```

### Components

- **`IVoice`** — `IEnumerable<byte[]> SynthesizeWavChunks(string text, int rate)`: lazily yields one WAV per
  clause (Piper) or one WAV for the whole text (Windows). `IDisposable`. `rate` is −10…10 (as today).
- **`SpeechReader`** plays chunk *n* while chunk *n+1* is being synthesized on a worker task. Cancellation
  (Stop / new line dropped) stops both.
- **`WindowsVoice`** — wraps `SpeechSynthesizer` for one SAPI voice. Behaviour identical to today.
- **`PiperVoiceConfig`** — parsed `.onnx.json`: `audio.sample_rate`, `espeak.voice`, `inference`
  (`noise_scale`, `length_scale`, `noise_w`), `phoneme_id_map` (IPA string → ids).
- **`PiperPhonemeEncoder`** (pure, unit-tested) — phoneme text → ids exactly like Piper: `^`, pad, then each
  phoneme followed by pad `_`, then `$`. Phonemes missing from the map are skipped (logged once per phoneme).
- **`EspeakPhonemizer`** — P/Invoke to `espeak_Initialize` (data path = app folder `espeak-ng-data`),
  `espeak_SetVoiceByName`, `espeak_TextToPhonemes` (IPA mode). Text is first split into clauses at
  `. , ? ! ; :` by **`ClauseSplitter`** (pure, unit-tested); each clause is phonemized and its punctuation
  character is appended so Piper produces natural pauses. One instance, all calls under a lock.
- **`PiperVoice`** — splits text with `ClauseSplitter`, synthesizes each clause on demand; owns one `InferenceSession` (4 intra-op threads, like the translator). Runs the model,
  converts float samples to 16-bit PCM WAV at the voice's sample rate. `length_scale` =
  config value × `RateToLengthScale(rate)` (pure: 0 → 1.0, +10 → 0.5, −10 → 2.0, geometric).
- **`PiperVoiceCatalog`** — the table above as code (id, display name, relative HF folder, size, license).
- **`VoiceFiles`** — `IsDownloaded(id)`, `DownloadAsync(id, IProgress<int>, CancellationToken)`. Same pattern
  as `ModelFiles`: write `*.part`, rename only after the complete file arrived. Cancel/failure deletes `*.part`.
- **`VoiceId`** (pure) — parses the saved setting: `piper:<id>`, `windows:<name>`; a plain name (old settings)
  = Windows voice; unknown/empty → first Windows voice.

### Settings drawer

- `VoiceBox` shows two groups: **Natural voices** ("Kristin – US · 64 MB" or "Kristin – US ✓") and
  **Windows voices**.
- Selecting a voice that is not downloaded starts the download. A status line under the box shows
  "Downloading Kristin… 42 %" with a Cancel link. The previous voice keeps reading meanwhile. On success the
  app switches to the new voice and saves the setting.
- "Test voice" uses the current voice as today.

## Error handling

| Situation | Behaviour |
|---|---|
| Download fails / offline / HF blocked | Status "Download failed – check internet connection"; previous voice stays; logged. |
| Voice files missing or broken at start | Fall back to the first Windows voice; short status note; logged. |
| `libespeak-ng.dll` or `espeak-ng-data` missing | Natural voices shown as unavailable ("see log"); Windows voices work. |
| One sentence fails in Piper | Logged and skipped; the read-aloud loop continues (existing behaviour). |

## Testing

Unit tests (no audio, no downloads): `PiperPhonemeEncoder`, `ClauseSplitter`, `RateToLengthScale`, `VoiceId`,
`PiperVoiceCatalog` (every entry has size, path, license), `VoiceFiles` (`.part` only renamed when complete;
failure leaves no voice; download via an injectable `HttpMessageHandler`), `PiperVoiceConfig` parsing.

Live tests (`TestCategory=Live`): real voice + espeak-ng → WAV is non-empty and has a plausible duration.

## Packaging

- `libespeak-ng.dll` + `espeak-ng-data\` copied to the output/publish folder (stored under `third_party\espeak-ng\`
  in the repo, with its `COPYING`).
- `LICENSE` (GPL-3.0) in repo root and zip; `THIRD-PARTY-NOTICES.txt` lists espeak-ng (GPL-3), Piper voices with
  their dataset licenses/attribution, OPUS-MT (CC-BY 4.0), ONNX Runtime (MIT), NAudio (MIT).
- `How to start.txt`: natural voices are downloaded when selected (~63 MB each).

## Out of scope (YAGNI)

Deleting voices in the app, preview before download, non-English voices, multi-speaker voices, cloud voices.

## Open risks

- A long sentence without any punctuation is one clause and still takes the whole-sentence time.
- espeak-ng without Piper's patched "terminator" API: clause splitting is our own and may differ slightly from
  Piper's pauses.
- Short texts ("Yes.") and names: pronunciation quality depends on espeak-ng.
- CPU load while translation runs at the same time — measured in step 0.
