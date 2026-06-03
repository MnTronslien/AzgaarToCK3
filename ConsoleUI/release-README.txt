AzgaarToCK3 — Quickstart
========================

1. Generate a map at https://azgaar.github.io/Fantasy-Map-Generator/
   Export THREE files: Full data .json, Cells .geojson, Rivers .geojson.

2. Run ConsoleUI.exe. The first time, it walks you through a short setup
   (CK3 location, mods folder, mod name, and your Azgaar exports). It writes
   a settings.json next to itself; edit that file later to change anything.

3. To convert different exports later, point the converter at them:
       ConsoleUI.exe --input-dir "C:\path\to\your\exports"
   Or set "InputDirectory" in settings.json and just double-click.

4. Enable the generated mod in the CK3 launcher and play.

Docs and issue tracker: https://github.com/MnTronslien/AzgaarToCK3
