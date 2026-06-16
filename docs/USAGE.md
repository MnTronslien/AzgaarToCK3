# Usage Guide

Assumes you have CK3 and [.NET 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) installed.

---

## 1. Export from Azgaar

You need two files from Azgaar's **Download** menu:

| Export | Format | Notes |
|--------|--------|-------|
| Full data | `.json` | Contains all map data |
| Cells | `.geojson` | Contains cell geometry |

Rivers are optional — export **Rivers** as `.geojson` if you want them drawn on the map.

Put all exported files in the same folder.

> **Major rivers vs minor rivers:** What rivers are major vs minor is determined by the `MajorRiverThreshold` set in `settings.json`. This value matches the discharge value of your rivers in Azgaar. See [CONVERSION_RULES.md](CONVERSION_RULES.md#major-rivers) for how this works and tips for best results.

---

## 2. Run the converter
When you are ready to run the converter there are two ways of doing it.

### Launch it directly
Just launch the .exe file or your platform's version thereof as you normally would. Edit the `settings.json` to tweak the converter. This will open a terminal window where the converter displays its output.

The converter auto-detects the most recent `.json`, cells `.geojson`, and rivers `.geojson` in that directory.

On first run you'll be prompted for your CK3 install path, mods directory, and a mod name. These are saved to `settings.json` — subsequent runs need no prompts.

### Using CLI

Point the converter at the Azgaar folder with `-d`:
```
./AzgaarToCK3 -d "C:/path/to/your/azgaar/exports"
```

For a full list of supported arguments see usage with:

```
./AzgaarToCK3 --help
```
---

## 3. Enable the mod in CK3

1. Launch CK3
2. Open the **Mods** menu → **Playsets**
3. Create a playset containing your generated mod
4. Launch

---

## Debug images

With `GenerateDebugImages = true` in `settings.json` (default), the converter saves intermediate map images to:

```
%LOCALAPPDATA%\AzgaarToCK3\debug\<mapname>_<timestamp>\
```

Useful for diagnosing province layout, river paths, and cell assignment without needing to launch CK3.
