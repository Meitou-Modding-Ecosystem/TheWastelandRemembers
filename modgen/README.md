# modgen: build the Kenshi mod from authored content

The mod reacts to the world with Kenshi's own dialogue system (no server, no AI at play time). `modgen` turns authored
JSON content packs (`content/*.json`) into a `.mod` file that appends dialogues to the game's NPC dialogue packages.

```
python modgen/build_mod.py "C:\Program Files (x86)\Steam\steamapps\common\Kenshi"
```
reads your game data (your own copy, read-only, with your mods.cfg load order), compiles every `content/*.json`, writes
`modgen/build/LivingWasteland/LivingWasteland.mod`, and checks it: references resolve and have the right type, enum
numbers are valid, every line has text, every dialogue is attached to some NPC package, and the file survives a
read-and-write round trip. Copy the `LivingWasteland` folder into `Kenshi\mods`, tick it in the launcher, put it LAST in
the load order.

Files: `builder.py` (record encodings, learned from the game's own files), `content.py` (the pack format, documented at
the top), `gamedb.py` (loads game data and decodes dialogues: `python gamedb.py DATA explain "dialogue name"`),
`validate.py`, `extract_enums.py` (numbers from KenshiLib's headers into `enums.json`), `server/tools/fcs_writer.py`
(re-creates the real game files byte for byte: `python fcs_writer.py roundtrip FILE`).

Never commit Kenshi's data files; this repo only holds our own content and tools.
