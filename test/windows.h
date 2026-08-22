/*
 * Minimal Win32 shim so matrix-bg-windows.c compiles NATIVELY on macOS/Linux
 * for the simulation parity test (test/sim-parity-test.c). Only the surface
 * the app actually touches is declared; every API is a no-op stub. The sim
 * functions (simInit, simTick, cellColor, randRange, randRangeF) contain no
 * Win32 calls, so the test exercises the real shipped logic byte for byte.
 *
 * This header is picked up ONLY via -Itest; real Windows builds use the SDK.
 */
#ifndef MATRIX_BG_TEST_WINSHIM_H
#define MATRIX_BG_TEST_WINSHIM_H

#include <stddef.h>
#include <wchar.h>

/* ---- Types ---- */
typedef int            BOOL;
typedef unsigned int   UINT;
typedef unsigned long  DWORD;
typedef unsigned long long DWORD_PTR;
typedef unsigned short USHORT;
typedef long           LONG;
typedef long long      LRESULT;
typedef unsigned long long WPARAM;
typedef long long      LPARAM;
typedef wchar_t        WCHAR;
typedef const WCHAR   *LPCWSTR;
typedef WCHAR         *PWSTR;
typedef void          *HWND, *HDC, *HBITMAP, *HFONT, *HBRUSH, *HGDIOBJ,
                      *HINSTANCE, *HRAWINPUT, *HMENU;
typedef DWORD          COLORREF;

typedef struct { LONG x, y; } POINT;
typedef struct { LONG left, top, right, bottom; } RECT;
typedef struct { int unused; } MSG;
typedef struct { int unused; } PAINTSTRUCT;
typedef struct { HWND hwnd, hwndInsertAfter; int x, y, cx, cy; UINT flags; } WINDOWPOS;
typedef struct { USHORT usUsagePage, usUsage; DWORD dwFlags; HWND hwndTarget; } RAWINPUTDEVICE;
typedef struct { DWORD dwType; } RAWINPUTHEADER;
typedef struct { USHORT MakeCode, Flags; } RAWKEYBOARD;
typedef struct { USHORT usFlags, usButtonFlags; } RAWMOUSE;
typedef struct { RAWINPUTHEADER header; union { RAWKEYBOARD keyboard; RAWMOUSE mouse; } data; } RAWINPUT;

#define CALLBACK
#define WINAPI
typedef LRESULT (*WNDPROC)(HWND, UINT, WPARAM, LPARAM);
typedef BOOL (*WNDENUMPROC)(HWND, LPARAM);
typedef struct {
    UINT style; WNDPROC lpfnWndProc; int cbClsExtra, cbWndExtra;
    HINSTANCE hInstance; void *hIcon, *hCursor; HBRUSH hbrBackground;
    LPCWSTR lpszMenuName, lpszClassName;
} WNDCLASSW;

/* ---- Constants (values match the SDK where the code compares them) ---- */
#define TRUE  1
#define FALSE 0
#define MAX_PATH 260
#define LF_FACESIZE 32
#define RGB(r, g, b) ((COLORREF)(((DWORD)(unsigned char)(r)) | ((DWORD)(unsigned char)(g) << 8) | ((DWORD)(unsigned char)(b) << 16)))
#define GetRValue(c) ((unsigned char)(c))
#define GetGValue(c) ((unsigned char)((c) >> 8))
#define GetBValue(c) ((unsigned char)((c) >> 16))

