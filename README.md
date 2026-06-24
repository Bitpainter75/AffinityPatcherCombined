# AffinityPatcherCombined

Patches `Serif.Affinity.dll` to improve compatibility and usability of **Affinity v3** under Wine.

> **Note:** Affinity v3 is free for everyone. All you need is a free [Canva account](https://www.affinity.studio) to download and use it.

---

## What it does

### 1. Settings Persistence
Removes the base call in `OnMainWindowLoaded` that resets application state on startup. After this patch, changes made in the settings dialogs and to the UI are preserved across restarts.

### 2. Parallel Font Enumeration Fix
Forces the `ParallelFontEnumerationDisabled` property getter to always return `true`. This prevents crashes and slowdowns caused by parallel font enumeration under Wine.

### 3. Colorful Icons
Replaces the monochrome tool icons with the original colorful icons. Affected icon sets include:
- Tool icons (brush, object selection, measure, stroke width, inpainting, and many more)
- Color picker
- Format dropper

---

## Usage

**Default path** — patches the Affinity installation at `~/.wine`:
```bash
./AffinityPatcherCombined
```

**Custom DLL path:**
```bash
./AffinityPatcherCombined /path/to/Serif.Affinity.dll
```

The patcher will:
1. Check write access to the target directory
2. Create a backup at `Serif.Affinity.dll.bak` (only on the first run)
3. Apply all three patches in sequence
4. Save the modified DLL in place

If the DLL is found to be empty or corrupted on a subsequent run, the patcher automatically restores it from the backup before patching.

---

## Building from source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Bitpainter75/AffinityPatcherCombined.git
cd AffinityPatcherCombined
dotnet publish -c Release -o ./publish
```

The resulting binary at `publish/AffinityPatcherCombined` has no external dependencies and runs standalone.

### Dependencies

| Package | Purpose |
|---|---|
| [dnlib](https://github.com/0xd4d/dnlib) | Read and write .NET assemblies at the CIL level |
| Microsoft.Extensions.FileProviders.Embedded | Access the embedded v2 icon resources |

---

## Disclaimer

These tools modify the Affinity application binary to improve compatibility under Wine. Affinity v3 is free software — no license purchase required, just a free Canva account. The authors take no responsibility for any damage to your installation.
