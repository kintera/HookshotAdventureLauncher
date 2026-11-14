import tempfile
import unittest
from pathlib import Path
from pipeline_warmup import make_warmup_text, write_warmup_env, state_counts


class PipelineWarmupTests(unittest.TestCase):
    def test_removes_conflicts_and_respects_unrelated_settings(self):
        original = ('RX_VK_NO_PREDICT=0\nRX_VK_PREPARE_BACKGROUND=0\n'
                    'RX_VK_PRECOMPILE_COMPUTES=all\nRX_VK_PREDICT_S=90\n'
                    'RX_VK_NO_STATE_LIST=0\nHOS_WINDOW_SIZE=1920x1080\n'
                    'RX_VK_VRAM_MB=10240\nRX_VK_KEEP_FINALIZED_MB=1024\n')
        out = make_warmup_text(original)
        self.assertNotIn('RX_VK_NO_PREDICT', out)
        self.assertNotIn('RX_VK_PREPARE_BACKGROUND', out)
        self.assertNotIn('RX_VK_NO_STATE_LIST', out)
        self.assertIn('RX_VK_PRECOMPILE_COMPUTES=used', out)
        self.assertNotIn('RX_VK_PRECOMPILE_COMPUTES=all', out)
        self.assertIn('RX_VK_PREDICT_S=600', out)
        self.assertIn('RX_VK_PREPARE_AGAIN=1', out)
        self.assertIn('HOS_WINDOW_SIZE=1920x1080', out)
        self.assertIn('RX_VK_VRAM_MB=10240', out)
        self.assertIn('RX_VK_KEEP_FINALIZED_MB=1024', out)

    def test_prediction_budget_bounds(self):
        for value in [-1, 0, 29, 1801, 90.5]:
            with self.assertRaises(ValueError):
                make_warmup_text('', predict_seconds=value)
        self.assertIn('RX_VK_PREDICT_S=1800', make_warmup_text('', predict_seconds=1800))

    def test_no_original_config_or_save_mutation(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            exe = root / 'hookshot'; exe.write_bytes(b'fake')
            original = root / 'hookshot_env.txt'
            original.write_text('RX_VK_NO_PREDICT=0\nHOS_WINDOW_SIZE=1280x720\n')
            save = root / 'save' / 'slot_00' / 'progress.sav'
            save.parent.mkdir(parents=True)
            save.write_bytes(b'keep existing progress')
            target = write_warmup_env(exe, cache_root=root / 'cache')
            self.assertEqual(save.read_bytes(), b'keep existing progress')
            self.assertEqual(original.read_text(), 'RX_VK_NO_PREDICT=0\nHOS_WINDOW_SIZE=1280x720\n')
            self.assertTrue(target.is_file())
            self.assertEqual(target.stat().st_mode & 0o777, 0o600)
            self.assertIn('RX_VK_PREDICT_S=600', target.read_text())

    def test_state_count_validation(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / 'hookshot_pipeline_states.bin').write_bytes(b'HKSTATE1' + (528).to_bytes(4, 'little') + b'X'*528*2)
            self.assertEqual(state_counts(root), {'learned': 2, 'shipped': None})
            (root / 'hookshot_pipeline_states_shipped.bin').write_bytes(b'bad data')
            self.assertIsNone(state_counts(root)['shipped'])

    def test_missing_executable_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(ValueError):
                write_warmup_env(Path(d) / 'missing')


if __name__ == '__main__': unittest.main()
