/*
 * Simulation parity test: compiles the SHIPPED matrix-bg-windows.c natively
 * (test/windows.h shims the Win32 surface) and executes its real simInit /
 * simTick / cellColor / randRange against the rules defined by matrix-bg.swift.
 *
 * What this proves: the Windows build's simulation obeys exactly the Swift
 * app's transition relation (fade 0.02/tick, head 1.0, trail 0.87, drop
 * ranges, reset bounds, charset size/indices, color ramp thresholds).
 * What it can't prove: pixel rendering and Win32 windowing on real Windows.
 *
 * Run: make sim-test
 *   ./test/sim-parity-test           invariant checks (exit 0 = pass)
 *   ./test/sim-parity-test charset   dump charset, diffed against Swift's
 */
#include "../matrix-bg-windows.c"

#include <stdio.h>
#include <math.h>

static int failures = 0;
#define CHECK(cond, ...) do { if (!(cond)) { failures++; printf("FAIL: " __VA_ARGS__); printf("\n"); } } while (0)

/* Same fade step the Swift tick applies: max(0, b - 0.02), in float like C.
 * Uses the LITERAL 0.02f, not FADE_PER_TICK, so a drifted constant in the
 * shipped file cannot silently track itself through the emulation. */
static float fadeOf(float b) {
    if (b <= 0) return b;
    b -= 0.02f;
    return b < 0 ? 0 : b;
}

/* Mirror of the Swift color branches (matrix-bg.swift:147-156) in double,
 * quantized by rounding as Quartz does. C truncates, so allow one 8-bit step. */
static void swiftColor(double b, int *r, int *g, int *bl) {
    if (b > 0.93) { *r = (int)lround(0.85 * 255); *g = 255; *bl = (int)lround(0.9 * 255); }
    else if (b > 0.78) { *r = (int)lround(0.1 * 255); *g = 255; *bl = (int)lround(0.2 * 255); }
    else if (b > 0.4) { *r = 0; *g = (int)lround((0.25 + b * 0.75) * 255); *bl = 0; }
    else { double a = b * 2.0 < 0.3 ? 0.3 : b * 2.0; *r = 0; *g = (int)lround(b * 0.8 * a * 255); *bl = 0; }
}

