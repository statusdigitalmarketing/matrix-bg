#!/usr/bin/env python3
"""Full-screen Matrix rain desktop overlay. Kill the process (SIGTERM/SIGINT) to dismiss."""

import random
import signal
import sys
import objc
from AppKit import (
    NSApplication,
    NSWindow,
    NSView,
    NSScreen,
    NSColor,
    NSFont,
    NSTimer,
    NSBorderlessWindowMask,
    NSBackingStoreBuffered,
    NSForegroundColorAttributeName,
    NSFontAttributeName,
    NSRectFill,
    NSGraphicsContext,
    NSMutableAttributedString,
    NSAttributedString,
)
from Foundation import NSMakePoint, NSMakeRange, NSString
from Quartz import kCGDesktopWindowLevel, kCGScreenSaverWindowLevel

CHAR_SIZE = 18.0
FPS = 24
MAX_TRAIL = 22
CHARSET = [chr(c) for c in range(33, 127)] + [chr(c) for c in range(0xFF66, 0xFF9E)]

# Pre-compute color palette (head white → bright green → fading green → black)
_COLOR_CACHE = []
_COLOR_CACHE.append(NSColor.colorWithCalibratedRed_green_blue_alpha_(1.0, 1.0, 1.0, 1.0))  # 0 = head
_COLOR_CACHE.append(NSColor.colorWithCalibratedRed_green_blue_alpha_(0.3, 1.0, 0.3, 1.0))  # 1 = bright
_COLOR_CACHE.append(NSColor.colorWithCalibratedRed_green_blue_alpha_(0.2, 0.9, 0.2, 1.0))  # 2 = bright-ish
for i in range(3, MAX_TRAIL + 1):
    fade = max(0.05, 1.0 - i / MAX_TRAIL)
    _COLOR_CACHE.append(
        NSColor.colorWithCalibratedRed_green_blue_alpha_(0.0, fade * 0.85, 0.0, fade)
    )


class Drop:
    __slots__ = ("y", "speed", "length", "chars")

    def __init__(self, num_rows):
        self.length = random.randint(6, MAX_TRAIL)
        self.speed = random.uniform(0.5, 1.6)
        self.y = random.uniform(-num_rows, 0)
        self.chars = [random.choice(CHARSET) for _ in range(self.length)]

    def reset(self, num_rows):
        self.length = random.randint(6, MAX_TRAIL)
        self.speed = random.uniform(0.5, 1.6)
        self.y = random.uniform(-num_rows, 0)
        self.chars = [random.choice(CHARSET) for _ in range(self.length)]


class MatrixView(NSView):
    columns = objc.ivar("columns")
    num_cols = objc.ivar.int("num_cols")
    num_rows = objc.ivar.int("num_rows")
    _timer = objc.ivar("_timer")
    _font = objc.ivar("_font")
    _attrs_base = objc.ivar("_attrs_base")

    def initWithFrame_(self, frame):
        self = objc.super(MatrixView, self).initWithFrame_(frame)
        if self is None:
            return None
        w, h = frame.size.width, frame.size.height
        self.num_cols = int(w / CHAR_SIZE)
        self.num_rows = int(h / CHAR_SIZE)
        self._font = NSFont.fontWithName_size_("Menlo", CHAR_SIZE)
        if self._font is None:
            self._font = NSFont.monospacedSystemFontOfSize_weight_(CHAR_SIZE, 0)

        # Pre-build base attribute dict (reused every frame)
        self._attrs_base = {NSFontAttributeName: self._font}

        self.columns = []
        for _ in range(self.num_cols):
            self.columns.append([Drop(self.num_rows) for _ in range(random.randint(1, 2))])

        self._timer = NSTimer.scheduledTimerWithTimeInterval_target_selector_userInfo_repeats_(
            1.0 / FPS, self, "tick:", None, True
        )
        return self

    def tick_(self, timer):
        nr = self.num_rows
        for col in self.columns:
            for drop in col:
                drop.y += drop.speed
                if drop.y > nr + drop.length + 8:
                    drop.reset(nr)
                # Flicker a random character
                if random.random() < 0.2:
                    drop.chars[random.randint(0, drop.length - 1)] = random.choice(CHARSET)
        self.setNeedsDisplay_(True)

    def drawRect_(self, rect):
        NSColor.blackColor().setFill()
        NSRectFill(self.bounds())

        font = self._font
        bounds_h = self.bounds().size.height
        nr = self.num_rows
        cs = CHAR_SIZE
        colors = _COLOR_CACHE
        max_ci = len(colors) - 1

        for ci, col in enumerate(self.columns):
            x = ci * cs
            for drop in col:
                head = int(drop.y)
                dlen = drop.length
                chars = drop.chars
                clen = len(chars)
                for t in range(dlen):
                    row = head - t
                    if row < 0 or row >= nr:
                        continue
                    color = colors[min(t, max_ci)]
                    attrs = {NSFontAttributeName: font, NSForegroundColorAttributeName: color}
                    pt = NSMakePoint(x, bounds_h - (row + 1) * cs)
                    NSString.stringWithString_(chars[t % clen]).drawAtPoint_withAttributes_(pt, attrs)


def main():
    fullscreen = "--fullscreen" in sys.argv

    app = NSApplication.sharedApplication()
    app.setActivationPolicy_(1)

    level = kCGScreenSaverWindowLevel if fullscreen else kCGDesktopWindowLevel + 1

    windows = []
    for screen in NSScreen.screens():
        frame = screen.frame()
        w = NSWindow.alloc().initWithContentRect_styleMask_backing_defer_screen_(
            frame, NSBorderlessWindowMask, NSBackingStoreBuffered, False, screen,
        )
        w.setLevel_(level)
        w.setBackgroundColor_(NSColor.blackColor())
        w.setOpaque_(True)
        w.setHasShadow_(False)
        w.setCollectionBehavior_(1 << 0 | 1 << 8)
        view = MatrixView.alloc().initWithFrame_(frame)
        w.setContentView_(view)
        w.makeKeyAndOrderFront_(None)
        windows.append(w)

    def _quit(signum, _frame):
        app.terminate_(None)

    signal.signal(signal.SIGTERM, _quit)
    signal.signal(signal.SIGINT, _quit)
    app.run()


if __name__ == "__main__":
    main()
