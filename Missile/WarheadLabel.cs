using UnityEngine;
using Color = UnityEngine.Color;

namespace BombsAway
{
    /// <summary>
    /// The warhead stencil on a held launcher's tube ("HEAT" / "HE"). It is paint, and it changes
    /// anyway: the game world being edited from its own command line. With WarheadLabelGlitch
    /// off, a DOS cursor backspaces the word and types the new one; with it on, the stencil
    /// corrupts (torn rows, colour fringes, junk glyphs, static) and resolves as the other word.
    /// Drawn per texel into the label part's own texture, the same 4.5 mm texel as the rest of
    /// the model; the base map is alpha-cut, and a second texture lights only the glitch.
    /// The part comes from the launcher's build script (WarheadLabel, LABEL_W x LABEL_H).
    /// </summary>
    internal sealed class WarheadLabel
    {
        private const int W = 24, H = 7;          // javelin_launcher_build.py LABEL_W / LABEL_H
        private const int Cell = PixelFont.SW + 1;   // PixelFont's small glyphs, 1 texel apart
        private const int Top = H - 2;            // glyph row 0 (its top); the cursor sits on row 0

        // The stencil's hazard yellow (the atlas bytes), the HUD's cyan and the LED red.
        private static readonly Color32 Paint = new Color32(199, 143, 0, 255);
        private static readonly Color32 Cyan = new Color32(89, 255, 242, 255);
        private static readonly Color32 Red = new Color32(158, 20, 15, 255);
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private static readonly Color32 NoGlow = new Color32(0, 0, 0, 255);

        private Texture2D _tex, _glow;
        private Material _mat;
        private readonly Color32[] _px = new Color32[W * H];
        private readonly Color32[] _em = new Color32[W * H];

        private string _shown = "";               // what the stencil says right now
        private string _target = "";
        private bool _glitchStyle;

        // DOS: lead (cursor shows up) -> delete -> gap -> type -> tail (cursor blinks, goes).
        private enum Phase { Idle, Lead, Delete, Gap, Type, Tail, Glitch }
        private Phase _phase = Phase.Idle;
        private float _wait, _clock;
        private string _glitchFrom = "";
        private int _step = -1;

        public static WarheadLabel Attach(Transform launcher, string text)
        {
            var part = launcher.Find("WarheadLabel");
            var r = part != null ? part.GetComponent<Renderer>() : null;
            if (r == null || r.sharedMaterial == null) return null;

            var l = new WarheadLabel();
            l._tex = NewTexture("BA_WarheadLabel");
            l._glow = NewTexture("BA_WarheadLabelGlow");
            l._mat = new Material(r.sharedMaterial);
            l._mat.SetTexture("_BaseMap", l._tex);
            l._mat.SetTexture("_EmissionMap", l._glow);
            l._mat.SetColor("_EmissionColor", new Color(1.6f, 1.6f, 1.6f, 1f));
            l._mat.EnableKeyword("_EMISSION");
            r.sharedMaterial = l._mat;
            l._shown = l._target = text;
            l.Draw(text, -1, null);
            return l;
        }

