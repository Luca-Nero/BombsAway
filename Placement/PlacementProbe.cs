using System;
using System.Text;
using Il2CppGame;
using Il2CppPlayer.Appearances.God.InventoryItems;
using Il2CppServices.Game;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BombsAway
{
    /// <summary>
    /// Phase 2 groundwork: can BombsAway drive the game's own spawn hologram?
    ///
    /// The decomp settled *how* the hologram works (see the bombsaway-surface-
    /// placement notes); this settles whether we can *reach* it. Two things the decomp
    /// cannot say, both of which have bitten other mods:
    ///
    /// <b>1. Is the service there before the spawner has been used?</b>
    /// <c>ObjectSpawnHologramService</c> is a Zenject binding, not a MonoBehaviour - no
    /// scene search finds one. It is only reachable through something it was injected
    /// into: in Release the god toolbar's <c>GAToolbarReferences.m_hologramService</c>,
    /// or the human spawner's <c>m_placement.Placement.HologramService</c>. FruktLink hit exactly this
    /// with the cursor tool ("has to have existed at least once"), and its own notes
    /// disagree on whether unselected tools are parked disabled or destroyed. So the
    /// report runs once per scene before anything is touched, and again on demand.
    ///
    /// <b>2. Is the material loaded before the game has shown a hologram?</b>
    /// GGG's fire sound worked only after the vanilla pistol had fired once:
    /// <c>FindObjectsOfTypeAll</c> only sees assets the game has already loaded. The
    /// hologram material has to carry <c>_BaseEmissionColor</c> and <c>_NoiseColor</c>
    /// or <c>HologramAppearance</c> throws, so it cannot be substituted - it has to be
    /// found. Release loads it on demand from Resources ("Materials/Holograms/..."),
    /// so the report shows which of Core's fallbacks resolved it.
    ///
    /// The F10 smoke test that proved the chain end to end is gone: its pieces became
    /// Core.Placement, and a second user of the shared service would only fight it.
    ///
    /// Temporary. Delete once placement has been through a release.
    /// </summary>
    internal static class PlacementProbe
    {
        private const string Tag = "[PlaceProbe] ";

        private static float _sceneStart;
        private static bool _autoReported;
        private const float AutoReportDelay = 5f;

        // ── Entry points (called from Core) ─────────────────────────────────────

        internal static void OnScene()
        {
            _sceneStart = Time.time;
            _autoReported = false;
        }

        internal static void Tick(bool inputLive)
        {
            if (!_autoReported && Config.Dbg1 && Time.time - _sceneStart > AutoReportDelay)
            {
                _autoReported = true;
                Report("auto, " + AutoReportDelay + "s after scene init - nothing equipped by us yet");
            }

            if (inputLive && Input.GetKeyDown(Config.PlacementProbeKey)) Report("manual");
        }

        // ── Discovery report ────────────────────────────────────────────────────

        private static void Report(string why)
        {
            var sb = new StringBuilder();
            sb.Append(Tag).Append("==== report (").Append(why).Append(") t+")
              .Append((Time.time - _sceneStart).ToString("F1")).Append("s ====");
            Log(sb);

            ReportLiveSpawners();
            ReportSpawnerAssets();
            ReportResolved();
            ReportOtherTools();
            ReportFadeMaterials();

            MelonLogger.Msg(Tag + "==== end report ====");
        }

        /// <summary>Live = in the scene, active or parked. This is the only route to the
        /// service itself.</summary>
        private static void ReportLiveSpawners()
        {
            HumanSpawnerGII[] live = LiveSpawners();
            MelonLogger.Msg($"{Tag}HumanSpawnerGII in scene (incl. inactive): {live.Length}");

            foreach (var gii in live)
            {
                var sb = new StringBuilder(Tag).Append("  - '").Append(Safe(() => gii.gameObject.name))
                    .Append("' activeInHierarchy=").Append(Safe(() => gii.gameObject.activeInHierarchy.ToString()));
                Log(sb);

                IObjectSpawnHologramService svc = null;
                try { svc = gii.m_placement?.Placement?.HologramService; } catch (Exception e) { Warn("    reading m_placement.Placement.HologramService threw: " + e.Message); }
                MelonLogger.Msg($"{Tag}    m_placement.Placement.HologramService: {(svc == null ? "NULL (not injected yet?)" : "present")}");

                Material mat = null;
                try { mat = gii.m_humanHologramMaterial; } catch { }
                MelonLogger.Msg($"{Tag}    m_humanHologramMaterial: {DescribeMaterial(mat)}");
            }
        }

        private static HumanSpawnerGII[] LiveSpawners()
        {
            var list = new System.Collections.Generic.List<HumanSpawnerGII>();
            try
            {
                var all = Object.FindObjectsOfType<HumanSpawnerGII>(true);
                if (all != null) foreach (var g in all) if (g != null) list.Add(g);
            }
            catch (Exception e) { Warn("FindObjectsOfType<HumanSpawnerGII> threw: " + e.Message); }
            return list.ToArray();
        }

        private static void DescribeService(IObjectSpawnHologramService svc)
        {
            ObjectSpawnHologramService c = null;
            try { c = svc.TryCast<ObjectSpawnHologramService>(); } catch { }
            if (c == null) { Warn("    TryCast<ObjectSpawnHologramService> failed - a different implementation?"); return; }

            MelonLogger.Msg($"{Tag}    concrete: m_placementRunning={Safe(() => c.m_placementRunning.ToString())} "
                          + $"monoFactory={Present(() => c.m_monoFactory)} "
                          + $"updateLoop={Present(() => c.m_sceneUpdateLoopService)} "
                          + $"appearTimings={Present(() => c.m_appearTimings)} "
                          + $"currentHologram={Present(() => c.m_currentHologram)}");
        }

        /// <summary>Prefab assets are what FindObjectsOfTypeAll adds over the scene
        /// search. Their serialized material exists without anything being spawned -
        /// if it is here at t+5s, the material problem is solved regardless of the
        /// service.</summary>
        private static void ReportSpawnerAssets()
        {
            int total = 0, assets = 0;
            try
            {
                foreach (var gii in Resources.FindObjectsOfTypeAll<HumanSpawnerGII>())
                {
                    if (gii == null) continue;
                    total++;
                    bool inScene = Safe(() => gii.gameObject.scene.IsValid().ToString()) == "True";
                    if (inScene) continue;
                    assets++;
                    Material mat = null;
                    try { mat = gii.m_humanHologramMaterial; } catch { }
                    MelonLogger.Msg($"{Tag}  asset '{Safe(() => gii.gameObject.name)}': material {DescribeMaterial(mat)}");
                }
            }
            catch (Exception e) { Warn("FindObjectsOfTypeAll<HumanSpawnerGII> threw: " + e.Message); }
            MelonLogger.Msg($"{Tag}HumanSpawnerGII loaded anywhere: {total} ({assets} not in a scene = prefab assets)");
        }

        /// <summary>What Core.Placement will actually use, and where it came from.</summary>
        private static void ReportResolved()
        {
            IObjectSpawnHologramService svc = null;
            string svcSource = null;
            try { svc = Core.FindHologramService(out svcSource); }
            catch (Exception e) { Warn("FindHologramService threw: " + e.Message); }
            MelonLogger.Msg($"{Tag}resolved service: {(svc == null ? "NONE" : "via " + svcSource)}");
            if (svc != null) DescribeService(svc);

            Material mat = null;
            string matSource = null;
            try { mat = Core.FindHologramMaterial(out matSource); }
            catch (Exception e) { Warn("FindHologramMaterial threw: " + e.Message); }
            MelonLogger.Msg($"{Tag}resolved material: {(mat == null ? "NONE" : "via " + matSource + " " + DescribeMaterial(mat))}");
        }

        /// <summary>Which god tools exist at all, and in what state - settles the
        /// "parked disabled vs destroyed" question for every tool at once.</summary>
        private static void ReportOtherTools()
        {
            try
            {
                var tools = Object.FindObjectsOfType<GodInventoryItem>(true);
                var sb = new StringBuilder(Tag).Append("GodInventoryItems in scene: ").Append(tools?.Length ?? 0);
                if (tools != null)
                    foreach (var t in tools)
                    {
                        if (t == null) continue;
                        sb.Append("\n").Append(Tag).Append("  - ").Append(Safe(() => t.GetIl2CppType().Name))
                          .Append(" '").Append(Safe(() => t.gameObject.name)).Append("' active=")
                          .Append(Safe(() => t.gameObject.activeInHierarchy.ToString()));
                    }
                Log(sb);
            }
            catch (Exception e) { Warn("GodInventoryItem search threw: " + e.Message); }
        }

        /// <summary>Last resort: any loaded material that would pass the fade check.</summary>
        private static void ReportFadeMaterials()
        {
            try
            {
                var sb = new StringBuilder();
                int n = 0;
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (!CanFade(m)) continue;
                    if (n++ < 12) sb.Append(" '").Append(m.name).Append("'");
                }
                MelonLogger.Msg($"{Tag}Loaded materials passing the fade check: {n}{(n > 0 ? " ->" + sb : "")}");
            }
            catch (Exception e) { Warn("Material scan threw: " + e.Message); }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static bool CanFade(Material m) => Core.CanFade(m);

        private static string DescribeMaterial(Material m)
        {
            if (m == null) return "NULL";
            return $"'{m.name}' shader='{(m.shader != null ? m.shader.name : "none")}' canFade={CanFade(m)}";
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? "null"; } catch (Exception e) { return "<threw " + e.GetType().Name + ">"; }
        }

        private static string Present(Func<object> f)
        {
            try { return f() == null ? "NULL" : "ok"; } catch (Exception e) { return "<threw " + e.GetType().Name + ">"; }
        }

        private static void Log(StringBuilder sb) => MelonLogger.Msg(sb.ToString());
        private static void Warn(string s) => MelonLogger.Warning(Tag + s);
    }
}
