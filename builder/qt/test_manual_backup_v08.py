"""Manual-only save backup policy regression checks (no Qt runtime required)."""
from __future__ import annotations

import ast
from pathlib import Path
import tempfile
import unittest

from profile_store import ProfileStore

HERE = Path(__file__).parent


def method_ast(filename: str, name: str) -> ast.FunctionDef:
    source = ast.parse((HERE / filename).read_text(encoding='utf-8'))
    window = next(item for item in source.body if isinstance(item, ast.ClassDef) and item.name == 'Window')
    return next(item for item in window.body if isinstance(item, ast.FunctionDef) and item.name == name)


def backup_invocations(node: ast.AST) -> list[ast.Call]:
    return [call for call in ast.walk(node) if isinstance(call, ast.Call)
            and isinstance(call.func, ast.Attribute) and call.func.attr == 'backup']


class ManualBackupPolicyTests(unittest.TestCase):
    def test_integrated_launch_never_backs_up_or_prompts_for_backups(self):
        node = method_ast('hookshot_qt.py', 'launch_game')
        body = ast.unparse(node)
        self.assertEqual(backup_invocations(node), [])
        self.assertNotIn('Protect original saves', body)
        self.assertNotIn('_original_backup_prompted', body)
        self.assertIn('game_command', body)

    def test_standalone_profile_launch_never_backs_up_or_prompts(self):
        node = method_ast('profile_gui.py', 'play')
        body = ast.unparse(node)
        self.assertEqual(backup_invocations(node), [])
        self.assertNotIn('backed_up_this_session', body)
        self.assertNotIn('Protect original saved games', body)
        self.assertIn('game_command', body)

    def test_manual_backup_button_is_wired_in_existing_settings_launch_tab(self):
        src = (HERE / 'hookshot_qt.py').read_text(encoding='utf-8')
        setup = ast.unparse(method_ast('hookshot_qt.py', 'setup_settings_tab'))
        self.assertIn("self.manual_save_backup_button.clicked.connect(self.manual_backup_selected_adventure)", setup)
        self.assertIn("self.tweaks_sections.addTab(self.launch_settings_page, 'Game Launch')", setup)
        self.assertNotIn('icon_manifest.py', src)
        manual = method_ast('hookshot_qt.py', 'manual_backup_selected_adventure')
        self.assertEqual(len(backup_invocations(manual)), 1)
        self.assertIn('self.profile_store.backup(profile_id)', ast.unparse(manual))
        self.assertIn('self.profile_combo.currentData()', ast.unparse(manual))

    def test_profile_manager_back_up_button_is_still_manual(self):
        src = (HERE / 'profile_gui.py').read_text(encoding='utf-8')
        self.assertIn('("Import", self.import_dir), ("Back up", self.backup)', src)
        self.assertEqual(len(backup_invocations(method_ast('profile_gui.py', 'backup'))), 1)

    def test_no_backup_is_created_until_explicit_call(self):
        with tempfile.TemporaryDirectory() as temp:
            store = ProfileStore(Path(temp) / 'registry')
            original = Path(temp) / 'original'
            save_file = original / 'save' / 'user' / 'progress.sav'
            save_file.parent.mkdir(parents=True)
            save_file.write_bytes(b'unmodified original')
            existing = store.register_legacy(original)
            self.assertFalse(store.backup_path.exists(), 'Registration should not back up saved games')
            command = None
            # game_command requires the installed game, so resolve only the
            # registered file's selected directory here.
            self.assertEqual(Path(store.get(existing['id'])['user_dir']).resolve(), original.resolve())
            self.assertFalse(store.backup_path.exists())
            created = store.backup(existing['id'])
            self.assertEqual((created / 'user' / 'save' / 'user' / 'progress.sav').read_bytes(),
                             b'unmodified original')
            self.assertEqual(save_file.read_bytes(), b'unmodified original')


if __name__ == '__main__':
    unittest.main()
