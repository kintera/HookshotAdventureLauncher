"""2.1.0 compatibility and v0.8 refinement checks (no Qt required)."""
import tempfile
import unittest
from pathlib import Path
from PIL import Image
from pipeline_warmup import state_counts

HERE=Path(__file__).parent
UI=(HERE/'hookshot_qt.py').read_text()
DRAW=(HERE/'zelda_summary_widget.py').read_text()
ART=HERE/'assets/zelda_icons'

class CompatibilityTests(unittest.TestCase):
    def test_hkstate1_hkstate2(self):
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'hookshot_pipeline_states.bin'
            for magic,size in [(b'HKSTATE1',528),(b'HKSTATE2',552)]:
                path.write_bytes(magic+size.to_bytes(4,'little')+bytes(size*2))
                self.assertEqual(state_counts(tmp)['learned'],2)
            path.write_bytes(b'HKSTATE2'+(528).to_bytes(4,'little')+bytes(552))
            self.assertIsNone(state_counts(tmp)['learned'])
    def test_updated_native_script_path(self):
        self.assertIn("ROOT / 'nx2' / 'make_game.py'", UI)
        self.assertIn("args = ['--update', self.out.text().strip()]", UI)
        self.assertIn('if self.update_requested and install.retained_files:', UI)
    def test_no_duplicate_readiness_labels_and_three_slots(self):
        self.assertIn('for i in range(3)', UI)
        self.assertIn("header.addWidget(self.install_tag)",UI)
        self.assertNotIn("'04 / 04",UI)
        self.assertIn("self.detail_preview.setFixedSize(186, 105)",UI)
    def test_embedded_save_preview_used_both_places(self):
        self.assertIn('self.set_save_preview(self.launcher_previews[i]',UI)
        self.assertIn('self.set_save_preview(self.detail_preview', UI)
        self.assertIn("read_adventure_slots(Path(profile['user_dir']))",UI)
    def test_equipment_and_eight_leg_skulltula(self):
        self.assertIn("text_rect('SWORDS'",DRAW)
        self.assertIn("text_rect('SHIELDS'",DRAW)
        self.assertIn('four jointed legs on each side',DRAW)
        self.assertIn("'heart_three_quarters' if fraction >= 0.75",DRAW)
        self.assertIn("'heart_half' if fraction >= 0.5",DRAW)
        self.assertIn("'heart_quarter' if fraction >= 0.25",DRAW)
        self.assertIn("'heart_double_defense'",DRAW)
    def test_release_does_not_bundle_extracted_game_icons(self):
        import json
        manifest = HERE / 'assets/game_icons/icon_manifest.json'
        doc = json.loads(manifest.read_text(encoding='utf-8'))
        self.assertEqual(doc['schema_version'], 1)
        self.assertGreater(sum(bool(v.get('verified')) for v in doc['icons'].values()), 50)
        self.assertFalse(list((HERE/'assets'/'game_icons').glob('*.png')),
                         'No game-derived PNG should be shipped in the release')

if __name__=='__main__': unittest.main()
