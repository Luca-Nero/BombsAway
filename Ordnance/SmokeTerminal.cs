using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The smoke can's colour change, done from the command line like the launchers' warhead
    /// stencil: the DOS window (Effects/DosTerminal.cs) pops up beside the can in hand, types
    /// DYE /SET, and while it runs the M18's real dye fill for that colour scrolls past and the
    /// band is fought over texel by texel (BandPixels) for SmokeRetintTime until the new colour
    /// holds every texel. Then BAND LOCKED, and the window folds away. With SmokeTerminal off
    /// the band is still fought over, without the window.
    /// </summary>
    internal sealed class SmokeTerminal
    {
        // The M18's dye fill per colour: the dyes of the current (sugar-based) formulas and
        // what burns to carry them (Wikipedia, "M18 smoke grenade", its old/new mixture table).
        // White isn't an M18 colour: that's the HC can's fill.
        private static readonly Dictionary<string, string[]> Dyes = new Dictionary<string, string[]>
        {
            ["WHITE"]  = new[] { "HC HEXACHLOROETHANE",       "ZINC OXIDE + AL GRAIN",     "ZNCL2 AEROSOL" },
            ["RED"]    = new[] { "SOLVENT RED 1       34.2%", "DISPERSE RED 11      6.8%", "KCLO3+SUGAR+MGCO3" },
            ["GREEN"]  = new[] { "SOLVENT GREEN 3     29.4%", "SOLVENT YELLOW 33   12.6%", "KCLO3+SUGAR+MGCO3" },
            ["VIOLET"] = new[] { "DISPERSE RED 11     38.0%", "TEREPHTHALIC ACID    7.6%", "KCLO3+SUGAR+MGCO3" },
            ["YELLOW"] = new[] { "SOLVENT YELLOW 33   42.0%", "KCLO3               24.1%", "SUGAR+MGCO3" },
        };

        private readonly DosTerminal _term = new DosTerminal { Title = "M18 DYE.EXE", DoneWord = "BAND LOCKED" };
        private readonly BandPixels _band = new BandPixels();
        private MaterialPropertyBlock _block;
        private Color _from, _to;

        public SmokeTerminal() { _term.OnRound = p => _band.Round(p); }

        /// <summary>The colour that holds most of the band right now.</summary>
        public Color Colour => _term.Progress < 0.5f ? _from : _to;

        /// <summary>The band is being fought over (PaintBand draws it).</summary>
        public bool Running => _term.Running;

        public bool Active => _term.Active;

        /// <summary>
        /// Starts a change to <paramref name="to"/>. <paramref name="from"/> is the colour the band
        /// had; if a change is still running, each texel fights on from wherever it is.
        /// </summary>
        public void Begin(Color from, Color to, string name, Renderer band)
        {
            bool midway = Running, open = _term.Open;
            _from = from; _to = to;
            var fill = Dyes.TryGetValue(name, out var f) ? f : new[] { "DYE " + name, "", "" };
            _term.Label = name;
            _term.Command = $"DYE /SET {name}";
            _term.Script = new[]
            {
                "LOAD M18.DYE ........ OK",
                " " + fill[0],
                " " + fill[1],
                " " + fill[2],
                $"BAND.TINT {Hex(from)}>{Hex(to)}",
                "WRITE BAND[]: CONTESTED",
            };
            Color32 a = from, b = to;
            a.a = b.a = 255;
            _term.CellOld = a; _term.CellNew = b; _term.CellStatic = DosTerminal.Amber;
            _band.Begin(band, from, to, midway);
            _term.Begin(Config.SmokeRetintTime, Config.SmokeTerminal, midway);
            if (!open) Sfx.PlayHeld("CluClick", _term.Windowed ? 0.7f : 1f);   // a window already up just retypes
        }

        /// <summary>Folds the window away now (the can left the hand); the new colour wins at once.</summary>
        public void Close() => _term.Close();

        /// <summary>Every LateUpdate. <paramref name="can"/> is the can in hand (null once it's gone).</summary>
        public void Tick(Camera cam, Transform can, int layer) =>
            _term.Tick(cam, can != null ? can.position : (Vector3?)null,
                new Vector3(Config.SmokeTermOffsetX, Config.SmokeTermOffsetY, Config.SmokeTermOffsetZ), Config.SmokeTermYaw, layer);

        /// <summary>
        /// Draws the band in hand: the fight while it runs (true), or nothing to do (false: the
        /// caller tints it the chosen colour as usual).
        /// </summary>
        public bool PaintBand(Renderer band)
        {
            if (band == null || !Running) return false;
            if (_block == null) _block = new MaterialPropertyBlock();
            _block.Clear();
            if (_band.Texture != null)
            {
                _block.SetTexture("_BaseMap", _band.Texture);
                _block.SetColor("_BaseColor", Color.white);
            }
            else _block.SetColor("_BaseColor", _band.Flat);   // no copy of the texture: the whole band flickers
            band.SetPropertyBlock(_block);
            return true;
        }

        public void Dispose()
        {
            _term.Dispose();
            _band.Dispose();
        }

        private static string Hex(Color c)
        {
            Color32 b = c;
            return $"{b.r:X2}{b.g:X2}{b.b:X2}";
        }
    }

    /// <summary>
    /// The band fought over texel by texel (TexelFight's rule). The can's atlas is read once
    /// (TexelFight.Read); the band gets its own texture, each texel the atlas times its own
    /// colour, so only the band's renderer sees it and the rest of the can is untouched.
    /// Multiplying sRGB bytes matches the shader's tint (a power law keeps products).
    /// </summary>
    internal sealed class BandPixels
    {
        private static readonly Color Static = new Color(1f, 0.73f, 0.1f);   // hazard yellow, lit

        private Color32[] _atlas;
        private int _w, _h;
        private bool _readTried;

        private Texture2D _tex;
        private Color32[] _px;
        private Color[] _old, _cur;           // per texel: what it fights from, what it shows
        private float[] _claim;               // per texel: the progress it gives in at
        private Color _to;

        /// <summary>The band's own texture while it is fought over; null if the atlas couldn't be read.</summary>
        public Texture2D Texture => _tex;

        /// <summary>The whole band's colour this round, without a texture.</summary>
        public Color Flat { get; private set; }

        public void Begin(Renderer band, Color from, Color to, bool midway)
        {
            _to = to;
            Flat = from;
            if (!Ensure(band)) return;
            int n = _w * _h;
            for (int i = 0; i < n; i++)
            {
                // A second change mid-way fights on from whatever each texel shows.
                _old[i] = midway ? _cur[i] : from;
                if (!midway) _cur[i] = from;
                _claim[i] = Random.Range(0.02f, 0.98f);
            }
        }

        /// <summary>One round of the fight at progress <paramref name="p"/>.</summary>
        public void Round(float p)
        {
            float g = TexelFight.Contest(p);
            if (_tex == null)
            {
                Flat = TexelFight.Taken(p, g, 0.5f) ? _to : Flat;
                return;
            }
            int n = _w * _h;
            for (int i = 0; i < n; i++)
                _cur[i] = TexelFight.Taken(p, g, _claim[i]) ? _to : _old[i];

            for (int y = 0; y < _h; y++)
            {
                int shift = TexelFight.Tear(g);   // torn rows: the row's claims slide sideways
                for (int x = 0; x < _w; x++)
                {
                    int i = y * _w + x;
                    int sx = x - shift;
                    Color c = sx >= 0 && sx < _w ? _cur[y * _w + sx] : _old[i];
                    if (TexelFight.Flash(g)) c = Static;
                    Color32 a = _atlas[i];
                    _px[i] = new Color32((byte)(a.r * c.r), (byte)(a.g * c.g), (byte)(a.b * c.b), 255);
                }
            }
            _tex.SetPixels32(_px);
            _tex.Apply(true);
        }

        public void Dispose()
        {
            if (_tex != null) Object.Destroy(_tex);
            _tex = null;
        }

        private bool Ensure(Renderer band)
        {
            if (_tex != null) return true;
            if (_atlas == null)
            {
                if (_readTried) return false;
                _readTried = true;
                var mat = band != null ? band.sharedMaterial : null;
                var src = mat == null ? null : mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture;
                _atlas = TexelFight.Read(src, out _w, out _h);
                if (_atlas == null) { MelonLogger.Warning("[Smoke] couldn't read the band's texture; it flickers whole"); return false; }
            }
            _tex = new Texture2D(_w, _h, TextureFormat.RGBA32, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset, name = "BA_SmokeBand" };
            int n = _w * _h;
            _px = new Color32[n];
            _old = new Color[n];
            _cur = new Color[n];
            _claim = new float[n];
            return true;
        }
    }
}
