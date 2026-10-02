using System.Collections.Generic;
using UnityEngine;
using Color = UnityEngine.Color;

namespace BombsAway
{
    /// <summary>
    /// The fire-support net, bottom left: each transmission typed out at a DOS cursor, one after
    /// the other, with a squelch at the start of each. OBS is you, FDC the battery's fire
    /// direction centre. Lines stay a while after the last one and then fade.
    /// </summary>
    internal static class RadioLog
    {
        private const int MaxLines = 6;
        private const float Gap = 0.35f;     // between one transmission ending and the next starting
        private const float Linger = 9f;     // seconds the log stays after its last line is typed
        private const float FadeOut = 1.5f;

        private sealed class Line { public string Who, Text; public float Start = -1f; public bool Fdc; }
        private static readonly List<Line> _lines = new List<Line>();
        private static readonly Queue<Line> _queue = new Queue<Line>();
        private static float _freeAt, _lastDone;

        private static readonly Color Prefix = new Color(0.55f, 0.57f, 0.6f, 1f);
        private static readonly Color Obs = new Color(0.86f, 0.87f, 0.88f, 1f);
        private static readonly Color Fdc = new Color(1f, 0.74f, 0.1f, 1f);   // hazard yellow
        private static readonly Color Panel = new Color(0.04f, 0.045f, 0.05f, 0.62f);

        /// <summary>You on the net.</summary>
        public static void Observer(string text) => Say("OBS", text, false);

        /// <summary>The battery's fire direction centre.</summary>
        public static void Battery(string text) => Say("FDC", text, true);

        private static void Say(string who, string text, bool fdc) => _queue.Enqueue(new Line { Who = who, Text = text.ToUpperInvariant(), Fdc = fdc });

        public static bool Busy => _queue.Count > 0 || Time.time < _freeAt;

        private static float Rate => Mathf.Max(5f, Config.RadioTypeRate);

        public static void Tick()
        {
            float now = Time.time;
            if (_queue.Count > 0 && now >= _freeAt)
            {
                var l = _queue.Dequeue();
                l.Start = now;
                _lines.Add(l);
                while (_lines.Count > MaxLines) _lines.RemoveAt(0);
                _freeAt = now + l.Text.Length / Rate + Gap;
                _lastDone = _freeAt;
                Sfx.PlayHeld("RadioSquelch");
            }
        }

        public static void Clear()
        {
            _lines.Clear();
            _queue.Clear();
            _freeAt = _lastDone = 0f;
        }

        /// <summary>OnGUI.</summary>
        public static void Draw()
        {
            if (_lines.Count == 0) return;
            float now = Time.time;
            float alpha = 1f - Mathf.Clamp01((now - _lastDone - Linger) / FadeOut);
            if (alpha <= 0f && _queue.Count == 0) { _lines.Clear(); return; }

            float px = Mathf.Max(2f, Mathf.Round(Screen.height / 480f));
            float lineH = (PixelFont.GH + 4) * px;
            float x = Mathf.Round(Screen.width * 0.02f), w = 0f;
            foreach (var l in _lines) w = Mathf.Max(w, PixelFont.Width(l.Who + "> " + l.Text + "_", px));
            float h = _lines.Count * lineH + 3 * px;
            float y = Mathf.Round(Screen.height * 0.70f) - h;
            PixelFont.Box(x - 3 * px, y - 2 * px, w + 6 * px, h + 2 * px, new Color(Panel.r, Panel.g, Panel.b, Panel.a * alpha));

            for (int i = 0; i < _lines.Count; i++)
            {
                var l = _lines[i];
                int shown = Mathf.Clamp(Mathf.FloorToInt((now - l.Start) * Rate), 0, l.Text.Length);
                float ly = y + i * lineH;
                string pre = l.Who + "> ";
                PixelFont.Draw(pre, x, ly, px, Fade(Prefix, alpha));
                float tx = x + PixelFont.Width(pre, px) + px;
                PixelFont.Draw(l.Text.Substring(0, shown), tx, ly, px, Fade(l.Fdc ? Fdc : Obs, alpha));
                // The cursor: typing, or blinking after the newest line.
                bool typing = shown < l.Text.Length;
                bool last = i == _lines.Count - 1;
                if (typing || (last && now - _lastDone < Linger && (now * 2.5f % 1f) < 0.5f))
                    PixelFont.Draw("_", tx + shown * (PixelFont.GW + 1) * px, ly, px, Fade(l.Fdc ? Fdc : Obs, alpha));
            }
        }

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);
    }
}
