/*
 * matrix-bg for Windows. Single-file Win32 + GDI port of matrix-bg.swift.
 *
 * Same simulation as the macOS build: ASCII 33-126 + half-width katakana,
 * 14x20 cells, 20fps, 2-3 drops per column, per-frame fade and char morph,
 * white head fading through greens, 60-second auto-kill.
 *
 * Modes:
 *   matrix-bg.exe               wallpaper mode. Renders BEHIND desktop icons
 *                               using the WorkerW window (the same mechanism
 *                               Wallpaper Engine uses). Never steals focus.
 *   matrix-bg.exe --fullscreen  screensaver mode. Covers everything, dismissed
 *                               by mouse movement, click, or any key. Uses
 *                               passive polling, no hooks, never takes focus.
 *
 * Build (from macOS/Linux): x86_64-w64-mingw32-gcc -O2 -mwindows matrix-bg-windows.c -o matrix-bg.exe -lgdi32 -luser32
 * Build (from Windows, MSVC): cl /O2 matrix-bg-windows.c /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib
 */

#ifndef UNICODE
#define UNICODE
#endif
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0601
#endif
#include <windows.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <stdarg.h>

/* ---- Diagnostic log: %TEMP%\matrix-bg.log, overwritten each run ----
 * The app is a silent GUI process; when something misbehaves on a machine we
 * can't see, this is the only evidence. Kept tiny: a dozen lines per run. */
static FILE *g_logFile;

static void logOpen(void) {
    WCHAR path[MAX_PATH];
    DWORD n = GetTempPathW(MAX_PATH, path);
    if (n == 0 || n > MAX_PATH - 14) return; /* need 13 chars + NUL for the name */
    wcscat(path, L"matrix-bg.log");
    g_logFile = _wfopen(path, L"w");
}

static void logMsg(const char *fmt, ...) {
    if (!g_logFile) return;
    va_list ap;
    va_start(ap, fmt);
    vfprintf(g_logFile, fmt, ap);
    va_end(ap);
    fputc('\n', g_logFile);
    fflush(g_logFile);
}

/* ---- Tuning (mirrors matrix-bg.swift) ---- */
#define CELL_W        14
#define CELL_H        20
#define FPS           20
#define FADE_PER_TICK 0.02f
#define LIFETIME_MS   60000   /* auto-kill safety net, same as macOS build */
#define ARM_DELAY_MS  500     /* fullscreen: ignore input for the first 0.5s */

#define TIMER_TICK 1
#define TIMER_KILL 2
#define TIMER_ARM  3

/* ---- Charset: ASCII 33..126 + half-width katakana U+FF66..U+FF9D ---- */
#define N_ASCII    (126 - 33 + 1)
#define N_KATAKANA (0xFF9D - 0xFF66 + 1)
#define N_CHARS    (N_ASCII + N_KATAKANA)
static WCHAR g_charset[N_CHARS];

/* ---- Simulation state ---- */
typedef struct { int col; float y; float speed; } Drop;

static int    g_cols, g_rows;
static float *g_brightness;  /* col * g_rows + row */
static int   *g_charIdx;
static Drop  *g_drops;
static int    g_numDrops;

/* ---- Windowing / rendering ---- */
static HWND    g_hwnd;         /* the rendering surface */
static HWND    g_msgWnd;       /* hidden top-level window: receives WM_CLOSE / WM_ENDSESSION */
static HDC     g_memDC;
static HBITMAP g_memBmp, g_memBmpOld;
static HFONT   g_font, g_fontOld;
static int     g_width, g_height;
static BOOL    g_fullscreen;
static BOOL    g_wallpaperMode;
static BOOL    g_shuttingDown;

/* Fullscreen dismiss state */
static BOOL  g_armed;
static POINT g_armOrigin;

static HWND  g_desktopParent;    /* WorkerW or Progman in wallpaper mode */

