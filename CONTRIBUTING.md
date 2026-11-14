# Contributing

Changes to the Python launcher are welcome. Preserve save safety, avoid distributing game assets, and include a regression test when changing profile persistence, decoding, or layout.

1. Fork and open a branch.
2. Run `python3 -m unittest discover -s builder/qt -p 'test_*.py'`.
3. Document any field-level ownership assumptions separately from confirmed game data.
4. Avoid adding sample saves, local caches, build outputs, or game textures.
