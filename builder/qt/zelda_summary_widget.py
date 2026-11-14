"""Zelda-style save summary using verified local-game icon cache when ready.

Vector art remains a complete fallback, with no game artwork shipped.
Compact mode fits into the three adventure launcher cards.
"""
from __future__ import annotations

from math import cos, pi, sin

from PyQt6.QtCore import QPointF, QRectF, QSize, Qt
from PyQt6.QtGui import QColor, QBrush, QFont, QPainter, QPainterPath, QPen, QPolygonF, QLinearGradient
from PyQt6.QtWidgets import QWidget, QSizePolicy, QToolTip

try:
    from .zelda_icon_assets import paint_icon, item_sprite
    from .ui_theme import selected_theme
    from .zelda_summary_data import (STONES, MEDALLIONS, heart_state,
                                     rupees_full, rupee_text, visual_from_stats, wallet_visual)
except ImportError:
    from zelda_icon_assets import paint_icon, item_sprite
    from ui_theme import selected_theme
    from zelda_summary_data import (STONES, MEDALLIONS, heart_state,
                                   rupees_full, rupee_text, visual_from_stats, wallet_visual)


# Landscape Adventure Details: Life & Quest on the left, Inventory/Bags on the right.
# The 10 masks fit the left-hand 415-pixel quest column, followed by a
# 7+6 song grid (13 songs, including Scarecrow's Song).
MEDALLIONS_LABEL_Y = 249
MEDALLIONS_ICON_Y = 246
MASKS_LABEL_Y = 275
MASKS_ICON_Y = 300
MASKS_ICON_X = 24
MASKS_ICON_STEP = 40
MASKS_ICON_SIZE = 34

# Fit within the adventure card's horizontal 10px gutters, with more vertical
# space made available by the smaller screenshot header on Steam Deck.
DETAIL_CANVAS_WIDTH = 1120
DETAIL_CANVAS_HEIGHT = 456
INVENTORY_GRID_X = 25
INVENTORY_GRID_STEP = 69
INVENTORY_GRID_Y = 65
INVENTORY_GRID_ROW_STEP = 54
QUEST_PANEL_X_OFFSET = 0
INVENTORY_PANEL_X_OFFSET = 430

# Ammunition badges should leave the underlying item artwork visible.
AMMO_BADGE_ALPHA = 86  # 34% opaque (previously 222 / 87%)

# Seven melodic items + six warp songs. Artwork roles are independent from
# save-decoding evidence and intentionally start unmapped in the manifest.
SONG_ITEMS = (
    ('song_zelda_lullaby', "Zelda's Lullaby"),
    ('song_epona', "Epona's Song"),
    ('song_saria', "Saria's Song"),
    ('song_sun', "Sun's Song"),
    ('song_time', 'Song of Time'),
    ('song_storms', 'Song of Storms'),
    ('song_scarecrow', "Scarecrow's Song"),
    ('song_forest_minuet', 'Minuet of Forest'),
    ('song_fire_bolero', 'Bolero of Fire'),
    ('song_water_serenade', 'Serenade of Water'),
    ('song_shadow_nocturne', 'Nocturne of Shadow'),
    ('song_spirit_requiem', 'Requiem of Spirit'),
    ('song_light_prelude', 'Prelude of Light'),
)
assert len(SONG_ITEMS) == 13


ACCENT = {
    'purple': '#caa0ff', 'zelda': '#6bcf8d', 'zelda_gold': '#dec270',
    'blue_crystal': '#91d8ff', 'gannon': '#f58b97', 'ruby': '#ff67af',
    'zelda_pearl': '#ecb0d2', 'goron': '#ff914b',
}


def c(hexcode: str, alpha=255) -> QColor:
    color = QColor(hexcode)
    color.setAlpha(alpha)
    return color


def draw_heart(p: QPainter, x, y, size, state, fraction, defense):
    path = QPainterPath()
    path.moveTo(x + size * 0.5, y + size * 0.94)
    path.cubicTo(x + size * 0.18, y + size * 0.76, x - size * 0.03, y + size * 0.40,
                 x + size * 0.10, y + size * 0.19)
    path.cubicTo(x + size * 0.22, y - size * 0.02, x + size * 0.42, y + size * 0.04,
                 x + size * 0.50, y + size * 0.20)
    path.cubicTo(x + size * 0.60, y + size * 0.02, x + size * 0.83, y - size * 0.01,
                 x + size * 0.92, y + size * 0.20)
    path.cubicTo(x + size * 1.06, y + size * 0.41, x + size * 0.82, y + size * 0.78,
                 x + size * 0.5, y + size * 0.94)
    p.save()
    # Use the supplied heart artwork for complete/empty icons. Keep the vector
    # path for partially depleted hearts so current health remains accurate.
    sprite = ('heart_full' if fraction >= 1.0 else
              'heart_three_quarters' if fraction >= 0.75 else
              'heart_half' if fraction >= 0.5 else
              'heart_quarter' if fraction >= 0.25 else 'heart_empty')
    if fraction in (0, 0.25, 0.5, 0.75, 1.0):
        sprite_opacity = 0.16 if state in ('uncollected', 'unknown') else 1.0
        if paint_icon(p, sprite, x, y, size, sprite_opacity):
            if defense and state not in ('uncollected', 'unknown'):
                if not paint_icon(p, 'heart_double_defense', x, y, size):
                    white_pen = QPen(c('#ffffff', 248), max(1.0, size * 0.09))
                    p.setPen(white_pen)
                    p.setBrush(Qt.BrushStyle.NoBrush)
                    p.drawPath(path)
            p.restore()
            return
    alpha = 21 if state in ('uncollected', 'unknown') else 190
    p.fillPath(path, c('#9b9ca5' if state in ('uncollected', 'unknown') else '#642729', alpha))
    if fraction > 0:
        p.save()
        p.setClipRect(QRectF(x, y, size * fraction, size + 1))
        p.fillPath(path, c('#ea373d'))
        highlight = QPainterPath()
        highlight.addEllipse(QRectF(x + size*.17, y + size*.11, size*.36, size*.20))
        p.fillPath(highlight, c('#ffd0d3', 145))
        p.restore()
    border = '#ffffff' if defense else ACCENT.get(selected_theme(), '#7edb94')
    pen = QPen(c(border, 240 if defense else (105 if state in ('unknown','uncollected') else 205)))
    pen.setWidthF(max(0.8, size * (0.102 if defense else 0.06)))
    p.setPen(pen)
    p.setBrush(Qt.BrushStyle.NoBrush)
    p.drawPath(path)
    if defense and state not in ('uncollected', 'unknown'):
        # Genuine white game outline, composited over quarter-heart fallback.
        paint_icon(p, 'heart_double_defense', x, y, size)
    p.restore()


