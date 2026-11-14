"""Headless contract tests for the post-install three-slot adventure homepage."""
from __future__ import annotations
import ast
from pathlib import Path
import tempfile
import unittest
from dataclasses import replace
from build_flow import GameInstall
from readiness import assess_readiness
from profile_store import ProfileStore

HERE = Path(__file__).resolve().parent
SRC = (HERE/'hookshot_qt.py').read_text()


def method(name):
    t = ast.parse(SRC)
    cls = next(n for n in t.body if isinstance(n, ast.ClassDef) and n.name == 'Window')
    return next(n for n in cls.body if isinstance(n, ast.FunctionDef) and n.name == name)


class ReadinessTests(unittest.TestCase):
    def test_green_when_files_present(self):
        i=GameInstall(Path('/g'),Path('/g/hookshot'),Path('/g/image.bin'),Path('/g/hookshot_shaders.bin'),Path('/g/hookshot_vk.so'), False)
        s=assess_readiness(i)
        self.assertEqual(s.level,'success')
        self.assertTrue(s.can_launch)

    def test_yellow_when_shader_archive_missing(self):
        i=GameInstall(Path('/g'),Path('/g/hookshot'),Path('/g/image.bin'),None,Path('/g/hookshot_vk.so'),False)
        s=assess_readiness(i)
        self.assertEqual(s.level,'warning')
        self.assertTrue(s.can_launch)

    def test_yellow_for_nonblocking_prepare_warning(self):
        i=GameInstall(Path('/g'),Path('/g/hookshot'),Path('/g/image.bin'),Path('/g/hookshot_shaders.bin'),Path('/g/hookshot_vk.so'),False)
        self.assertEqual(assess_readiness(i,preparation_warning='prewarm failed').level,'warning')

    def test_red_for_missing_renderer_or_image(self):
        i=GameInstall(Path('/g'),Path('/g/hookshot'),Path('/g/image.bin'),Path('/g/hookshot_shaders.bin'),None,False)
        if not i.playable:
            s=assess_readiness(i)
            self.assertEqual(s.level,'error')
            self.assertFalse(s.can_launch)
        self.assertFalse(assess_readiness(replace(i,image=None)).can_launch)

    def test_validation_error_overrides_green(self):
        i=GameInstall(Path('/g'),Path('/g/hookshot'),Path('/g/image.bin'),Path('/g/hookshot_shaders.bin'),Path('/g/hookshot_vk.so'),False)
        self.assertEqual(assess_readiness(i,'renderer mismatch').level,'error')


class NavigationContracts(unittest.TestCase):
    def test_main_menu_has_three_select_buttons(self):
        launch=method('make_launch_page')
        self.assertIn('range(3)', ast.unparse(launch))
        self.assertIn("QPushButton('Create')", ast.unparse(launch))
        self.assertIn('self.activate_launcher_box', ast.unparse(launch))
        self.assertIn("'Choose Your Adventure'".upper().replace(' ',' '),SRC)

    def test_profile_details_only_launch_normal_game(self):
        self.assertIn("self.show_adventure_details(profile['id'])",SRC)
        self.assertIn("self.show_sequence('details')",SRC)
        self.assertIn("self.detail_launch.clicked.connect",SRC)
        self.assertNotIn("self.primary_button.setText('OK · Launch game')",SRC)
        self.assertIn("if self.sequence_step in ('launch', 'details'):",SRC)

    def test_installer_only_from_explicit_settings_or_missing(self):
        self.assertIn("self.settings_sections.addTab(reinstall_page, 'Reinstall')",SRC)
        self.assertIn("self.show_sequence('source')",ast.unparse(method('begin_install')))
        self.assertIn("if install.playable and not self.reinstall.isChecked():",SRC)
        self.assertIn("self.footer.setVisible(installing)",SRC)
        self.assertIn("'01 / 03  ·  GAME FILES'",SRC)
        self.assertNotIn("'04 / 04  ·  '",SRC)

    def test_exit_installer_not_exit_app(self):
        src=ast.unparse(method('cancel'))
        self.assertNotIn('self.close(',src)
        self.assertIn('self.show_launch_summary',src)
        self.assertIn('Previous step',SRC)

    def test_profile_manager_selection_does_not_launch(self):
        src=(HERE/'profile_gui.py').read_text()
        self.assertIn('View adventure details',src)
        self.assertIn('self.launch_callback(p[\'id\'])',src)
        self.assertIn('self.show_adventure_details(profile_id)',ast.unparse(method('launch_selected_adventure')))
        self.assertNotIn('return self.launch_game(profile_id=profile_id)',ast.unparse(method('launch_selected_adventure')))

    def test_pin_only_affects_shortcuts(self):
        with tempfile.TemporaryDirectory() as t:
            store=ProfileStore(Path(t)/'registry')
            a=store.create('Link')
            b=store.create('Zelda')
            store.pin_launcher_slot(b['id'],0)
            self.assertEqual(store.launcher_slots()[0]['id'],b['id'])
            self.assertEqual(set(p['id'] for p in store.list_profiles()),{a['id'],b['id']})
            self.assertTrue(Path(a['user_dir']).is_dir())
            self.assertTrue(Path(b['user_dir']).is_dir())

if __name__ == '__main__': unittest.main()
