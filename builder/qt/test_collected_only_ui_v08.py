"""Ownership-only equipment presentation contract (no equipped rings or hover status)."""
import ast
from pathlib import Path
import unittest
from adventure_panel_model import item_appearance, item_hover_text

HERE = Path(__file__).parent

class CollectedOnlyUITests(unittest.TestCase):
    def test_equipped_can_still_confirm_owned_but_is_not_shown_as_a_status(self):
        example = {
            'equipment_flags': {'shield_hylian': False, 'shield_deku': False},
            'equipped_shield': 'Weapon_Shield_Hylian',
        }
        self.assertTrue(item_appearance(example, 'shield_hylian').owned)
        self.assertNotIn('Equipped', item_hover_text(example, 'shield_hylian'))
        self.assertIn('Not Collected', item_hover_text(example, 'shield_deku'))

    def test_widget_does_not_render_equipment_selected_ring_or_loadout_labels(self):
        source = (HERE/'zelda_summary_widget.py').read_text()
        ast.parse(source)
        self.assertNotIn('if equipped:', source)
        self.assertNotIn('mask_equipped(', source)
        self.assertNotIn('view.equipped', source)
        for heading in ('SWORDS', 'SHIELDS', 'TUNICS'):
            self.assertIn(f"text_rect('{heading}'", source)
        self.assertNotIn('Swords: {sword}', source)
        self.assertNotIn('Shield: {shield}', source)
        self.assertNotIn('Tunics: {tunic}', source)

if __name__ == '__main__':
    unittest.main()