#define WM_PAINT 0x000F
#define WM_ERASEBKGND 0x0014
#define WM_WINDOWPOSCHANGING 0x0046
#define WM_NCHITTEST 0x0084
#define WM_CLOSE 0x0010
#define WM_DESTROY 0x0002
#define WM_TIMER 0x0113
#define WM_INPUT 0x00FF
#define WM_QUERYENDSESSION 0x0011
#define WM_ENDSESSION 0x0016
#define HTTRANSPARENT (-1)
#define HWND_BOTTOM ((HWND)1)
#define SWP_NOSIZE 0x0001
#define SWP_NOMOVE 0x0002
#define SWP_NOZORDER 0x0004
#define SWP_NOACTIVATE 0x0010
#define WS_CHILD 0x40000000
#define WS_POPUP 0x80000000u
#define WS_EX_TOPMOST 0x0008
#define WS_EX_TOOLWINDOW 0x0080
#define WS_EX_NOACTIVATE 0x08000000
#define BLACK_BRUSH 4
#define SPI_GETDESKWALLPAPER 0x0073
#define SPI_SETDESKWALLPAPER 0x0014
#define SPIF_UPDATEINIFILE 0x01
#define SPIF_SENDCHANGE 0x02
#define SMTO_NORMAL 0x0000
#define SMTO_ABORTIFHUNG 0x0002
#define RDW_INVALIDATE 0x0001
#define RDW_ERASE 0x0004
#define RDW_ALLCHILDREN 0x0080
#define RDW_UPDATENOW 0x0100
#define SM_XVIRTUALSCREEN 76
#define SM_YVIRTUALSCREEN 77
#define SM_CXVIRTUALSCREEN 78
#define SM_CYVIRTUALSCREEN 79
#define FW_NORMAL 400
#define DEFAULT_CHARSET 1
#define OUT_DEFAULT_PRECIS 0
#define CLIP_DEFAULT_PRECIS 0
#define DEFAULT_QUALITY 0
#define FIXED_PITCH 1
#define FF_MODERN 48
#define TRANSPARENT 1
#define TA_LEFT 0
#define TA_BASELINE 24
#define SRCCOPY 0x00CC0020
#define SW_HIDE 0
#define SW_SHOWNOACTIVATE 4
#define RIDEV_INPUTSINK 0x00000100
#define RID_INPUT 0x10000003
#define RIM_TYPEMOUSE 0
#define RIM_TYPEKEYBOARD 1
#define RI_KEY_BREAK 1
#define RI_MOUSE_LEFT_BUTTON_DOWN 0x0001
#define RI_MOUSE_RIGHT_BUTTON_DOWN 0x0004
#define RI_MOUSE_MIDDLE_BUTTON_DOWN 0x0010
#define RI_MOUSE_BUTTON_4_DOWN 0x0040
#define RI_MOUSE_BUTTON_5_DOWN 0x0100
#define RI_MOUSE_WHEEL 0x0400
#define RI_MOUSE_HWHEEL 0x0800

#define _wcsicmp wcscasecmp

