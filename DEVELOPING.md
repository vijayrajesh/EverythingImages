# Building EverythingImages from source

Everything you need to compile, run, test and change EverythingImages. For
publishing a new version, see [RELEASING.md](RELEASING.md).

## Requirements

| What | Why | Get it |
| --- | --- | --- |
| Windows 10 version 2004 (build 19041) or later, x64 | The app uses Windows OCR and imaging APIs, and WPF | — |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | Compiles everything | `winget install Microsoft.DotNet.SDK.10` |
| [Git](https://git-scm.com/download/win) | To clone | `winget install Git.Git` |
| Internet, once | The build downloads the AI engine (about 30 MB) from llama.cpp's GitHub releases | — |
| *Optional:* [Inno Setup 6](https://jrsoftware.org/isinfo.php) | Only for the setup exe (`release.bat`) | `winget install JRSoftware.InnoSetup` |
| *Optional:* an editor | See [Editors](#editors) | — |

PowerShell 5.1 and `curl.exe` are also used. Both come with Windows 10 and 11.

Check the SDK:

```
dotnet --version
```

It should print `10.0.something`. `global.json` asks for .NET 10 or newer, so an
older SDK fails with a clear message instead of odd compile errors.

## Quick start

```
git clone https://github.com/vijayrajesh/EverythingImages.git
cd EverythingImages
build.bat
dist\EverythingImages.exe
```

`build.bat` does three things, and stops at the first that fails:

1. **Fetches the AI engine** (`scripts\fetch-ai-engine.ps1`). It downloads the
   llama.cpp Vulkan build pinned in `scripts\llama-build.json`, checks its SHA-256,
   and keeps only the files the app needs (about 72 MB) in `runtime\ai-engine\`. Later runs see it's
   there and skip the download.
2. **Runs the tests** (`dotnet test`, Release).
3. **Publishes** `dist\EverythingImages.exe` (one framework-dependent exe) with
   `dist\ai-engine\` beside it.

To run the app, start `dist\EverythingImages.exe`, or `run.bat`, which builds a debug copy first if `dist` is empty.

## The scripts

| Script | Use it to |
| --- | --- |
| `run.bat` | Start the app. Runs `dist\EverythingImages.exe` if there is one, otherwise `dotnet run` (debug). |
| `build.bat` | Get a tested release build in `dist\`. Use `build.bat nopause` in scripts or CI. |
| `release.bat` | Build what ships, into `release\`: the portable folder, a zip and the setup exe. `release.bat nosetup` skips the setup exe (and Inno Setup). |
| `scripts\fetch-ai-engine.ps1` | Only fetch the AI engine. Needed before `dotnet publish`, or before a first `dotnet build` if you want AI descriptions to work. |

### Working with `dotnet` directly

```
powershell -ExecutionPolicy Bypass -File scripts\fetch-ai-engine.ps1
dotnet build EverythingImages.slnx
dotnet test  EverythingImages.slnx
dotnet run   --project src\EverythingImages
```

A plain `dotnet build` works without the AI engine. The app then says the engine is
missing and everything except AI descriptions still works. `dotnet publish`
refuses to run without it, so a build that can't describe images can't be shipped by
accident.

## Editors

- **Visual Studio 2026** (or 2022 17.14+) with the *.NET desktop development*
  workload: open `EverythingImages.slnx`, set **EverythingImages** as the startup
  project, press F5. The XAML designer and hot reload work.
- **VS Code** with the *C# Dev Kit* extension: open the folder. Run and debug from the
  Solution Explorer, or use `dotnet run` in the terminal.
- **JetBrains Rider**: open `EverythingImages.slnx`.

Run `scripts\fetch-ai-engine.ps1` once first, whichever you use. The build copies
`runtime\ai-engine\` next to the exe it produces.

## Project layout

```
EverythingImages.slnx
├─ src\EverythingImages.Core\      everything except UI: no WPF, unit-tested
│    Paths.cs          where data lives (normal vs portable, overrides)
│    ImageIndex.cs     records, inverted index, prefix search, save/load (index.json)
│    Scanner.cs        walks folders, decodes images, Windows OCR, change detection
│    Colors.cs         dominant colours and colour names
│    VisionModels.cs   which Liquid AI models on Hugging Face count, and their files
│    ModelCatalog.cs   the catalogue, downloads, delete
│    Download.cs       resumable, SHA-256-verified downloads
│    AiRuntime.cs      runs llama-mtmd-cli.exe to describe an image, GPU or CPU
│    EnginesLogic.cs   parses the engine's output (devices, progress stages)
│    Settings.cs       settings.json
├─ src\EverythingImages\           the WPF app
│    AppState.cs       all app state; the windows bind to it
│    MainWindow        search box, filters, grid/list, inspector
│    FoldersWindow     folders to scan
│    OptionsWindow     GPU or CPU, models, description prompt, OCR
│    AboutWindow       version, signature (Signature.cs), links
├─ tests\EverythingImages.Tests\   xUnit tests for the core
├─ installer\                      Inno Setup script + portable.txt
└─ scripts\                        AI engine fetch script and its pinned build
```

Not in git, created by building: `runtime\` (the AI engine), `dist\` (a
release build), `release\` (what ships), and each project's `bin\` and `obj\`.

## How it works, briefly

- **Search** is an in-memory inverted index. Each query word must match the *start* of
  an indexed word (filename parts, OCR text, colour names, AI description), so
  results update as you type.
- **OCR** is Windows' own (`Windows.Media.Ocr`), in the languages of the user's
  Windows profile.
- **AI descriptions** run `ai-engine\llama-mtmd-cli.exe` (llama.cpp) as a separate
  process with a small Liquid AI vision model. The Vulkan build uses any NVIDIA,
  AMD or Intel GPU through the installed driver, and falls back to the CPU.
- **Models** are downloaded by the app from Hugging Face on request, never bundled.

## Where data is kept

| What | Normal | Portable (`portable.txt` beside the exe) |
| --- | --- | --- |
| Index and settings | `%APPDATA%\EverythingImages` | `data\` beside the exe |
| AI models | `%APPDATA%\EverythingImages\models` | `data\shared\models` |

To develop against a separate library, point everything somewhere else first:

```
set EVERYTHINGIMAGES_DATA_DIR=E:\tmp\ei-data
dotnet run --project src\EverythingImages
```

## Tests

```
dotnet test EverythingImages.slnx
```

The unit tests need nothing else and take a few seconds.

The **end-to-end test** (scan, OCR, search, describe on GPU and CPU) runs only when
`EVERYTHINGIMAGES_TEST_IMAGES` points at a folder of images, including one named
`folders_dialog.png` that contains text. It reuses a model already downloaded by
the app, so download one in Options first. Without the variable it passes
without doing anything.

```
set EVERYTHINGIMAGES_TEST_IMAGES=E:\tmp\test-images
dotnet test EverythingImages.slnx --logger "console;verbosity=detailed"
```

## Changing things

- **Version:** `<Version>` in `src\EverythingImages\EverythingImages.csproj`, the only
  place. The About window, the setup exe name and its version info read it.
- **AI engine version:** change `build` and `vulkan_zip_sha256` in
  `scripts\llama-build.json`. The hash is the SHA-256 of
  `llama-<build>-bin-win-vulkan-x64.zip` from
  [llama.cpp releases](https://github.com/ggml-org/llama.cpp/releases). The next build
  fetches it.
- **Colours and styles:** `src\EverythingImages\App.xaml`.
- **The About signature:** `src\EverythingImages\Signature.cs` is a copy of the shared
  one. Keep its wording identical to the original.

Before sending a change: `build.bat` passes, and you've tried the change in the app.

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `'dotnet' is not recognized` | The .NET SDK isn't installed, or the terminal was open before it was. Install it and open a new terminal. |
| `A compatible .NET SDK was not found` | An older SDK. Install the .NET 10 SDK. |
| `runtime\ai-engine is missing` on publish | Run `scripts\fetch-ai-engine.ps1`, or use `build.bat`. |
| `Checksum mismatch` from the fetch script | The download was corrupted or blocked by a proxy. Run it again; if it persists, check `scripts\llama-build.json`. |
| Running scripts is disabled | The `.bat` files pass `-ExecutionPolicy Bypass`. Run them rather than the `.ps1` directly, or add that flag yourself. |
| App says "No usable graphics card" | No Vulkan driver answered. Update the graphics driver, or choose CPU in Options. |
| Build fails with `CS2001 ... .g.cs could not be found` after adding a XAML window | A stale WPF temp build. Run the build again, or delete the project's `obj\` folder. |
| `release.bat` can't update `release\...` | A copy of the app is running from that folder. Close it. |
| `Inno Setup 6 builds the portable installer` error | Install Inno Setup, or use `release.bat nosetup`. |