def draw_rupee(p: QPainter, x, y, s, tier):
    palette = {'green': ('#2dda7d', '#07582e'), 'purple': ('#a879ff', '#473080'),
               'gold': ('#ffd362', '#8b5515'), 'unknown': ('#8e9a9c', '#424951')}
    light, dark = palette[tier]
    polygon = QPolygonF([QPointF(x+s*.5,y), QPointF(x+s*.85,y+s*.19),
                         QPointF(x+s*.85,y+s*.7), QPointF(x+s*.48,y+s),
                         QPointF(x+s*.12,y+s*.71), QPointF(x+s*.12,y+s*.20)])
    p.setPen(QPen(c('#0b1721', 200), max(0.9, s*.055)))
    p.setBrush(c(light)); p.drawPolygon(polygon)
    p.setPen(Qt.PenStyle.NoPen)
    p.setBrush(c(dark, 128))
    p.drawPolygon(QPolygonF([QPointF(x+s*.5,y), QPointF(x+s*.85,y+s*.19),
                             QPointF(x+s*.85,y+s*.7), QPointF(x+s*.5,y+s*.91)]))
    p.setBrush(c('#ffffff', 85))
    p.drawPolygon(QPolygonF([QPointF(x+s*.2,y+s*.19), QPointF(x+s*.5,y+s*.05),
                             QPointF(x+s*.37,y+s*.32)]))


def draw_wallet(p: QPainter, x, y, s, tier):
    """A pouch silhouette, never a rupee gem, when wallet artwork is missing."""
    palette = {'green': ('#57b56f', '#153b28'), 'purple': ('#9d78db', '#342449'),
               'gold': ('#dfc77b', '#63451e'), 'unknown': ('#8c909d', '#393c48')}
    light, dark = palette[tier]
    pouch = QPainterPath()
    pouch.moveTo(x+s*.22, y+s*.29)
    pouch.lineTo(x+s*.78, y+s*.29)
    pouch.cubicTo(x+s*.88, y+s*.54, x+s*.80, y+s*.88, x+s*.57, y+s*.94)
    pouch.cubicTo(x+s*.32, y+s*.96, x+s*.13, y+s*.80, x+s*.22, y+s*.29)
    p.save()
    p.setPen(QPen(c('#151b23', 230),max(1.0,s*.06)))
    p.setBrush(c(dark))
    p.drawPath(pouch)
    p.setBrush(c(light))
    p.drawRoundedRect(QRectF(x+s*.15,y+s*.16,s*.70,s*.22),s*.08,s*.08)
    p.setPen(QPen(c('#e9ce88',230),max(1.0,s*.055)))
    p.drawLine(QPointF(x+s*.25,y+s*.45),QPointF(x+s*.76,y+s*.45))
    p.restore()


def draw_skulltula(p: QPainter, x, y, s):
    p.save()
    pen=QPen(c('#b98b2d'),max(1.1,s*.09),Qt.PenStyle.SolidLine,Qt.PenCapStyle.RoundCap)
    p.setPen(pen)
    # Real arachnid silhouette: four jointed legs on each side.
    for dx, dy in [(-.30,-.30),(-.40,-.10),(-.40,.11),(-.30,.31),
                   (.30,-.30),(.40,-.10),(.40,.11),(.30,.31)]:
        p.drawLine(QPointF(x+s*.5,y+s*.48),QPointF(x+s*(.5+dx),y+s*(.48+dy)))
    p.setPen(QPen(c('#70470e'),max(.8,s*.045)))
    p.setBrush(c('#e3ad38'))
    p.drawEllipse(QRectF(x+s*.25,y+s*.13,s*.5,s*.7))
    p.setPen(Qt.PenStyle.NoPen)
    p.setBrush(c('#5b3a10'))
    p.drawEllipse(QRectF(x+s*.32,y+s*.29,s*.14,s*.17))
    p.drawEllipse(QRectF(x+s*.54,y+s*.29,s*.14,s*.17))
    p.drawEllipse(QRectF(x+s*.43,y+s*.53,s*.14,s*.13))
    p.restore()


def draw_bottle(p: QPainter, x, y, s):
    p.save()
    body=QPainterPath()
    body.moveTo(x+s*.37,y+s*.08)
    body.lineTo(x+s*.63,y+s*.08)
    body.lineTo(x+s*.63,y+s*.33)
    body.cubicTo(x+s*.89,y+s*.40,x+s*.87,y+s*.91,x+s*.64,y+s*.93)
    body.lineTo(x+s*.36,y+s*.93)
    body.cubicTo(x+s*.13,y+s*.91,x+s*.12,y+s*.40,x+s*.37,y+s*.33)
    body.closeSubpath()
    p.setPen(QPen(c('#9de7df'),max(.9,s*.055)))
    p.setBrush(c('#68a9bd', 70))
    p.drawPath(body)
    p.setPen(Qt.PenStyle.NoPen)
    p.setBrush(c('#d5ae65'))
    p.drawRect(QRectF(x+s*.33,y+s*.06,s*.34,s*.18))
    p.setBrush(c('#d9ffff', 120))
    p.drawEllipse(QRectF(x+s*.33,y+s*.48,s*.13,s*.29))
    p.restore()


def draw_scrub_seed(p: QPainter, x, y, s):
    """Distinct seed silhouette until a user-verified game texture is mapped."""
    p.save()
    p.setRenderHint(QPainter.RenderHint.Antialiasing, True)
    p.translate(x, y)
    p.setPen(QPen(c('#9f6a34'), max(1, s*.05)))
    p.setBrush(c('#d6a453'))
    p.drawEllipse(QRectF(s*.17, s*.24, s*.66, s*.67))
    p.setPen(QPen(c('#4a8e35'), max(1, s*.06)))
    p.setBrush(c('#78bd50'))
    p.drawEllipse(QRectF(s*.50, s*.05, s*.35, s*.22))
    p.drawLine(QPointF(s*.51, s*.28), QPointF(s*.47, s*.12))
    p.restore()



