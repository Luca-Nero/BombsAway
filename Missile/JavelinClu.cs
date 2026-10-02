using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>The CLU's picture: the day sight, a green night view, or thermal white/black hot.</summary>
    internal enum CluView { Day, Night, WHot, BHot }

    /// <summary>What the CLU display shows this frame.</summary>
    internal struct CluState
    {
        public bool Nfov;          // narrow field of view (else wide)
        public CluView View;
        public AttackMode Attack;
        public bool Seeking;       // seeker active (scanning for a lock)
        public bool Locked;
        public float LockProgress; // 0..1 toward a lock on the target being tracked
        public bool InFlight;      // a missile of ours is flying
        public Transform Target;   // locked, else the one being tracked (a limb, or a body's root)
    }

    /// <summary>
    /// The Command Launch Unit's display on the held Javelin launcher: a camera behind the day
    /// window renders into a 640x480 RenderTexture shown on the Display face, and a pixel-drawn
    /// Javelin HUD sits on the Hud face over it. The HUD texture is redrawn only when what it
    /// shows changes; the track gates are four small corner quads moved every frame.
    ///
    /// Layout follows the sims: a black frame round the picture carrying the mode pictograms
    /// (DAY / NIGHT, the NFOV and WFOV boxes, SEEK, TOP / DIR, FLT, CLU / BCU), a reticle in the
    /// middle, and NFOV corner marks while in WFOV.
    /// </summary>
    internal sealed class JavelinClu
    {
        public const int W = 320, H = 240;                        // HUD resolution (chunky on purpose)
        public const int FeedW = 640, FeedH = 480;                // camera feed resolution
        private const int VX0 = 36, VX1 = 284, VY0 = 24, VY1 = 216;   // picture inside the frame (from the top-left)
        private const int Corner = 8;                             // rounded picture corners
        private const float IdleHz = 15f;                         // camera refresh while not up at the eye

        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private static readonly Color32 Frame = new Color32(6, 7, 8, 255);
        private static readonly Color32 Lit   = new Color32(90, 255, 235, 255);
        private static readonly Color32 Dim   = new Color32(62, 70, 76, 255);
        private static readonly Color32 Ret   = new Color32(90, 255, 235, 200);

        private GameObject _camGo;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _hud, _gateTex;
        private Material _displayMat, _hudMat, _gateMat;
        private Mesh _gateMesh;
        private readonly Transform[] _gates = new Transform[4];
        private float _hudW, _hudH;
        private int _drawnKey = -1;
        private float _nextIdleFrame;
        private readonly Color32[] _px = new Color32[W * H];
        private Transform _boundsFor;
        private Renderer[] _boundsRenderers;
        private CluView _shownView = (CluView)(-1);

        /// <summary>Sets the CLU up on a held launcher; null if the model lacks its display parts.</summary>
        public static JavelinClu Attach(Transform launcher)
        {
            var display = launcher.Find("Display"); var hud = launcher.Find("Hud"); var window = launcher.Find("Window");
            if (display == null || hud == null || window == null) return null;
            var clu = new JavelinClu();
            try
            {
                clu.Build(display, hud, window);
                // Start the picture past the tube's front, which rides above the lens and would
                // otherwise poke into it. The real CLU has a 65 m minimum range; nothing that close matters.
                var muzzle = launcher.Find("Muzzle");
                if (muzzle != null)
                    clu._cam.nearClipPlane = Mathf.Max(0.05f, muzzle.localPosition.z - window.localPosition.z + 0.15f);
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning($"[CLU] display unavailable: {e.Message}");
                clu.Dispose();
                return null;
            }
            return clu;
        }

        private void Build(Transform display, Transform hud, Transform window)
        {
            _rt = new RenderTexture(FeedW, FeedH, 24) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontUnloadUnusedAsset };
            _rt.Create();

            _camGo = new GameObject("BA_CluCam");
            _camGo.transform.SetParent(window, false);
            _camGo.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            _cam = _camGo.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.nearClipPlane = 0.05f;
            var main = Camera.main;
            if (main != null) _cam.farClipPlane = main.farClipPlane;
            _cam.enabled = false;

            var dr = display.GetComponent<Renderer>();
            _displayMat = new Material(dr.sharedMaterial) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            SetTex(_displayMat, _rt);
            _displayMat.SetVector("_FeedSize", new Vector4(FeedW, FeedH, 0f, 0f));   // for the grain (CluDisplay.shader)
            dr.sharedMaterial = _displayMat;

            _hud = new Texture2D(W, H, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
            var hr = hud.GetComponent<Renderer>();
            _hudMat = new Material(hr.sharedMaterial) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            SetTex(_hudMat, _hud);
            hr.sharedMaterial = _hudMat;
            var hb = hud.GetComponent<MeshFilter>().sharedMesh.bounds;
            _hudW = hb.size.x; _hudH = hb.size.y;

            BuildGates(hud);
        }

        private static void SetTex(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            m.mainTexture = t;
        }

        // ── Track gates ─────────────────────────────────────────────────────────

        private void BuildGates(Transform hud)
        {
            const int G = 8;   // an L, 2 px thick, along the bottom and left edges
            _gateTex = new Texture2D(G, G, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
            var px = new Color32[G * G];
            for (int y = 0; y < G; y++)
                for (int x = 0; x < G; x++)
                    px[y * G + x] = (x < 2 || y < 2) ? Lit : Clear;
            _gateTex.SetPixels32(px);
            _gateTex.Apply(false);
            _gateMat = new Material(_hudMat) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            SetTex(_gateMat, _gateTex);

            float s = G / (float)W * _hudW;   // HUD pixels -> metres on the face
            _gateMesh = new Mesh { hideFlags = HideFlags.DontUnloadUnusedAsset };
            _gateMesh.SetVertices(new[] { new Vector3(0, 0, 0), new Vector3(s, 0, 0), new Vector3(s, s, 0), new Vector3(0, s, 0) });
            _gateMesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            _gateMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _gateMesh.RecalculateBounds();

            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Gate" + i);
                go.layer = hud.gameObject.layer;
                go.transform.SetParent(hud, false);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f * i);   // BL, BR, TR, TL
                go.AddComponent<MeshFilter>().sharedMesh = _gateMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _gateMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                _gates[i] = go.transform;
            }
        }

        private void PlaceGates(in CluState st)
        {
            bool show = false;
            float cx = 0f, cy = 0f, hw = 0f, hh = 0f;
            if (st.Target != null && _cam != null && TargetRect(st.Target, out Rect r))
            {
                // Viewport -> the face; hidden outside the picture or while blinking off.
                float u0 = VX0 / (float)W, u1 = VX1 / (float)W, v0 = 1f - VY1 / (float)H, v1 = 1f - VY0 / (float)H;
                bool inside = r.center.x > u0 && r.center.x < u1 && r.center.y > v0 && r.center.y < v1;
                // Blinking while it tracks, faster as the lock comes; steady once locked.
                bool blinkOn = st.Locked || (Time.unscaledTime * Mathf.Lerp(3f, 10f, st.LockProgress) % 1f) < 0.55f;
                show = inside && blinkOn;
                cx = (r.center.x - 0.5f) * _hudW; cy = (r.center.y - 0.5f) * _hudH;
                hw = Mathf.Clamp(r.width * 0.5f, 0.035f, 0.3f) * _hudW;
                hh = Mathf.Clamp(r.height * 0.5f, 0.035f, 0.3f) * _hudH;
            }
            for (int i = 0; i < 4; i++)
            {
                if (_gates[i] == null) continue;
                if (_gates[i].gameObject.activeSelf != show) _gates[i].gameObject.SetActive(show);
                if (!show) continue;
                float x = (i == 1 || i == 2) ? cx + hw : cx - hw;
                float y = (i >= 2) ? cy + hh : cy - hh;
                _gates[i].localPosition = new Vector3(x, y, -0.0005f);
            }
        }

        /// <summary>The target's on-screen box through the CLU camera, from its renderers' bounds.</summary>
        private bool TargetRect(Transform target, out Rect rect)
        {
            rect = default;
            if (_boundsFor != target)
            {
                _boundsFor = target;
                try { _boundsRenderers = target.GetComponentsInChildren<Renderer>(); }
                catch { _boundsRenderers = null; }
            }
            Bounds b;
            if (_boundsRenderers != null && _boundsRenderers.Length > 0 && _boundsRenderers[0] != null)
            {
                b = _boundsRenderers[0].bounds;
                for (int i = 1; i < _boundsRenderers.Length && i < 24; i++)
                    if (_boundsRenderers[i] != null) b.Encapsulate(_boundsRenderers[i].bounds);
            }
            else b = new Bounds(target.position, Vector3.one * 0.5f);   // a Transform's position

            float xMin = 1f, yMin = 1f, xMax = 0f, yMax = 0f;
            bool any = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 vp = _cam.WorldToViewportPoint(c);
                if (vp.z <= 0f) continue;
                any = true;
                xMin = Mathf.Min(xMin, vp.x); xMax = Mathf.Max(xMax, vp.x);
                yMin = Mathf.Min(yMin, vp.y); yMax = Mathf.Max(yMax, vp.y);
            }
            if (!any) return false;
            rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        /// <param name="ads">0 = at the hip, 1 = up at the eye.</param>
        public void Tick(float ads, in CluState st)
        {
            if (_cam == null) return;
            var main = Camera.main;
            float baseFov = main != null ? main.fieldOfView : 60f;
            float zoom = Mathf.Max(1f, st.Nfov ? Config.CluZoomNarrow : Config.CluZoomWide);
            _cam.fieldOfView = Mathf.Clamp(baseFov / zoom, 0.5f, 90f);

            if (st.View != _shownView)
            {
                // Thermal views clear to black: the sky has no heat to show.
                _shownView = st.View;
                _displayMat.SetFloat("_Mode", (float)st.View);
                bool thermal = st.View == CluView.WHot || st.View == CluView.BHot;
                _cam.clearFlags = thermal ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
                _cam.backgroundColor = Color.black;
            }
            // Thermal sees through smoke: its layer is left out (checked every frame, as the
            // smoke picks its layer when the first can lights).
            int smoke = SmokeCloud.LayerBit;
            if (smoke != 0)
            {
                bool hot = st.View == CluView.WHot || st.View == CluView.BHot;
                int mask = hot ? _cam.cullingMask & ~smoke : _cam.cullingMask | smoke;
                if (mask != _cam.cullingMask) _cam.cullingMask = mask;
            }

            if (ads > 0.05f) _cam.enabled = true;
            else
            {
                bool due = Time.unscaledTime >= _nextIdleFrame;
                _cam.enabled = due;
                if (due) _nextIdleFrame = Time.unscaledTime + 1f / IdleHz;
            }

            int key = (st.Nfov ? 1 : 0) | ((int)st.Attack << 1) | (st.Seeking ? 8 : 0) | (st.Locked ? 16 : 0)
                    | (st.InFlight ? 32 : 0) | ((int)st.View << 7)
                    | ((st.Seeking && !st.Locked && (Time.unscaledTime * 2f % 1f) < 0.5f) ? 64 : 0);   // SEEK blinks until locked
            if (key != _drawnKey) { DrawHud(st, (key & 64) == 0); _drawnKey = key; }

            PlaceGates(st);
        }

        public void Dispose()
        {
            if (_camGo != null) Object.Destroy(_camGo);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            foreach (Object o in new Object[] { _hud, _gateTex, _displayMat, _hudMat, _gateMat, _gateMesh })
                if (o != null) Object.Destroy(o);
            _cam = null;
        }

        // ── HUD texture ─────────────────────────────────────────────────────────

        private void DrawHud(in CluState st, bool seekVisible)
        {
            // Frame: black everywhere but the picture, whose corners are rounded.
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    Put(x, y, InPicture(x, y) ? Clear : Frame);

            // Reticle: a cross with an open centre.
            int cx = (VX0 + VX1) / 2, cy = (VY0 + VY1) / 2;
            for (int d = 10; d <= 44; d++) { Put(cx - d, cy, Ret); Put(cx + d, cy, Ret); }
            for (int d = 10; d <= 34; d++) { Put(cx, cy - d, Ret); Put(cx, cy + d, Ret); }

            // In WFOV, corner marks where the NFOV picture would be.
            if (!st.Nfov)
            {
                float k = Config.CluZoomWide / Mathf.Max(1f, Config.CluZoomNarrow);
                int hw = Mathf.RoundToInt((VX1 - VX0) * k * 0.5f), hh = Mathf.RoundToInt((VY1 - VY0) * k * 0.5f);
                CornerMarks(cx - hw, cy - hh, cx + hw, cy + hh, 7, Ret);
            }

            // Top: DAY, the NFOV and WFOV pictograms, NIGHT.
            Text("DAY", 40, 7, 2, st.View == CluView.Day ? Lit : Dim);
            Box(132, 9, 12, 8, st.Nfov ? Lit : Dim, 2);            // NFOV: the small box
            Box(154, 6, 24, 14, st.Nfov ? Dim : Lit, 2);           // WFOV: the large box
            Text("NIGHT", 242, 7, 2, st.View == CluView.Day ? Dim : Lit);
            // Sides: SEEK and FLT on the left, TOP and DIR on the right.
            Text("SEEK", 3, 50, 2, st.Seeking ? (seekVisible ? Lit : Dim) : Dim);
            Text("FLT", 7, 170, 2, st.InFlight ? Lit : Dim);
            Text("TOP", 289, 50, 2, st.Attack == AttackMode.Top ? Lit : Dim);
            Text("DIR", 289, 80, 2, st.Attack == AttackMode.Direct ? Lit : Dim);
            // Bottom: unit status.
            Text("CLU", 40, 223, 2, Lit);
            Text("BCU", 76, 223, 2, Lit);
            // Thermal polarity, bottom right.
            if (st.View == CluView.WHot) Text("WHOT", 250, 223, 2, Lit);
            if (st.View == CluView.BHot) Text("BHOT", 250, 223, 2, Lit);

            _hud.SetPixels32(_px);
            _hud.Apply(false);
        }

        private static bool InPicture(int x, int y)
        {
            if (x < VX0 || x >= VX1 || y < VY0 || y >= VY1) return false;
            int dx = x < VX0 + Corner ? VX0 + Corner - x : (x >= VX1 - Corner ? x - (VX1 - Corner - 1) : 0);
            int dy = y < VY0 + Corner ? VY0 + Corner - y : (y >= VY1 - Corner ? y - (VY1 - Corner - 1) : 0);
            return dx * dx + dy * dy <= Corner * Corner;
        }

        /// <summary>x, y from the top-left, like the layout above.</summary>
        private void Put(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            _px[(H - 1 - y) * W + x] = c;
        }

        private void Box(int x, int y, int w, int h, Color32 c, int t)
        {
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                    if (i < t || j < t || i >= w - t || j >= h - t) Put(x + i, y + j, c);
        }

        private void CornerMarks(int x0, int y0, int x1, int y1, int len, Color32 c)
        {
            for (int i = 0; i < len; i++)
            {
                Put(x0 + i, y0, c); Put(x0, y0 + i, c);
                Put(x1 - i, y0, c); Put(x1, y0 + i, c);
                Put(x0 + i, y1, c); Put(x0, y1 - i, c);
                Put(x1 - i, y1, c); Put(x1, y1 - i, c);
            }
        }

        private static readonly Dictionary<char, string> Font = new Dictionary<char, string>
        {
            ['A'] = "010101111101101", ['B'] = "110101110101110", ['C'] = "011100100100011",
            ['D'] = "110101101101110", ['E'] = "111100110100111", ['F'] = "111100110100100",
            ['G'] = "011100101101011", ['H'] = "101101111101101", ['I'] = "111010010010111",
            ['K'] = "101101110101101", ['L'] = "100100100100111", ['N'] = "110101101101101",
            ['O'] = "111101101101111", ['P'] = "110101110100100", ['R'] = "110101110101101",
            ['S'] = "011100010001110", ['T'] = "111010010010010", ['U'] = "101101101101111",
            ['W'] = "101101101111101", ['Y'] = "101101010010010",
        };

        /// <summary>3x5 pixel font at <paramref name="scale"/>, one glyph column of gap.</summary>
        private void Text(string s, int x, int y, int scale, Color32 c)
        {
            foreach (char ch in s)
            {
                if (Font.TryGetValue(ch, out var g))
                    for (int r = 0; r < 5; r++)
                        for (int col = 0; col < 3; col++)
                            if (g[r * 3 + col] == '1')
                                for (int a = 0; a < scale; a++)
                                    for (int b = 0; b < scale; b++)
                                        Put(x + col * scale + a, y + r * scale + b, c);
                x += 4 * scale;
            }
        }
    }
}
