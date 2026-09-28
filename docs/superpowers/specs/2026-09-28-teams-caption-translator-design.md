# Teams Caption Translator — Design

*Date: 2026-09-28*

## Goal

A small Windows desktop app that shows live English translations of German Teams meeting
captions, in an always-on-top window next to the meeting.

## Context and constraints

- Teams' built-in "Translate to" for captions is not available (requires Teams Premium /
  Microsoft 365 Copilot license).
- Teams' untranslated live captions (spoken language set to German) **are** available and are
  the input source.
- **All processing must stay on the local PC.** No cloud translation service, no meeting text
  leaves the machine.
- Standalone project in its own repository.

## Chosen approach

Read Teams' German caption text via Windows UI Automation and translate each finished line
with a local German→English machine-translation model.

Rejected alternative: capture system audio (WASAPI loopback) and run local Whisper. It is
independent of Teams, but needs much more CPU/GPU, adds 2–5 s latency and loses speaker names.
It remains the fallback if Step 0 shows the captions cannot be read via UI Automation.

## Tech stack

- C# / .NET 8, WPF (`net8.0-windows`)
- `System.Windows.Automation` (UI Automation, part of the Windows Desktop SDK)
- `Microsoft.ML.OnnxRuntime` (model inference)
- SentencePiece unigram tokenizer implemented in-app (no extra package)
- Model: Helsinki-NLP `opus-mt-de-en`, exported to ONNX (Xenova/opus-mt-de-en: encoder_model.onnx + decoder_model_merged.onnx, ~420 MB), downloaded by the app on first start
- Tests: MSTest + Moq

## Step 0 — Feasibility spike (before any production code)

A throwaway console app that attaches to a running Teams meeting window with German captions
enabled and dumps its UI Automation tree to a file.

Success criteria:
1. The caption text is present in the tree and updates while people speak.
2. The speaker name for each caption line can be identified.
3. The caption container can be located by a stable property (AutomationId, ClassName, or a
   reliable structural path).

The dumped tree snapshots are kept as test fixtures for `TeamsCaptionParser`.
If criterion 1 fails, stop and switch to the Whisper approach (new design needed).
If only criterion 2 fails, continue without speaker names.

## Architecture

```
Teams window --UI Automation--> TeamsCaptionReader --> CaptionStabilizer --> TranslationQueue --> OverlayWindow
                                (speaker, German text)  (final lines only)   (OpusMtTranslator)    (English lines)
```

### Components

Each component has one responsibility and is behind an interface so it can be tested alone.

| Component | Responsibility | Depends on |
|---|---|---|
| `ITeamsCaptionReader` / `TeamsCaptionReader` | Find the Teams meeting window and its caption container; subscribe to UI Automation change events; emit raw caption snapshots `(lineKey, speaker, text)`. | `System.Windows.Automation`, `TeamsCaptionParser` |
| `TeamsCaptionParser` | Pure function: given a UI Automation element snapshot of the caption container, return the list of `(speaker, text)` lines. | nothing (pure) |
| `CaptionStabilizer` | Teams rewrites a line while the speaker is still talking. Emits a line as **final** when a newer line appears after it, or when its text has not changed for 1.5 s. Each line is emitted as final exactly once. | `TimeProvider` (injected) |
| `ITranslator` / `OpusMtTranslator` | Translate one German sentence to English using the ONNX model + SentencePiece tokenizer. | ONNX Runtime, Tokenizers, model files |
| `TranslationQueue` | Background worker; translates final lines in arrival order so the UI thread never blocks. | `ITranslator` |
| `OverlayWindow` (+ ViewModel) | Always-on-top, resizable WPF window showing the last 10 lines: speaker name + English text, German original in small grey text below. Shows a single status line for problems. | ViewModel only |

### Data model

`CaptionLine { string Speaker; string GermanText; string? EnglishText; bool TranslationFailed; DateTimeOffset Timestamp; }`

## Error handling

The app must never crash; problems are shown in the overlay status line and logged.

| Situation | Behavior |
|---|---|
| Teams not running / no meeting window / captions off | Status "Waiting for Teams captions…"; reader retries every 2 s. |
| Meeting window found but caption container not found | Status "Captions not found – Teams layout may have changed"; dump the UI Automation tree to the log once. Keep retrying every 2 s. |
| Model files missing or unloadable at startup | Status shows the expected model folder and the missing file. Translation disabled; German lines still displayed. |
| Translation of a single line throws | Show the German line with `[translation failed]`; log the exception; continue with the next line. |

Logging: plain text file in `%LocalAppData%\CaptionTranslator\logs\`.
Model location: `%LocalAppData%\CaptionTranslator\models\opus-mt-de-en\`.

## Testing

- **`CaptionStabilizer`** — unit tests with a fake `TimeProvider`: in-place rewrites, new line
  finalizes the previous one, 1.5 s timeout, speaker change, no duplicate finals.
- **`TeamsCaptionParser`** — unit tests against UI Automation tree snapshots recorded in Step 0.
- **`TranslationQueue`** — unit tests with a mocked `ITranslator`: ordering, failure of one line
  does not stop the queue.
- **`OpusMtTranslator`** — one integration test with the real model on a few known sentences
  (e.g. "Guten Morgen" → output contains "morning"). Skipped when the model is not present.
- **`TeamsCaptionReader`** against live Teams and the full app — manual end-to-end test in a
  real Teams meeting with German captions.

## Out of scope (YAGNI)

- Saving transcripts
- Other language pairs
- Settings UI
- Non-Teams sources (system audio / other apps)

## Open risks

- Teams client updates may change the UI Automation tree and break `TeamsCaptionReader`.
  Mitigation: parser isolated in one pure function with snapshot tests; tree dump on failure.
- OPUS-MT quality on technical/domain-specific German (e.g. crane terminology) may be mediocre.
  Acceptable for a first version; model can be swapped behind `ITranslator`.

## Changes during implementation (2026-09-28)

- Step 0 done against a live meeting (new Teams, English UI): the caption pane is a `Group` named "Live Captions";
  the list sits several wrapper groups deep; each item is a `Group` with `[Text speaker, Text caption]`, separated by
  empty groups. `TeamsCaptionParser` picks the node with the most non-text children that contain text.
- Added **Windows Live Captions** (`LiveCaptions.exe`, element `CaptionsTextBlock`) as a second, user-selectable source.
- Decoding uses the merged decoder with key/value cache (≈0.1–0.8 s per sentence on CPU).
- On startup only the newest 3 existing caption lines are translated (Teams keeps a long history).
- Transient UI Automation `COMException`s (list changing during a read) skip one poll instead of failing.
- Added font size, "Copy all", "Clear" and "Diagnose" (UI tree dump) to the window; settings in `settings.json`.