def draw_deku_supply(p: QPainter, role: str, x: float, y: float, s: float):
    """Distinct, non-game-texture fallback for the 3 locally unmapped supplies."""
    p.save()
    p.setRenderHint(QPainter.RenderHint.Antialiasing, True)
    p.setPen(QPen(c('#c5965b'), max(1.0, s * .055)))
    if role == 'deku_sticks':
        p.setPen(QPen(c('#ae7845'), max(2.3, s*.18),
                      Qt.PenStyle.SolidLine, Qt.PenCapStyle.RoundCap))
        p.drawLine(QPointF(x+s*.24, y+s*.83), QPointF(x+s*.69, y+s*.16))
        p.setBrush(c('#7caf58'))
        p.setPen(Qt.PenStyle.NoPen)
        p.drawEllipse(QRectF(x+s*.60,y+s*.08,s*.24,s*.16))
    elif role == 'deku_nuts':
        p.setBrush(c('#b78342'))
        p.drawEllipse(QRectF(x+s*.16,y+s*.27,s*.70,s*.63))
        p.setPen(QPen(c('#7a512f'),max(1.0,s*.07)))
        p.drawArc(QRectF(x+s*.25,y+s*.33,s*.52,s*.48), 20*16, 100*16)
        p.setBrush(c('#79b850'))
        p.setPen(Qt.PenStyle.NoPen)
        p.drawEllipse(QRectF(x+s*.43,y+s*.08,s*.28,s*.20))
    else:  # deku_seeds, which also feed the slingshot ammunition counter
        for offset in (0, .23, .46):
            p.setBrush(c('#c39c55'))
            p.drawEllipse(QRectF(x+s*(.17+offset),y+s*(.33-offset*.22),s*.24,s*.37))
    p.restore()

def draw_gem(p: QPainter, x, y, s, color, owned):
    p.save()
    p.setOpacity(1.0 if owned is True else (.48 if owned is None else .16))
    polygon = QPolygonF([QPointF(x+s*.5,y),QPointF(x+s*.95,y+s*.42),
                         QPointF(x+s*.68,y+s*.95),QPointF(x+s*.31,y+s*.95),
                         QPointF(x+s*.05,y+s*.42)])
    p.setPen(QPen(c(color), max(1,s*.075)))
    p.setBrush(c(color, 185))
    p.drawPolygon(polygon)
    p.setPen(Qt.PenStyle.NoPen)
    p.setBrush(c('#ffffff',75))
    p.drawPolygon(QPolygonF([QPointF(x+s*.5,y+s*.08),QPointF(x+s*.86,y+s*.40),
                             QPointF(x+s*.5,y+s*.45),QPointF(x+s*.18,y+s*.40)]))
    p.restore()


def draw_medallion(p: QPainter, x, y, s, color, owned, symbol):
    p.save()
    p.setOpacity(1.0 if owned is True else (.48 if owned is None else .16))
    p.setPen(QPen(c('#f5d785'),max(.9,s*.08)))
    p.setBrush(c(color, 200))
    p.drawEllipse(QRectF(x+s*.05,y+s*.05,s*.9,s*.9))
    p.setPen(QPen(c('#f8e7a8'),max(.8,s*.038)))
    p.setBrush(Qt.BrushStyle.NoBrush)
    p.drawEllipse(QRectF(x+s*.21,y+s*.21,s*.58,s*.58))
    p.setPen(c('#ffffff',240))
    f=QFont('Sans Serif',max(6,int(s*.42)),QFont.Weight.Bold)
    p.setFont(f)
    p.drawText(QRectF(x,y+s*.03,s,s*.92),Qt.AlignmentFlag.AlignCenter,symbol)
    p.restore()


def wallet_asset_role(data):
    """Use the wallet icon corresponding to the verified/decoded capacity tier.

    A new adventure starts with the Child's Wallet; don't draw a
    substitute vector pouch when a fresh save has no decoded wallet data.
    """
    return {'Kokiri': 'wallet_child', 'Adult': 'wallet_adult',
            'Giant': 'wallet_giant'}.get(data.wallet_tier, 'wallet_child')


def outline_count(p: QPainter, text, x, y, size, defense, accent):
    # Text is drawn with a soft thematic outline, or a bold white Double Defense outline.
    font=QFont('Sans Serif',size,QFont.Weight.Black)
    path=QPainterPath()
    path.addText(QPointF(x,y),font,text)
    if defense:
        pen=QPen(c('#ffffff',250),2.9)
        fill=c('#d04c56')
    else:
        pen=QPen(c(accent,185),2.4)
        fill=c('#fff2db')
    p.setPen(pen)
    p.setBrush(fill)
    p.drawPath(path)


def paint_magic_upgrade(p: QPainter, level: int | None, x: float, y: float,
                        width: float, height: float, edge: str):
    """One two-cell capacity indicator, consistent in compact/expanded views.

    Level 0 = neither segment; level 1 = left; level 2 = both.
    Unknown = no green with a muted outline. Current magic depletion does not
    change the segments. The center divider is always visible.
    """
    if type(level) is not int or level not in (0, 1, 2):
        level = None
    p.save()
    border_color = c(edge, 210)
    meter = QRectF(x, y, width, height)
    p.setPen(QPen(border_color, 1))
    p.setBrush(c('#0c151d', 150))
    p.drawRoundedRect(meter, min(4, height*.25), min(4, height*.25))
    half = width / 2
    for segment in range(2):
        inset = max(1.5, height*.14)
        rect = QRectF(x+segment*half+inset, y+inset,
                      half-2*inset, height-2*inset)
        if level is not None and level > segment:
            grad = QLinearGradient(rect.topLeft(), rect.bottomLeft())
            grad.setColorAt(0, c('#92f9a0'))
            grad.setColorAt(1, c('#249c54'))
            p.setPen(Qt.PenStyle.NoPen)
            p.setBrush(QBrush(grad))
            p.drawRoundedRect(rect, min(2, rect.height()*.25), min(2, rect.height()*.25))
    p.setPen(QPen(border_color, 1))
    p.drawLine(QPointF(x+half, y), QPointF(x+half, y+height))
    p.restore()


