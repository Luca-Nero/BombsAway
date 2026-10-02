using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// A smoke grenade's smoke: the plume (SmokePlume.cs) the can vents while it burns, moved
    /// every frame, then drawn as one faceted volume. Plumes whose smoke meets are splatted into
    /// one grid (SmokeGrid), colour and all, so two colours mix where they meet instead of one
    /// covering the other. Each group is drawn by one of its clouds' Volume boxes (from the
    /// prefab, sized over the group's smoke); the others' boxes are hidden. SmokeVolume.shader
    /// marches through the grid: solid, lit facets where the smoke is thick, nothing (or haze,
    /// with SmokeHaze) where it is thin. The volume writes its own depth, so walls and terrain in
    /// front of it hide it.
    ///
    /// The box is taken off the can at ignition, so the smoke outlives the can.
    ///
    /// Blasts and rockets push the smoke (Blast, Wake): its parcels are thrown aside, and the
    /// hole itself is a void (SmokeVoids) the shader cuts out until the smoke closes in again.
    ///
    /// Smoke hides things (Transmittance): the Javelin's day and night sights can't lock through
    /// it, and it shields eyes from a flashbang. Thermal sees through it: the boxes sit on a
    /// layer of their own (LayerBit) that the CLU's thermal views leave out.
    /// </summary>
    internal sealed class SmokeCloud
    {
        private static readonly List<SmokeCloud> _all = new List<SmokeCloud>();
        private static readonly List<SmokePlume> _plumes = new List<SmokePlume>();
        private static readonly Dictionary<SmokePlume, SmokeCloud> _byPlume = new Dictionary<SmokePlume, SmokeCloud>();
        private static Vector3 _drift;   // the breeze integrated: the noise rides on it, shared so groups match
        private static readonly SmokeVoids _voids = new SmokeVoids();
        private static readonly Il2CppStructArray<Vector4> _voidPack = new Il2CppStructArray<Vector4>(SmokeVoids.Max * 2);

        private readonly SmokePlume _plume;
        private readonly SmokeGrid _grid = new SmokeGrid();
        private readonly GameObject _box;
        private readonly Material _mat;
        private readonly Texture3D _tex;
        private readonly Il2CppStructArray<Color> _px;
        private readonly Transform _vent;
        private Vector3 _ventAt;
        private float _burnUntil;

        /// <summary>Clouds alive now (the HUD's debug readout).</summary>
        public static int Count => _all.Count;

        private SmokeCloud(GameObject box, Transform vent, float burnFor, Color colour, SmokePlume.Settings s)
        {
            _box = box; _vent = vent;
            _ventAt = vent.position;
            _burnUntil = Time.time + burnFor;
            s.Mask = Config.WorldLayerMask;
            _plume = new SmokePlume(s) { Colour = colour };

            const int X = SmokeGrid.MaxDims, Y = SmokeGrid.MaxHeight, Z = SmokeGrid.MaxDims;
            _tex = new Texture3D(X, Y, Z, TextureFormat.RGBAHalf, false);
            _tex.name = "SmokeGrid";
            _tex.wrapMode = TextureWrapMode.Clamp;
            _tex.filterMode = FilterMode.Bilinear;
            _px = new Il2CppStructArray<Color>(SmokeGrid.Count);

            int layer = SmokeLayer();
            if (layer >= 0) box.layer = layer;

            var r = box.GetComponent<Renderer>();
            _mat = new Material(r.sharedMaterial);
            _mat.SetColor("_Color", Color.white);   // the colour is in the grid
            _mat.SetTexture("_Grid", _tex);
            _mat.SetVector("_GridDims", new Vector4(X, Y, Z, 0f));
            r.sharedMaterial = _mat;
            box.SetActive(false);
        }

        /// <summary>
        /// Starts smoke venting at <paramref name="vent"/> (the can's top) for <paramref name="burnFor"/>
        /// seconds. <paramref name="volume"/> is the prefab's Volume box: it is taken off the can so
        /// the smoke outlives it.
        /// </summary>
        public static SmokeCloud Start(GameObject volume, Transform vent, float burnFor, Color colour)
            => Start(volume, vent, burnFor, colour, SmokePlume.Settings.Defaults);

        /// <summary>As above, venting as <paramref name="settings"/> says (a smoke shell's canister vents harder than a grenade).</summary>
        public static SmokeCloud Start(GameObject volume, Transform vent, float burnFor, Color colour, SmokePlume.Settings settings)
        {
            if (volume == null || vent == null || volume.GetComponent<Renderer>() == null) return null;
            var t = volume.transform;
            t.SetParent(null, false);
            t.rotation = Quaternion.identity;
            var c = new SmokeCloud(volume, vent, burnFor, colour, settings);
            _all.Add(c);
            return c;
        }

        /// <summary>The can stopped (burnt out, or gone): no more smoke in.</summary>
        public void StopFeeding() => _burnUntil = Mathf.Min(_burnUntil, Time.time);

        public static void TickAll()
        {
            if (_all.Count == 0) return;
            float dt = Time.deltaTime;
            Vector3 wind = Breeze.Velocity;
            _drift += wind * dt;
            _voids.Tick(dt, wind);

            for (int i = _all.Count - 1; i >= 0; i--)
            {
                var c = _all[i];
                bool alive;
                try { alive = c.Tick(dt, wind); }
                catch (System.Exception e)
                {
                    MelonLogger.Warning($"[Smoke] cloud failed, removing it: {e.Message}");
                    alive = false;
                }
                if (!alive) { c.Destroy(); _all.RemoveAt(i); }
            }

            ShowLayer();

            // Smoke that meets is drawn from one grid, by the first cloud of its group.
            _plumes.Clear(); _byPlume.Clear();
            foreach (var c in _all) { _plumes.Add(c._plume); _byPlume[c._plume] = c; }
            float margin = Margin();
            foreach (var group in SmokeGrid.Groups(_plumes, margin))
            {
                var lead = _byPlume[group[0]];
                try { lead.Draw(group, margin); }
                catch (System.Exception e)
                {
                    MelonLogger.Warning($"[Smoke] drawing failed: {e.Message}");
                    lead._box.SetActive(false);
                }
                for (int k = 1; k < group.Count; k++) _byPlume[group[k]]._box.SetActive(false);
            }
        }

        public static void ClearAll()
        {
            foreach (var c in _all) c.Destroy();
            _all.Clear();
            _voids.Clear();
        }

        /// <summary>
        /// An explosion at <paramref name="at"/>: the smoke near it is thrown out and a bubble
        /// cleared, SmokeBlastClear metres per cube root of a kilo of TNT across (a hand grenade
        /// about 1.8 m, the HE warhead about 4.3 m). A directed charge (the claymore) clears
        /// ahead of itself. Without a charge, the size comes from the blast radius.
        /// </summary>
        public static void Blast(Vector3 at, Vector3 forward, float kgTnt, float blastRadius, bool directed)
        {
            if (_all.Count == 0 || Config.SmokeBlastClear <= 0f) return;
            float clear = kgTnt > 0f
                ? SmokeVoids.ClearRadius(kgTnt, Config.SmokeBlastClear)
                : Mathf.Clamp(blastRadius * 0.3f, 0.6f, 4f) * Config.SmokeBlastClear / 3f;
            if (directed && forward.sqrMagnitude > 1e-4f) at += forward.normalized * (clear * 0.35f);
            Plumes();
            if (_voids.Blast(_plumes, at, clear, Config.SmokeRefill) && Config.Dbg1)
                MelonLogger.Msg($"[Smoke] blast at {at} cleared {clear:F1} m");
        }

        /// <summary>
        /// Something fast flew <paramref name="from"/> to <paramref name="to"/> this frame: a
        /// tunnel <paramref name="radius"/> wide through any smoke on the way.
        /// </summary>
        public static void Wake(Vector3 from, Vector3 to, float radius, float push)
        {
            if (_all.Count == 0 || Config.SmokeBlastClear <= 0f) return;
            Plumes();
            _voids.Wake(_plumes, from, to, radius, push, Config.SmokeRefill);
        }

        private static void Plumes()
        {
            _plumes.Clear();
            foreach (var c in _all) _plumes.Add(c._plume);
        }

        /// <summary>
        /// Share of the view from <paramref name="from"/> to <paramref name="to"/> left through
        /// the smoke (1 = clear, or SmokeBlocksSight off). A metre of solid smoke leaves about 8 %.
        /// </summary>
        public static float Transmittance(Vector3 from, Vector3 to)
        {
            if (_all.Count == 0 || !Config.SmokeBlocksSight) return 1f;
            Plumes();
            return SmokeSight.Transmittance(_plumes, _voids, from, to);
        }

        // ── The smoke's layer ───────────────────────────────────────────────────

        private static int _layer = -2;   // -2: not picked yet; -1: none, the boxes stay on Default

        /// <summary>The smoke boxes' layer as a mask bit; 0 if they have none of their own.</summary>
        public static int LayerBit => _layer >= 0 ? 1 << _layer : 0;

        /// <summary>
        /// A free layer for the boxes, so a camera can leave smoke out (the CLU's thermal views).
        /// Only taken if the pipeline's renderer draws transparent things on it; otherwise the
        /// smoke would vanish for everyone, so it stays where it was.
        /// </summary>
        private static int SmokeLayer()
        {
            if (_layer != -2) return _layer;
            _layer = -1;
            try
            {
                int l = FreeLayers.Take("Smoke");
                if (l < 0) { MelonLogger.Warning("[Smoke] no free layer: thermal sights see the smoke."); return -1; }
                var asset = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
                var list = asset != null ? asset.m_RendererDataList : null;
                if (list != null)
                    foreach (var d in list)
                    {
                        var u = d != null ? d.TryCast<UnityEngine.Rendering.Universal.UniversalRendererData>() : null;
                        if (u != null && (u.transparentLayerMask.value & (1 << l)) == 0)
                        {
                            MelonLogger.Warning($"[Smoke] the renderer doesn't draw transparent layer {l}: thermal sights see the smoke.");
                            return -1;
                        }
                    }
                _layer = l;
                if (Config.Dbg1) MelonLogger.Msg($"[Smoke] boxes on layer {l}.");
            }
            catch (System.Exception e) { MelonLogger.Warning($"[Smoke] picking a layer failed ({e.Message}): thermal sights see the smoke."); }
            return _layer;
        }

        /// <summary>The world camera must draw the smoke's layer (a free layer may be left out of its mask).</summary>
        private static void ShowLayer()
        {
            int bit = LayerBit;
            var cam = CameraCache.Main;
            if (bit != 0 && cam != null && (cam.cullingMask & bit) == 0) cam.cullingMask |= bit;
        }

        /// <summary>Room round the smoke for the shader's warp.</summary>
        private static float Margin() => _all.Count > 0 && _all[0]._mat != null ? _all[0]._mat.GetFloat("_Warp") + 0.6f : 1.6f;

        private void Destroy()
        {
            if (_box != null) Object.Destroy(_box);
            if (_mat != null) Object.Destroy(_mat);
            if (_tex != null) Object.Destroy(_tex);
        }

        private bool Tick(float dt, Vector3 wind)
        {
            if (_box == null) return false;
            if (_vent != null) _ventAt = _vent.position;
            bool feeding = Time.time < _burnUntil && _vent != null;
            _plume.Tick(Mathf.Min(dt, 0.1f), wind, _ventAt, feeding, Config.SmokeDensity);
            return _plume.Alive || feeding;
        }

        /// <summary>A group's smoke into this cloud's texture, its box over it.</summary>
        private void Draw(List<SmokePlume> group, float margin)
        {
            if (!_grid.Splat(group, margin)) { _box.SetActive(false); return; }
            float[] d = _grid.D, r = _grid.R, g = _grid.G, b = _grid.B;
            for (int k = 0; k < SmokeGrid.Count; k++) _px[k] = new Color(r[k], g[k], b[k], d[k]);
            _tex.SetPixels(_px, 0);
            _tex.Apply(false);

            float cell = _grid.Cell;
            Vector3 lo = _grid.Min - Vector3.one * (cell * 0.5f);
            Vector3 hi = _grid.Min + new Vector3(_grid.Nx - 0.5f, _grid.Ny - 0.5f, _grid.Nz - 0.5f) * cell;
            _mat.SetVector("_GridMin", _grid.Min);
            _mat.SetFloat("_GridCell", cell);
            _mat.SetVector("_BoxMin", lo);
            _mat.SetVector("_BoxMax", hi);
            _mat.SetVector("_Drift", _drift);
            _mat.SetFloat("_Age", Time.time);
            _mat.SetFloat("_HazeOn", Config.SmokeHaze ? 1f : 0f);
            int voids = _voids.Pack(_voidPack, lo, hi);
            _mat.SetVectorArray("_Voids", _voidPack);
            _mat.SetFloat("_VoidCount", voids);
            _box.transform.position = (lo + hi) * 0.5f;
            _box.transform.localScale = hi - lo;
            _box.SetActive(true);
        }
    }
}