        private static Texture2D NewTexture(string name) =>
            new Texture2D(W, H, TextureFormat.RGBA32, false)
            { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

        /// <summary>
        /// Lets go of the label as it is (a spent tube taking it into the world): returns what it
        /// made, for whoever destroys the tube to destroy too.
        /// </summary>
        public Object[] Orphan()
        {
            var owned = new Object[] { _mat, _tex, _glow };
            _mat = null; _tex = _glow = null;
            return owned;
        }

        public void Dispose()
        {
            if (_mat != null) Object.Destroy(_mat);
            if (_tex != null) Object.Destroy(_tex);
            if (_glow != null) Object.Destroy(_glow);
            _mat = null; _tex = _glow = null;
        }

        public void Tick(float dt, string want)
        {
            if (_tex == null) return;
            _clock += dt;
            if (want != _target) Retarget(want);

            switch (_phase)
            {
                case Phase.Idle: return;
                case Phase.Glitch: TickGlitch(); return;
            }

            _wait -= dt;
            if (_wait <= 0f)
            {
                switch (_phase)
                {
                    case Phase.Lead:
                        _phase = Phase.Delete; _wait = Key(0.05f); break;
                    case Phase.Delete:
                        if (_shown.Length > 0) { _shown = _shown.Substring(0, _shown.Length - 1); _wait = Key(0.045f); Sfx.PlayHeld("Keystroke", 0.8f); }
                        else { _phase = Phase.Gap; _wait = 0.16f; }
                        break;
                    case Phase.Gap:
                        _phase = Phase.Type; _wait = Key(0.07f); break;
                    case Phase.Type:
                        if (_shown.Length < _target.Length)
                        {
                            _shown = _target.Substring(0, _shown.Length + 1);
                            Sfx.PlayHeld("Keystroke");
                            // Now and then a hunt for the next key.
                            _wait = Random.value < 0.2f ? Key(0.16f) : Key(0.07f);
                        }
                        else { _phase = Phase.Tail; _wait = 0.65f; }
                        break;
                    case Phase.Tail:
                        _phase = Phase.Idle; break;
                }
            }

            // Solid while it works, blinking at DOS's ~2 Hz while it waits.
            bool working = _phase == Phase.Delete || _phase == Phase.Type;
            bool on = _phase != Phase.Idle && (working || (_clock % 0.5f) < 0.27f);
            Draw(_shown, on ? _shown.Length : -1, null);
        }

        private static float Key(float mean) => mean * Random.Range(0.6f, 1.5f);

        private void Retarget(string want)
        {
            _target = want;
            _glitchStyle = Config.WarheadLabelGlitch;
            if (_glitchStyle)
            {
                // From whatever it reads as now, even mid-way through the last one.
                _glitchFrom = _shown;
                _phase = Phase.Glitch;
                Sfx.PlayHeld("Glitch");
                _clock = 0f;
                _step = -1;
                return;
            }
            switch (_phase)
            {
                case Phase.Idle: case Phase.Tail: case Phase.Glitch:
                    _phase = Phase.Lead; _wait = 0.14f; _clock = 0f; break;
                case Phase.Type:
                    _phase = Phase.Delete; _wait = Key(0.06f); break;   // wrong word: take it back
            }
        }

        // ── Glitch ──────────────────────────────────────────────────────────────

        private const float GlitchTime = 0.5f, GlitchStep = 1f / 24f;

        private void TickGlitch()
        {
            // Stepped, not smooth: a new corruption each "frame" of a broken signal.
            int step = (int)(_clock / GlitchStep);
            if (step == _step) return;
            _step = step;

            float p = _clock / GlitchTime;
            if (p >= 1f)
            {
                _shown = _target;
                _phase = Phase.Idle;
                Draw(_shown, -1, null);
                return;
            }

            // Worst in the middle, where the word flips; never quite the same twice.
            float amount = Mathf.Clamp01(1f - Mathf.Abs(2f * p - 1f) + Random.Range(-0.15f, 0.3f));
            _shown = p < 0.5f ? _glitchFrom : _target;
            Draw(_shown, -1, amount);
        }

        // ── Drawing ─────────────────────────────────────────────────────────────

        /// <summary>The text (and a cursor after cell <paramref name="cursor"/>), corrupted by <paramref name="glitch"/> 0..1.</summary>
        private void Draw(string text, int cursor, float? glitch)
        {
            for (int i = 0; i < _px.Length; i++) { _px[i] = Clear; _em[i] = NoGlow; }
            float g = glitch ?? 0f;

            int cells = text.Length;
            if (g > 0f)   // the word's length flickers too: junk cells past its end
                cells = Mathf.Max(cells, Mathf.Max(_glitchFrom.Length, _target.Length) - (Random.value < 0.5f ? 1 : 0));

            // Colour fringes first, under the paint: a red ghost one way, a cyan one the other.
            if (g > 0.3f)
            {
                int dx = Random.value < 0.7f ? 1 : 2;
                Glyphs(text, cells, g, dx, 0, Red, true);
                Glyphs(text, cells, g, -dx, Random.value < 0.3f ? 1 : 0, Cyan, true);
            }
            Glyphs(text, cells, g, 0, 0, Paint, false);

            if (cursor >= 0)
                for (int x = 0; x < PixelFont.SW; x++) Put(1 + cursor * Cell + x, 0, Paint, false);

            if (g > 0f)
            {
                // Torn rows: whole lines slide sideways.
                for (int y = 0; y < H; y++)
                    if (Random.value < g * 0.45f) ShiftRow(y, Random.Range(-3, 4));
                // Static.
                for (int i = 0; i < _px.Length; i++)
                    if (Random.value < g * 0.05f)
                    {
                        var c = Random.value < 0.5f ? Cyan : Paint;
                        _px[i] = c; _em[i] = c;
                    }
            }

            _tex.SetPixels32(_px);
            _tex.Apply(false);
            _glow.SetPixels32(_em);
            _glow.Apply(false);
        }

        private void Glyphs(string text, int cells, float g, int dx, int dy, Color32 c, bool glow)
        {
            for (int i = 0; i < cells; i++)
            {
                string bits = i < text.Length ? PixelFont.SmallBits(text[i]) : null;
                bool junk = bits == null || (g > 0f && Random.value < g * 0.55f);
                for (int r = 0; r < PixelFont.SH; r++)
                    for (int x = 0; x < PixelFont.SW; x++)
                    {
                        bool lit = junk ? Random.value < 0.45f : bits[r * PixelFont.SW + x] == '#';
                        if (!lit || (g > 0f && Random.value < g * 0.2f)) continue;   // dropout
                        Put(1 + i * Cell + x + dx, Top - r + dy, c, glow);
                    }
            }
        }

        private void Put(int x, int y, Color32 c, bool glow)
        {
            if (x < 0 || x >= W || y < 0 || y >= H) return;
            int i = y * W + x;
            if (!glow)
            {
                // The atlas's per-texel grain, so the stencil sits in the paint around it.
                float f = 1f + 0.06f * (Hash(x, y) * 2f - 1f);
                c = new Color32((byte)Mathf.Min(255, c.r * f), (byte)Mathf.Min(255, c.g * f), (byte)Mathf.Min(255, c.b * f), 255);
            }
            _px[i] = c;
            _em[i] = glow ? c : NoGlow;
        }

        private void ShiftRow(int y, int by)
        {
            if (by == 0) return;
            var row = new Color32[W]; var erow = new Color32[W];
            for (int x = 0; x < W; x++)
            {
                int sx = x - by;
                bool inside = sx >= 0 && sx < W;
                row[x] = inside ? _px[y * W + sx] : Clear;
                erow[x] = inside ? _em[y * W + sx] : NoGlow;
            }
            for (int x = 0; x < W; x++) { _px[y * W + x] = row[x]; _em[y * W + x] = erow[x]; }
        }

        private static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }
    }
}