int main(int argc, char **argv) {
    g_width = 1024;
    g_height = 768;
    srand(12345);
    simInit();

    if (argc > 1 && strcmp(argv[1], "charset") == 0) {
        for (int i = 0; i < N_CHARS; i++) printf("U+%04X\n", (unsigned)g_charset[i]);
        return 0;
    }

    /* ---- Tuning constants pinned to matrix-bg.swift's values ---- */
    CHECK(CELL_W == 14 && CELL_H == 20, "cell size %dx%d != 14x20", CELL_W, CELL_H);
    CHECK(FPS == 20, "FPS %d != 20 (matrix-bg.swift:78 uses 1.0/20.0)", FPS);
    CHECK(FADE_PER_TICK == 0.02f, "fade %.4f != 0.02", (double)FADE_PER_TICK);
    CHECK(LIFETIME_MS == 60000, "auto-kill %d != 60s (matrix-bg.swift:284)", LIFETIME_MS);

    /* ---- Charset: ASCII 33..126 + halfwidth katakana U+FF66..U+FF9D ---- */
    CHECK(N_CHARS == 150, "charset count %d != 150", N_CHARS);
    CHECK(g_charset[0] == 33 && g_charset[93] == 126, "ASCII bounds wrong");
    CHECK(g_charset[94] == 0xFF66 && g_charset[149] == 0xFF9D, "katakana bounds wrong");

    /* ---- Grid geometry: 14x20 cells over the surface ---- */
    CHECK(g_cols == 1024 / 14, "cols %d != %d", g_cols, 1024 / 14);
    CHECK(g_rows == 768 / 20, "rows %d != %d", g_rows, 768 / 20);
    int total = g_cols * g_rows;

    /* ---- Initial state: 2-3 drops/column, staggered y, speed 0.25..1.15 ---- */
    int *perCol = (int *)calloc(g_cols, sizeof(int));
    for (int i = 0; i < g_numDrops; i++) {
        perCol[g_drops[i].col]++;
        CHECK(g_drops[i].y >= (float)(-g_rows * 2) && g_drops[i].y <= (float)g_rows,
              "drop %d start y %.2f out of [-2*rows, rows]", i, g_drops[i].y);
        CHECK(g_drops[i].speed >= 0.25f && g_drops[i].speed <= 1.15f,
              "drop %d speed %.3f out of [0.25, 1.15]", i, g_drops[i].speed);
    }
    for (int c = 0; c < g_cols; c++)
        CHECK(perCol[c] >= 2 && perCol[c] <= 3, "column %d has %d drops, want 2-3", c, perCol[c]);
    free(perCol);
    for (int i = 0; i < total; i++) {
        CHECK(g_brightness[i] == 0.0f, "cell %d starts lit", i);
        CHECK(g_charIdx[i] >= 0 && g_charIdx[i] < N_CHARS, "cell %d charIdx %d out of range", i, g_charIdx[i]);
    }

    /* ---- Full brightness emulation over 2000 ticks ----
     * The brightness field is fully determined by the drop states (only char
     * morphing and drop resets consume randomness), so we can predict the
     * ENTIRE next brightness array and every drop's exact movement from the
     * Swift tick's rules, then require bit-exact equality. This catches frozen
     * drops, heads landing on the wrong cell/column, missing trails, wrong
     * ordering, and stray writes, none of which a value-set check would see. */
    int nDrops = g_numDrops; /* Swift never grows or shrinks the drop pool */
    float *exp_b = (float *)malloc(total * sizeof(float));
    Drop *dBefore = (Drop *)malloc(nDrops * sizeof(Drop));
    int litSeen = 0, resetsSeen = 0;
    for (int t = 0; t < 2000; t++) {
        /* Predict from the pre-tick state, mirroring matrix-bg.swift tick() */
        for (int i = 0; i < total; i++) exp_b[i] = fadeOf(g_brightness[i]);
        memcpy(dBefore, g_drops, nDrops * sizeof(Drop));
        simTick();
        CHECK(g_numDrops == nDrops, "tick %d drop count changed %d -> %d",
              t, nDrops, g_numDrops);

        for (int i = 0; i < nDrops; i++) {
            float advY = dBefore[i].y + dBefore[i].speed; /* same float op as sim */
            int row = (int)advY;
            int col = dBefore[i].col;
            if (row >= 0 && row < g_rows) exp_b[col * g_rows + row] = 1.0f;
            if (row - 1 >= 0 && row - 1 < g_rows) {
                int idx = col * g_rows + (row - 1);
                if (exp_b[idx] < 0.87f) exp_b[idx] = 0.87f;
            }
            CHECK(g_drops[i].col == col, "tick %d drop %d changed column", t, i);
            if (row > g_rows + 25) { /* must have reset into [-rows, -1] */
                resetsSeen++;
                CHECK(g_drops[i].y >= (float)(-g_rows) && g_drops[i].y <= -1.0f,
                      "tick %d drop %d reset y %.2f outside [-rows, -1]", t, i, g_drops[i].y);
                CHECK(g_drops[i].speed >= 0.25f && g_drops[i].speed <= 1.15f,
                      "tick %d drop %d reset speed %.3f outside [0.25, 1.15]", t, i, g_drops[i].speed);
            } else { /* must have advanced by exactly its speed */
                CHECK(g_drops[i].y == advY && g_drops[i].speed == dBefore[i].speed,
                      "tick %d drop %d expected y %.4f speed %.4f, got %.4f %.4f",
                      t, i, advY, dBefore[i].speed, g_drops[i].y, g_drops[i].speed);
            }
        }
        for (int i = 0; i < total; i++) {
            CHECK(g_brightness[i] == exp_b[i],
                  "tick %d cell %d brightness %.6f, emulation predicts %.6f",
                  t, i, g_brightness[i], exp_b[i]);
            CHECK(g_charIdx[i] >= 0 && g_charIdx[i] < N_CHARS,
                  "tick %d cell %d charIdx %d out of range", t, i, g_charIdx[i]);
            if (g_brightness[i] > 0) litSeen = 1;
            if (failures > 20) goto done; /* don't spam thousands of lines */
        }
    }
done:
    free(exp_b);
    free(dBefore);
    CHECK(litSeen, "no cell ever lit in 2000 ticks; rain is dead");
    CHECK(resetsSeen > 0, "no drop ever reset in 2000 ticks; recycling is dead");

    /* ---- Color ramp vs matrix-bg.swift:147-156 ---- */
    static const double samples[] = { 1.0, 0.94, 0.93, 0.90, 0.80, 0.785, 0.78,
                                      0.60, 0.50, 0.41, 0.40, 0.30, 0.10, 0.03 };
    for (size_t i = 0; i < sizeof(samples) / sizeof(samples[0]); i++) {
        float b = (float)samples[i];
        COLORREF c = cellColor(b);
        int cr = GetRValue(c), cg = GetGValue(c), cb = GetBValue(c);
        int sr, sg, sb;
        swiftColor(samples[i], &sr, &sg, &sb);
        CHECK(abs(cr - sr) <= 1 && abs(cg - sg) <= 1 && abs(cb - sb) <= 1,
              "b=%.3f: C(%d,%d,%d) vs Swift(%d,%d,%d) differ by more than 1/255",
              samples[i], cr, cg, cb, sr, sg, sb);
        printf("b=%.3f  C=(%3d,%3d,%3d)  Swift=(%3d,%3d,%3d)\n",
               samples[i], cr, cg, cb, sr, sg, sb);
    }

    /* ---- randRange: full inclusive range, never out of bounds ---- */
    int seenMin = 999, seenMax = -1;
    for (int i = 0; i < 200000; i++) {
        int v = randRange(0, N_CHARS - 1);
        if (v < seenMin) seenMin = v;
        if (v > seenMax) seenMax = v;
        CHECK(v >= 0 && v < N_CHARS, "randRange out of bounds: %d", v);
        if (failures > 20) break;
    }
    CHECK(seenMin == 0 && seenMax == N_CHARS - 1,
          "randRange never hit bounds: min %d max %d", seenMin, seenMax);

    if (failures) {
        printf("\n%d FAILURES\n", failures);
        return 1;
    }
    printf("\nPASS: shipped Windows simulation matches the Swift rules "
           "(%d cells, %d drops, 2000 ticks verified)\n", total, g_numDrops);
    return 0;
}
