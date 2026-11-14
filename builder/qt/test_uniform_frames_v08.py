"""Uniform frame geometry in all themes, with only colors changing."""
import unittest
from ui_theme import THEMES, stylesheet_for, theme_labels
from adventure_v08_settings_ui import selected_save_info_qss, resolve_theme_colors
from adventure_panel_model import item_appearance, INVENTORY_GRID_ROLES, EQUIPMENT_ROLES

class UniformFrameV08Tests(unittest.TestCase):
    def test_alphabetical_theme_labels_and_unchanged_ids(self):
        labels = [label for _, label in theme_labels()]
        self.assertEqual(labels, sorted(labels, key=str.casefold))
        self.assertEqual(set(key for key, _, _ in THEMES),
            {'goron','gannon','zelda','purple','blue_crystal','zelda_gold','zelda_pearl'})
        self.assertEqual(dict(theme_labels())['goron'], 'Darunia')
        self.assertEqual(resolve_theme_colors('Darunia'), resolve_theme_colors('Goron'))

    def test_unified_outer_and_save_frames(self):
        for key, label, _ in THEMES:
            css = stylesheet_for(key)
            self.assertIn('border-radius:9px', css)
            self.assertIn('unified low-contrast outline geometry', css)
            self.assertIn('QFrame#profileCardFrame', css)
            save_css = selected_save_info_qss(label)
            self.assertIn('border: 1px solid', save_css)
            self.assertIn('border-radius: 9px', save_css)
            self.assertIn('border: none;', save_css) # header labels unboxed

    def test_ammo_on_items_but_no_fake_bow_or_slingshot(self):
        data = {'arrows': 0, 'deku_seeds': 47, 'deku_sticks': 30,
                'deku_nuts':29, 'inventory_flags': {'slingshot':True, 'bow':False}}
        self.assertEqual(item_appearance(data, 'slingshot').quantity, 47)
        self.assertEqual(item_appearance(data, 'bow').quantity, 0)
        self.assertFalse(item_appearance(data, 'bow').owned)
        self.assertEqual(item_appearance(data, 'deku_sticks').quantity, 30)
        self.assertIn('hammer', INVENTORY_GRID_ROLES)
        self.assertEqual(INVENTORY_GRID_ROLES[21:26], tuple(f'bottle_slot_{i}' for i in range(1,6)))
        self.assertEqual(INVENTORY_GRID_ROLES[26], 'agony_stone')
        self.assertNotIn('hammer', EQUIPMENT_ROLES)

if __name__=='__main__': unittest.main()