/* ---- API stubs: every Win32 call the app makes, as a benign no-op ---- */
static WCHAR shim_empty_wstr[1];
static inline BOOL SystemParametersInfoW(UINT a, UINT b, void *c, UINT d) { (void)a;(void)b;(void)c;(void)d; return 0; }
static inline BOOL IsWindow(HWND h) { (void)h; return 1; }
static inline BOOL RedrawWindow(HWND h, const RECT *r, void *rg, UINT f) { (void)h;(void)r;(void)rg;(void)f; return 1; }
static inline HWND FindWindowW(LPCWSTR c, LPCWSTR n) { (void)c;(void)n; return 0; }
static inline HWND FindWindowExW(HWND a, HWND b, LPCWSTR c, LPCWSTR d) { (void)a;(void)b;(void)c;(void)d; return 0; }
static inline LRESULT SendMessageTimeoutW(HWND h, UINT m, WPARAM w, LPARAM l, UINT f, UINT t, DWORD_PTR *r) { (void)h;(void)m;(void)w;(void)l;(void)f;(void)t;(void)r; return 1; }
static inline BOOL EnumWindows(WNDENUMPROC p, LPARAM l) { (void)p;(void)l; return 1; }
static inline BOOL ShowWindow(HWND h, int c) { (void)h;(void)c; return 1; }
static inline void PostQuitMessage(int c) { (void)c; }
static inline int FillRect(HDC d, const RECT *r, HBRUSH b) { (void)d;(void)r;(void)b; return 1; }
static inline HGDIOBJ GetStockObject(int i) { (void)i; return 0; }
static inline COLORREF SetTextColor(HDC d, COLORREF c) { (void)d;(void)c; return 0; }
static inline BOOL TextOutW(HDC d, int x, int y, const WCHAR *s, int n) { (void)d;(void)x;(void)y;(void)s;(void)n; return 1; }
static inline HDC BeginPaint(HWND h, PAINTSTRUCT *p) { (void)h;(void)p; return 0; }
static inline BOOL EndPaint(HWND h, const PAINTSTRUCT *p) { (void)h;(void)p; return 1; }
static inline BOOL BitBlt(HDC a, int b, int c, int d, int e, HDC f, int g, int i, DWORD r) { (void)a;(void)b;(void)c;(void)d;(void)e;(void)f;(void)g;(void)i;(void)r; return 1; }
static inline LRESULT DefWindowProcW(HWND h, UINT m, WPARAM w, LPARAM l) { (void)h;(void)m;(void)w;(void)l; return 0; }
static inline BOOL GetCursorPos(POINT *p) { if (p) { p->x = 0; p->y = 0; } return 1; }
static inline BOOL KillTimer(HWND h, WPARAM i) { (void)h;(void)i; return 1; }
static inline UINT GetRawInputData(HRAWINPUT h, UINT c, void *d, UINT *s, UINT hs) { (void)h;(void)c;(void)d;(void)s;(void)hs; return (UINT)-1; }
static inline BOOL SetWindowPos(HWND h, HWND a, int x, int y, int cx, int cy, UINT f) { (void)h;(void)a;(void)x;(void)y;(void)cx;(void)cy;(void)f; return 1; }
static inline BOOL ScreenToClient(HWND h, POINT *p) { (void)h;(void)p; return 1; }
static inline HWND CreateWindowExW(DWORD ex, LPCWSTR cls, LPCWSTR name, DWORD st, int x, int y, int w, int hgt, HWND par, HMENU mnu, HINSTANCE in, void *lp) { (void)ex;(void)cls;(void)name;(void)st;(void)x;(void)y;(void)w;(void)hgt;(void)par;(void)mnu;(void)in;(void)lp; return 0; }
static inline int GetSystemMetrics(int i) { (void)i; return 0; }
static inline BOOL SetProcessDPIAware(void) { return 1; }
static inline unsigned short RegisterClassW(const WNDCLASSW *w) { (void)w; return 1; }
static inline HDC GetDC(HWND h) { (void)h; return 0; }
static inline HDC CreateCompatibleDC(HDC d) { (void)d; return 0; }
static inline HBITMAP CreateCompatibleBitmap(HDC d, int w, int h) { (void)d;(void)w;(void)h; return 0; }
static inline int ReleaseDC(HWND h, HDC d) { (void)h;(void)d; return 1; }
static inline HGDIOBJ SelectObject(HDC d, HGDIOBJ o) { (void)d;(void)o; return 0; }
static inline HFONT CreateFontW(int h, int w, int e, int o, int wt, DWORD i, DWORD u, DWORD s, DWORD cs, DWORD op, DWORD cp, DWORD q, DWORD pf, LPCWSTR f) { (void)h;(void)w;(void)e;(void)o;(void)wt;(void)i;(void)u;(void)s;(void)cs;(void)op;(void)cp;(void)q;(void)pf;(void)f; return 0; }
static inline int GetTextFaceW(HDC d, int n, WCHAR *f) { (void)d; if (f && n > 0) f[0] = 0; return 0; }
static inline int SetBkMode(HDC d, int m) { (void)d;(void)m; return 0; }
static inline UINT SetTextAlign(HDC d, UINT a) { (void)d;(void)a; return 0; }
static inline WPARAM SetTimer(HWND h, WPARAM i, UINT e, void *p) { (void)h;(void)i;(void)e;(void)p; return 1; }
static inline BOOL RegisterRawInputDevices(const RAWINPUTDEVICE *r, UINT n, UINT s) { (void)r;(void)n;(void)s; return 1; }
static inline BOOL GetMessageW(MSG *m, HWND h, UINT a, UINT b) { (void)m;(void)h;(void)a;(void)b; return 0; }
static inline BOOL TranslateMessage(const MSG *m) { (void)m; return 0; }
static inline LRESULT DispatchMessageW(const MSG *m) { (void)m; return 0; }
static inline BOOL DeleteObject(HGDIOBJ o) { (void)o; return 1; }
static inline BOOL DeleteDC(HDC d) { (void)d; return 1; }
static inline BOOL DestroyWindow(HWND h) { (void)h; return 1; }
static inline DWORD GetTickCount(void) { return 0; }
static inline WCHAR *GetCommandLineW(void) { return shim_empty_wstr; }
static inline BOOL InvalidateRect(HWND h, const RECT *r, BOOL e) { (void)h;(void)r;(void)e; return 1; }
static inline BOOL UpdateWindow(HWND h) { (void)h; return 1; }

#endif /* MATRIX_BG_TEST_WINSHIM_H */
