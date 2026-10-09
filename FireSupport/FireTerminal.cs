using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>One mission on the terminal: the program running it, the conversation it picked, and the words it fills in.</summary>
    internal sealed class TermSession
    {
        public TerminalScript.Group Group;
        public TerminalScript.Convo Convo;
        public string Unit;
        public bool Danger;
        public readonly Dictionary<string, string> Vars = new Dictionary<string, string>();

        public TermSession Set(string key, object value)
        {
            Vars[key.ToUpperInvariant()] = (value is float f ? f.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : value?.ToString() ?? "").ToUpperInvariant();
            return this;
        }
    }

    /// <summary>
    /// Fire support without a crew (prototypes, ArtyTerminal and AirTerminal): instead of the
    /// radio net (RadioLog), a DOS window where the net would be (the same place on screen and
    /// the same glyph size, but in 3D, turned toward you) spawns the unit, boots a program that
    /// runs it, runs the mission with it in the net's own order, and kills it and frees the unit
    /// when it's done. What is typed comes from TerminalScript (a text file the player can edit):
    /// each unit has one or more programs with their own personality and several conversations.
    ///
    /// Lines are scheduled, not queued: each starts when the one before is typed out (plus a
    /// gap), at its own pace (commands as typed, output at once, a program at its own rate), so
    /// a caller can tell when the call is done and start the mission's timeline from there
    /// (Play returns that lead). Several missions share the one window; their lines interleave.
    /// The window is a quad parented to the world camera on the viewmodel layer, its texture
    /// drawn per pixel with PixelFont's glyphs in the set's yellow, redrawn only when what it
    /// shows changes; the material is a copy of the binoculars' HUD material.
    /// </summary>
    internal static class FireTerminal
    {
        private const int W = 400, H = 100;
        private const int Rows = 9, RowY = 14, RowPitch = 9, MaxChars = 64;
        private const float OpenTime = 0.08f, CloseTime = 0.12f, Linger = 6f;
        private const string Prompt = "C:\\BA>";
        private const string Glitch = "#%@$&*?";
        private const char CutMark = '^';

        private enum Kind { Cmd, Sys, Ai, Obs }

        private sealed class Line
        {
            public Kind K;
            public string Pre = "", Text = "";
            public float Start, Rate;
            public int Cut = -1;              // killed after this many characters
            public bool Lead = true;          // the first row of a line (sound, status)
            public string Status;             // the title bar's state from here on
            public string Program;            // the title bar's program from here on
            public float Pitch = 1f;          // its squelch's
            public bool Flicker;              // its newest letter flickers through junk as it types
            public bool Sounded, CutSounded;
            public int Keyed;                 // keystrokes played (commands)
        }

        private static readonly List<Line> _lines = new List<Line>();
        private static float _tail, _closeAt = -1f, _openedAt = -10f;
        private static bool _active, _warned;
        private static string _status = "", _program = "";
        private static string _pendingStatus;

        private static GameObject _go;
        private static Material _mat;
        private static Texture2D _tex;
        private static Mesh _mesh;
        private static readonly PixelCanvas _cv = new PixelCanvas(W, H, 4);   // text cut 4 px short of the right edge
        private static long? _drawnKey;

        private static readonly Color32 ObsPre  = new Color32(140, 144, 150, 255);
        private static readonly Color32 ObsText = new Color32(222, 224, 226, 255);
        private static readonly Color32 Rim     = new Color32(90, 66, 10, 255);

        private static readonly string[] Words =
        {
            "NONE", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE", "TEN", "ELEVEN", "TWELVE",
            "THIRTEEN", "FOURTEEN", "FIFTEEN", "SIXTEEN", "SEVENTEEN", "EIGHTEEN", "NINETEEN", "TWENTY",
            "TWENTY-ONE", "TWENTY-TWO", "TWENTY-THREE", "TWENTY-FOUR",
        };

        /// <summary>A count as the programs say it: SIX, TWENTY-TWO, 202.</summary>
        public static string Word(int n) => n >= 0 && n < Words.Length ? Words[n] : n.ToString();

        /// <summary>The window can be drawn (the bundle has the material to copy).</summary>
        public static bool Available => Ensure();

        // ── Missions ────────────────────────────────────────────────────────────

        /// <summary>
        /// A mission for <paramref name="unit"/> (the strip's code) on the terminal, with one of
        /// its programs and one of that program's conversations; null if the window can't be
        /// drawn or the script has no program for the unit (the radio net it is, then).
        /// </summary>
        public static TermSession Begin(string unit, bool danger)
        {
            if (!Available) return null;
            var g = TerminalScript.For(unit);
            if (g == null) return null;
            var s = new TermSession { Group = g, Convo = TerminalScript.Pick(g), Unit = unit, Danger = danger };
            s.Set("PROGRAM", g.Program).Set("PREFIX", g.Prefix).Set("PID", Random.Range(0x100, 0xFFF).ToString("X3"))
             .Set("DANGER", danger ? "DANGER CLOSE. " : "").Set("UNIT", unit);
            if (Config.Dbg1) MelonLogger.Msg($"[Terminal] {unit}: {g.Program}, conversation \"{s.Convo?.Name}\"");
            return s;
        }

        /// <summary>
        /// Types <paramref name="steps"/> of the session's script, one after the other, after
        /// whatever the window is still typing. Returns the seconds from now until it's all typed.
        /// </summary>
        public static float Play(TermSession s, params string[] steps)
        {
            if (s == null) return 0f;
            foreach (var step in steps)
            {
                var lines = TerminalScript.Find(s.Group, s.Convo, step, s.Unit, s.Danger);
                if (lines == null) continue;
                foreach (var l in lines)
                {
                    string text = TerminalScript.Fill(l.Text, s.Vars);
                    if (l.K == TerminalScript.Kind.Status) { _pendingStatus = text; continue; }
                    Say(l.K == TerminalScript.Kind.Cmd ? Kind.Cmd : l.K == TerminalScript.Kind.Sys ? Kind.Sys : l.K == TerminalScript.Kind.Ai ? Kind.Ai : Kind.Obs, text, s.Group);
                }
            }
            return _active ? Mathf.Max(0f, _tail - Time.time) : 0f;
        }

        /// <summary>
        /// A call to a unit that is busy: the program on the line (<paramref name="live"/>, or a
        /// fresh one of <paramref name="unit"/>'s) turns it down, with the new call's words in
        /// <paramref name="call"/>. False if there is no program for it (use the radio net).
        /// </summary>
        public static bool Refuse(TermSession live, string unit, Dictionary<string, string> call)
        {
            var s = live ?? Begin(unit, false);
            if (s == null) return false;
            var r = new TermSession { Group = s.Group, Convo = s.Convo, Unit = s.Unit, Danger = false };
            foreach (var kv in s.Vars) r.Vars[kv.Key] = kv.Value;
            if (call != null) foreach (var kv in call) r.Vars[kv.Key] = kv.Value;
            r.Vars["DANGER"] = "";
            Play(r, "unable");
            return true;
        }

        public static void Clear()
        {
            _lines.Clear();
            _active = false;
            _tail = 0f;
            _closeAt = -1f;
            _status = "";
            _program = "";
            _pendingStatus = null;
            if (_go != null) _go.SetActive(false);
            _drawnKey = null;
        }

        // ── Scheduling ──────────────────────────────────────────────────────────

        private static float RateOf(Kind k, TerminalScript.Group g) => k switch
        {
            Kind.Cmd => 70f,
            Kind.Sys => 260f,
            Kind.Ai  => Mathf.Max(5f, Config.RadioTypeRate) * (g != null ? g.Rate : 0.8f),   // each program at its own pace
            _        => Mathf.Max(5f, Config.RadioTypeRate),
        };

        private static float GapAfter(Kind k) => k == Kind.Sys ? 0.06f : k == Kind.Cmd ? 0.15f : 0.35f;

        private static string PrefixOf(Kind k, TerminalScript.Group g) => k switch
        {
            Kind.Cmd => Prompt,
            Kind.Ai  => (g != null ? g.Prefix : "AI") + "> ",
            Kind.Obs => "OBS> ",
            _        => "",
        };

        /// <summary>
        /// A line, wrapped to the window, starting when the last one is done. A ^ in the text is
        /// where the line is killed: it stops there with noise.
        /// </summary>
        private static void Say(Kind k, string text, TerminalScript.Group g)
        {
            float now = Time.time;
            if (!_active)
            {
                _active = true;
                _openedAt = now;
                _tail = now + OpenTime;
            }
            _tail = Mathf.Max(_tail, now);
            text = text.ToUpperInvariant();
            int cut = text.IndexOf(CutMark);
            if (cut >= 0) text = text.Remove(cut, 1).Replace(CutMark.ToString(), "");
            string pre = PrefixOf(k, g);
            float rate = RateOf(k, g);
            int room = Mathf.Max(8, MaxChars - pre.Length);
            string status = _pendingStatus;
            _pendingStatus = null;

            bool lead = true;
            while (true)
            {
                string part = text;
                if (part.Length > room)
                {
                    int at = text.LastIndexOf(' ', room);
                    if (at <= 0) at = room;
                    part = text.Substring(0, at);
                    text = text.Substring(at).TrimStart();
                }
                else text = "";

                int lineCut = cut >= 0 && cut < part.Length ? cut : -1;
                var l = new Line
                {
                    K = k, Pre = lead ? pre : new string(' ', pre.Length), Text = part, Start = _tail, Rate = rate, Cut = lineCut,
                    Lead = lead, Status = lead ? status : null, Program = g?.Program,
                    Pitch = g != null ? g.Pitch : 1f, Flicker = k == Kind.Ai && g != null && g.Flicker,
                };
                _lines.Add(l);
                _tail += (lineCut >= 0 ? lineCut : part.Length) / rate;
                if (cut >= 0) cut -= part.Length;
                lead = false;
                if (text.Length == 0 || lineCut >= 0) break;
            }
            _tail += GapAfter(k);
            while (_lines.Count > 64) _lines.RemoveAt(0);
            _closeAt = _tail + Linger;
        }

        private static int Typed(Line l, float now)
        {
            int n = Mathf.Clamp(Mathf.FloorToInt((now - l.Start) * l.Rate), 0, l.Text.Length);
            return l.Cut >= 0 ? Mathf.Min(n, l.Cut) : n;
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        public static void Tick()
        {
            if (!_active) return;
            float now = Time.time;

            foreach (var l in _lines)
            {
                if (now < l.Start) break;
                if (!l.Sounded)
                {
                    l.Sounded = true;
                    if (l.Status != null) _status = l.Status;
                    if (l.Program != null) _program = l.Program;
                    if (l.Lead && l.K == Kind.Ai) { var s = Sfx.PlayHeld("RadioSquelch", 0.8f); if (s != null) s.pitch *= l.Pitch; }
                    else if (l.Lead && l.K == Kind.Obs) Sfx.PlayHeld("RadioSquelch");
                }
                if (l.K == Kind.Cmd)
                {
                    int typed = Typed(l, now);
                    for (; l.Keyed < typed; l.Keyed++) if (l.Keyed % 2 == 0) Sfx.PlayHeld("Keystroke", 0.4f);
                }
                if (l.Cut >= 0 && !l.CutSounded && Typed(l, now) >= l.Cut && now >= l.Start + l.Cut / l.Rate)
                {
                    l.CutSounded = true;
                    Sfx.PlayHeld("Glitch", 0.5f);
                }
            }

            if (_go == null && !Ensure()) return;
            float shut = Mathf.Clamp01((now - _closeAt) / CloseTime);
            if (now >= _closeAt + CloseTime)
            {
                _go.SetActive(false);
                _active = false;
                _lines.Clear();
                _status = "";
                _program = "";
                return;
            }

            Place(now, shut);
            long key = Key(now);
            if (key != _drawnKey) { Draw(now); _drawnKey = key; }
        }

        /// <summary>
        /// Where the radio log is drawn (RadioLog.Draw: 2 % in from the left, its bottom at 70 %
        /// down, its font pixel the window's texel times ArtyTermScale), at ArtyTermDistance, then
        /// turned to the eye and ArtyTermYaw about its upright. The turn brings the outer edge
        /// nearer, so it projects further out than the flat rectangle: the left corners are
        /// projected and the window pushed right until they sit on the log's left margin.
        /// </summary>
        private static void Place(float now, float shut)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (_go.transform.parent != cam.transform) _go.transform.SetParent(cam.transform, false);
            int layer = ViewmodelCamera.Layer(cam);
            if (layer >= 0) { ViewmodelCamera.Sync(); if (_go.layer != layer) _go.layer = layer; }
            float fov = cam.fieldOfView;

            float sw = Screen.width, sh = Screen.height;
            float px = Mathf.Max(1.5f, Mathf.Round(sh / 480f) * Mathf.Clamp(Config.RadioTextSize, 0.4f, 2f));
            float d = Mathf.Max(0.05f, Config.ArtyTermDistance);
            float mpp = 2f * d * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / sh;   // metres per screen pixel at d
            float scale = Mathf.Clamp(Config.ArtyTermScale, 0.3f, 1.5f);
            float w = W * px * scale, h = H * px * scale;
            float left = Mathf.Round(sw * 0.02f) - 3f * px;
            float bottomFromTop = Mathf.Round(sh * 0.70f) + 2f * px;
            float cx = left + w * 0.5f, cy = sh - (bottomFromTop - h * 0.5f);
            var local = new Vector3((cx - sw * 0.5f) * mpp, (cy - sh * 0.5f) * mpp, d);

            float open = Mathf.Clamp01((now - _openedAt) / OpenTime);
            float hs = 1f - (1f - open) * (1f - open);                    // ease out
            hs = Mathf.Lerp(hs, 0.02f, shut);                              // into a line...
            float ws = shut > 0.6f ? Mathf.Lerp(1f, 0.01f, (shut - 0.6f) / 0.4f) : 1f;   // ...then a dot
            var rot = Quaternion.LookRotation(local, Vector3.up) * Quaternion.Euler(0f, Config.ArtyTermYaw, 0f);

            // Keep the near edge on screen: project the full-size window's left corners (pinhole,
            // f in pixels) and move it right by what the nearer one overshoots the margin.
            float f = sh * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            Vector3 halfW = rot * new Vector3(w * mpp * 0.5f, 0f, 0f), halfH = rot * new Vector3(0f, h * mpp * 0.5f, 0f);
            float want = left - sw * 0.5f, worst = float.MaxValue, worstZ = d;
            for (int k = 0; k < 2; k++)
            {
                var c = k == 0 ? local - halfW + halfH : local - halfW - halfH;
                if (c.z <= 0.01f) continue;
                float x = c.x / c.z * f;
                if (x < worst) { worst = x; worstZ = c.z; }
            }
            if (worst < want) local.x += (want - worst) * worstZ / f;

            _go.transform.localPosition = local;
            _go.transform.localRotation = rot;
            _go.transform.localScale = new Vector3(w * mpp * ws, h * mpp * Mathf.Max(0.02f, hs), 1f);
            if (!_go.activeSelf) _go.SetActive(true);
        }

        private static long Key(float now)
        {
            int shown = 0, typed = 0;
            bool flickering = false;
            foreach (var l in _lines)
            {
                if (now < l.Start) break;
                shown++;
                typed = Typed(l, now);
                flickering = l.Flicker && typed < (l.Cut >= 0 ? l.Cut : l.Text.Length);
            }
            bool blink = (now * 2.5f % 1f) < 0.5f;
            int flicker = flickering ? Mathf.FloorToInt(now * 20f) : 0;
            return new DrawKey().Add(shown).Add(typed).Add(blink).Add(_status).Add(_program).Add(flicker).Value;
        }

        // ── Drawing ─────────────────────────────────────────────────────────────

        private static void Draw(float now)
        {
            _cv.Clear(DosTerminal.Back);
            _cv.Outline(0, 0, W, H, Rim);
            _cv.Fill(1, 1, W - 2, 10, DosTerminal.Bar);
            _cv.Text(string.IsNullOrEmpty(_program) ? "FIRE.NET" : "FIRE.NET  " + _program, 4, 3, DosTerminal.Ink);
            _cv.Text(_status, W - 4 - PixelCanvas.Width(_status), 3, DosTerminal.Ink);

            // The lines out so far (they start in order), the last Rows of them.
            int shown = 0;
            while (shown < _lines.Count && now >= _lines[shown].Start) shown++;
            int first = Mathf.Max(0, shown - Rows);
            for (int i = first; i < shown; i++)
            {
                var l = _lines[i];
                int y = RowY + (i - first) * RowPitch;
                Color32 pc = l.K == Kind.Obs ? ObsPre : l.K == Kind.Ai ? DosTerminal.Bar : l.K == Kind.Sys ? DosTerminal.Dim : DosTerminal.Amber;
                Color32 tc = l.K == Kind.Obs ? ObsText : l.K == Kind.Sys ? DosTerminal.Dim : DosTerminal.Amber;
                _cv.Text(l.Pre, 4, y, pc);
                int tx = 4 + l.Pre.Length * (PixelFont.GW + 1);
                int typed = Typed(l, now);
                string text = l.Text.Substring(0, typed);
                // Some programs' voices don't quite settle as they come: the newest letter flickers.
                bool typing = typed < (l.Cut >= 0 ? l.Cut : l.Text.Length);
                if (l.Flicker && typing && typed > 0 && TexelFight.Rand() < 0.5f)
                    text = text.Substring(0, typed - 1) + Glitch[(int)(TexelFight.Rand() * Glitch.Length) % Glitch.Length];
                // Killed mid-word: the rest of the line is noise.
                if (l.Cut >= 0 && typed >= l.Cut && now >= l.Start + l.Cut / l.Rate)
                    text += new string(Glitch[(int)(l.Start * 97f) % Glitch.Length], 2);
                _cv.Text(text, tx, y, tc);
                bool newest = i == shown - 1;
                if (typing || (newest && (now * 2.5f % 1f) < 0.5f))
                    _cv.Text("_", tx + typed * (PixelFont.GW + 1), y, tc);
            }

            _cv.Upload(_tex);
        }

        private static bool Ensure()
        {
            if (_go != null && _mat != null) return true;
            var cam = Camera.main;
            var src = DosTerminal.HudSource();
            if (cam == null || src == null)
            {
                if (!_warned && src == null) MelonLogger.Warning("[Terminal] no window: the binoculars' HUD material isn't in the bundle; the radio net it is");
                _warned = true;
                return false;
            }
            if (_tex == null)
            {
                _tex = PixelCanvas.NewTexture(W, H);
                _tex.name = "BA_FireTerminal";
            }
            if (_mat == null)
            {
                _mat = new Material(src) { hideFlags = HideFlags.DontUnloadUnusedAsset };
                PixelCanvas.SetTex(_mat, _tex);
            }
            if (_mesh == null) _mesh = PixelCanvas.Quad(0.5f, 0.5f);   // a unit quad; Place scales it to the window's size
            _go = new GameObject("BA_FireTerminal");
            _go.transform.SetParent(cam.transform, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _go.SetActive(false);
            _drawnKey = null;
            return true;
        }
    }
}