static int randRange(int lo, int hi) { /* inclusive, unbiased */
    int n = hi - lo + 1;
    int rem = RAND_MAX % n;
    /* rem == n-1 means n divides RAND_MAX+1 exactly: plain modulo is already
     * uniform (and the rejection limit below would be 0 and spin forever). */
    if (rem == n - 1) return lo + rand() % n;
    int limit = RAND_MAX - rem; /* reject the top partial band */
    int r;
    do { r = rand(); } while (r >= limit);
    return lo + r % n;
}
static float randRangeF(float lo, float hi) {
    return lo + (float)rand() / (float)RAND_MAX * (hi - lo);
}

/* ---- Wallpaper refresh: same role as the macOS wallpaper save/restore.
 * We paint on the WorkerW surface, so on exit we re-set the current wallpaper
 * (path unchanged) which forces the desktop to repaint and clears our pixels. */
static void refreshWallpaper(void) {
    WCHAR path[MAX_PATH] = L"";
    /* Solid-color desktops return an empty path; skip the re-set then. */
    if (SystemParametersInfoW(SPI_GETDESKWALLPAPER, MAX_PATH, path, 0) && path[0]) {
        SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }
    /* Extra repaint nudge; SPI_SETDESKWALLPAPER alone doesn't always clear residue */
    if (g_desktopParent && IsWindow(g_desktopParent)) {
        RedrawWindow(g_desktopParent, NULL, NULL,
                     RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
    }
}

static void shutdownApp(void) {
    if (g_shuttingDown) return;
    g_shuttingDown = TRUE;
    if (g_hwnd) ShowWindow(g_hwnd, SW_HIDE);
    if (g_wallpaperMode) refreshWallpaper();
    PostQuitMessage(0);
}

/* ---- WorkerW discovery (render behind desktop icons) ----
 * Message 0x052C tells Progman to spawn a WorkerW window between the wallpaper
 * and the icon layer. The WorkerW we want is the sibling AFTER the window that
 * contains SHELLDLL_DefView. On some Win11 builds no separate WorkerW exists
 * and SHELLDLL_DefView lives directly inside Progman; there we parent into
 * Progman itself. */
static BOOL CALLBACK findWorkerW(HWND top, LPARAM lp) {
    if (FindWindowExW(top, NULL, L"SHELLDLL_DefView", NULL) != NULL) {
        *(HWND *)lp = FindWindowExW(NULL, top, L"WorkerW", NULL);
        return FALSE;
    }
    return TRUE;
}

static HWND acquireDesktopParent(void) {
    HWND progman = FindWindowW(L"Progman", NULL);
    if (!progman) { logMsg("desktop: no Progman window found"); return NULL; }
    DWORD_PTR unused;
    /* Older builds respond to (0,0); Win11 24H2 wants (0xD, 1). Send both, harmless.
     * ABORTIFHUNG so a wedged Explorer can't stall our startup. */
    SendMessageTimeoutW(progman, 0x052C, 0, 0, SMTO_NORMAL | SMTO_ABORTIFHUNG, 1000, &unused);
    SendMessageTimeoutW(progman, 0x052C, 0xD, 1, SMTO_NORMAL | SMTO_ABORTIFHUNG, 1000, &unused);
    HWND workerw = NULL;
    EnumWindows(findWorkerW, (LPARAM)&workerw);
    if (workerw) { logMsg("desktop: WorkerW %p (behind icons)", (void *)workerw); return workerw; }
    /* Win11 22H2+: DefView inside Progman, draw into Progman directly */
    if (FindWindowExW(progman, NULL, L"SHELLDLL_DefView", NULL)) {
        logMsg("desktop: DefView inside Progman, parenting into Progman %p", (void *)progman);
        return progman;
    }
    logMsg("desktop: no WorkerW and no DefView in Progman; using bottom-most fallback");
    return NULL;
}

/* ---- Simulation (identical to matrix-bg.swift tick()) ---- */
static void simInit(void) {
    int i = 0, v;
    for (v = 33; v <= 126; v++) g_charset[i++] = (WCHAR)v;
    for (v = 0xFF66; v <= 0xFF9D; v++) g_charset[i++] = (WCHAR)v;

    g_cols = g_width / CELL_W;
    g_rows = g_height / CELL_H;
    if (g_cols < 1) g_cols = 1;
    if (g_rows < 1) g_rows = 1;
    int total = g_cols * g_rows;

    g_brightness = (float *)calloc(total, sizeof(float));
    g_charIdx    = (int *)malloc(total * sizeof(int));
    for (i = 0; i < total; i++) g_charIdx[i] = randRange(0, N_CHARS - 1);

    /* 2-3 drops per column, staggered start positions */
    g_drops = (Drop *)malloc(g_cols * 3 * sizeof(Drop));
    g_numDrops = 0;
    for (int col = 0; col < g_cols; col++) {
        int n = randRange(2, 3);
        for (int d = 0; d < n; d++) {
            g_drops[g_numDrops].col = col;
            g_drops[g_numDrops].y = randRangeF((float)(-g_rows * 2), (float)g_rows);
            g_drops[g_numDrops].speed = randRangeF(0.25f, 1.15f);
            g_numDrops++;
        }
    }
}

static void simTick(void) {
    int total = g_cols * g_rows;

    /* Fade visible cells + randomly morph their characters */
    for (int i = 0; i < total; i++) {
        if (g_brightness[i] <= 0) continue;
        g_brightness[i] -= FADE_PER_TICK;
        if (g_brightness[i] < 0) g_brightness[i] = 0;
        if (randRange(0, 5) == 0) g_charIdx[i] = randRange(0, N_CHARS - 1);
    }

    /* Advance each rain drop */
    for (int i = 0; i < g_numDrops; i++) {
        g_drops[i].y += g_drops[i].speed;
        int row = (int)g_drops[i].y;
        int col = g_drops[i].col;

        if (row >= 0 && row < g_rows) {
            int idx = col * g_rows + row;
            g_brightness[idx] = 1.0f;
            g_charIdx[idx] = randRange(0, N_CHARS - 1);
        }
        if (row - 1 >= 0 && row - 1 < g_rows) {
            int idx = col * g_rows + (row - 1);
            if (g_brightness[idx] < 0.87f) g_brightness[idx] = 0.87f;
        }
        if (row > g_rows + 25) {
            g_drops[i].y = randRangeF((float)(-g_rows), -1.0f);
            g_drops[i].speed = randRangeF(0.25f, 1.15f);
        }
    }
}

/* Color ramp, same thresholds as the Swift draw(). The lowest tier's alpha is
 * premultiplied toward black since the background is pure black anyway. */
static COLORREF cellColor(float b) {
    if (b > 0.93f) return RGB(217, 255, 230);
    if (b > 0.78f) return RGB(26, 255, 51);
    if (b > 0.4f) {
        int g = (int)((0.25f + b * 0.75f) * 255.0f);
        return RGB(0, g > 255 ? 255 : g, 0);
    }
    float alpha = b * 2.0f; if (alpha < 0.3f) alpha = 0.3f;
    int g = (int)(b * 0.8f * alpha * 255.0f);
    return RGB(0, g, 0);
}

static void render(void) {
    RECT r = { 0, 0, g_width, g_height };
    FillRect(g_memDC, &r, (HBRUSH)GetStockObject(BLACK_BRUSH));

    /* ponytail: one TextOutW per visible glyph. GDI handles the ~10-20k
     * glyphs/frame this produces at 20fps fine for a 60s overlay; the upgrade
     * path if it ever matters is Direct2D/DirectWrite glyph runs. */
    for (int col = 0; col < g_cols; col++) {
        int x = col * CELL_W + 1;
        int base = col * g_rows;
        for (int row = 0; row < g_rows; row++) {
            float b = g_brightness[base + row];
            if (b <= 0.02f) continue;
            SetTextColor(g_memDC, cellColor(b));
            /* TA_BASELINE set once at init; baseline sits 0.78 * cellH down the cell */
            TextOutW(g_memDC, x, row * CELL_H + (CELL_H * 78) / 100,
                     &g_charset[g_charIdx[base + row]], 1);
        }
    }
}

/* ---- Window procs ---- */
static LRESULT CALLBACK renderWndProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    switch (m) {
    case WM_PAINT: {
        PAINTSTRUCT ps;
        HDC dc = BeginPaint(h, &ps);
        BitBlt(dc, 0, 0, g_width, g_height, g_memDC, 0, 0, SRCCOPY);
        EndPaint(h, &ps);
        return 0;
    }
    case WM_ERASEBKGND:
        return 1;
    case WM_WINDOWPOSCHANGING:
        /* Bottom-most fallback mode: pin the window to the bottom of the
         * z-order so it always stays behind normal app windows. */
        if (!g_fullscreen && !g_wallpaperMode) {
            WINDOWPOS *wp = (WINDOWPOS *)l;
            wp->hwndInsertAfter = HWND_BOTTOM;
            wp->flags &= ~SWP_NOZORDER;
            return 0;
        }
        break;
    case WM_NCHITTEST:
        return HTTRANSPARENT; /* never intercept clicks, matches ignoresMouseEvents */
    case WM_CLOSE:
        logMsg("exit: render window WM_CLOSE");
        shutdownApp();
        return 0;
    case WM_DESTROY:
        return 0;
    }
    return DefWindowProcW(h, m, w, l);
}

