"""Launch integration tests for the compact v0.3 profile UI.

Uses a disposable fake game executable: never starts the real game or touches real saves.
Qt widget rendering itself must be smoke-tested on a desktop with PyQt6.
"""
import ast
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

from profile_store import ProfileStore
from save_info import read_game_slots


class ProfileLaunchTests(unittest.TestCase):
    def setUp(self):
        self.context = tempfile.TemporaryDirectory()
        self.addCleanup(self.context.cleanup)
        self.path = Path(self.context.name)
        self.store = ProfileStore(self.path / 'registry')
        self.game_dir = self.path / 'game'
        self.game_dir.mkdir()
        (self.game_dir / 'image.bin').write_bytes(b'test')
        self.game = self.game_dir / 'hookshot'
        self.game.write_text('''#!/usr/bin/env python3
from pathlib import Path
import sys
assert sys.argv[1] == '--user'
root = Path(sys.argv[2])
assert root.is_absolute() and root.is_dir()
save = root / 'save' / 'account' / 'slot_00'
save.mkdir(parents=True, exist_ok=True)
(save / 'progress.sav').write_bytes(b'fake game save')
''')
        self.game.chmod(0o755)
        self.store.set_game_dir(self.game_dir)

    def test_newly_created_adventures_use_different_real_launch_paths(self):
        original = self.path / 'original'
        original_save = original / 'save' / 'account' / 'slot_00' / 'progress.sav'
        original_save.parent.mkdir(parents=True)
        original_save.write_bytes(b'important old progress')
        existing = self.store.register_legacy(original)
        first = self.store.create('New 1')
        second = self.store.create('New 2')
        self.assertNotEqual(first['user_dir'], second['user_dir'])
        for profile in (first, second):
            command, cwd = self.store.game_command(profile['id'])
            subprocess.run(command, cwd=cwd, check=True, timeout=10)
            self.assertTrue((Path(profile['user_dir']) / 'save' / 'account' / 'slot_00' / 'progress.sav').is_file())
            self.assertEqual([slot.index for slot in read_game_slots(Path(profile['user_dir']))], [0])
        self.assertEqual(original_save.read_bytes(), b'important old progress')
        self.assertEqual(Path(existing['user_dir']).resolve(), original)

    def test_selected_profile_survives_registry_reload(self):
        p = self.store.create('Persisted Adventure')
        reopened = ProfileStore(self.path / 'registry')
        command, _ = reopened.game_command(p['id'])
        self.assertEqual(command[-1], p['user_dir'])

    def test_builder_has_selection_sync_close_safety_and_tabs(self):
        # Syntax/structural verification when PyQt6 isn't available in CI.
        base = Path(__file__).resolve().parent
        builder = ast.parse((base / 'hookshot_qt.py').read_text())
        profile = ast.parse((base / 'profile_gui.py').read_text())
        def method_in(tree, klass, method):
            cls = next(n for n in tree.body if isinstance(n, ast.ClassDef) and n.name == klass)
            return next(n for n in cls.body if isinstance(n, ast.FunctionDef) and n.name == method)
        for method in ('set_adventure_profile', 'on_adventure_window_closed', 'create_adventure_profile', 'update_save_root_preview'):
            self.assertIsNotNone(method_in(builder, 'Window', method))
        self.assertIsNotNone(method_in(profile, 'Window', 'notify_selection'))
        code = (base / 'hookshot_qt.py').read_text()
        self.assertIn('self.launch_tabs.addTab(adventure', code)
        self.assertIn("self.tweaks_sections.addTab(self.tool_page, 'Shader / Pipeline Tools')", code)
        self.assertIn('self.launch_tabs.addTab(diagnostics', code)
        self.assertIn('on_select=self.set_adventure_profile', code)
        # v0.5 uses one real main window; auxiliary windows remain independent.
        self.assertIn('app.setQuitOnLastWindowClosed(True)', code)
        self.assertIn('self.wizard_stack.setCurrentWidget(', code)
        self.assertNotIn('class StepDialog(', code)
        self.assertIn("self.settings.setValue('adventure_profile', str(profile_id))", code)


if __name__ == '__main__':
    unittest.main()
