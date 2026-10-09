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

        private static readonly ScreenFeed _feed = new ScreenFeed("BA_CluCam", FeedW, FeedH);   // kept between equips
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _hud, _gateTex;
        private Material _displayMat, _hudMat, _gateMat;
        private Mesh _gateMesh;
        private readonly Transform[] _gates = new Transform[4];
        private float _hudW, _hudH;
        private int _drawnKey = -1;
        private float _nextIdleFrame;
        private readonly PixelCanvas _cv = new PixelCanvas(W, H);
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
            _cam = _feed.Take(window, out _rt);
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.cullingMask = -1;   // as a new camera's: Tick takes the smoke and shock layers in and out
            _cam.nearClipPlane = 0.05f;
            var main = Camera.main;
            if (main != null) _cam.farClipPlane = main.farClipPlane;

            var dr = display.GetComponent<Renderer>();
            _displayMat = new Material(dr.sharedMaterial) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            PixelCanvas.SetTex(_displayMat, _rt);
            _displayMat.SetVector("_FeedSize", new Vector4(FeedW, FeedH, 0f, 0f));   // for the grain (CluDisplay.shader)
            dr.sharedMaterial = _displayMat;

            _hud = PixelCanvas.NewTexture(W, H);
            var hr = hud.GetComponent<Renderer>();
            _hudMat = new Material(hr.sharedMaterial) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            PixelCanvas.SetTex(_hudMat, _hud);
            hr.sharedMaterial = _hudMat;
            var hb = hud.GetComponent<MeshFilter>().sharedMesh.bounds;
            _hudW = hb.size.x; _hudH = hb.size.y;

            BuildGates(hud);
        }

        // ── Track gates ─────────────────────────────────────────────────────────

        private void BuildGates(Transform hud)
        {
            const int G = 8;   // an L, 2 px thick, along the bottom and left edges
            _gateTex = PixelCanvas.NewTexture(G, G);
            var px = new Color32[G * G];
            for (int y = 0; y < G; y++)
                for (int x = 0; x < G; x++)
                    px[y * G + x] = (x < 2 || y < 2) ? Lit : Clear;
            _gateTex.SetPixels32(px);
            _gateTex.Apply(false);
            _gateMat = new Material(_hudMat) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            PixelCanvas.SetTex(_gateMat, _gateTex);

            float s = G / (float)W * _hudW;   // HUD pixels -> metres on the face
            _gateMesh = PixelCanvas.Quad(0f, 0f, s, s);

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
            Shockwave.Admit(_cam);   // shock fronts, while one is out

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
            if (_cam != null) _feed.Give();
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
                    _cv.Put(x, y, InPicture(x, y) ? Clear : Frame);

            // Reticle: a cross with an open centre.
            int cx = (VX0 + VX1) / 2, cy = (VY0 + VY1) / 2;
            for (int d = 10; d <= 44; d++) { _cv.Put(cx - d, cy, Ret); _cv.Put(cx + d, cy, Ret); }
            for (int d = 10; d <= 34; d++) { _cv.Put(cx, cy - d, Ret); _cv.Put(cx, cy + d, Ret); }

            // In WFOV, corner marks where the NFOV picture would be.
            if (!st.Nfov)
            {
                float k = Config.CluZoomWide / Mathf.Max(1f, Config.CluZoomNarrow);
                int hw = Mathf.RoundToInt((VX1 - VX0) * k * 0.5f), hh = Mathf.RoundToInt((VY1 - VY0) * k * 0.5f);
                CornerMarks(cx - hw, cy - hh, cx + hw, cy + hh, 7, Ret);
            }

            // Top: DAY, the NFOV and WFOV pictograms, NIGHT.
            _cv.SmallText("DAY", 40, 7, 2, st.View == CluView.Day ? Lit : Dim);
            _cv.Outline(132, 9, 12, 8, st.Nfov ? Lit : Dim, 2);            // NFOV: the small box
            _cv.Outline(154, 6, 24, 14, st.Nfov ? Dim : Lit, 2);           // WFOV: the large box
            _cv.SmallText("NIGHT", 242, 7, 2, st.View == CluView.Day ? Dim : Lit);
            // Sides: SEEK and FLT on the left, TOP and DIR on the right.
            _cv.SmallText("SEEK", 3, 50, 2, st.Seeking ? (seekVisible ? Lit : Dim) : Dim);
            _cv.SmallText("FLT", 7, 170, 2, st.InFlight ? Lit : Dim);
            _cv.SmallText("TOP", 289, 50, 2, st.Attack == AttackMode.Top ? Lit : Dim);
            _cv.SmallText("DIR", 289, 80, 2, st.Attack == AttackMode.Direct ? Lit : Dim);
            // Bottom: unit status.
            _cv.SmallText("CLU", 40, 223, 2, Lit);
            _cv.SmallText("BCU", 76, 223, 2, Lit);
            // Thermal polarity, bottom right.
            if (st.View == CluView.WHot) _cv.SmallText("WHOT", 250, 223, 2, Lit);
            if (st.View == CluView.BHot) _cv.SmallText("BHOT", 250, 223, 2, Lit);

            _cv.Upload(_hud);
        }

        private static bool InPicture(int x, int y)
        {
            if (x < VX0 || x >= VX1 || y < VY0 || y >= VY1) return false;
            int dx = x < VX0 + Corner ? VX0 + Corner - x : (x >= VX1 - Corner ? x - (VX1 - Corner - 1) : 0);
            int dy = y < VY0 + Corner ? VY0 + Corner - y : (y >= VY1 - Corner ? y - (VY1 - Corner - 1) : 0);
            return dx * dx + dy * dy <= Corner * Corner;
        }

        private void CornerMarks(int x0, int y0, int x1, int y1, int len, Color32 c)
        {
            for (int i = 0; i < len; i++)
            {
                _cv.Put(x0 + i, y0, c); _cv.Put(x0, y0 + i, c);
                _cv.Put(x1 - i, y0, c); _cv.Put(x1, y0 + i, c);
                _cv.Put(x0 + i, y1, c); _cv.Put(x0, y1 - i, c);
                _cv.Put(x1 - i, y1, c); _cv.Put(x1, y1 - i, c);
            }
        }
    }
}
