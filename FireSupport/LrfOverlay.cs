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
        public bool OffMark;         // the reticle has wandered off the lased point: the fill pauses
        public Vector3 LasePoint;    // the point being lased
        public bool NoReturn;        // lasing at nothing
        public bool HasReading;      // a range to show (lasing now, or the last fix)
        public float Range, AzimuthMils, ElevationMils;
        public string Mission;       // the mission type ("155MM HE")
        public string MissionLine;   // under the strip: "155MM HE  6 RDS"
        public int MissionIndex;     // the chosen slot on the strip (FireMission.CodeAt)
        public int Rounds;
        public float ZoomLevel;      // the chosen magnification (the screen shows "7X")
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
            DrawStrip(s, cx, top - (PixelFont.GH + 8) * fpx, fpx, led, dim);
            PixelFont.DrawCentred(s.MissionLine ?? s.Mission, cx, top, fpx, led);

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
                bool blink = (Time.unscaledTime * 4f % 1f) < 0.6f;
                for (int i = 0; i < Segs; i++)
                    PixelFont.Box(x0 + i * (segW + gap), y0, segW, 2f * px, i < lit && (!s.OffMark || blink) ? led : dim);
                if (s.OffMark) PixelFont.DrawCentred("ON TGT", cx, y0 + 4f * px, fpx, blink ? led : dim);
                else if (blink) PixelFont.DrawCentred("LASING", cx, y0 + 4f * px, fpx, led);
                Spot(cam, s.LasePoint, px, s.OffMark ? led : dim);
            }
            else if (s.MarkStatus != null)
                PixelFont.DrawCentred(s.MarkStatus, cx, bottom + (PixelFont.GH + 4) * fpx, fpx, led);

            if (s.HasMark) Diamond(cam, s.Mark, px, led, cx, cy);
        }

        /// <summary>The mission strip, as on the screen (LrfDisplay): a slot per type, the chosen one solid.</summary>
        private static void DrawStrip(in LrfState s, float cx, float y, float px, Color led, Color dim)
        {
            const int SlotH = 11;
            int page = s.MissionIndex >= 0 ? FireMission.PageOf(s.MissionIndex) : 0;
            var slots = new System.Collections.Generic.List<int>();
            for (int i = 0; i < FireMission.TypeCount; i++) if (FireMission.PageOf(i) == page) slots.Add(i);
            int n = slots.Count;
            var widths = FireMission.SlotWidths(slots, out int gap, out int totalPx);
            float total = totalPx * px, x0 = Mathf.Round(cx - total * 0.5f);
            // The pages in a row over it (ARTY, AIR, BOMB), the shown one lit, as on the screen.
            const int TabGap = 8;
            int tabs = (FireMission.PageCount - 1) * TabGap;
            for (int pg = 0; pg < FireMission.PageCount; pg++) tabs += FireMission.PageName(pg).Length * (PixelFont.GW + 1) - 1;
            float tx = Mathf.Round(cx - tabs * 0.5f * px), ty = y - (PixelFont.GH + 4) * px;
            for (int pg = 0; pg < FireMission.PageCount; pg++)
            {
                string name = FireMission.PageName(pg);
                int w = name.Length * (PixelFont.GW + 1) - 1;
                PixelFont.DrawCentred(name, tx + w * 0.5f * px, ty, px, pg == page ? led : dim);
                if (pg != page && PageRunning(pg)) PixelFont.Box(tx + (w / 2 - 2) * px, ty + (PixelFont.GH + 1) * px, 5f * px, px, led);
                tx += (w + TabGap) * px;
            }
            float x = x0;
            for (int j = 0; j < n; x += (widths[j] + gap) * px, j++)
            {
                int i = slots[j], sw = widths[j];
                string code = FireMission.CodeAt(i);
                bool chosen = i == s.MissionIndex;
                if (chosen) PixelFont.Box(x, y, sw * px, SlotH * px, led);
                else Outline(x, y, sw * px, SlotH * px, px, dim);
                PixelFont.DrawCentred(code, x + (sw * 0.5f + 0.5f) * px, y + 2f * px, px, chosen ? new Color(0.02f, 0.02f, 0.03f, led.a) : dim);
                if (FireMission.Running(FireMission.TypeAt(i))) PixelFont.Box(x + (sw / 2 - 2) * px, y + (SlotH + 1) * px, 5f * px, px, led);
            }
            bool lit = Time.unscaledTime - FireMission.SwitchedAt < 0.25f;
            PixelFont.DrawCentred(Config.MissionPrevKey.ToString(), x0 - 9f * px, y + 2f * px, px, lit && FireMission.SwitchedDir < 0 ? led : dim);
            PixelFont.DrawCentred(Config.MissionNextKey.ToString(), x0 + total + 9f * px, y + 2f * px, px, lit && FireMission.SwitchedDir > 0 ? led : dim);
        }

        private static bool PageRunning(int page)
        {
            for (int i = 0; i < FireMission.TypeCount; i++)
                if (FireMission.PageOf(i) == page && FireMission.Running(FireMission.TypeAt(i))) return true;
            return false;
        }

        /// <summary>The point being lased: four LED corners round it, so the eye can bring the reticle back.</summary>
        private static void Spot(Camera cam, Vector3 p, float px, Color c)
        {
            Vector3 sp = cam.WorldToScreenPoint(p);
            if (sp.z <= 0f) return;
            float x = Mathf.Round(sp.x), y = Mathf.Round(Screen.height - sp.y);
            float r = 4f * px, l = 2f * px;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    float ex = x + sx * r, ey = y + sy * r;
                    PixelFont.Box(sx < 0 ? ex : ex - l + px, ey - px * 0.5f, l, px, c);
                    PixelFont.Box(ex - px * 0.5f, sy < 0 ? ey : ey - l + px, px, l, c);
                }
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
