Hookshot v2.1.0 — Verified icon mapping manifest (Adventure UI v0.8)

Only icon_manifest.json is shipped. Each mapping references a local BNTX
archive, real internal texture, optional source-pixel crop, and alpha mode.
The release launcher loads this JSON and generates a PRIVATE cache under
~/.cache/hookshot/save-assets/ with a Creating Save Assets progress dialog.
When an icon fails decoding, only that icon uses generated/vector artwork; successfully decoded icons appear automatically. The source
archive and save files are never modified.

The separate visual icon browser is an internal authoring utility and is not
included in Hookshot launcher release builds. No extracted game textures,
cache PNGs, or contact sheets are distributed. Legacy output fields in the
JSON are ignored.