static LRESULT CALLBACK msgWndProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    switch (m) {
    case WM_TIMER:
        if (w == TIMER_TICK) {
            simTick();

            /* Explorer restarted and took our WorkerW child with it: bail out
             * cleanly instead of ticking a dead window until the auto-kill. */
            if (g_wallpaperMode && !IsWindow(g_hwnd)) {
                logMsg("exit: desktop parent window died (explorer restart?)");
                shutdownApp();
                return 0;
            }

            if (g_fullscreen && g_armed) {
                /* Belt and suspenders next to the WM_INPUT path: dismiss on
                 * mouse movement > 5px from the armed origin. */
                POINT p;
                if (GetCursorPos(&p)) {
                    int dx = p.x - g_armOrigin.x, dy = p.y - g_armOrigin.y;
                    if (dx * dx + dy * dy > 25) {
                        logMsg("dismiss: mouse moved (timer check)");
                        shutdownApp();
                        return 0;
                    }
                }
            }

            render();
            InvalidateRect(g_hwnd, NULL, FALSE);
        } else if (w == TIMER_KILL) {
            logMsg("exit: 60s auto-kill");
            shutdownApp();
        } else if (w == TIMER_ARM) {
            KillTimer(h, TIMER_ARM);
            GetCursorPos(&g_armOrigin);
            g_armed = TRUE;
        }
        return 0;
    case WM_INPUT:
        /* Fullscreen dismiss: raw input (RIDEV_INPUTSINK) delivers key, button,
         * and wheel events to this hidden window without focus or hooks, the
         * same passive-observer model as the macOS global event monitor. */
        if (g_fullscreen && g_armed && !g_shuttingDown) {
            RAWINPUT ri;
            UINT size = sizeof(ri);
            if (GetRawInputData((HRAWINPUT)l, RID_INPUT, &ri, &size,
                                sizeof(RAWINPUTHEADER)) != (UINT)-1) {
                if (ri.header.dwType == RIM_TYPEKEYBOARD) {
                    if (!(ri.data.keyboard.Flags & RI_KEY_BREAK)) {
                        logMsg("dismiss: key press");
                        shutdownApp();
                    }
                } else if (ri.header.dwType == RIM_TYPEMOUSE) {
                    USHORT bf = ri.data.mouse.usButtonFlags;
                    if (bf & (RI_MOUSE_LEFT_BUTTON_DOWN | RI_MOUSE_RIGHT_BUTTON_DOWN |
                              RI_MOUSE_MIDDLE_BUTTON_DOWN | RI_MOUSE_BUTTON_4_DOWN |
                              RI_MOUSE_BUTTON_5_DOWN | RI_MOUSE_WHEEL | RI_MOUSE_HWHEEL)) {
                        logMsg("dismiss: mouse button or wheel");
                        shutdownApp();
                    } else {
                        POINT p;
                        if (GetCursorPos(&p)) {
                            int dx = p.x - g_armOrigin.x, dy = p.y - g_armOrigin.y;
                            if (dx * dx + dy * dy > 25) {
                                logMsg("dismiss: mouse moved");
                                shutdownApp();
                            }
                        }
                    }
                }
            }
        }
        break; /* WM_INPUT must still reach DefWindowProc for cleanup */
    case WM_CLOSE:            /* taskkill (no /F) lands here */
        logMsg("exit: WM_CLOSE");
        shutdownApp();
        return 0;
    case WM_QUERYENDSESSION:
        return TRUE;
    case WM_ENDSESSION:
        if (w) {
            logMsg("exit: session ending");
            g_shuttingDown = TRUE;
            if (g_wallpaperMode) refreshWallpaper();
        }
        return 0;
    case WM_DESTROY:
        return 0;
    }
    return DefWindowProcW(h, m, w, l);
}

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE prev, PWSTR cmdLine, int show) {
    (void)prev; (void)show; (void)cmdLine;
    logOpen();
    logMsg("matrix-bg.exe build %s %s", __DATE__, __TIME__);
    g_fullscreen = wcsstr(GetCommandLineW(), L"--fullscreen") != NULL;
    logMsg("mode: %s", g_fullscreen ? "fullscreen" : "wallpaper");
    srand(GetTickCount());
    SetProcessDPIAware();

    int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
    int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
    g_width  = GetSystemMetrics(SM_CXVIRTUALSCREEN);
    g_height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
    logMsg("virtual screen: %dx%d at (%d,%d)", g_width, g_height, vx, vy);
    if (g_width < 1 || g_height < 1) { logMsg("FATAL: empty virtual screen"); return 1; }

    WNDCLASSW wc = {0};
    wc.lpfnWndProc = renderWndProc;
    wc.hInstance = inst;
    wc.lpszClassName = L"MatrixBGRender";
    wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    RegisterClassW(&wc);

    WNDCLASSW mc = {0};
    mc.lpfnWndProc = msgWndProc;
    mc.hInstance = inst;
    mc.lpszClassName = L"MatrixBGMain";
    RegisterClassW(&mc);

    /* Hidden top-level window: owns timers and receives WM_CLOSE/WM_ENDSESSION
     * (the render surface may be a child of WorkerW, which taskkill and session
     * broadcasts never reach). */
    g_msgWnd = CreateWindowExW(WS_EX_TOOLWINDOW, L"MatrixBGMain", L"matrix-bg",
                               WS_POPUP, 0, 0, 0, 0, NULL, NULL, inst, NULL);
    if (!g_msgWnd) return 1;

    if (g_fullscreen) {
        g_wallpaperMode = FALSE;
        g_hwnd = CreateWindowExW(WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
                                 L"MatrixBGRender", L"matrix-bg", WS_POPUP,
                                 vx, vy, g_width, g_height, NULL, NULL, inst, NULL);
    } else {
        HWND desktop = acquireDesktopParent();
        if (desktop) {
            g_wallpaperMode = TRUE;
            g_desktopParent = desktop;
            POINT origin = { vx, vy };
            ScreenToClient(desktop, &origin);
            g_hwnd = CreateWindowExW(0, L"MatrixBGRender", L"matrix-bg",
                                     WS_CHILD, origin.x, origin.y, g_width, g_height,
                                     desktop, NULL, inst, NULL);
            /* When the parent is Progman itself (Win11 22H2+), the icon layer
             * (SHELLDLL_DefView) is our sibling: pin ourselves to the bottom of
             * the child z-order so icons stay visible and clickable above us. */
            if (g_hwnd) SetWindowPos(g_hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        } else {
            /* Fallback: bottom-most non-activating overlay above the wallpaper.
             * Sits over desktop icons but under every app window. */
            g_wallpaperMode = FALSE;
            g_hwnd = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
                                     L"MatrixBGRender", L"matrix-bg", WS_POPUP,
                                     vx, vy, g_width, g_height, NULL, NULL, inst, NULL);
        }
    }
    if (!g_hwnd) { logMsg("FATAL: CreateWindowExW failed"); return 1; }
    logMsg("render window: %p (wallpaperMode=%d, fullscreen=%d)",
           (void *)g_hwnd, g_wallpaperMode, g_fullscreen);

    /* Double buffer + font */
    HDC screen = GetDC(g_hwnd);
    g_memDC = CreateCompatibleDC(screen);
    g_memBmp = CreateCompatibleBitmap(screen, g_width, g_height);
    ReleaseDC(g_hwnd, screen);
    if (!g_memDC || !g_memBmp) { logMsg("FATAL: double buffer creation failed"); return 1; }
    g_memBmpOld = (HBITMAP)SelectObject(g_memDC, g_memBmp);

    /* MS Gothic ships with Windows and covers half-width katakana; fall back
     * through other JP faces before settling for Consolas (ASCII-only rain). */
    static const WCHAR *faces[] = { L"MS Gothic", L"Yu Gothic UI", L"Meiryo", L"Consolas" };
    for (size_t f = 0; f < sizeof(faces) / sizeof(faces[0]); f++) {
        HFONT candidate = CreateFontW(-(CELL_H * 72) / 100, 0, 0, 0, FW_NORMAL,
                                      FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                                      OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                                      DEFAULT_QUALITY, FIXED_PITCH | FF_MODERN, faces[f]);
        HFONT prev = (HFONT)SelectObject(g_memDC, candidate);
        if (g_font == NULL) g_fontOld = prev;  /* first select: remember stock font */
        else DeleteObject(prev);               /* discard the non-matching candidate */
        g_font = candidate;
        WCHAR got[LF_FACESIZE] = L"";
        GetTextFaceW(g_memDC, LF_FACESIZE, got);
        if (_wcsicmp(got, faces[f]) == 0) break;  /* real match, keep it */
    }
    {
        WCHAR face[LF_FACESIZE] = L"";
        GetTextFaceW(g_memDC, LF_FACESIZE, face);
        logMsg("font: %ls", face);
    }
    SetBkMode(g_memDC, TRANSPARENT);
    SetTextAlign(g_memDC, TA_LEFT | TA_BASELINE);

    simInit();
    render();

    ShowWindow(g_hwnd, SW_SHOWNOACTIVATE);
    UpdateWindow(g_hwnd);

    SetTimer(g_msgWnd, TIMER_TICK, 1000 / FPS, NULL);
    SetTimer(g_msgWnd, TIMER_KILL, LIFETIME_MS, NULL);
    if (g_fullscreen) {
        SetTimer(g_msgWnd, TIMER_ARM, ARM_DELAY_MS, NULL);
        /* Passive input observation for dismissal (keyboard 0x06, mouse 0x02).
         * INPUTSINK: events arrive even though we never take focus. */
        RAWINPUTDEVICE rid[2];
        rid[0].usUsagePage = 0x01; rid[0].usUsage = 0x06;
        rid[0].dwFlags = RIDEV_INPUTSINK; rid[0].hwndTarget = g_msgWnd;
        rid[1].usUsagePage = 0x01; rid[1].usUsage = 0x02;
        rid[1].dwFlags = RIDEV_INPUTSINK; rid[1].hwndTarget = g_msgWnd;
        BOOL riOk = RegisterRawInputDevices(rid, 2, sizeof(RAWINPUTDEVICE));
        logMsg("raw input registered: %d", riOk);
        /* If registration fails, the 5px cursor check and 60s auto-kill in the
         * tick handler still dismiss; keys/wheel just lose their shortcut. */
    }

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    /* Belt and suspenders if we quit through a path that skipped shutdownApp */
    if (g_wallpaperMode && !g_shuttingDown) refreshWallpaper();

    SelectObject(g_memDC, g_fontOld);
    DeleteObject(g_font);
    SelectObject(g_memDC, g_memBmpOld);
    DeleteObject(g_memBmp);
    DeleteDC(g_memDC);
    DestroyWindow(g_hwnd);
    DestroyWindow(g_msgWnd);
    return 0;
}
