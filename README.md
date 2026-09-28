# Caption Translator (DE → EN)

Shows live English translations of German Teams meeting captions in a small always-on-top window.
Everything runs locally: the captions are read from the Teams window, and translation uses an offline model on your CPU.
No meeting text leaves the PC.

## Use

1. In the Teams meeting: **More (…) → Language and speech → Turn on live captions**, and set the spoken language to **German**.
2. Start `CaptionTranslator.exe`. New finished caption lines appear with speaker name, English text and (optionally) the German original.
   On startup only the newest 3 existing lines are translated.

Fallback source: choose **Windows Live Captions** in the drop-down (start it with Win+Ctrl+L, set the language to German).
It works for any audio, but without speaker names.

Buttons: font size (A− / A+), *Copy all* (English + German to the clipboard), *Clear*, *Diagnose* (saves the UI tree of the source app to a file; send it to the author if captions are not detected after a Teams update).

## First start

The translation model (Helsinki-NLP opus-mt-de-en, ONNX, about 420 MB) is downloaded once from Hugging Face into
`%LocalAppData%\CaptionTranslator\models`. Logs and settings: `%LocalAppData%\CaptionTranslator`.

## Requirements

Windows 10/11 x64 with the .NET 8 Desktop Runtime.

## Build / share

```
dotnet test
dotnet publish src/CaptionTranslator -c Release -o publish
```
Share the `publish` folder (zip it). Colleagues start `CaptionTranslator.exe`; the model downloads on first start.

## Settings (`settings.json`)

- `TeamsCaptionContainerPattern` – regex used to find the Teams caption area (default `caption|untertitel`).
- `QuietPeriodMilliseconds` – how long the newest line must stay unchanged before it is translated (default 1500).

## Known limits

- Teams UI updates may change the caption structure; use *Diagnose* and adjust the parser.
- The offline model is weaker on technical terms (e.g. "Winkelgeber" → "angler").
