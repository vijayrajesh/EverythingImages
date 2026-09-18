# EverythingImages

Instant local image search for Windows: by filename, text in images (Windows
OCR), colours and AI descriptions (Liquid AI vision models, on the GPU or the
CPU). Everything stays on the PC.

The AI engine ships beside the exe and runs the models on any NVIDIA, AMD or
Intel card through the driver already on the PC, so there is nothing to
download but the models themselves.

## Download

Get the portable zip or the setup exe from the
[Releases](../../releases) page. Needs Windows 10 (version 2004) or later and
the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0),
which both packages offer to fetch when it is missing. The exe is not signed,
so Windows SmartScreen may warn the first time it runs.

## Build and run

Needs the .NET 10 SDK.

| Script | What it does |
| --- | --- |
| `run_wpf.bat` | Runs `dist\EverythingImages.exe`, or builds and runs a debug copy if there is none yet. |
| `build_wpf.bat` | Fetches the AI engine, runs the tests, publishes `dist\EverythingImages.exe` + `dist\ai-engine\`. |
| `build-installer.bat` | Builds everything that ships, into `release\`. `build-installer.bat nosetup` skips the one step that needs Inno Setup. |

### What ships: portable, and only portable

`build-installer.bat` makes three things, all the same copy of the app:

| In `release\` | What it is |
| --- | --- |
| `EverythingImages-Portable-<ver>\` | A folder to run from anywhere. |
| `EverythingImages-Portable-<ver>.zip` | The same folder, zipped. |
| `EverythingImages-Portable-Setup-<ver>.exe` | Asks for a folder and copies the app there. |

There is deliberately no installing setup: no Start menu entry, no uninstaller,
nothing in the registry, nothing in Apps & features. The setup exe is only a
friendlier way to unpack the same folder — it needs Inno Setup 6 to build
(`winget install JRSoftware.InnoSetup`), and `build-installer.bat nosetup`
leaves it out.

What makes a copy portable is `portable.txt` beside the exe
(`installer\portable.txt`, shipped in all three): with it there the app keeps
its index, settings and AI models in a `data` folder next to itself, so the
folder can move to another PC or a USB drive. Delete it to use the normal
per-user folders instead.

The version comes from `<Version>` in `src\EverythingImages\EverythingImages.csproj`.

### The .NET runtime

EverythingImages.exe is framework-dependent: it runs on the .NET 10 Desktop Runtime
rather than carrying its own copy (most of its ~26 MB is the Windows OCR/imaging
interop library; the AI engine beside it is another ~73 MB). None of the three
packages needs the runtime to be there already, and all of them ask for it
when it is missing:

- **The setup exe** is a native program and runs on a bare PC. If the runtime
  is missing, the Ready page says so and a Yes/No follows before copying:
  Yes downloads it from Microsoft (about 60 MB, Windows asks for permission)
  and installs it; No copies EverythingImages anyway and the Finished page says it
  will ask later. A silent run (`/VERYSILENT`) answers No: it never downloads
  or asks for elevation without someone there.
- **The folder and the zip**, started on a PC without the runtime, show .NET's
  own "You must install or update .NET to run this application" box with a
  **Download it now** link.

## Where things are kept

| What | Normal | Portable (`portable.txt` present) |
| --- | --- | --- |
| Index and settings | `%APPDATA%\EverythingImages` | `data\` |
| AI models | `%APPDATA%\EverythingImages\models` | `data\shared\` |

A portable copy that finds models already downloaded on the PC copies them
instead of downloading them again.

`EVERYTHINGIMAGES_DATA_DIR` overrides both (handy for
testing on a separate library).

## Layout

- `src\EverythingImages.Core`: everything except UI (index and search, colours,
  scanning, OCR, model catalogue, downloads, GPU/CPU, describing).
- `src\EverythingImages`: the WPF app (`AppState` holds all state; windows bind to it).
- `tests\EverythingImages.Tests`: xUnit tests. Set `EVERYTHINGIMAGES_TEST_IMAGES` to a
  folder of images to include the end-to-end test (scan, OCR, GPU and CPU describe).
- `installer\EverythingImages.iss`: the Inno Setup script for the portable setup exe,
  and `installer\portable.txt`, the marker every package ships.
- `scripts\fetch-ai-engine.ps1`: downloads the pinned AI engine into
  `runtime\ai-engine` (build and checksum in `llama-build.json`).

## License

MIT, see [LICENSE](LICENSE). The AI engine and the models it runs come under
their own licenses: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
