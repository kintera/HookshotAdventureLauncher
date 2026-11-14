"""Source contracts for alphabetical, nested Settings; no PyQt6 requirement."""
from __future__ import annotations

import ast
from pathlib import Path
import unittest

SOURCE = (Path(__file__).resolve().parent / 'hookshot_qt.py').read_text(encoding='utf-8')
TREE = ast.parse(SOURCE)


def window_method(name):
    cls = next(node for node in TREE.body
               if isinstance(node, ast.ClassDef) and node.name == 'Window')
    return next(node for node in cls.body
                if isinstance(node, ast.FunctionDef) and node.name == name)


def tab_labels(method, attribute):
    labels = []
    for node in ast.walk(method):
        if not isinstance(node, ast.Call) or not isinstance(node.func, ast.Attribute):
            continue
        if node.func.attr != 'addTab' or len(node.args) < 2:
            continue
        if (isinstance(node.func.value, ast.Attribute)
                and node.func.value.attr == attribute
                and isinstance(node.args[1], ast.Constant)):
            labels.append(node.args[1].value)
    return labels


class SettingsTweaksNavigation(unittest.TestCase):
    def test_top_level_is_alphabetical_and_has_four_sections(self):
        self.assertEqual(tab_labels(window_method('setup_settings_tab'), 'settings_sections'),
                         ['Appearance', 'Reinstall', 'Tweaks', 'Warnings & Errors'])

    def test_tweaks_contains_shader_actions_and_pipeline_prediction(self):
        self.assertEqual(tab_labels(window_method('setup_settings_tab'), 'tweaks_sections'),
                         ['Builder', 'Game Launch', 'Shader / Pipeline Tools'])
        settings_body = ast.unparse(window_method('setup_settings_tab'))
        self.assertIn('self.tweaks_sections.addTab(self.tool_page', settings_body)
        self.assertIn('self.tweaks_sections.addTab(self.builder_settings_page', settings_body)
        self.assertIn('self.tweaks_sections.addTab(self.launch_settings_page', settings_body)
        self.assertNotIn("self.settings_sections.addTab(launches, 'Launch')", settings_body)

    def test_shortcuts_navigate_to_nested_pages(self):
        tools = ast.unparse(window_method('show_tools'))
        launch = ast.unparse(window_method('open_launch_settings'))
        for body in (tools, launch):
            self.assertIn('self.settings_sections.setCurrentWidget(self.tweaks_page)', body)
        self.assertIn('self.tweaks_sections.setCurrentWidget(self.tool_page)', tools)
        self.assertIn('self.tweaks_sections.setCurrentWidget(self.launch_settings_page)', launch)
        reinstall = ast.unparse(window_method('open_reinstall_settings'))
        self.assertIn("_settings_tab_index('Reinstall')", reinstall)

    def test_pipeline_prediction_still_uses_original_persisted_key(self):
        shader = ast.unparse(window_method('setup_shader_tools'))
        settings = ast.unparse(window_method('setup_settings_tab'))
        self.assertIn("self.settings.value('predict_seconds', 600)", shader)
        self.assertIn("self.settings.setValue('predict_seconds', n)", shader)
        self.assertIn('self.predict_seconds.setRange(30, 1800)', shader)
        self.assertIn("prediction_form.addRow('Prediction budget', self.predict_seconds)", shader)
        self.assertIn('shader_layout.addLayout(prediction_form)', shader)
        self.assertIn('self.tool_sections.addTab(warm', shader)
        self.assertIn('self.tool_sections.addTab(states_page', shader)
        self.assertNotIn('pipeline_defaults_page', settings)
        self.assertNotIn("'Pipeline Tools')", settings)
        self.assertEqual(SOURCE.count('self.predict_seconds = QSpinBox()'), 1)
        self.assertIn("self.predict_seconds.value()", ast.unparse(window_method('prepare_shaders')))
        self.assertIn("predict_seconds=self.predict_seconds.value()", ast.unparse(window_method('prepare_shaders')))

    def test_native_pipeline_actions_remain_available(self):
        shader = ast.unparse(window_method('setup_shader_tools'))
        for name in ('self.prepare_button', 'self.force_prepare_button',
                     'self.extended_prepare_button', 'self.inspect_button',
                     'self.merge_button', 'self.tool_cancel_button',
                     'self.tool_bar', 'self.tool_log'):
            with self.subTest(widget=name):
                self.assertIn(name, shader)
        self.assertIn('pipelineWarmupScroll', shader)
        self.assertIn('pipelineStatesScroll', shader)


if __name__ == '__main__':
    unittest.main()
