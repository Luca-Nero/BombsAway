using System;
using System.Collections.Generic;
using FruitLib;
using Il2CppCore.MeshData;
using Il2CppGame;
using Il2CppPlayer.Appearances.God.InventoryItems;
using Il2CppPlayer.Appearances.God.Toolbar;
using Il2CppPlayer.GameplayInput.ButtonsActions.MouseKeyboard;
using Il2CppServices.Game;
using Il2CppServices.Inputs;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // Place when close, throw when far.
    //
    // Aim at a surface within PlaceDistance with a grenade, C4 or claymore in hand and
    // the game's own spawn hologram shows exactly where it will go; a click places it
    // there. Farther away the hologram goes and a click throws, as before.
    //
    // The hologram is ObjectSpawnHologramService, the one every spawnable god tool uses,
    // driven directly - PlacementSession is skipped because its surface maths is five
    // lines and ours has to match the thrown-stick pose anyway. What the decomp (v0_17L,
    // re-checked on Release) and the probe settled:
    //   - The service is a Zenject binding; the only reach is something it was injected
    //     into: the god toolbar (GAToolbarReferences) or the parked HumanSpawnerGII's
    //     placement session. Both exist from scene start.
    //   - The material must carry _BaseEmissionColor and _NoiseColor or the service
    //     throws, so it is the game's own (Materials/Holograms/ObjectSpawnHologram), never ours.
    //   - One shared service: StartPlacement twice is an error, so a running session is
    //     stopped before another ordnance's starts.
    //
    // The hologram is a preview, not a dependency. If any of it fails, placing still
    // works - it just places blind.
    // ══════════════════════════════════════════════════════════════════════════════

    public partial class Core
    {
        private const int IgnoreRaycastLayer = 2;
        private const float HologramRetrySeconds = 5f;

        private static IObjectSpawnHologramService _holoSvc;
        private static Material _holoMaterial;
        private static Material _holoTemplateMaterial;
        private static readonly GameObject[] _holoTemplates = new GameObject[3];
        private static bool _holoRunning, _holoVisible;
        private static Ordnance _holoFor;
        private static float _holoRetryAt;
        private static bool _holoWarned;

        /// <summary>This frame's placement target; read by TickLoadout on click.</summary>
        private static bool _placeValid;
        private static RaycastHit _placeHit;

        private static float _lastThrowTime = -99f;

        /// <summary>Yaw about the surface normal, per ordnance, kept between placements.</summary>
        private static readonly float[] _placeYaw = new float[System.Enum.GetValues(typeof(Ordnance)).Length];
        /// <summary>This placement's random yaw, rolled after each one so the hologram shows it.</summary>
        private static float _placeJitter;

        private static bool Placeable(Ordnance o) => !IsLauncher(o) && !IsTool(o);

        /// <summary>Every frame, before TickLoadout, so a click uses this frame's target.</summary>
        private static void TickPlacement()
        {
            _placeValid = false;

            bool want = Config.PlacementEnabled && Equipped && Placeable(Selected)
                     && !FruitMenu.BlocksGameplayInput && !FruitMenu.IsInputSuppressed;
            if (!want) { StopHologram(); return; }

            var cam = Camera.main;
            if (cam == null) { HideHologram(); return; }

            if (Time.time - _lastThrowTime < Config.PlaceCooldownAfterThrow) { HideHologram(); return; }

            _placeValid = TryGetPlaceTarget(cam, out _placeHit);
            if (!_placeValid || !Config.ShowPlacementHologram) { HideHologram(); return; }

            TickPlaceRotation();

            if (_holoRunning && _holoFor != Selected) StopHologram();
            if (!_holoRunning && !StartHologram(Selected)) return;

            Vector3 aim = cam.transform.forward;
            PlacePose(Selected, _placeHit, aim, out Vector3 pos, out _);
            try
            {
                // Snap when it reappears; glide while it is already showing. Yaw goes in
                // separately, the way the game's own placement does it: the service applies
                // SetYRotation on top of its smoothed rotation, so turning is instant instead
                // of fighting the smoothing (which re-times itself from the angle every call).
                _holoSvc.UpdateTargetPosition(pos, SurfaceRotation(_placeHit.normal, aim), !_holoVisible);
                _holoSvc.SetYRotation(PlaceYaw(Selected));
                if (!_holoVisible) { _holoSvc.EnableHologram(); _holoVisible = true; }
                OccupyRotation(true);
            }
            catch (Exception e) { HologramFailed("update", e); }
        }

        /// <summary>
        /// Nearest surface in reach, skipping ordnance that is still loose - a round in
        /// flight, or rolling after a bounce, is not somewhere to put a charge. The throw
        /// cooldown covers the first moments; this covers the rest.
        /// </summary>
        private static bool TryGetPlaceTarget(Camera cam, out RaycastHit hit)
        {
            hit = default;
            // Layer 2 is where the hologram lives; never let the ray land on it.
            int mask = Config.WorldLayerMask & ~(1 << IgnoreRaycastLayer);
            var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward,
                Config.PlaceDistance, mask, QueryTriggerInteraction.Ignore);
            if (hits == null) return false;

            bool found = false;
            foreach (var h in hits)
            {
                if (h.collider == null || (found && h.distance >= hit.distance)) continue;
                if (IsLooseOrdnance(h.collider)) continue;
                hit = h;
                found = true;
            }
            return found;
        }

        private static bool IsLooseOrdnance(Collider c)
        {
            var t = c.transform;
            foreach (var g in _grenades)
                if (!g.Dead && !g.Stuck && g.Obj != null && t.IsChildOf(g.Obj.transform)) return true;
            return false;
        }

        /// <summary>
        /// The one pose both the hologram and the placed charge use.
        /// C4 and claymore get exactly the pose a thrown one sticks with (SurfaceRotation
        /// plus the Placement offsets), so the hologram also previews a thrown landing.
        /// A grenade has no stick offset; it is set down resting on its own bounds.
        /// </summary>
        private static void PlacePose(Ordnance o, RaycastHit hit, Vector3 aim,
            out Vector3 pos, out Quaternion rot)
        {
            // Same composition as the service: surface first, then yaw about its normal.
            rot = SurfaceRotation(hit.normal, aim) * Quaternion.Euler(0f, PlaceYaw(o), 0f);
            Vector3 local = o switch
            {
                Ordnance.C4       => StickOffset(DetonationMode.Remote),
                Ordnance.Claymore => StickOffset(DetonationMode.Proximity),
                _                 => new Vector3(0f, GrenadeRestHeight(o), 0f),
            };
            pos = hit.point + rot * local;
        }

        private static float PlaceYaw(Ordnance o) => _placeYaw[(int)o] + _placeJitter;

        /// <summary>Half the grenade's height, plus a hair so it does not start interpenetrating.</summary>
        private static float GrenadeRestHeight(Ordnance o)
        {
            var prefab = OrdnanceModels.Prefab(o);
            if (prefab != null) return -OrdnanceModels.LocalBounds(prefab, skipLooseParts: true).min.y + 0.01f;

            OrdnanceVisual(Ordnance.Grenade, out string meshName, out float scale);
            var mesh = Meshes?.GetMesh(meshName);
            float half = mesh != null ? mesh.bounds.extents.y * scale : 0.1f;
            return half + 0.01f;
        }

        /// <summary>Mesh and scale of each ordnance, as CreateOrdnance builds it.</summary>
        internal static void OrdnanceVisual(Ordnance o, out string meshName, out float scale)
        {
            switch (o)
            {
                case Ordnance.Grenade:  meshName = "TAG19_mesh";    scale = 0.10f; break;
                case Ordnance.Claymore: meshName = "Claymore_mesh"; scale = 0.15f; break;
                default:                meshName = "C4_mesh";       scale = 0.15f; break;
            }
        }

        // ── Placing ─────────────────────────────────────────────────────────────

        private static GrenadeState PlaceOrdnance(Ordnance o, RaycastHit hit)
        {
            var cam = Camera.main;
            if (cam == null) return null;

            Vector3 aim = cam.transform.forward;
            PlacePose(o, hit, aim, out Vector3 pos, out Quaternion rot);

            var g = CreateOrdnance(o, pos);
            g.ThrowDir = aim;
            g.Obj.transform.rotation = rot;

            if (g.Params.Sticky)
            {
                StickAt(g, pos, rot, hit.collider);
            }
            else if (g.Rb != null)
            {
                // Set down, not thrown: it settles under physics and the fuse runs.
                g.Rb.linearVelocity = Vector3.zero;
                g.Rb.angularVelocity = Vector3.zero;
            }

            _grenades.Add(g);
            _placeJitter = UnityEngine.Random.Range(-Config.PlaceYawJitter, Config.PlaceYawJitter);
            // Vanilla: placing ends rotate mode. Unlike vanilla the yaw is kept for the next one.
            EndRotationMode();

            // The game replays the hologram's fade on every placement; so do we.
            if (_holoRunning && _holoVisible)
                try { _holoSvc.ReplayAppearance(); } catch { }

            if (Config.Dbg1)
                MelonLogger.Msg($"[Place] {o} on '{hit.collider.name}' at {pos} d={hit.distance:F2}");
            return g;
        }

        // ── Hologram session ────────────────────────────────────────────────────

        private static bool StartHologram(Ordnance o)
        {
            if (Time.time < _holoRetryAt) return false;

            try
            {
                if (_holoSvc == null) _holoSvc = FindHologramService(out _);
                if (_holoMaterial == null) _holoMaterial = FindHologramMaterial(out _);

                if (_holoSvc == null || _holoMaterial == null)
                {
                    RetryLater($"hologram unavailable (service {(_holoSvc == null ? "missing" : "ok")}, "
                             + $"material {(_holoMaterial == null ? "missing" : "ok")}) - placing without preview");
                    return false;
                }

                // Somebody else's placement is running on the shared service; leave it be.
                var concrete = _holoSvc.TryCast<ObjectSpawnHologramService>();
                if (concrete != null && concrete.m_placementRunning) { _holoRetryAt = Time.time + 1f; return false; }

                var template = HologramTemplate(o);
                if (template == null) { RetryLater("no mesh for the " + o + " hologram"); return false; }

                // modelTurn 0: our templates are authored facing the way they stick.
                _holoSvc.StartPlacement(template, _holoMaterial, 0f);
                _holoRunning = true;
                _holoVisible = false;
                _holoFor = o;
                DisableVertexGlitch();
                return true;
            }
            catch (Exception e) { HologramFailed("start", e); return false; }
        }

        private static void HideHologram()
        {
            if (!_holoRunning || !_holoVisible) return;
            try { _holoSvc.DisableHologram(); } catch (Exception e) { HologramFailed("hide", e); }
            _holoVisible = false;
            OccupyRotation(false);
        }

        private static void StopHologram()
        {
            if (!_holoRunning) return;
            _holoRunning = false;
            _holoVisible = false;
            OccupyRotation(false);
            // The yaw lives on the shared service; the human spawner must not inherit ours.
            try { _holoSvc.ResetYRotation(); } catch { }
            try { _holoSvc.StopPlacement(); } catch { }
        }

        /// <summary>The service belongs to the scene's container; a new scene has a new one.
        /// The old clone went with the old scene, so there is nothing to stop.</summary>
        private static void ResetPlacementForScene()
        {
            _holoSvc = null;
            _holoRunning = false;
            _holoVisible = false;
            _holoRetryAt = 0f;
            _placeValid = false;
            // The cursor tool the rotation services came from went with the old scene.
            _rotData = null;
            _rotButtons = null;
            _rotLookedUp = false;
            _rotOccupied = false;
            _rotTurning = false;
        }

        private static void HologramFailed(string step, Exception e)
        {
            MelonLogger.Warning($"[Place] hologram {step} failed, retrying in {HologramRetrySeconds}s: {e.Message}");
            StopHologram();
            _holoSvc = null;
            _holoRetryAt = Time.time + HologramRetrySeconds;
        }

        private static void RetryLater(string why)
        {
            _holoRetryAt = Time.time + HologramRetrySeconds;
            if (_holoWarned) return;
            _holoWarned = true;
            MelonLogger.Warning("[Place] " + why + ". Will keep trying quietly.");
        }

        // ── Finding the game's pieces ───────────────────────────────────────────

        /// <summary>The service is injected, not in the scene. Release hands it to the god
        /// toolbar (a MonoBehaviour) and to every PlacementSession; the parked human
        /// spawner's session is the fallback.</summary>
        internal static IObjectSpawnHologramService FindHologramService(out string source)
        {
            try
            {
                foreach (var refs in Object.FindObjectsOfType<GAToolbarReferences>(true))
                {
                    var svc = refs?.m_hologramService;
                    if (svc != null) { source = "GAToolbarReferences"; return svc; }
                }
            }
            catch { }

            try
            {
                foreach (var gii in Object.FindObjectsOfType<HumanSpawnerGII>(true))
                {
                    var svc = gii?.m_placement?.Placement?.HologramService;
                    if (svc != null) { source = "HumanSpawnerGII placement"; return svc; }
                }
            }
            catch { }

            source = null;
            return null;
        }

        /// <summary>Release serves the object hologram material from
        /// IHologramMaterialsHandler, which loads "Materials/Holograms/" + name through
        /// the asset provider. Take the loaded copy if any spawnable already asked for it,
        /// load it ourselves if not; the human spawner's material is the last resort.</summary>
        internal static Material FindHologramMaterial(out string source)
        {
            try
            {
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m != null && m.name == ObjectHologramMaterialName && CanFade(m)) { source = "loaded"; return m; }
            }
            catch { }

            try
            {
                var m = Resources.Load<Material>(ObjectHologramMaterialPath);
                if (CanFade(m)) { source = "Resources.Load"; return m; }
            }
            catch { }

            try
            {
                foreach (var gii in Resources.FindObjectsOfTypeAll<HumanSpawnerGII>())
                    if (gii != null && CanFade(gii.m_humanHologramMaterial)) { source = "HumanSpawnerGII"; return gii.m_humanHologramMaterial; }
            }
            catch { }

            source = null;
            return null;
        }

        private const string ObjectHologramMaterialName = "ObjectSpawnHologram";
        private const string ObjectHologramMaterialPath = "Materials/Holograms/" + ObjectHologramMaterialName;

        internal static bool CanFade(Material m) =>
            m != null && m.HasProperty("_BaseEmissionColor") && m.HasProperty("_NoiseColor");

        private static MeshDataHandler HologramTemplate(Ordnance o)
        {
            int i = (int)o;
            // Unity null: survives scene loads (DontDestroyOnLoad), but not an asset unload.
            if (_holoTemplates[i] == null)
            {
                var built = BuildHologramTemplate(o);
                _holoTemplates[i] = built != null ? built.gameObject : null;
            }
            return _holoTemplates[i] != null ? _holoTemplates[i].GetComponent<MeshDataHandler>() : null;
        }

        /// <summary>
        /// A MeshDataHandler the hologram service accepts, built at runtime.
        ///
        /// Built <b>inactive</b>: MeshDataHandler.Awake dereferences m_bounds without a null
        /// check, so the fields must be written before any Awake runs. The clone inherits
        /// the inactive state and EnableHologram switches it on - which is when Bounds
        /// appears (the probe confirmed it). Layer 2 everywhere keeps our own crosshair ray
        /// off it.
        /// </summary>
        internal static MeshDataHandler BuildHologramTemplate(Ordnance o)
        {
            // Bundled model: one visual per part, at its offset in the prefab (no Rigidbody or
            // collider comes along). Otherwise the JSON mesh, scaled as CreateOrdnance scales it.
            var parts = new List<(string name, Mesh mesh, Vector3 offset, float scale)>();
            Bounds bounds;
            var prefab = OrdnanceModels.Prefab(o);
            if (prefab != null)
            {
                // What gets set down: the body, without the pin and spoon the hand lets go of.
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && !OrdnanceModels.IsLoosePart(mf.name))
                        parts.Add((mf.name, mf.sharedMesh, prefab.transform.InverseTransformPoint(mf.transform.position), 1f));
                bounds = OrdnanceModels.LocalBounds(prefab, skipLooseParts: true);
            }
            else
            {
                OrdnanceVisual(o, out string meshName, out float scale);
                var mesh = HologramMesh(Meshes?.GetMesh(meshName));
                if (mesh != null) parts.Add(("Visual", mesh, Vector3.zero, scale));
                bounds = mesh != null ? new Bounds(mesh.bounds.center * scale, mesh.bounds.size * scale) : default;
            }
            if (parts.Count == 0) return null;

            var root = new GameObject("BA_HoloTemplate_" + o);
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);
            root.layer = IgnoreRaycastLayer;

            // One shared plain material for every template: it is only the "original" the game
            // swaps its hologram material over, so a rebuilt template must not leave a new one behind.
            if (_holoTemplateMaterial == null) _holoTemplateMaterial = new Material(Config.FindShader());

            var renderers = new Il2CppSystem.Collections.Generic.List<MeshRenderer>();
            foreach (var (name, mesh, offset, scale) in parts)
            {
                var visual = new GameObject(name);
                visual.layer = IgnoreRaycastLayer;
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = offset;
                visual.transform.localScale = Vector3.one * scale;
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = visual.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _holoTemplateMaterial;
                renderers.Add(mr);
            }

            C4Rig.Bind(root.transform)?.Armed();          // the charge lands armed, so the preview is too
            ClaymoreRig.Bind(root.transform)?.Deployed();   // and a claymore stands deployed

            // Bounds on the root, in root space, like the game's own prefabs.
            var box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            box.isTrigger = true;
            box.enabled = false;

            var group = new MeshGroup
            {
                m_renderers = renderers,
                m_originalMaterial = _holoTemplateMaterial,
                m_materialType = MeshGroupMaterialType.LitOpaque,
            };

            var mdh = root.AddComponent<MeshDataHandler>();
            mdh.m_bounds = box;
            var groups = new Il2CppSystem.Collections.Generic.List<MeshGroup>();
            groups.Add(group);
            mdh.m_meshData = groups;

            return mdh;
        }

        /// <summary>
        /// The mesh with every submesh folded into one.
        ///
        /// MeshGroup.SetMaterial writes a <b>one-element</b> material array, and Unity draws
        /// submesh N only with material N - so a multi-material mesh showed only its first
        /// piece: the C4's display bar, the claymore without its legs. A copy, so the real
        /// ordnance keeps its materials.
        /// </summary>
        private static Mesh HologramMesh(Mesh src)
        {
            if (src == null || src.subMeshCount <= 1) return src;
            var merged = Object.Instantiate(src);
            merged.name = src.name + "_hologram";
            var all = src.triangles;   // every submesh, concatenated
            merged.subMeshCount = 1;
            merged.triangles = all;
            merged.RecalculateBounds();
            return merged;
        }

        // ── Yaw about the surface normal ────────────────────────────────────────
        //
        // The game's own placement rotates by claiming ObjectRotationMode (MK button action
        // 300) on the buttons-slots service and reading the rotation mode's mouse delta -
        // the same hold-to-rotate the cursor tool uses. Both services are read off the
        // parked cursor tool. Polled, not subscribed: an IManagedEvent wants a
        // SingleShotActionsBag to release into (FruitMenuScreen hit the same thing).
        //
        // Hold PlaceRotateKey + wheel is the fallback, and gives exact steps either way.

        private static IObjectRotationModeDataProvider _rotData;
        private static IMKButtonsSlotsService _rotButtons;
        private static bool _rotLookedUp, _rotOccupied, _rotTurning, _rotWarned, _mouseAxisBroken;
        private static float _rotReclaimAt;
        private const float RotReclaimDelay = 0.1f;

        private static bool PlaceRotateHeld => Placeable(Selected) && Input.GetKey(Config.PlaceRotateKey);

        private static void TickPlaceRotation()
        {
            int i = (int)Selected;

            bool turning = false;
            if (_rotOccupied && _rotData != null)
                try { turning = _rotData.Turn != null && _rotData.Turn.IsRunning; } catch { }

            if (turning != _rotTurning)
            {
                _rotTurning = turning;
                if (Config.Dbg1)
                    MelonLogger.Msg($"[Place] game rotate mode {(turning ? "on" : "off")}"
                                  + (turning ? $" (its RotationSpeed={SafeRotationSpeed():F2})" : ""));
            }

            if (turning) _placeYaw[i] -= MouseX() * Config.PlaceRotateSensitivity;   // the game's sign

            if (PlaceRotateHeld)
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f) _placeYaw[i] += Mathf.Sign(scroll) * Config.PlaceRotateStep;
            }

            _placeYaw[i] = Mathf.Repeat(_placeYaw[i], 360f);
        }

        /// <summary>
        /// Ends the game's rotate mode after a placement, the way the human spawner does:
        /// by handing the key back. Its yaw mode's GiveKeyBack is RemoveOccupyRequest, and
        /// releasing the claim is what makes the button system end the mode <i>and</i> reset
        /// its toggle. ObjectRotationMode.Stop() only did the first half - the mode ended but
        /// the key's toggle stayed latched on, so the next R press merely switched it off.
        /// The claim is taken back on a later frame, as the spawner's Resume does. Yaw is
        /// deliberately kept.
        /// </summary>
        private static void EndRotationMode()
        {
            if (!_rotOccupied) return;
            OccupyRotation(false);
            _rotReclaimAt = Time.time + RotReclaimDelay;
            _rotTurning = false;
        }

        private static float MouseX()
        {
            if (_mouseAxisBroken) return 0f;
            try { return Input.GetAxisRaw("Mouse X"); }
            catch (Exception e)
            {
                _mouseAxisBroken = true;
                MelonLogger.Warning("[Place] no legacy 'Mouse X' axis - mouse rotate off, key + wheel still works: " + e.Message);
                return 0f;
            }
        }

        private static float SafeRotationSpeed()
        {
            try { return _rotData != null ? _rotData.RotationSpeed : 0f; } catch { return 0f; }
        }

        private static void OccupyRotation(bool on)
        {
            if (!Config.UseGameRotationMode) on = false;
            if (on && Time.time < _rotReclaimAt) return;   // just handed back after a placement
            if (on == _rotOccupied) return;

            if (!_rotLookedUp)
            {
                _rotLookedUp = true;
                try
                {
                    foreach (var c in Object.FindObjectsOfType<CursorGodInventoryItem>(true))
                    {
                        if (c == null) continue;
                        _rotData = c.m_objectRotationModeData;
                        _rotButtons = c.m_buttonsSlotsService;
                        break;
                    }
                }
                catch { }
                if (Config.Dbg1)
                    MelonLogger.Msg($"[Place] rotation services: data {(_rotData != null ? "ok" : "missing")}, "
                                  + $"buttons {(_rotButtons != null ? "ok" : "missing")}");
            }
            if (_rotButtons == null) { _rotOccupied = false; return; }

            try
            {
                if (on) _rotButtons.AddOccupyRequest(MKButtonActionType.ObjectRotationMode);
                else    _rotButtons.RemoveOccupyRequest(MKButtonActionType.ObjectRotationMode);
                _rotOccupied = on;
            }
            catch (Exception e)
            {
                _rotOccupied = false;
                _rotButtons = null;
                if (!_rotWarned)
                {
                    _rotWarned = true;
                    MelonLogger.Warning("[Place] claiming the game's rotate key failed - key + wheel still works: " + e.Message);
                }
            }
        }

        // ── Vertex glitch: off ──────────────────────────────────────────────────

        /// <summary>
        /// Switches the NoiseHologram vertex glitch off on the service's own copy of the
        /// material (HologramAppearance clones it and destroys the clone on stop), so the
        /// game's asset and every other hologram keep it.
        ///
        /// It pushes a sparse random set of vertices out along their normals, which throws
        /// long spikes off our meshes. Welding the triangle-soup exports did not cure it and
        /// the developer is replacing the effect for release, so it is simply turned off.
        /// The keyword picks the shader variant; the float is only its inspector toggle,
        /// set too so the two agree.
        /// </summary>
        private static void DisableVertexGlitch()
        {
            try
            {
                var m = _holoSvc.TryCast<ObjectSpawnHologramService>()?.m_appearance?.Material;
                if (m == null) return;
                m.DisableKeyword("_USE_VERTEX_GLITCHES");
                if (m.HasProperty("_USE_VERTEX_GLITCHES")) m.SetFloat("_USE_VERTEX_GLITCHES", 0f);
            }
            catch (Exception e) { MelonLogger.Warning("[Place] could not switch off the hologram vertex glitch: " + e.Message); }
        }
    }
}