class ZeldaStatsWidget(QWidget):
    def __init__(self, compact=False, parent=None):
        super().__init__(parent)
        self.compact = bool(compact)
        self.stats = visual_from_stats(None)
        self.raw_stats = {}
        self._hotspots = []
        self._hover_text = None
        self._canvas_scale = 1.0
        self._canvas_origin = (0.0, 0.0)
        self.setMouseTracking(not self.compact)
        self.setMinimumHeight(58 if compact else 182)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed if compact else QSizePolicy.Policy.Expanding)
        # Bottle contents are visible in expanded view, never hover-only.
        # Keep the legacy short hint on compact profile strips only.
        self.setToolTip('Quest icons are bright when verified; unknown icons remain faded.' if compact else '')

    def sizeHint(self):
        return QSize(690,58) if self.compact else QSize(DETAIL_CANVAS_WIDTH, DETAIL_CANVAS_HEIGHT)

    def set_stats(self, stats):
        self.raw_stats = dict(stats) if isinstance(stats, dict) else {}
        self.stats = visual_from_stats(stats)
        self._hover_text = None
        QToolTip.hideText()
        # The expanded panel paints each bottle's actual content and readable
        # caption; do not require a tooltip to discover bottle contents.
        self.update()

    def refresh_hover_theme(self):
        """Discard stale hover state after an app-wide theme change.

        Qt creates tooltips as popup windows; hiding the previous one ensures
        the next hover is painted with the active theme immediately.
        """
        self._hover_text = None
        QToolTip.hideText()
        self.update()

    def paintEvent(self, event):
        p=QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing, True)
        p.setRenderHint(QPainter.RenderHint.TextAntialiasing, True)
        accent=ACCENT.get(selected_theme(),'#90d49a')
        if self.compact:
            self._draw_compact(p,accent)
        else:
            self._hotspots = []
            self._draw_expanded(p,accent)
        p.end()

    def _register_hover(self, painter, rect, description, accent):
        """Register an icon's *actual scaled* hitbox on the painter-only canvas.

        This is not a child button: game save values remain strictly read-only.
        """
        # Current painter transform includes logical-canvas scaling AND any
        # per-column translation (inventory panel lives to the right).
        mapped = painter.worldTransform().mapRect(rect)
        self._hotspots.append((mapped, description))
        if self._hover_text == description:
            painter.save()
            glow = QColor(accent)
            glow.setAlpha(215)
            painter.setPen(QPen(glow, 1.5))
            painter.setBrush(Qt.BrushStyle.NoBrush)
            painter.drawRoundedRect(rect.adjusted(-2, -2, 2, 2), 5, 5)
            painter.restore()

    def mouseMoveEvent(self, event):
        if self.compact:
            return super().mouseMoveEvent(event)
        hit = next((label for rect, label in reversed(self._hotspots)
                    if rect.contains(event.position())), None)
        if hit != self._hover_text:
            self._hover_text = hit
            if hit:
                QToolTip.showText(event.globalPosition().toPoint(), hit, self)
            else:
                QToolTip.hideText()
            self.update()
        super().mouseMoveEvent(event)

    def leaveEvent(self, event):
        if self._hover_text:
            self._hover_text = None
            QToolTip.hideText()
            self.update()
        super().leaveEvent(event)

    def _item(self, painter, name, x, y, size, fallback, opacity=1.0):
        if not paint_icon(painter, name, x, y, size, opacity):
            fallback()

    def _draw_compact(self,p,accent):
        d=self.stats
        # Never stretch the 690-unit icon strip across a maximized window.
        # Uniform scaling keeps heart, wallet and quest artwork proportional.
        width = min(690, max(1, self.width() - 8))
        scale = width / 690.0
        p.save()
        p.translate(max(0, (self.width() - width) / 2.0), 0)
        p.scale(scale, scale)
        for i in range(20):
            state,fraction=heart_state(d,i)
            draw_heart(p,7+(i%10)*14,6+(i//10)*20,12,state,fraction,d.double_defense is True)
        # Profile-strip magic indicator is immediately below the heart rows.
        p.setFont(QFont('Sans Serif',7,QFont.Weight.DemiBold))
        p.setPen(c('#c7c0d6'))
        p.drawText(QRectF(7,43,35,10),Qt.AlignmentFlag.AlignVCenter,'MAGIC')
        paint_magic_upgrade(p,d.magic_level,45,44,103,8,accent)
        tier=wallet_visual(d)
        self._item(p, wallet_asset_role(d), 170,14,23,
                   lambda:draw_wallet(p,170,14,23,tier))
        p.setFont(QFont('Sans Serif',11,QFont.Weight.Bold))
        p.setPen(c('#6be589' if rupees_full(d) else '#f5ebdf'))
        p.drawText(QRectF(199,9,89,32),Qt.AlignmentFlag.AlignVCenter,rupee_text(d))
        # Gold Skulltulas occupy the former bottle-count position. Bottle
        # contents are available only in the selected-adventure detail page.
        self._item(p,'gold_skulltula',371,12,27,lambda:draw_skulltula(p,371,12,27))
        p.setPen(c('#f5ebdf'))
        p.drawText(QRectF(403,10,38,31),Qt.AlignmentFlag.AlignVCenter,str(d.skulltulas) if d.skulltulas is not None else '—')
        for i,(name,color) in enumerate(zip(('kokiri','goron','zora'),('#27c27a','#d95b6b','#64a8ff'))):
            x=451+i*25
            self._item(p,'stone_'+name,x,16,21,
                       lambda x=x,color=color,i=i:draw_gem(p,x,16,21,color,d.stones[i]),
                       1 if d.stones[i] is True else (.48 if d.stones[i] is None else .16))
        for i,(name,color,symbol) in enumerate(zip(
            ('forest','fire','water','spirit','shadow','light'),
            ('#36a85e','#e35b3e','#549ce5','#c48b47','#875ab5','#e8cf55'),
            ('F','F','W','S','S','L'))):
            x=538+i*25
            self._item(p,'medallion_'+name,x,16,20,
                       lambda x=x,color=color,i=i,symbol=symbol:draw_medallion(p,x,16,20,color,d.medallions[i],symbol),
                       1 if d.medallions[i] is True else (.48 if d.medallions[i] is None else .16))
        p.restore()

    def _draw_equipment(self,p,x,y,s,kind,item):
        sprite=item_sprite(item) or ('equipment_sword' if kind=='sword' else 'equipment_shield')
        if paint_icon(p,sprite,x,y,s):
            return
        p.save()
        p.setRenderHint(QPainter.RenderHint.Antialiasing,True)
        if kind=='sword':
            p.setPen(QPen(c('#d4e3ff'),max(2,s*.12),Qt.PenStyle.SolidLine,Qt.PenCapStyle.RoundCap))
            p.drawLine(QPointF(x+s*.2,y+s*.83),QPointF(x+s*.77,y+s*.17))
            p.setPen(QPen(c('#e7bf66'),max(2,s*.14),Qt.PenStyle.SolidLine,Qt.PenCapStyle.RoundCap))
            p.drawLine(QPointF(x+s*.18,y+s*.54),QPointF(x+s*.49,y+s*.8))
        else:
            polygon=QPolygonF([QPointF(x+s*.15,y+s*.15),QPointF(x+s*.85,y+s*.15),
                                QPointF(x+s*.78,y+s*.7),QPointF(x+s*.5,y+s*.9),
                                QPointF(x+s*.22,y+s*.7)])
            p.setPen(QPen(c('#a2c4ff'),2))
            p.setBrush(c('#5479b8',170))
            p.drawPolygon(polygon)
        p.restore()

    def _draw_expanded(self, p, accent):
        """SoH-inspired single-panel, two-column quest / inventory composition.

        Coordinates form a 1120×456 logical canvas. Only the verified read-only
        statistics are highlighted; unverified quest and inventory flags stay
        translucent even when their authentic local-game icons are available.
        """
        try:
            from .adventure_panel_model import (INVENTORY_GRID_ROLES, INVENTORY_COLUMNS, EQUIPMENT_ROLES,
                OTHER_GEAR_ROLES, BAG_UPGRADES, bag_upgrade, bag_hover_text,
                item_hover_text, bottle_hover_text, item_appearance, bottle_appearance, bottle_caption,
                latest_trading_item, trading_quest_overview_hover_text,
                gerudo_pass_hover_text)
            from .adventure_v08_settings_ui import resolve_theme_colors
        except ImportError:
            from adventure_panel_model import (INVENTORY_GRID_ROLES, INVENTORY_COLUMNS, EQUIPMENT_ROLES,
                OTHER_GEAR_ROLES, BAG_UPGRADES, bag_upgrade, bag_hover_text,
                item_hover_text, bottle_hover_text, item_appearance, bottle_appearance, bottle_caption,
                latest_trading_item, trading_quest_overview_hover_text,
                gerudo_pass_hover_text)
            from adventure_v08_settings_ui import resolve_theme_colors
        from PyQt6.QtGui import QLinearGradient
        d = self.stats
        raw = getattr(self, 'raw_stats', {})
        theme_label = {'purple': 'royal', 'zelda': 'link',
                       'zelda_gold': 'tri force', 'blue_crystal': 'ruto',
                       'gannon': 'gannon', 'zelda_pearl': 'zelda',
                       'goron': 'darunia'}.get(selected_theme(), 'royal')
        palette = resolve_theme_colors(theme_label)
        pearl = False  # Zelda now uses deep royal-violet panels, not pale ivory ones
        tint, back, border = QColor(palette.accent), QColor(palette.fill), QColor(palette.border)
        scale = max(.1, min((self.width()-8)/DETAIL_CANVAS_WIDTH,
                              (self.height()-8)/DETAIL_CANVAS_HEIGHT))
        origin = ((self.width()-DETAIL_CANVAS_WIDTH*scale)/2,
                  (self.height()-DETAIL_CANVAS_HEIGHT*scale)/2)
        self._canvas_scale = scale
        self._canvas_origin = origin
        p.save()
        p.translate(*origin)
        p.scale(scale, scale)

        def text_rect(value, x, y, w, h, size=12, weight=QFont.Weight.DemiBold,
                      ink='#eee9e2', align=Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter):
            if pearl and isinstance(ink, str):
                # Readable dark-plum labels on ivory/magenta Zelda surfaces.
                ink = {'#eee9e2':'#493047','#e9ddcc':'#62495c',
                       '#c7c0d6':'#80566f','#fff0d4':'#493c45',
                       '#86ed95':'#26755b','#ffffff':'#6b405b',
                       '#ffe4a0':'#8d5f2d','#d0c3ab':'#7a5666'}.get(ink, ink)
            font = QFont('DejaVu Sans', 10)
            font.setPixelSize(size)
            font.setWeight(weight)
            p.setFont(font)
            p.setPen(c(ink) if isinstance(ink, str) else ink)
            p.drawText(QRectF(x, y, w, h), align, str(value))

        def framed(rect, shade=0):
            """Sunken metal/glass tray: dark top cavity, illuminated low lip."""
            grad = QLinearGradient(rect.left(), rect.top(), rect.left(), rect.bottom())
            top = QColor(back).darker(165 if shade else 148)
            middle = QColor(back).darker(122 if shade else 110)
            bottom = QColor(back).lighter(116 if shade else 124)
            grad.setColorAt(0.0, top)
            grad.setColorAt(0.55, middle)
            grad.setColorAt(1.0, bottom)
            p.setBrush(QBrush(grad))
            p.setPen(QPen(QColor(border).darker(150), 1.1))
            p.drawRoundedRect(rect, 9, 9)
            # The top/left black inset and lower highlight visually sink the
            # item well into the raised selected-adventure card.
            p.setBrush(Qt.BrushStyle.NoBrush)
            p.setPen(QPen(c('#08090e', 150 if shade else 110), 2.0))
            p.drawLine(QPointF(rect.left()+11, rect.top()+3),
                       QPointF(rect.right()-11, rect.top()+3))
            p.drawLine(QPointF(rect.left()+3, rect.top()+11),
                       QPointF(rect.left()+3, rect.bottom()-11))
            rim = QColor(tint)
            rim.setAlpha(115 if shade else 95)
            p.setPen(QPen(rim, 1.1))
            p.drawLine(QPointF(rect.left()+11, rect.bottom()-3),
                       QPointF(rect.right()-11, rect.bottom()-3))
            p.drawLine(QPointF(rect.right()-3, rect.top()+11),
                       QPointF(rect.right()-3, rect.bottom()-11))

        def numeric_badge(x, y, size, value, ink):
            """Shared lower-right number badge for ammo and bag *capacity*.

            Use the subdued ammo background so the game icon remains visible.
            Capacity is the decoded max for an acquired bag, never stock on hand.
            """
            label = str(value)
            badge_width = 8 + len(label) * 7
            badge_width = min(size - 4, badge_width)
            badge = QRectF(x+size-badge_width-2, y+size-17,
                           badge_width, 15)
            p.setPen(Qt.PenStyle.NoPen)
            p.setBrush(c('#10101d', AMMO_BADGE_ALPHA))
            p.drawRoundedRect(badge, 3, 3)
            text_rect(label, badge.x()+2, badge.y(), badge.width()-4, 15,
                      10 if len(label) < 3 else 9,
                      QFont.Weight.Bold, ink,
                      Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)

        def icon_tile(role, x, y, size=41, owned=None,
                      quantity=None, hover=None, available=False):
            shadow = QRectF(x-3, y-3, size+6, size+6)
            p.setBrush(c('#020a14', 73))
            p.setPen(QPen(c(palette.border, 125), 1))
            p.drawRoundedRect(shadow, 6, 6)
            # Missing and confirmed-absent ownership look equally uncollected
            # in the Adventure UI; decoder diagnostics still retain None.
            opacity = 1.0 if owned is True or available else .13
            # A single failed or missing PNG uses a vector slot glyph; other
            # cached game icons continue to render normally.
            if not paint_icon(p, role, x, y, size, opacity):
                p.save()
                p.setOpacity(opacity)
                if role.startswith(('sword_', 'shield_')):
                    self._draw_equipment(p, x, y, size, 'sword' if role.startswith('sword_') else 'shield', role)
                elif role == 'business_scrub_seed':
                    draw_scrub_seed(p, x, y, size)
                elif role in ('deku_sticks', 'deku_nuts', 'deku_seeds'):
                    draw_deku_supply(p, role, x, y, size)
                elif role.startswith('bottle'):
                    draw_bottle(p, x, y, size)
                elif role.startswith(('stone_', 'medallion_')):
                    draw_gem(p, x, y, size, palette.accent, owned)
                else:
                    # Generic vector inventory rune; no copyrighted art bundled.
                    p.setPen(QPen(c(palette.accent, 240), max(1.0,size*.055)))
                    p.setBrush(c('#7aa4c4', 80))
                    p.drawEllipse(QRectF(x+size*.14,y+size*.14,size*.72,size*.72))
                    p.drawLine(QPointF(x+size*.30,y+size*.50),QPointF(x+size*.70,y+size*.50))
                    p.drawLine(QPointF(x+size*.50,y+size*.30),QPointF(x+size*.50,y+size*.70))
                p.restore()
            if quantity is not None:
                # Current ammo/consumable STOCK, in the familiar amber badge.
                numeric_badge(x, y, size, quantity, '#ffe4a0')
            if hover:
                self._register_hover(p, QRectF(x-3, y-3, size+6, size+6),
                                     hover, palette.accent)

        framed(QRectF(1, 1, DETAIL_CANVAS_WIDTH-2, DETAIL_CANVAS_HEIGHT-2))
        framed(QRectF(13, 12, 415, 430), 1)   # Life & Quest, back on the left
        framed(QRectF(443, 12, 665, 430), 1)  # Wide Inventory/Bags on the right
        title_ink = c('#8b416e') if pearl else tint.lighter(150)
        p.save()
        p.translate(QUEST_PANEL_X_OFFSET, 0)
        text_rect('LIFE & QUEST', 24, 18, 190, 22, 15,
                  QFont.Weight.Bold, title_ink)
        current = f'{d.hearts_current:g}' if d.hearts_current is not None else '—'
        total = f'{d.hearts_max:g}' if d.hearts_max is not None else '—'
        text_rect(f'{current} / {total} hearts', 226, 21, 186, 18, 12,
                  QFont.Weight.DemiBold, '#eee9e2', Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        for i in range(20):
            state, fraction = heart_state(d, i)
            draw_heart(p, 25 + (i % 10)*38, 45 + (i // 10)*36, 29,
                       state, fraction, d.double_defense is True)
        # The same upgrade bar is drawn in compact and expanded views.  Both
        # represent acquired maximum MP capacity, not the current MP amount.
        text_rect('MAGIC', 25, 115, 57, 17, 10, QFont.Weight.Bold, '#c7c0d6')
        paint_magic_upgrade(p, d.magic_level, 89, 117, 233, 12, palette.border)
        if d.double_defense is True:
            text_rect('DOUBLE DEFENSE', 325, 115, 96, 17, 9,
                      QFont.Weight.Bold, '#ffffff',
                      Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        text_rect('WALLET / RUPEES', 25, 137, 165, 18, 10, ink='#c7c0d6')
        text_rect('SKULLTULAS', 294, 137, 125, 18, 10, ink='#c7c0d6')
        self._item(p, wallet_asset_role(d), 27, 159, 36,
                   lambda: draw_wallet(p, 27, 159, 36, wallet_visual(d)))
        wallet_name = {'Kokiri': 'Kokiri Wallet', 'Adult': 'Adult Wallet',
                       'Giant': "Giant's Wallet"}.get(d.wallet_tier, "Child's Wallet (default)")
        self._register_hover(p, QRectF(24, 156, 42, 42), wallet_name, palette.accent)
        text_rect(rupee_text(d), 70, 163, 128, 31, 17, QFont.Weight.Bold,
                  '#86ed95' if rupees_full(d) else '#fff0d4')
        self._item(p, 'gold_skulltula', 296, 159, 36,
                   lambda: draw_skulltula(p, 296, 159, 36))
        text_rect(str(d.skulltulas) if d.skulltulas is not None else '—',
                  337, 163, 65, 31, 17, QFont.Weight.Bold)
        text_rect('SPIRITUAL STONES', 25, 208, 235, 19, 11, ink='#c7c0d6')
        for i, name in enumerate(('kokiri','goron','zora')):
            x=259+i*49
            flag=d.stones[i]
            self._item(p,'stone_'+name,x,203,33,
                       lambda x=x,flag=flag:draw_gem(p,x,203,33,palette.accent,flag),
                       1 if flag is True else (.48 if flag is None else .16))
            self._register_hover(p, QRectF(x-2, 201, 37, 37),
                                 STONES[i], palette.accent)
        text_rect('MEDALLIONS', 25, MEDALLIONS_LABEL_Y, 160, 18, 11, ink='#c7c0d6')
        for i, (name,color,symbol) in enumerate(zip(
                ('forest','fire','water','spirit','shadow','light'),
                ('#36a85e','#e35b3e','#549ce5','#c48b47','#875ab5','#e8cf55'),
                ('F','F','W','S','S','L'))):
            x=156+i*43
            flag=d.medallions[i]
            self._item(p,'medallion_'+name,x,MEDALLIONS_ICON_Y,32,
                       lambda x=x,color=color,i=i,symbol=symbol:draw_medallion(p,x,MEDALLIONS_ICON_Y,32,color,d.medallions[i],symbol),
                       1 if flag is True else (.48 if flag is None else .16))
            self._register_hover(p, QRectF(x-2, MEDALLIONS_ICON_Y-2, 36, 36),
                                 MEDALLIONS[i]+' Medallion', palette.accent)
        # Ten larger masks use the full available width without overlapping
        # tile frames. The heading and row sit higher, with a bottom gutter
        # inside the inner rail. Ownership still controls opacity.
        text_rect('MASKS', 25, MASKS_LABEL_Y, 72, 17, 10, ink='#c7c0d6')
        try:
            from .adventure_panel_model import MASK_ROLES as mask_roles
        except ImportError:
            from adventure_panel_model import MASK_ROLES as mask_roles
        mask_available = raw.get('mask_available_flags', {})
        for i, role in enumerate(mask_roles):
            view = item_appearance(raw, role)
            unlocked = (isinstance(mask_available, dict)
                        and mask_available.get(role) is True)
            icon_tile(view.role, MASKS_ICON_X+i*MASKS_ICON_STEP,
                      MASKS_ICON_Y, MASKS_ICON_SIZE, view.owned,
                      hover=item_hover_text(raw, role), available=unlocked)
        # Ocarina songs: 7 across the top (six common melodies + Scarecrow),
        # six warp melodies below. This game has 13, not 14, songs.
        text_rect('SONGS', 25, 350, 130, 18, 11, ink='#c7c0d6')
        for index, (role, name) in enumerate(SONG_ITEMS):
            row = 0 if index < 7 else 1
            col = index if row == 0 else index - 7
            x = (35 if row == 0 else 60) + col * 50
            y = 371 + row * 37
            # Direct IsGet fields and positive acquisition-event candidates
            # come from the read-only decoder. Never infer story progression.
            flag = raw.get('song_flags', {}).get(role) if isinstance(raw.get('song_flags'), dict) else None
            icon_tile(role, x, y, 27, flag,
                      hover=f'{name}\n' + ('Collected' if flag is True else 'Not Collected'))
        p.restore()
        # Keep the logical inventory coordinates unchanged. Translate the
        # entire rail (including icon hitboxes and the Bags column) as a unit.
        p.save()
        p.translate(INVENTORY_PANEL_X_OFFSET, 0)
        # Small raised-header well above Inventory, without an extra outer frame.
        framed(QRectF(24, 19, 470, 29), 1)
        text_rect('INVENTORY', 35, 22, 190, 22, 15,
                  QFont.Weight.Bold, title_ink)
        # Exact seven-column upgrade-aware layout: three equipment/item rows,
        # then five individual bottles, Echo Gem and strongest strength tier.
        # Ordinary arrows and Deku Seeds are counts on Bow and Slingshot.
        for i, role in enumerate(INVENTORY_GRID_ROLES):
            x = INVENTORY_GRID_X+(i % INVENTORY_COLUMNS)*INVENTORY_GRID_STEP
            y = INVENTORY_GRID_Y+(i // INVENTORY_COLUMNS)*INVENTORY_GRID_ROW_STEP
            if role.startswith('bottle_slot_'):
                number = int(role.rsplit('_', 1)[1])
                bottle = bottle_appearance(raw, number)
                owned = bottle.get('owned')
                bottle_icon = bottle.get('role') if owned is True else 'bottle_empty'
                # Unknown contents use a bottle glyph without claiming an enum.
                icon_tile(bottle_icon or 'bottle_empty', x, y, 32, owned,
                          hover=bottle_hover_text(raw, number))
                # Permanent bottle-content caption, centered in this column.
                # Contents stay visibly labeled without requiring a hover.
                caption = bottle_caption(bottle)
                caption_ink = '#e9ddcc' if owned is True else '#c7c0d6'
                text_rect(caption, x-20, y+37, 72, 12, 9,
                          QFont.Weight.DemiBold, caption_ink,
                          Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignVCenter)
            else:
                view=item_appearance(raw,role)
                # Progressive layout roles resolve to authentic manifest keys.
                # Never ask the icon cache for a synthetic progressive_* name.
                icon_tile(view.role,x,y,32,view.owned,quantity=view.quantity,
                          hover=item_hover_text(raw, role))
                # Show quantities on the icon rather than a separate caption.
                # No extra text tags for Business Scrub Seed or Echo Gem;
                # their verified artwork identifies the item without clutter.
        # Display equipment categories and acquisition only. Do not expose
        # the current child/adult loadout: owning an item is not the same as
        # having it equipped in the active era.
        text_rect('SWORDS', 25, 282, 265, 17, 11, ink='#e9ddcc')
        text_rect('SHIELDS', 310, 282, 200, 17, 11, ink='#e9ddcc')
        # All equipment and related quest slots share one 69px column grid.
        # Reuse its coordinates instead of placing Pass/Trading by eye.
        def equipment_x(column):
            return INVENTORY_GRID_X + column * INVENTORY_GRID_STEP

        equip_col = (0, 1, 2, 4, 5, 6)
        for role, col in zip(EQUIPMENT_ROLES, equip_col):
            view = item_appearance(raw, role)
            icon_tile(view.role, equipment_x(col), 305, 36, view.owned,
                      hover=item_hover_text(raw, role))
        # Keep the tunic heading out of the Pass label's layout region.
        text_rect('TUNICS', 25, 361, 260, 16, 11, ink='#e9ddcc')
        for col, role in enumerate(OTHER_GEAR_ROLES):
            view = item_appearance(raw, role)
            icon_tile(view.role, equipment_x(col), 385, 35, view.owned,
                      hover=item_hover_text(raw, role))
        # Two compact quest slots below the shield row. Align the Pass icon
        # exactly under the Deku Shield (column 4), and the Trading icon
        # under the Mirror Shield (column 6). Their labels share the same
        # centerlines. No Child/Adult breakdown appears in the main panel.
        quest_icon_size = 36
        pass_x = equipment_x(4)      # Deku Shield column
        trading_x = equipment_x(6)   # Mirror Shield column
        pass_label_width = 90
        # Stay left of the Bags divider at x=510 while centered on the
        # Mirror Shield; 10px bold text fits within this 100px label.
        trading_label_width = 100
        pass_flag = raw.get('inventory_flags', {}).get('gerudo_pass') is True
        text_rect('PASS', pass_x + (quest_icon_size - pass_label_width) / 2,
                  363, pass_label_width, 16, 10,
                  QFont.Weight.Bold, '#c7c0d6',
                  Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignVCenter)
        icon_tile('gerudo_pass', pass_x, 385, quest_icon_size, pass_flag,
                  hover=gerudo_pass_hover_text(raw))

        trade = latest_trading_item(raw)
        text_rect('TRADING QUEST', trading_x + (quest_icon_size - trading_label_width) / 2,
                  363, trading_label_width, 16, 10,
                  QFont.Weight.Bold, '#c7c0d6',
                  Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignVCenter)
        # The first child item is a subdued empty-state silhouette until a
        # saved held item or the explicit Mask of Truth stock is confirmed.
        # Prior traded-away stages are never guessed as "collected".
        icon_tile(trade.role or 'trade_weird_egg', trading_x, 385, quest_icon_size,
                  trade.role is not None,
                  hover=trading_quest_overview_hover_text(raw))
        # Five distinct capacity fields. Deku-Seed Bag is also known as the
        # Bullet Bag; do not present an invented sixth independently owned bag.
        p.setPen(QPen(c(palette.border, 105), 1))
        p.drawLine(QPointF(510, 61), QPointF(510, 421))
        text_rect('BAGS', 523, 24, 126, 20, 14,
                  QFont.Weight.Bold, title_ink)

        def bag_glyph(x, y, size, bag):
            opacity = 1.0 if bag.tier and bag.tier > 0 else (.48 if bag.tier is None else .13)
            p.save()
            p.setOpacity(opacity)
            # Unmapped roles deliberately use an elegant procedural pouch,
            # not an unverified game texture or an invented texture filename.
            if bag.icon_role and paint_icon(p, bag.icon_role, x, y, size, opacity):
                p.restore()
                return
            p.setPen(QPen(c(palette.border, 220), 1.5))
            pouch = QPainterPath()
            pouch.moveTo(x + size*.16, y + size*.27)
            pouch.lineTo(x + size*.84, y + size*.27)
            pouch.lineTo(x + size*.89, y + size*.66)
            pouch.quadTo(x + size*.50, y + size*1.00, x + size*.11, y + size*.66)
            pouch.closeSubpath()
            face = QLinearGradient(x, y, x, y+size)
            face.setColorAt(0, QColor(palette.accent).lighter(118))
            face.setColorAt(1, QColor(palette.fill).darker(115))
            p.setBrush(QBrush(face))
            p.drawPath(pouch)
            p.setPen(QPen(c('#eeddbf', 190), 1.4))
            p.drawLine(QPointF(x + size*.19, y + size*.31),
                       QPointF(x + size*.81, y + size*.31))
            if bag.tier:
                # Three small upgrade markers identify the verified tier.
                p.setPen(Qt.PenStyle.NoPen)
                for dot in range(3):
                    p.setBrush(c('#ffda84' if dot < bag.tier else '#2a2634', 240))
                    p.drawEllipse(QRectF(x+size*.28+dot*size*.16,
                                         y+size*.54, size*.10, size*.10))
            p.restore()

        for i, (bag_key, _, _, _, _) in enumerate(BAG_UPGRADES):
            upgrade = bag_upgrade(raw, bag_key)
            y = 70 + i*68
            bag_glyph(525, y, 35, upgrade)
            # Green number on the icon is the acquired MAX bag capacity, not
            # the current number of sticks / nuts / seeds / arrows / bombs.
            # No badge for a missing bag or undecodable capacity.
            if upgrade.tier is not None and upgrade.tier > 0 and upgrade.capacity is not None:
                numeric_badge(525, y, 35, upgrade.capacity, '#80f5a0')
            short = {'sticks':'Sticks', 'nuts':'Nuts', 'seeds':'Seed / Bullet',
                     'quiver':'Quiver', 'bombs':'Bomb Bag'}[bag_key]
            text_rect(short, 567, y+1, 96, 16, 11,
                      QFont.Weight.DemiBold, '#eee9e2')
            detail = ('— · Unknown' if upgrade.tier is None else
                      'Not acquired' if upgrade.tier == 0 else
                      f'{upgrade.level} · Level {upgrade.tier}/3')
            text_rect(detail, 567, y+19, 96, 16, 10,
                      QFont.Weight.Normal, '#c7c0d6')
            self._register_hover(p, QRectF(522, y-3, 142, 43),
                                 bag_hover_text(raw, bag_key), palette.accent)
        p.restore()
        p.restore()
