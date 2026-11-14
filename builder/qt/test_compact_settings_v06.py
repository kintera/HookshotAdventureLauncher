import ast
from pathlib import Path
import unittest
from runtime_profile import make_profile_text, runtime_env_overrides

ROOT=Path(__file__).resolve().parent

class NewFeatures(unittest.TestCase):
    def test_no_scroll_areas_in_profile_manager(self):
        s=(ROOT/'profile_gui.py').read_text()
        self.assertNotIn('QScrollArea(', s)
        self.assertIn('self.profiles_per_page = 5',s)
        self.assertIn("self.slot_picker",s)
        self.assertIn('read_adventure_slots(',s)

    def test_theme_selection_only_on_builder(self):
        p=(ROOT/'profile_gui.py').read_text()
        b=(ROOT/'hookshot_qt.py').read_text()
        self.assertNotIn('self.theme_combo',p)
        self.assertIn("self.tabs.addTab(page, 'Settings')",b)
        self.assertIn("self.settings_sections.addTab(appearance, 'Appearance')",b)
        self.assertIn('self.theme_combo',b)
        self.assertNotIn('self.statusBar().addPermanentWidget(self.theme_combo)',b)

    def test_tools_settings_and_frame_diagnostics(self):
        b=(ROOT/'hookshot_qt.py').read_text()
        self.assertIn('self.predict_seconds.value()',b)
        self.assertIn("self.tweaks_sections.addTab(self.tool_page, 'Shader / Pipeline Tools')",b)
        self.assertIn('RX_VK_FRAME_LOG',b)

    def test_off_really_disables_inherited_experiments(self):
        config = ('RX_VK_PRECOMPILE=1\nRX_VK_VRAM_MB=10240\n'
                  'RX_VK_KEEP_FINALIZED_MB=1024\nRX_VK_EVICT_MIN_UNUSED=120\n'
                  'HOS_WINDOW_SIZE=1280x800\n')
        txt=make_profile_text(config,precompile=False, terrain=False)
        self.assertIn('RX_VK_PRECOMPILE=0',txt)
        self.assertNotIn('RX_VK_VRAM_MB=',txt)
        self.assertNotIn('RX_VK_KEEP_FINALIZED_MB=',txt)
        self.assertNotIn('RX_VK_EVICT_MIN_UNUSED=',txt)
        self.assertIn('HOS_WINDOW_SIZE=1280x800',txt)
        removed=runtime_env_overrides(precompile=False, terrain=False)
        self.assertIn('RX_VK_VRAM_MB',removed)
        self.assertIn('RX_VK_PRECOMPILE',removed)

if __name__=='__main__': unittest.main()
