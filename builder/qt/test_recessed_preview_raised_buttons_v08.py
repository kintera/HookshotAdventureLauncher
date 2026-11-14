"""Headless regressions for the v0.8 recessed save screenshot + raised buttons."""
from __future__ import annotations

import ast
from pathlib import Path
import unittest

from adventure_v08_settings_ui import selected_save_info_qss
from ui_theme import THEMES, raised_button_qss, stylesheet_for

HERE = Path(__file__).resolve().parent
SOURCE = (HERE / 'hookshot_qt.py').read_text(encoding='utf-8')
TREE = ast.parse(SOURCE)
WINDOW = next(n for n in TREE.body if isinstance(n, ast.ClassDef) and n.name == 'Window')


def method(name):
    return ast.unparse(next(n for n in WINDOW.body if isinstance(n, ast.FunctionDef) and n.name == name))


class RecessedPreviewAndRaisedButtons(unittest.TestCase):
    def test_recess_uses_dark_upper_lip_not_outer_shadow(self):
        src = method('make_adventure_detail_page')
        self.assertNotIn('detail_preview_inset', src)
        self.assertIn('bezel.addWidget(self.detail_preview, 0, Qt.AlignmentFlag.AlignCenter)', src)
        self.assertIn('bezel.setContentsMargins(9, 9, 9, 11)', src)
        self.assertIn('self.detail_preview_shell.setFixedSize(210, 130)', src)
        self.assertIn('self.detail_preview.setFixedSize(186, 105)', src)
        self.assertNotIn('self.detail_preview_shell.setGraphicsEffect', src)
        for key, label, _ in THEMES:
            css = selected_save_info_qss(label)
            self.assertIn('QFrame#detailPreviewShell', css)
            self.assertNotIn('QFrame#detailPreviewInset', css)
            self.assertIn('QLabel#adventureSavePreview', css)
            self.assertIn('border-top:3px solid', css)
            self.assertIn('border-bottom:2px solid', css)

    def test_compact_resize_keeps_screenshot_and_lips_inside_shell(self):
        src = method('resizeEvent')
        self.assertIn('(186, 105) if self.height() >= 590 else (158, 89)', src)
        self.assertIn('size[0] + 24', src)
        self.assertIn('size[1] + 25', src)
        self.assertIn('self.set_save_preview', src)

    def test_theme_aware_button_normal_hover_pressed_disabled(self):
        for key, _, _ in THEMES:
            with self.subTest(theme=key):
                css = stylesheet_for(key)
                effect = raised_button_qss(key)
                self.assertIn(effect, css)
                for target in ('QPushButton#primaryButton',
                               'QPushButton#secondaryButton',
                               'QPushButton#adventureSelectButton'):
                    self.assertIn(target, effect)
                self.assertIn('QPushButton:pressed', effect)
                self.assertIn('QPushButton:disabled', effect)
                self.assertIn('border-bottom:3px solid', effect)
                self.assertIn('border-top:3px solid', effect)
                self.assertIn('padding-top:7px', effect)
                self.assertIn('qlineargradient(', effect)

    def test_pipeline_and_equipment_fixes_remain_in_same_source(self):
        settings = method('setup_settings_tab')
        self.assertNotIn('pipeline_defaults_page', settings)
        self.assertIn("prediction_form.addRow('Prediction budget', self.predict_seconds)", method('setup_shader_tools'))
        self.assertIn("self.tweaks_sections.addTab(self.tool_page, 'Shader / Pipeline Tools')", settings)
        save_reader = (HERE / 'save_reader.py').read_text(encoding='utf-8')
        self.assertIn('EQUIPPED_WEAPON_NAMES', save_reader)
        panel_model = (HERE / 'adventure_panel_model.py').read_text(encoding='utf-8')
        self.assertIn('Not Collected', panel_model)


if __name__ == '__main__':
    unittest.main()
