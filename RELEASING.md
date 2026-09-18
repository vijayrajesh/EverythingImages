# Releasing EverythingImages

How a new version goes out, using GitHub Desktop and the GitHub website. There's
no command line beyond one build script.

**What goes where**

| Where | What | How it gets there |
| --- | --- | --- |
| The repository | Source code only | GitHub Desktop: commit and push |
| A GitHub Release | `EverythingImages-Portable-Setup-<version>.exe`, and nothing else | Uploaded on the website |

`.gitignore` keeps builds, the AI engine, models and your own index out of the
repository, so committing everything that GitHub Desktop lists is safe. If
Desktop ever lists an `.exe`, `.dll`, `.zip`, `.gguf` or anything under `dist\`,
`release\` or `runtime\`, stop: something is wrong with `.gitignore`.

GitHub adds "Source code (zip)" and "Source code (tar.gz)" to every release by
itself. They can't be removed and don't need to be.

---

## Every release

### 1. Finish the work

Make the changes, then check that it builds and the tests pass:

```
build.bat
```

Run `dist\EverythingImages.exe` and try the new feature once.

### 2. Pick the version number

`MAJOR.MINOR.PATCH`, e.g. `0.1.0` → `0.2.0`:

| Change | Bump | Example |
| --- | --- | --- |
| Bug fixes only | PATCH | 0.2.0 → 0.2.1 |
| New feature | MINOR | 0.2.1 → 0.3.0 |
| Something users must redo (settings or index format changed) | MAJOR | 0.9.0 → 1.0.0 |

Set it in **one place**: `<Version>` in `src\EverythingImages\EverythingImages.csproj`.
The installer file name, the setup's version info and the About window all read it
from there.

### 3. Write the changelog entry

Add a section at the top of `CHANGELOG.md`:

```markdown
## 0.2.0 - 2026-10-05

### Added
- What's new, in words a user understands.

### Changed
- ...

### Fixed
- ...
```

Leave out headings with nothing under them. This text becomes the release notes in step 7.

### 4. Build the installer

```
release.bat
```

It fetches the AI engine if needed, runs the tests and builds
`release\EverythingImages-Portable-Setup-<version>.exe`. It needs Inno Setup 6
(`winget install JRSoftware.InnoSetup`). It also makes a portable folder and a zip
in `release\`, which aren't uploaded.

If it fails, nothing is published. Fix it and run it again.

### 5. Try the installer

Run the new setup exe, install into a scratch folder (e.g. `E:\tmp\ei-test`),
start the app, and check the About window (ⓘ) shows the new version. Delete the
folder afterwards.

### 6. Commit and push (GitHub Desktop)

1. Open GitHub Desktop. The **EverythingImages** repository shows the changed files.
2. Look at the file list (see "What goes where" above).
3. Summary: `Release 0.2.0` (or describe the change). Click **Commit to main**.
4. Click **Push origin**.

### 7. Publish the release (github.com)

1. Open https://github.com/vijayrajesh/EverythingImages/releases/new
2. **Choose a tag** → type `v0.2.0` → **Create new tag: v0.2.0 on publish**.
   Target: `main`.
3. **Release title**: `EverythingImages 0.2.0`
4. **Description**: paste the version's `CHANGELOG.md` section, then this footer:

   ```markdown
   ### Download
   **EverythingImages-Portable-Setup-0.2.0.exe** below. It asks for a folder and
   copies the app there; nothing is installed into Windows. Needs Windows 10
   (2004) or later; if the .NET 10 Desktop Runtime is missing, the setup offers
   to install it.

   The exe is not code-signed, so Windows SmartScreen may say "Windows protected
   your PC". Click **More info → Run anyway**.
   ```

5. Drag `release\EverythingImages-Portable-Setup-0.2.0.exe` into
   **Attach binaries**. Wait for the upload to finish.
6. Leave **Set as the latest release** ticked, then click **Publish release**.

### 8. Check

- The release page shows the setup exe, plus GitHub's two "Source code" files.
- Download the exe from the page and run it once.
- https://github.com/vijayrajesh/EverythingImages/releases/latest points at the new version.

---

## If something went wrong

| Problem | Fix |
| --- | --- |
| Uploaded the wrong file | Edit the release (✏️), delete the asset with its 🗑, attach the right one. |
| Mistake in the release notes | Edit the release. It can be changed any time. |
| A bad build went out | Fix it and release the next PATCH version. Don't reuse a version number. |
| Committed something private | Tell Claude before pushing. After a push it is public; deleting it later doesn't remove it from history. |

---

## First publish (done once)

1. GitHub Desktop → **File → Add local repository** → `E:\Git\EverythingImages` → **Add repository**.
2. **Publish repository**: name `EverythingImages`; description *Instant local image
   search for Windows: filename, OCR text, colours and on-device AI descriptions.*;
   **untick "Keep this code private"**; then click **Publish repository**.
3. On github.com, next to **About** on the repository page, click ⚙:
   - Description: as above.
   - Website: `https://pixelthemes.com/`
   - Topics: `wpf` `dotnet` `windows` `image-search` `ocr` `llama-cpp` `desktop-app`
   - Tick **Releases** and untick **Packages** and **Deployments** under
     "Include in the home page".
4. Publish `v0.1.0` with steps 7–8 above.

The About window's links point to `https://github.com/vijayrajesh/EverythingImages`
(`RepoUrl` in `src\EverythingImages\AboutWindow.xaml.cs`). If the repository is ever
renamed or moved, change it there.
