using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// A small DOS window that pops up beside the hand and runs one command: it opens out of a
    /// line, the command is typed at the prompt, the script's lines scroll past with a flicker of
    /// memory dump under them while a field of cells shows a fight (each cell held by the old
    /// state, taken by the new, or static), then the done line, and it folds away. What is being
    /// fought over is the caller's (OnRound gives the progress each round of the fight): the smoke
    /// band's dye (SmokeTerminal), the held model itself (SpawnTerminal).
    ///
    /// The window is a quad parented to the world camera, placed each frame by the caller's
    /// anchor (a camera-space offset from it, so it follows the hand's sway) and turned toward the
    /// eye, on the viewmodel camera's layer when there is one so it never sinks into a wall. Its
    /// 176x112 texture is drawn per pixel with PixelFont's glyphs, as LrfDisplay's HUD is, in the
    /// set's hazard yellow, and redrawn only when what it shows changes. The material is a copy of
    /// the binoculars' HUD material (URP Unlit, transparent, double-sided), already in the bundle.
    ///
    /// With the window off (Begin's show false, or no material) the timeline and the fight still
    /// run, without the typing.
    /// </summary>
    internal sealed class DosTerminal
    {
        private const int W = 176, H = 112;
        private const int Rows = 8, RowY = 14, RowPitch = 9;   // the log: 8 rows under the title bar
        private const int FieldY = 90, FieldCols = 34, FieldRows = 2, Block = 3;
        private const float OpenTime = 0.08f, CloseTime = 0.1f, HoldTime = 0.45f;
        private const float HexStep = 0.035f;                  // the memory dump's flicker
        private const string Prompt = "C:\\BA>";

        // The set's palette (handover, "The palette"): charcoal glass, the hazard yellow as the
        // title bar, amber text lit off it, a dim amber for what's less important.
        internal static readonly Color32 Back  = new Color32(14, 14, 15, 228);
        internal static readonly Color32 Bar   = new Color32(209, 150, 0, 255);    // PAL yel
        internal static readonly Color32 Amber = new Color32(255, 186, 26, 255);
        internal static readonly Color32 Dim   = new Color32(126, 90, 8, 255);
        internal static readonly Color32 Ink   = new Color32(22, 22, 23, 255);     // PAL black
        private static readonly Color32 Rim    = new Color32(90, 66, 10, 255);

        // What to run; set before Begin.
        public string Title = "", Label = "", Command = "", DoneWord = "DONE";
        public string[] Script = new string[0];
        public Color32 CellOld, CellNew, CellStatic = Amber;
        public float FightStep = 1f / 30f;                     // a new round of the fight this often
        public float TypeRate = 80f;                           // characters a second at the prompt
        public Action<float> OnRound;                          // each round, with the progress 0..1

        private GameObject _go;
        private Material _mat;
        private Texture2D _tex;
        private Mesh _mesh;
        private readonly Color32[] _px = new Color32[W * H];
        private string _drawnKey;
        private bool _warned;

        private readonly float[] _cellClaim = new float[FieldCols * FieldRows];
        private readonly byte[] _cells = new byte[FieldCols * FieldRows];          // 0 old, 1 new, 2 static
        private int _round = -1;

        private float _duration;
        private float _openedAt = -10f, _typeAt, _runAt, _closeAt = -1f;
        private bool _show, _active;
        private int _typedSounded;
        private bool _doneSounded;

        /// <summary>The fight is on (closing or done ends it).</summary>
        public bool Running => _active && _closeAt < 0f && Time.time < _runAt + _duration;

        /// <summary>Anything still to draw.</summary>
        public bool Active => _active;

        /// <summary>The window is up and not folding away.</summary>
        public bool Open => _active && _show && _closeAt < 0f && _go != null && _go.activeSelf;

        /// <summary>This run is drawn in the window (not just timed).</summary>
        public bool Windowed => _active && _show;

        /// <summary>0 until the command runs, 1 once it's done (or closed).</summary>
        public float Progress => Fraction(Time.time);

        /// <summary>
        /// Runs the command set in the fields for <paramref name="duration"/> seconds, in the window
        /// if <paramref name="show"/> (and it can be built). A window still open goes straight to
        /// the prompt without a second pop; <paramref name="keepCells"/> lets the cells fight on.
        /// </summary>
        public void Begin(float duration, bool show, bool keepCells)
        {
            float now = Time.time;
            bool open = Open;
            _duration = Mathf.Max(0f, duration);
            _show = show && _duration > 0f && Ensure();
            _typedSounded = 0;
            _doneSounded = false;
            _round = -1;
            _closeAt = -1f;
            for (int i = 0; i < _cellClaim.Length; i++) _cellClaim[i] = UnityEngine.Random.Range(0.02f, 0.98f);
            if (!keepCells) Array.Clear(_cells, 0, _cells.Length);
            _active = _duration > 0f;
            if (!_active) return;

            if (!_show) { _runAt = now; return; }
            if (!open) _openedAt = now;
            _typeAt = open ? now : now + OpenTime;
            _runAt = _typeAt + Command.Length / TypeRate + 0.04f;
        }

        /// <summary>Folds the window away now; the fight is over.</summary>
        public void Close()
        {
            if (!_active) return;
            if (_closeAt < 0f || _closeAt > Time.time) _closeAt = Time.time;
            if (!_show) _active = false;
        }

        /// <summary>Every LateUpdate: the fight's rounds, and the window beside <paramref name="anchor"/> (null: where it was).</summary>
        public void Tick(Camera cam, Vector3? anchor, Vector3 offset, float yaw, int layer)
        {
            if (!_active) return;
            float now = Time.time;
            float p = Fraction(now);

            int round = now < _runAt ? -1 : Mathf.FloorToInt((now - _runAt) / FightStep);
            if (round != _round && _closeAt < 0f && now < _runAt + _duration)
            {
                _round = round;
                if (round >= 0) { Cells(p); OnRound?.Invoke(p); }
            }

            if (!_show)
            {
                if (p >= 1f) { if (_closeAt < 0f) OnRound?.Invoke(1f); _active = false; }
                return;
            }
            if (_go == null) { Release(); _active = false; return; }   // the camera went with a scene

            float done = _runAt + _duration;
            if (_closeAt < 0f && now >= done)
            {
                // The last round at full, whatever frame it lands on.
                if (_round != int.MaxValue) { _round = int.MaxValue; Cells(1f); OnRound?.Invoke(1f); }
                if (now >= done + HoldTime) _closeAt = done + HoldTime;
            }
            if (_closeAt >= 0f && now >= _closeAt + CloseTime)
            {
                _go.SetActive(false);
                _active = false;
                return;
            }

            int typed = Typed(now);
            while (_typedSounded < typed)
            {
                if (_typedSounded % 2 == 0) Sfx.PlayHeld("Keystroke", 0.45f);
                _typedSounded++;
            }
            if (!_doneSounded && now >= done && _closeAt < 0f) { _doneSounded = true; Sfx.PlayHeld("LrfFix", 0.35f); }

            Place(cam, anchor, offset, yaw, layer, now);
            string key = Key(now, typed);
            if (key != _drawnKey) { Draw(now, typed); _drawnKey = key; }
        }

        public void Dispose()
        {
            Release();
            _active = false;
        }

        private void Release()
        {
            foreach (Object o in new Object[] { _go, _mat, _tex, _mesh }) if (o != null) Object.Destroy(o);
            _go = null; _mat = null; _tex = null; _mesh = null;
            _drawnKey = null;
        }

        private float Fraction(float now)
        {
            if (!_active || _closeAt >= 0f) return 1f;
            return _duration > 0f ? Mathf.Clamp01((now - _runAt) / _duration) : 1f;
        }

        private void Cells(float p)
        {
            float g = TexelFight.Contest(p);
            for (int i = 0; i < _cells.Length; i++)
            {
                if (p >= 1f) { _cells[i] = 1; continue; }
                if (TexelFight.Rand() < g * 0.05f) { _cells[i] = 2; continue; }
                _cells[i] = (byte)(TexelFight.Taken(p, g, _cellClaim[i]) ? 1 : 0);
            }
        }

        // ── Building ────────────────────────────────────────────────────────────

        /// <summary>The window, made on first use; false (with a warning, once) if it can't be.</summary>
        private bool Ensure()
        {
            if (_go != null && _mat != null) return true;
            Release();
            var cam = Camera.main;
            var src = HudSource();
            if (cam == null || src == null)
            {
                if (!_warned) MelonLogger.Warning($"[Terminal] no window: {(cam == null ? "no camera" : "the binoculars' HUD material isn't in the bundle")}");
                _warned = true;
                return false;
            }

            _tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
            _mat = new Material(src) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", _tex);
            _mat.mainTexture = _tex;

            // A quad SmokeTermWidth wide, the texture's aspect, centred on its origin, facing -Z.
            float hw = Mathf.Max(0.02f, Config.SmokeTermWidth) * 0.5f, hh = hw * H / W;
            _mesh = new Mesh { hideFlags = HideFlags.DontUnloadUnusedAsset };
            _mesh.SetVertices(new[] { new Vector3(-hw, -hh, 0), new Vector3(hw, -hh, 0), new Vector3(hw, hh, 0), new Vector3(-hw, hh, 0) });
            _mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            _mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _mesh.RecalculateBounds();

            _go = new GameObject("BA_Terminal");
            _go.transform.SetParent(cam.transform, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _go.SetActive(false);
            return true;
        }

        /// <summary>
        /// The binoculars' HUD material (URP Unlit, transparent, double-sided) to copy for
        /// anything drawn as a lit-up texture; null without the bundle. The binoculars are a
        /// held-only model: their prefab is the held one.
        /// </summary>
        internal static Material HudSource()
        {
            var hud = OrdnanceModels.HeldPrefab(Ordnance.Binoculars)?.transform.Find("Hud");
            return hud != null ? hud.GetComponent<Renderer>()?.sharedMaterial : null;
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        private int Typed(float now) => Mathf.Clamp(Mathf.FloorToInt((now - _typeAt) * TypeRate), 0, Command.Length);

        /// <summary>Beside the anchor, turned to the eye; it opens out of a line and folds back into one.</summary>
        private void Place(Camera cam, Vector3? anchor, Vector3 offset, float yaw, int layer, float now)
        {
            if (cam != null && _go.transform.parent != cam.transform) _go.transform.SetParent(cam.transform, false);
            if (_go.layer != layer) _go.layer = layer;

            if (anchor.HasValue && cam != null)
            {
                Vector3 local = cam.transform.InverseTransformPoint(anchor.Value) + offset;
                _go.transform.localPosition = local;
                _go.transform.localRotation = Quaternion.LookRotation(local, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);
            }

            float open = Mathf.Clamp01((now - _openedAt) / OpenTime);
            float shut = _closeAt >= 0f ? Mathf.Clamp01((now - _closeAt) / CloseTime) : 0f;
            float h = 1f - (1f - open) * (1f - open);                     // ease out
            h = Mathf.Lerp(h, 0.03f, shut);                               // into a line...
            float w = shut > 0.6f ? Mathf.Lerp(1f, 0.02f, (shut - 0.6f) / 0.4f) : 1f;   // ...then a dot
            _go.transform.localScale = new Vector3(w, Mathf.Max(0.03f, h), 1f);
            if (!_go.activeSelf) _go.SetActive(true);
        }

        /// <summary>Everything the texture shows; it is redrawn when this changes.</summary>
        private string Key(float now, int typed)
        {
            int lines = LinesShown(now);
            int hex = now >= _runAt && now < _runAt + _duration ? Mathf.FloorToInt((now - _runAt) / HexStep) : -1;
            bool cursor = (now * 2f % 1f) < 0.55f;
            bool blink = (now * 8f % 1f) < 0.5f;
            return $"{Command}|{typed}|{lines}|{hex}|{_round}|{cursor}|{blink}|{now >= _runAt + _duration}";
        }

        /// <summary>The script's lines out so far: the first as it starts, the last by 85 % of the run.</summary>
        private int LinesShown(float now)
        {
            if (now < _runAt) return 0;
            if (_duration <= 0f) return Script.Length;
            float u = (now - _runAt) / (_duration * 0.85f);
            return Mathf.Clamp(1 + Mathf.FloorToInt(u * (Script.Length - 1)), 0, Script.Length);
        }

        // ── Drawing ─────────────────────────────────────────────────────────────

        private void Draw(float now, int typed)
        {
            // The window: charcoal glass with a thin rim, the title bar solid hazard yellow.
            for (int i = 0; i < _px.Length; i++) _px[i] = Back;
            Outline(0, 0, W, H, Rim);
            Fill(1, 1, W - 2, 10, Bar);
            TextAt(Title, 4, 3, Ink);
            TextAt(Label, W - 4 - Width(Label), 3, Ink);

            // The log, scrolled so the newest row is the last: the prompt, the script so far,
            // then the memory dump flickering under it while it runs, or the done line.
            var rows = new List<(string text, Color32 c)>();
            bool done = now >= _runAt + _duration;
            bool cursorOn = (now * 2f % 1f) < 0.55f;
            rows.Add((Prompt + Command.Substring(0, typed) + (typed < Command.Length || (now < _runAt && cursorOn) ? "_" : ""), Amber));
            int lines = LinesShown(now);
            for (int i = 0; i < lines; i++)
            {
                // The last line (the contested write) blinks while the fight is on.
                bool last = i == Script.Length - 1;
                rows.Add((Script[i], last ? (!done && (now * 8f % 1f) < 0.5f ? Amber : Dim) : Amber));
            }
            if (now >= _runAt && !done) rows.Add((Dump(now), Dim));
            if (done)
            {
                rows.Add(($"{DoneWord}  {_duration:0.00}S", Amber));
                rows.Add((Prompt + (cursorOn ? "_" : ""), Amber));
            }
            int first = Mathf.Max(0, rows.Count - Rows);
            for (int i = first; i < rows.Count; i++) TextAt(rows[i].text, 4, RowY + (i - first) * RowPitch, rows[i].c);

            // The fight in cells.
            if (now >= _runAt)
            {
                int won = 0;
                for (int r = 0; r < FieldRows; r++)
                    for (int c = 0; c < FieldCols; c++)
                    {
                        byte s = done ? (byte)1 : _cells[r * FieldCols + c];
                        if (s == 1) won++;
                        Fill(4 + c * (Block + 1), FieldY + r * (Block + 1), Block, Block, s == 0 ? CellOld : s == 1 ? CellNew : CellStatic);
                    }
                string pct = $"{Mathf.RoundToInt(100f * won / _cells.Length)}%";
                TextAt(pct, W - 4 - Width(pct), FieldY, Amber);
            }

            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        /// <summary>A line of memory dump that changes every HexStep, at an address that counts up.</summary>
        private string Dump(float now)
        {
            int step = Mathf.FloorToInt((now - _runAt) / HexStep);
            uint h = (uint)(step * 2654435761u) ^ (uint)Label.GetHashCode();
            int addr = 0x7A00 + step * 16;
            var sb = new System.Text.StringBuilder($"{addr & 0xFFFF:X4}:");
            for (int i = 0; i < 6; i++)
            {
                h ^= h << 13; h ^= h >> 17; h ^= h << 5;
                sb.Append($" {h & 0xFF:X2}");
            }
            return sb.ToString();
        }

        private static int Width(string s) => string.IsNullOrEmpty(s) ? 0 : s.Length * (PixelFont.GW + 1) - 1;

        /// <summary>x, y from the top-left.</summary>
        private void Put(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            _px[(H - 1 - y) * W + x] = c;
        }

        private void Fill(int x, int y, int w, int h, Color32 c)
        {
            for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) Put(x + i, y + j, c);
        }

        private void Outline(int x, int y, int w, int h, Color32 c)
        {
            for (int i = 0; i < w; i++) { Put(x + i, y, c); Put(x + i, y + h - 1, c); }
            for (int j = 0; j < h; j++) { Put(x, y + j, c); Put(x + w - 1, y + j, c); }
        }

        private void TextAt(string s, int x, int y, Color32 c)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (char raw in s)
            {
                if (x > W - 4) return;   // cut at the window's edge
                var g = PixelFont.Bits(char.ToUpperInvariant(raw));
                if (g != null)
                    for (int r = 0; r < PixelFont.GH; r++)
                        for (int col = 0; col < PixelFont.GW; col++)
                            if (g[r * PixelFont.GW + col] == '#') Put(x + col, y + r, c);
                x += PixelFont.GW + 1;
            }
        }
    }

    /// <summary>
    /// The rule every texel fight follows, and the textures it is fought on. Each texel (or cell)
    /// has its own threshold; each round it is taken if the progress, shaken by a contest that is
    /// nothing at the ends and worst half way, passes it, so texels flip back and forth before
    /// they settle. Static flashes the set's hazard yellow and whole rows tear sideways, as the
    /// warhead stencil's glitch does.
    /// </summary>
    internal static class TexelFight
    {
        public static readonly Color32 Static = new Color32(255, 186, 26, 255);   // lit hazard yellow

        private static uint _seed = 2463534242u;

        /// <summary>How hard the fight is at <paramref name="p"/>.</summary>
        public static float Contest(float p) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(p));

        /// <summary>A quick 0..1 random (xorshift): a fight rolls hundreds of thousands a second.</summary>
        public static float Rand()
        {
            _seed ^= _seed << 13; _seed ^= _seed >> 17; _seed ^= _seed << 5;
            return (_seed & 0xFFFFFF) / 16777216f;
        }

        public static bool Taken(float p, float contest, float threshold) =>
            p >= 1f || p + (Rand() - 0.5f) * 0.9f * contest > threshold;

        /// <summary>A row's tear this round: usually none, up to 4 texels at the worst.</summary>
        public static int Tear(float contest) => Rand() < contest * 0.2f ? (int)(Rand() * 9f) - 4 : 0;

        public static bool Flash(float contest) => Rand() < contest * 0.03f;

        private static readonly Dictionary<int, (Color32[] px, int w, int h)> _atlases = new Dictionary<int, (Color32[], int, int)>();

        /// <summary>
        /// A texture's texels on the CPU, read once and kept: bundle textures aren't readable, so
        /// it goes through a RenderTexture (Graphics.Blit with scale and offset, the overload the
        /// game build keeps) and ReadPixels. Null, with a warning, if that fails.
        /// </summary>
        public static Color32[] Read(Texture src, out int w, out int h)
        {
            w = h = 0;
            if (src == null) return null;
            int id = src.GetInstanceID();
            if (_atlases.TryGetValue(id, out var hit)) { w = hit.w; h = hit.h; return hit.px; }

            RenderTexture rt = null, prev = RenderTexture.active;
            Texture2D read = null;
            Color32[] atlas = null;
            try
            {
                w = src.width; h = src.height;
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(src, rt, Vector2.one, Vector2.zero);
                RenderTexture.active = rt;
                read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                read.Apply(false);
                var px = read.GetPixels32();
                atlas = new Color32[w * h];
                for (int i = 0; i < atlas.Length; i++) atlas[i] = px[i];
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[Terminal] couldn't read texture '{src.name}' ({e.Message})");
                atlas = null;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (read != null) Object.Destroy(read);
            }
            _atlases[id] = (atlas, w, h);   // misses too: one warning
            if (atlas == null) { w = h = 0; }
            return atlas;
        }
    }
}
