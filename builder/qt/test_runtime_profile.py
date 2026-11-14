import tempfile
import unittest
from pathlib import Path
from runtime_profile import make_profile_text, write_runtime_profile, runtime_env_overrides

class RuntimeProfileTests(unittest.TestCase):
    def test_preserves_other_game_options(self):
        original='RX_VK_PRECOMPILE=0\nHOS_WINDOW_SIZE=1920x1080\nRX_VK_VRAM_MB=2048\n'
        text=make_profile_text(original, precompile=True, terrain=True)
        self.assertIn('HOS_WINDOW_SIZE=1920x1080',text)
        self.assertIn('RX_VK_PRECOMPILE=1',text)
        self.assertNotIn('RX_VK_PRECOMPILE=0',text)
        self.assertIn('RX_VK_VRAM_MB=10240',text)
        self.assertNotIn('RX_VK_VRAM_MB=2048',text)
    def test_presence_only_flags_removed(self):
        original='RX_VK_PREPARE_BACKGROUND=0\nRX_VK_NO_PROGRAM_CACHE=0\nRX_VK_EVICT_RENDERED=0\n'
        text=make_profile_text(original, precompile=True, terrain=True)
        self.assertNotIn('RX_VK_PREPARE_BACKGROUND',text)
        self.assertNotIn('RX_VK_NO_PROGRAM_CACHE',text)
        self.assertNotIn('RX_VK_EVICT_RENDERED',text)
    def test_turning_terrain_off_removes_old_memory_override(self):
        text=make_profile_text('RX_VK_VRAM_MB=2048\n', precompile=True, terrain=False)
        self.assertNotIn('RX_VK_VRAM_MB=',text)
    def test_write_does_not_modify_original(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp); exe=root/'hookshot';exe.write_bytes(b'exe')
            orig=root/'hookshot_env.txt';orig.write_text('RX_VK_PRECOMPILE=0\n',encoding='utf-8')
            profile=write_runtime_profile(exe,precompile=True,terrain=True,cache_root=root/'cache')
            self.assertTrue(profile.is_file())
            self.assertIn('RX_VK_PRECOMPILE=1',profile.read_text())
            self.assertEqual(orig.read_text(),'RX_VK_PRECOMPILE=0\n')
    def test_inherited_flags_to_remove(self):
        keys=runtime_env_overrides(precompile=True,terrain=True)
        self.assertIn('RX_VK_PREPARE_BACKGROUND', keys)
        self.assertIn('RX_VK_EVICT_RENDERED', keys)

if __name__ == '__main__': unittest.main()
