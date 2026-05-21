AzgaarToCK3 — Quickstart
========================

1. Subscribe to "Total Conversion Sandbox" on the Steam Workshop
   (Crusader Kings III → Workshop tab). Wait for Steam to download it.

2. Generate a map at https://azgaar.github.io/Fantasy-Map-Generator/
   Export THREE files: Full data .json, Cells .geojson, Rivers .geojson.

3. Run ConsoleUI.exe. The first time, it walks you through a 3-step setup
   (CK3 location, TCS location, mod name). It writes a settings.json
   next to itself; edit that file later to change anything.

4. Tell the converter where your Azgaar exports are. Easiest:
       ConsoleUI.exe --input-dir "C:\path\to\your\exports"
   Or set "InputDirectory" in settings.json and just double-click.

5. Activate the generated mod in the CK3 launcher alongside TCS.

Docs and issue tracker: https://github.com/MnTronslien/AzgaarToCK3
