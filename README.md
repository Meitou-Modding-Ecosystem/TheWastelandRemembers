# The Wasteland Remembers

Source for The Wasteland Remembers, a Meitou-only mod for Kenshi: a world that notices the player and remembers. Towns talk about what
really happened, outlaws react, rivals come back for the squad, towns hire bounty hunters, and the squad keeps its own record.

- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3839368189
- Needs MeitouClient 0.17.3 or later (below 0.21.0).
- For other mod makers: `modgen/interop/README.md` (what the mod does, the API, engine notes) and `modgen/interop/MODDER_API.md`.

## Layout

- `modgen/content/*.json`: the authored content packs (dialogue, outlaws, quests, rivals, titles...). The pack format is documented at the
  top of `modgen/content.py`; writing guidance in `modgen/AUTHORING.md`.
- `modgen/*.py`: the build tools. `build_mod.py` compiles the packs into `LivingWasteland.mod` against your own game data; `meitou_target.py`
  writes the codes the C# helper reads (`lw_codes.json`).
- `modgen/meitou/LivingWastelandMeitou`: the C# code mod (Meitou systems, settings, the squad record window). `LivingWastelandMeitou.Tests`:
  unit tests for the engine-free rules. `LivingWastelandMeitou.WorldTests`: the engine-side systems run in small synthetic worlds
  against the real Meitou DLLs. `SquadRecord`: the stand-alone squad record mod built from the same files.
- `server/tools`: the FCS file reader and writer the build uses (`fcs_reader.py`, `fcs_writer.py`, `derive_*.py`).

## Building

Kenshi's data files are never in this repo; the build reads your own copy.

```
python3 modgen/build_mod.py "<Kenshi folder>" --target meitou --single --out <out folder>
dotnet build modgen/meitou/LivingWastelandMeitou -c Release -p:MeitouDir="<folder with Meitou's DLLs>"
dotnet test modgen/meitou/LivingWastelandMeitou.Tests
dotnet test modgen/meitou/LivingWastelandMeitou.WorldTests -p:MeitouDir="<folder with Meitou's DLLs>" -p:MeitouTfm=net11.0
python3 modgen/make_release.py "<Kenshi folder>"      # the release zip (run from the repo root)
```

`MeitouDir` is a folder holding `MeitouClient.dll` (or `Meitou.Modding.dll`), `Meitou.Simulation.dll`, `Meitou.Core.dll`,
`Meitou.Data.dll` and `0Harmony.dll` from a MeitouClient release.

## Credits

VlcekM and the MeitouClient contributors; the Kenshi Wiki and kenshimap.com; Lo-Fi Games for Kenshi. Made largely with Claude Code. No code
from other mods is included.

## Licence

GPL-3.0-or-later (see [LICENSE](LICENSE)): you may copy, change and share this mod, as long as you credit it and release your version
under the same licence, with its source.
