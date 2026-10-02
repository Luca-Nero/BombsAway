using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The rangefinder's screen on the held binoculars, built the way the Javelin's CLU is
    /// (Missile/JavelinClu.cs): a camera at the receiver lens (Window) renders the zoomed view
    /// into a 640x480 RenderTexture on the Display face, and a 320x240 pixel-drawn HUD sits on
    /// the Hud face over it: mil reticle, the mission strip, LED readout, the lase's segments
    /// and the mission status, in the red LED of the old eyepiece overlay. The strip along the
    /// top is the game's own toolbar in LED: one slot per mission type, the chosen one lit
    /// solid, Q and E either side (the pressed one flashes), a tick under any type in the air,
    /// and the chosen type's name under it, retyped at a DOS cursor on every switch like the
    /// launchers' warhead stencil. The HUD texture is redrawn
    /// only when what it shows changes; the lase spot and the called target's diamond are small
    /// quads moved every frame.
    /// </summary>
    internal sealed class LrfDisplay
    {
        public const int W = 320, H = 240;                        // HUD resolution (chunky on purpose)
        public const int FeedW = 640, FeedH = 480;                // camera feed resolution
        private const int Border = 4, Corner = 10;                // the black frame round the picture
        private const float IdleHz = 15f;                         // camera refresh while not at the eye
        private const int SlotW = 23, SlotH = 11, SlotGap = 2, StripY = 9;   // the mission strip
        private const float TypeRate = 45f;                       // characters a second as the name retypes
        private const float KeyFlash = 0.25f;                     // seconds Q or E stays lit after a press

        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private static readonly Color32 Frame = new Color32(6, 7, 8, 255);
        private static readonly Color32 Led   = new Color32(255, 66, 41, 255);
        private static readonly Color32 Dim   = new Color32(140, 31, 20, 230);

        private GameObject _camGo;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _hud, _spotTex, _diamondTex;
        private Material _displayMat, _hudMat, _spotMat, _diamondMat;
        private Mesh _spotMesh, _diamondMesh;
        private Transform _spot, _diamond;
        private float _hudW, _hudH;
        private string _drawnKey;
        private float _nextIdleFrame;
        private string _typedLine;                                // the mission line being typed, and since when
        private float _typedAt = -10f;
        private readonly Color32[] _px = new Color32[W * H];

        /// <summary>The screen's camera: what the reticle is on (the lase rays from here).</summary>
        public Camera Feed => _cam;

        /// <summary>Sets the screen up on the held binoculars; null if the model lacks its parts.</summary>
        public static LrfDisplay Attach(Transform model)
        {
            var display = model.Find("Display"); var hud = model.Find("Hud"); var window = model.Find("Window");
            if (display == null || hud == null || window == null) return null;
            var lrf = new LrfDisplay();
            try { lrf.Build(display, hud, window); }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning($"[LRF] screen unavailable: {e.Message}");
                lrf.Dispose();
                return null;
            }
            return lrf;
        }

        private void Build(Transform display, Transform hud, Transform window)
        {
            _rt = new RenderTexture(FeedW, FeedH, 24) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontUnloadUnusedAsset };
            _rt.Create();

            _camGo = new GameObject("BA_LrfCam");
            _camGo.transform.SetParent(window, false);
            _camGo.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            _cam = _camGo.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.nearClipPlane = 0.05f;
            var main = Camera.main;
            if (main != null) { _cam.farClipPlane = main.farClipPlane; _cam.cullingMask = main.cullingMask; }
            _cam.enabled = false;

            var dr = display.GetComponent<Renderer>();
            _displayMat = new Material(dr.sharedMaterial) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            SetTex(_displayMat, _rt);
            _displayMat.SetVector("_FeedSize", new Vector4(FeedW, FeedH, 0f, 0f));
            dr.sharedMaterial = _displayMat;

            _hud = NewTex(W, H);
            var hr = hud.GetComponent<Renderer>();
            _hudMat = new Material(hr.sharedMaterial) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            SetTex(_hudMat, _hud);
            hr.sharedMaterial = _hudMat;
            var hb = hud.GetComponent<MeshFilter>().sharedMesh.bounds;
            _hudW = hb.size.x; _hudH = hb.size.y;

            // The lase spot: four corners round the point. The target: a diamond.
            _spotTex = Marker(13, (x, y) => (x < 4 || x > 8) && (y < 4 || y > 8) && (x == 0 || x == 12 || y == 0 || y == 12));
            _diamondTex = Marker(11, (x, y) => Mathf.Abs(x - 5) + Mathf.Abs(y - 5) == 5);
            _spot = MarkerQuad(hud, "Spot", _spotTex, 13, out _spotMat, out _spotMesh);
            _diamond = MarkerQuad(hud, "Diamond", _diamondTex, 11, out _diamondMat, out _diamondMesh);
        }

        private static Texture2D NewTex(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };

        private static void SetTex(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            m.mainTexture = t;
        }

        private static Texture2D Marker(int n, Func<int, int, bool> lit)
        {
            var t = NewTex(n, n);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = lit(x, y) ? Led : Clear;
            t.SetPixels32(px);
            t.Apply(false);
            return t;
        }

        /// <summary>A quad <paramref name="n"/> HUD pixels square, centred on its origin, on the Hud face.</summary>
        private Transform MarkerQuad(Transform hud, string name, Texture2D tex, int n, out Material mat, out Mesh mesh)
        {
            mat = new Material(_hudMat) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            SetTex(mat, tex);
            float s = n / (float)W * _hudW * 0.5f;
            mesh = new Mesh { hideFlags = HideFlags.DontUnloadUnusedAsset };
            mesh.SetVertices(new[] { new Vector3(-s, -s, 0), new Vector3(s, -s, 0), new Vector3(s, s, 0), new Vector3(-s, s, 0) });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.layer = hud.gameObject.layer;
            go.transform.SetParent(hud, false);
            go.transform.localRotation = Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        /// <param name="ads">0 = at the hip, 1 = up at the eye.</param>
        /// <param name="zoom">The optical zoom: the feed's field of view is the main camera's over this.</param>
        public void Tick(float ads, in LrfState st, float zoom)
        {
            if (_cam == null) return;
            var main = Camera.main;
            if (main != null) _cam.cullingMask = main.cullingMask;   // never the held models' own layer
            float baseFov = main != null ? main.fieldOfView : 60f;
            _cam.fieldOfView = Mathf.Clamp(baseFov / Mathf.Max(1f, zoom), 0.5f, 90f);

            if (ads > 0.05f) _cam.enabled = true;
            else
            {
                bool due = Time.unscaledTime >= _nextIdleFrame;
                _cam.enabled = due;
                if (due) _nextIdleFrame = Time.unscaledTime + 1f / IdleHz;
            }

            string key = HudKey(st);
            if (key != _drawnKey) { DrawHud(st); _drawnKey = key; }

            Place(_spot, st.Lasing && !st.NoReturn, st.LasePoint);
            Place(_diamond, st.HasMark && (Time.unscaledTime * 2f % 1f) < 0.75f, st.Mark);
            // The spot is bright while the reticle is off it (bring it back), dim while on.
            if (_spot != null && _spot.gameObject.activeSelf) _spotMat.color = st.OffMark ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.9f);
        }

        private void Place(Transform marker, bool show, Vector3 world)
        {
            if (marker == null) return;
            if (show)
            {
                Vector3 vp = _cam.WorldToViewportPoint(world);
                float inset = (Border + 6) / (float)H;
                show = vp.z > 0f && vp.x > inset && vp.x < 1f - inset && vp.y > inset && vp.y < 1f - inset;
                if (show) marker.localPosition = new Vector3((vp.x - 0.5f) * _hudW, (vp.y - 0.5f) * _hudH, -0.0005f);
            }
            if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
        }

        public void Dispose()
        {
            if (_camGo != null) Object.Destroy(_camGo);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            foreach (Object o in new Object[] { _hud, _spotTex, _diamondTex, _displayMat, _hudMat, _spotMat, _diamondMat, _spotMesh, _diamondMesh })
                if (o != null) Object.Destroy(o);
            _cam = null;
        }

        // ── HUD texture ─────────────────────────────────────────────────────────

        private static string Readout(in LrfState s) => s.HasReading
            ? $"RNG {Mathf.RoundToInt(s.Range):0000}M  AZ {Mathf.RoundToInt(s.AzimuthMils) % 6400:0000}  EL {(s.ElevationMils >= 0 ? "+" : "-")}{Mathf.Abs(Mathf.RoundToInt(s.ElevationMils)):000}"
            : "RNG ----M  AZ ----  EL ----";

        /// <summary>Everything the HUD texture shows; it is redrawn when this changes.</summary>
        private string HudKey(in LrfState s)
        {
            float t = Time.unscaledTime;
            bool blink4 = (t * 4f % 1f) < 0.6f, blink3 = (t * 3f % 1f) < 0.6f;
            int lit = s.Lasing && !s.NoReturn ? Mathf.FloorToInt(s.Progress * 12) : -1;
            if (s.MissionLine != _typedLine)
            {
                // A new mission chosen (or the first draw: shown whole at once).
                _typedAt = _typedLine == null ? -10f : t;
                _typedLine = s.MissionLine;
            }
            return $"{s.MissionLine}|{s.MissionIndex}|{Typed()}|{Strip()}|{s.ZoomLevel}|{(s.NoReturn ? (blink3 ? "NR" : "") : Readout(s))}|{lit}|{s.OffMark}|{blink4}|{s.MarkStatus}|{Mathf.RoundToInt(_cam.fieldOfView * 10f)}";
        }

        private void DrawHud(in LrfState s)
        {
            // The frame: black round the picture, rounded corners.
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    Put(x, y, InPicture(x, y) ? Clear : Frame);

            // Mils to HUD pixels at the feed's field of view.
            float tanHalf = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            int Mil(float mils) => Mathf.RoundToInt((H * 0.5f) * Mathf.Tan(mils * (Mathf.PI * 2f / 6400f)) / tanHalf);
            int cx = W / 2, cy = H / 2;

            // The reticle: an open aiming box and a scale every 10 mils across and below.
            int box = Mathf.Max(3, Mil(2.5f));
            Outline(cx - box, cy - box, 2 * box + 1, 2 * box + 1, Led);
            for (int m = 10; m <= 50; m += 10)
            {
                int d = Mil(m), len = m % 50 == 0 ? 5 : 3;
                for (int i = -len / 2; i <= len / 2; i++)
                {
                    Put(cx + d, cy + i, Led); Put(cx - d, cy + i, Led);
                    if (m <= 40) Put(cx + i, cy + d, Led);
                }
            }
            int ten = Mil(10f);
            for (int x = box + 3; x <= ten - 3; x++) { Put(cx + x, cy, Dim); Put(cx - x, cy, Dim); }

            // The mission strip, top; the zoom, top left; the readout, bottom; under it the lase
            // or the mission's status.
            DrawStrip(s, cx);
            string zoom = $"{s.ZoomLevel:0.#}X";
            TextCentred(zoom, Border + 10 + (zoom.Length * (PixelFont.GW + 1) - 1) / 2, StripY + 2, Led);   // top left
            int bottom = H - 44;
            string line = s.NoReturn ? ((Time.unscaledTime * 3f % 1f) < 0.6f ? "NO RETURN" : "") : Readout(s);
            TextCentred(line, cx, bottom, Led);

            bool blink = (Time.unscaledTime * 4f % 1f) < 0.6f;
            if (s.Lasing && !s.NoReturn)
            {
                const int Segs = 12, SegW = 8, Gap = 2;
                int total = Segs * SegW + (Segs - 1) * Gap, x0 = cx - total / 2, y0 = bottom + 11;
                int lit = Mathf.FloorToInt(s.Progress * Segs);
                for (int i = 0; i < Segs; i++)
                    Fill(x0 + i * (SegW + Gap), y0, SegW, 3, i < lit && (!s.OffMark || blink) ? Led : Dim);
                if (s.OffMark) TextCentred("ON TGT", cx, y0 + 7, blink ? Led : Dim);
                else if (blink) TextCentred("LASING", cx, y0 + 7, Led);
            }
            else if (s.MarkStatus != null)
                TextCentred(s.MarkStatus, cx, bottom + 13, Led);

            _hud.SetPixels32(_px);
            _hud.Apply(false);
        }

        /// <summary>Characters of the mission line typed so far (all of it once done).</summary>
        private int Typed()
        {
            int n = _typedLine?.Length ?? 0;
            return Mathf.Clamp(Mathf.FloorToInt((Time.unscaledTime - _typedAt) * TypeRate), 0, n);
        }

        /// <summary>What the strip shows that changes on its own: the lit key and the types in the air.</summary>
        private static string Strip()
        {
            bool lit = Time.unscaledTime - FireMission.SwitchedAt < KeyFlash;
            int mask = 0;
            for (int i = 0; i < FireMission.TypeCount; i++) if (FireMission.Running(FireMission.TypeAt(i))) mask |= 1 << i;
            return $"{(lit ? FireMission.SwitchedDir : 0)}:{mask}";
        }

        /// <summary>A key's name for the strip: the letter, or an arrow for a longer name.</summary>
        private static string KeyLabel(KeyCode k, string fallback)
        {
            string n = k.ToString();
            if (n.StartsWith("Alpha")) n = n.Substring(5);
            return n.Length <= 2 ? n : fallback;
        }

        /// <summary>The game's toolbar in LED: a slot per mission type, the chosen one solid, the keys either side.</summary>
        private void DrawStrip(in LrfState s, int cx)
        {
            int n = FireMission.TypeCount;
            int total = n * SlotW + (n - 1) * SlotGap, x0 = cx - total / 2;
            for (int i = 0; i < n; i++)
            {
                int x = x0 + i * (SlotW + SlotGap);
                string code = FireMission.CodeAt(i);
                if (i == s.MissionIndex)
                {
                    Fill(x, StripY, SlotW, SlotH, Led);
                    TextCentred(code, x + SlotW / 2 + 1, StripY + 2, Frame);
                }
                else
                {
                    Outline(x, StripY, SlotW, SlotH, Dim);
                    TextCentred(code, x + SlotW / 2 + 1, StripY + 2, Dim);
                }
                // A mission of this type in the air: a lit tick under its slot.
                if (FireMission.Running(FireMission.TypeAt(i))) Fill(x + SlotW / 2 - 2, StripY + SlotH + 1, 5, 1, Led);
            }

            bool lit = Time.unscaledTime - FireMission.SwitchedAt < KeyFlash;
            string prev = KeyLabel(Config.MissionPrevKey, "<"), next = KeyLabel(Config.MissionNextKey, ">");
            int pw = prev.Length * (PixelFont.GW + 1) - 1, nw = next.Length * (PixelFont.GW + 1) - 1;
            TextCentred(prev, x0 - 6 - (pw + 1) / 2, StripY + 2, lit && FireMission.SwitchedDir < 0 ? Led : Dim);
            TextCentred(next, x0 + total + 6 + nw / 2, StripY + 2, lit && FireMission.SwitchedDir > 0 ? Led : Dim);

            // The chosen type under the strip, retyped at a cursor after a switch.
            string line = s.MissionLine ?? "";
            int shown = Typed();
            if (shown < line.Length)
            {
                // Typed from the left of where the whole line will sit, so it doesn't slide.
                int w = line.Length * (PixelFont.GW + 1) - 1;
                string part = line.Substring(0, shown) + "_";
                TextAt(part, cx - w / 2, StripY + SlotH + 5, Led);
            }
            else TextCentred(line, cx, StripY + SlotH + 5, Led);
        }

        private static bool InPicture(int x, int y)
        {
            int x0 = Border, x1 = W - Border, y0 = Border, y1 = H - Border;
            if (x < x0 || x >= x1 || y < y0 || y >= y1) return false;
            int dx = x < x0 + Corner ? x0 + Corner - x : (x >= x1 - Corner ? x - (x1 - Corner - 1) : 0);
            int dy = y < y0 + Corner ? y0 + Corner - y : (y >= y1 - Corner ? y - (y1 - Corner - 1) : 0);
            return dx * dx + dy * dy <= Corner * Corner;
        }

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

        private void TextCentred(string s, int cx, int y, Color32 c)
        {
            if (string.IsNullOrEmpty(s)) return;
            int w = s.Length * (PixelFont.GW + 1) - 1;
            TextAt(s, cx - w / 2, y, c);
        }

        private void TextAt(string s, int x, int y, Color32 c)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (char raw in s)
            {
                var g = PixelFont.Bits(char.ToUpperInvariant(raw));
                if (g != null)
                    for (int r = 0; r < PixelFont.GH; r++)
                        for (int col = 0; col < PixelFont.GW; col++)
                            if (g[r * PixelFont.GW + col] == '#') Put(x + col, y + r, c);
                x += PixelFont.GW + 1;
            }
        }
    }
}
