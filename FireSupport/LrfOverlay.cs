using UnityEngine;
using Color = UnityEngine.Color;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>What the rangefinder shows this frame.</summary>
    internal struct LrfState
    {
        public float Ads;            // 0..1, the eyepieces' fade
        public bool Lasing;
        public float Progress;       // 0..1 toward a fix
        public bool NoReturn;        // lasing at nothing
        public bool HasReading;      // a range to show (lasing now, or the last fix)
        public float Range, AzimuthMils, ElevationMils;
        public string Mission;       // the mission type ("ARTY 155 HE")
        public int Rounds;
        public bool HasMark;
        public Vector3 Mark;
        public string MarkStatus;    // "MSN 01 SPLASH 07"
    }

    /// <summary>
    /// The binoculars' picture, drawn over the zoomed view in OnGUI: the two eyepieces' circles
    /// (a point-filtered mask, chunky like the rest of the mod's screens), a mil reticle and the
    /// red LED readout of a laser rangefinder binocular: range, azimuth and elevation in mils,
    /// the mission type, the lase's progress, and a diamond over a called target.
    /// </summary>
    internal static class LrfOverlay
    {
        private static readonly Color Led = new Color(1f, 0.26f, 0.16f, 0.95f);
        private static readonly Color LedDim = new Color(0.55f, 0.12f, 0.08f, 0.85f);

        private static Texture2D _mask;
        private static int _maskW, _maskH, _maskPx;

        /// <summary>Screen pixels per pixel of the mask and the font.</summary>
        private static float Px => Mathf.Max(2f, Mathf.Round(Screen.height / 360f));

        public static void Draw(in LrfState s)
        {
            float a = Mathf.Clamp01(s.Ads * 1.6f);
            if (a <= 0f) return;
            var cam = Camera.main;
            float px = Px;
            float W = Screen.width, H = Screen.height;

            DrawMask(a, (int)px);
            if (s.Ads < 0.9f || cam == null) return;
            float k = Mathf.Clamp01((s.Ads - 0.9f) / 0.1f);
            Color led = Fade(Led, k), dim = Fade(LedDim, k);

            // Mils to screen pixels at the current field of view.
            float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float Mil(float mils) => (H * 0.5f) * Mathf.Tan(mils * (Mathf.PI * 2f / 6400f)) / tanHalf;
            float cx = Mathf.Round(W * 0.5f), cy = Mathf.Round(H * 0.5f);

            // The reticle: an open aiming box and a scale every 10 mils across and below.
            float box = Mathf.Max(3f * px, Mil(2.5f));
            Outline(cx - box, cy - box, box * 2f, box * 2f, px, led);
            for (int m = 10; m <= 50; m += 10)
            {
                float d = Mil(m), len = (m % 50 == 0 ? 5f : 3f) * px;
                PixelFont.Box(cx + d - px * 0.5f, cy - len * 0.5f, px, len, led);
                PixelFont.Box(cx - d - px * 0.5f, cy - len * 0.5f, px, len, led);
                if (m <= 40) PixelFont.Box(cx - len * 0.5f, cy + d - px * 0.5f, len, px, led);
            }
            PixelFont.Box(cx + box + 2f * px, cy - px * 0.5f, Mil(10f) - box - 4f * px, px, dim);
            PixelFont.Box(cx - Mil(10f) + 2f * px, cy - px * 0.5f, Mil(10f) - box - 4f * px, px, dim);

            // Mission type, top; the readout, bottom.
            float fpx = px;
            float top = cy - H * 0.36f, bottom = cy + H * 0.30f;
            PixelFont.DrawCentred($"{s.Mission}  {s.Rounds} RDS", cx, top, fpx, led);

            string line;
            if (s.NoReturn) line = (Time.unscaledTime * 3f % 1f) < 0.6f ? "NO RETURN" : "";
            else if (s.HasReading) line = $"RNG {Mathf.RoundToInt(s.Range):0000}M  AZ {Mathf.RoundToInt(s.AzimuthMils) % 6400:0000}  EL {(s.ElevationMils >= 0 ? "+" : "-")}{Mathf.Abs(Mathf.RoundToInt(s.ElevationMils)):000}";
            else line = "RNG ----M  AZ ----  EL ----";
            PixelFont.DrawCentred(line, cx, bottom, fpx, led);

            if (s.Lasing && !s.NoReturn)
            {
                // Twelve segments filling toward the fix, under the readout.
                const int Segs = 12;
                float segW = 4f * px, gap = px, total = Segs * segW + (Segs - 1) * gap;
                float x0 = cx - total * 0.5f, y0 = bottom + (PixelFont.GH + 4) * fpx;
                int lit = Mathf.FloorToInt(s.Progress * Segs);
                for (int i = 0; i < Segs; i++)
                    PixelFont.Box(x0 + i * (segW + gap), y0, segW, 2f * px, i < lit ? led : dim);
                if ((Time.unscaledTime * 4f % 1f) < 0.6f) PixelFont.DrawCentred("LASING", cx, y0 + 4f * px, fpx, led);
            }
            else if (s.MarkStatus != null)
                PixelFont.DrawCentred(s.MarkStatus, cx, bottom + (PixelFont.GH + 4) * fpx, fpx, led);

            if (s.HasMark) Diamond(cam, s.Mark, px, led, cx, cy);
        }

        /// <summary>The called target: a blinking diamond over it and its range.</summary>
        private static void Diamond(Camera cam, Vector3 mark, float px, Color c, float cx, float cy)
        {
            Vector3 sp = cam.WorldToScreenPoint(mark);
            if (sp.z <= 0f) return;
            float x = Mathf.Round(sp.x), y = Mathf.Round(Screen.height - sp.y);
            if ((Time.unscaledTime * 2f % 1f) > 0.75f) return;
            int s = 5;
            for (int i = -s; i <= s; i++)
            {
                int dy = s - Mathf.Abs(i);
                PixelFont.Box(x + i * px - px * 0.5f, y - dy * px - px * 0.5f, px, px, c);
                PixelFont.Box(x + i * px - px * 0.5f, y + dy * px - px * 0.5f, px, px, c);
            }
            string r = $"TGT {Mathf.RoundToInt(Vector3.Distance(cam.transform.position, mark)):0000}M";
            PixelFont.Draw(r, x + (s + 3) * px, y - PixelFont.GH * px * 0.5f, px, c);
        }

        private static void Outline(float x, float y, float w, float h, float t, Color c)
        {
            PixelFont.Box(x, y, w, t, c);
            PixelFont.Box(x, y + h - t, w, t, c);
            PixelFont.Box(x, y, t, h, c);
            PixelFont.Box(x + w - t, y, t, h, c);
        }

        /// <summary>
        /// The eyepieces: black outside two overlapping circles, a dark rim stippled inward,
        /// clear inside. Rebuilt when the screen size changes.
        /// </summary>
        private static void DrawMask(float alpha, int px)
        {
            int w = Mathf.CeilToInt(Screen.width / (float)px), h = Mathf.CeilToInt(Screen.height / (float)px);
            if (_mask == null || w != _maskW || h != _maskH || px != _maskPx) BuildMask(w, h, px);
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, w * px, h * px), _mask);
            GUI.color = prev;
        }

        private static void BuildMask(int w, int h, int px)
        {
            if (_mask != null) Object.Destroy(_mask);
            _maskW = w; _maskH = h; _maskPx = px;
            _mask = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            float r = h * 0.47f, off = r * 0.52f;
            float lx = w * 0.5f - off, rx = w * 0.5f + off, my = h * 0.5f;
            var pxs = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float px0 = x + 0.5f, py0 = y + 0.5f;
                    float dl = Mathf.Sqrt((px0 - lx) * (px0 - lx) + (py0 - my) * (py0 - my));
                    float dr = Mathf.Sqrt((px0 - rx) * (px0 - rx) + (py0 - my) * (py0 - my));
                    float inside = r - Mathf.Min(dl, dr);   // depth inside the nearer circle
                    byte a;
                    if (inside <= 0f) a = 255;
                    else if (inside < 1.5f) a = 200;
                    else if (inside < 4f) a = ((x + y) & 1) == 0 ? (byte)140 : (byte)60;
                    else if (inside < 7f) a = ((x & 1) == 0 && (y & 1) == 0) ? (byte)70 : (byte)0;
                    else a = 0;
                    pxs[y * w + x] = new Color32(0, 0, 0, a);
                }
            _mask.SetPixels32(pxs);
            _mask.Apply(false);
        }

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);
    }
}
