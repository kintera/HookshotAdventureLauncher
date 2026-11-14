"""Headless data and GUI-source contracts for Zelda visual adventure summary."""
import ast
from pathlib import Path
import unittest

from zelda_summary_data import (STONES, MEDALLIONS, heart_state,
                                heart_count_text, rupee_text, rupees_full,
                                tri_flags, visual_from_stats, wallet_visual)

HERE=Path(__file__).parent
GUI=(HERE/'hookshot_qt.py').read_text()
PROFILE=(HERE/'profile_gui.py').read_text()

class ZeldaSaveModelTests(unittest.TestCase):
    def test_two_rows_twenty_hearts(self):
        data=visual_from_stats({'hearts': 11, 'current_hearts': 7.5})
        self.assertEqual([heart_state(data,i)[0] for i in range(20)].count('uncollected'),9)
        self.assertEqual(heart_state(data,7),('owned-filled',0.5))
        self.assertEqual(heart_state(data,8),('owned-empty',0))
        self.assertEqual(heart_state(data,10),('owned-empty',0))
        self.assertEqual(heart_state(data,11),('uncollected',0))
        self.assertEqual(heart_count_text(data),'11')
    def test_unknown_hearts_are_not_drawn_as_filled(self):
        data=visual_from_stats({})
        self.assertEqual(heart_state(data,0),('unknown',0))
        self.assertEqual(heart_count_text(data),'—')
    def test_full_hearts(self):
        data=visual_from_stats({'hearts':20,'current_hearts':20,'double_defense':True})
        self.assertTrue(data.double_defense)
        self.assertEqual(heart_state(data,19),('owned-filled',1.0))
    def test_health_cannot_overflow_heart_total(self):
        data=visual_from_stats({'hearts':4,'current_hearts':12})
        self.assertEqual(heart_state(data,3),('owned-filled',1.0))
        self.assertEqual(heart_state(data,4),('uncollected',0))
    def test_fractional_container(self):
        data=visual_from_stats({'hearts':10.5,'current_hearts':10.25})
        self.assertEqual(heart_count_text(data),'10.5')
        self.assertEqual(heart_state(data,10),('owned-filled',.25))
    def test_wallet_colors_and_capacity(self):
        for wallet, capacity, color in [('Kokiri',200,'green'),('Adult',500,'purple'),('Giant',1000,'gold')]:
            data=visual_from_stats({'wallet':wallet,'wallet_capacity':capacity,'rupees':capacity})
            self.assertEqual(wallet_visual(data),color)
            self.assertTrue(rupees_full(data))
            self.assertEqual(rupee_text(data),f'{capacity}/{capacity}')
    def test_not_full_or_unknown_wallet(self):
        data=visual_from_stats({'rupees':12})
        self.assertFalse(rupees_full(data))
        self.assertEqual(wallet_visual(data),'unknown')
        self.assertEqual(rupee_text(data),'12/—')
    def test_bottle_and_skulltula(self):
        data=visual_from_stats({'bottles':2,'gold_skulltulas':23})
        self.assertEqual((data.bottles,data.skulltulas),(2,23))
    def test_missing_quest_flags_unknown_not_false(self):
        data=visual_from_stats({'spiritual_stones':2,'medallions':5})
        self.assertEqual(data.stones,(None,)*3)
        self.assertEqual(data.medallions,(None,)*6)
    def test_explicit_quest_flags(self):
        data=visual_from_stats({'stone_flags':{'Kokiri Emerald':True,'Goron Ruby':False},
                                'medallion_flags':{'Forest':True,'Light':False}})
        self.assertEqual(data.stones,(True,False,None))
        self.assertEqual(data.medallions[0],True)
        self.assertEqual(data.medallions[-1],False)
    def test_quest_flag_counts_not_guessed(self):
        self.assertEqual(tri_flags(2,STONES),(None,None,None))
        self.assertEqual(len(MEDALLIONS),6)
    def test_invalid_values_not_treated_as_game_data(self):
        d=visual_from_stats({'hearts':-1,'current_hearts':999,'rupees':False,
                             'bottles':99,'gold_skulltulas':200,'double_defense':1})
        self.assertIsNone(d.hearts_max)
        self.assertIsNone(d.rupees)
        self.assertIsNone(d.double_defense)
        self.assertIsNone(d.bottles)
        self.assertIsNone(d.skulltulas)

class UiContracts(unittest.TestCase):
    def test_main_readiness_only_top_right(self):
        self.assertEqual(GUI.count("self.install_tag = tag('Checking installation…')"),1)
        self.assertIn('header.addWidget(self.install_tag)',GUI)
        self.assertNotIn("'04 / 04  ·  '",GUI)
        self.assertNotIn('ADVENTURE INFORMATION · READ ONLY',GUI)
        self.assertNotIn('self.detail_health = QLabel',GUI)
        self.assertNotIn('self.launch_status = QLabel',GUI)
        self.assertIn('self.step_status.setVisible(installing)',GUI)
    def test_three_profiles_each_have_icon_summary(self):
        self.assertIn('for i in range(3)',GUI)
        self.assertIn('ZeldaStatsWidget(compact=True)',GUI)
        self.assertIn('self.launcher_icon_summaries[i].set_stats(',GUI)
        self.assertIn('self.detail_visual = ZeldaStatsWidget(compact=False)',GUI)
        self.assertIn('self.detail_visual.set_stats(stats)',GUI)
    def test_manager_no_read_only_tag_and_icon_view(self):
        self.assertNotIn("suffix = ' · read-only'",PROFILE)
        self.assertIn('visual = ZeldaStatsWidget(compact=True)',PROFILE)
        self.assertIn('visual.setFixedHeight(64)',PROFILE)
        self.assertIn('visual.set_stats(info if from_save',PROFILE)

if __name__=='__main__': unittest.main()
