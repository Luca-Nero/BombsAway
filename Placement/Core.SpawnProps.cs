using System;
using FruitLib;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // Put down with the game's own spawn mode, used with its interaction key.
    //
    // Every item can be put down as the game's guns are: the spawn-mode key (G) brings up the
    // game's hologram, a click sets the item down in the world as a prop (FruitLib's
    // FruitItem.Prop, FruitSpawnMode). It goes down safe: pin in, cover shut, legs folded,
    // tube unarmed. Pointing at it then shows the game's interaction key, as a placed gun
    // does (FruitPropUse), and pressing it:
    //   grenade, smoke, flashbang - pulls the pin: the pin and spoon fly off and the fuse runs;
    //   C4                        - sets it off;
    //   claymore                  - arms it where it stands: legs out, head up, lasers on;
    //   AT-4, Javelin             - fires it straight along the tube (once: then it is spent);
    //   binoculars                - nothing (a prop).
    // The prop's GameObject becomes the live charge itself (ArmProp), so a body holding it
    // keeps holding it: an NPC can pull the pin and then throw it. NPCs use it through the
    // same OnUse, in the order each item's handling recipe sets (5.31.1, FruitLib 5.30.0):
    //   grenade, smoke, flashbang - pull the pin, then throw it at the target;
    //   C4                        - set it down, get clear, then set it off;
    //   claymore                  - set it down facing the target, arm it, get clear;
    //   AT-4, Javelin             - aim the tube at the target, then fire.
    // Before that an NPC told to use a grenade pulled the pin and stood there holding it.
    //
    // This is apart from the place-when-close click (Core.Placement.cs), which puts a charge
    // down live. While the game's spawn mode is on the mouse is the game's, so that click,
    // the throw and the mod's own hologram all stand down (SpawnModeOn).
    // ══════════════════════════════════════════════════════════════════════════════

    public partial class Core
    {
        private static readonly FruitItem[] _items = new FruitItem[OrdnanceLabels.Length];
        private static readonly GameObject[] _propTemplates = new GameObject[OrdnanceLabels.Length];
        private static GameObject _propHolder;

        /// <summary>What a placed prop is: kept on its FruitWorldItem.Tag.</summary>
        private sealed class PropInfo
        {
            public Ordnance Kind;
            public C4Rig C4;
            public ClaymoreRig Clay;
            public AT4Rig AT4;
            public Color SmokeColor = Color.white;
            public bool Spent;
        }

        /// <summary>The item in hand is in the game's spawn mode: the mouse places it.</summary>
        private static bool SpawnModeOn => Equipped && _items[(int)Selected]?.InSpawnMode == true;

        private static void MakePlaceable(FruitItem item, Ordnance o)
        {
            _items[(int)o] = item;
            item.Placeable(null, PropMass(o), PropAffords(o));
            var p = item.Prop;
            p.MakeModel    = () => PropTemplate(o);
            p.Name         = OrdnanceLabels[(int)o];
            p.MenuCategory = "weapons";
            p.OnPlaced     = (fi, it) => OnPropPlaced(o, it);
            // The game tells the host before it starts its hologram: ours has to be off by then.
            p.OnSpawnMode  = (fi, entered) => { if (entered) StopHologram(); };
            p.UseVerb      = UseVerb(o);
            p.Handling     = HandlingOf(o);
            if (o == Ordnance.C4) p.RetreatDistance = 12f;
            if (o == Ordnance.Claymore) p.RetreatDistance = 6f;
            if (o != Ordnance.Binoculars)
                p.OnUse = (user, it, target, point) => UseProp(o, user, it, target, point);
        }

        private static float PropMass(Ordnance o) => o switch
        {
            Ordnance.Grenade    => 0.5f,
            Ordnance.Smoke      => 0.55f,
            Ordnance.Flash      => 0.4f,
            Ordnance.C4         => 0.75f,
            Ordnance.Claymore   => 1.6f,
            Ordnance.Rocket     => 7f,
            Ordnance.Missile    => 15f,
            _                   => 1f,
        };

        private static bool IsHandGrenade(Ordnance o) => o == Ordnance.Grenade || o == Ordnance.Smoke || o == Ordnance.Flash;

        private static FruitAffords PropAffords(Ordnance o)
        {
            if (o == Ordnance.Smoke)       return FruitAffords.Carry | FruitAffords.Throw;
            if (IsHandGrenade(o))          return FruitAffords.Carry | FruitAffords.Throw | FruitAffords.Explosive;
            if (o == Ordnance.Binoculars)  return FruitAffords.Carry;
            return FruitAffords.Carry | FruitAffords.Explosive;
        }

        private static string UseVerb(Ordnance o)
        {
            if (IsHandGrenade(o))         return "pull the pin";
            if (o == Ordnance.C4)         return "set it off";
            if (o == Ordnance.Claymore)   return "arm it";
            if (IsLauncher(o))            return "fire it";
            return "";
        }

        /// <summary>How a body handles it when told to use it (FruitLib runs the steps).</summary>
        private static FruitUseStep[] HandlingOf(Ordnance o)
        {
            if (IsHandGrenade(o))         return FruitHandling.ArmAndThrow;
            if (o == Ordnance.C4)         return FruitHandling.PlaceAndDetonate;
            if (o == Ordnance.Claymore)   return FruitHandling.PlaceAndArm;
            if (IsLauncher(o))            return FruitHandling.AimAndFire;
            return null;
        }

        // ── The model put down ──────────────────────────────────────────────────

        /// <summary>
        /// A copy of what the hand holds, in its prefab pose, kept inactive. FruitLib makes the
        /// hologram from it and puts copies of it down; each copy is posed safe as it lands
        /// (OnPropPlaced). Null until the bundle has the model.
        /// </summary>
        private static GameObject PropTemplate(Ordnance o)
        {
            var cached = _propTemplates[(int)o];
            if (cached != null) return cached;
            var prefab = OrdnanceModels.HeldPrefab(o);
            if (prefab == null) return null;

            if (_propHolder == null)
            {
                _propHolder = new GameObject("BombsAway Prop Templates");
                _propHolder.SetActive(false);
                _propHolder.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(_propHolder);
            }

            var go = Object.Instantiate(prefab, _propHolder.transform, false);
            go.name = "BombsAway " + OrdnanceLabels[(int)o];
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // Loose in the world, whatever the prefab's body was set to for the hand.
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = false; rb.useGravity = true; }

            // A launcher has no collider of its own (the hand never needed one): a box round it.
            if (go.GetComponentInChildren<Collider>(true) == null)
            {
                var b = OrdnanceModels.LocalBounds(go);
                var box = go.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = Vector3.Max(b.size, Vector3.one * 0.01f);
            }

            _propTemplates[(int)o] = go;
            return go;
        }

        /// <summary>A copy just landed: bind its rig to its prefab pose and fold it safe.</summary>
        private static void OnPropPlaced(Ordnance o, FruitWorldItem it)
        {
            var root = it.Root;
            if (root == null) return;
            var info = new PropInfo { Kind = o };
            info.C4 = C4Rig.Bind(root.transform);
            info.C4?.Safe();
            info.Clay = ClaymoreRig.Bind(root.transform);
            info.Clay?.Stowed();
            info.AT4 = AT4Rig.Bind(root.transform);
            info.AT4?.Safe();
            if (o == Ordnance.Smoke)
            {
                // A property block: Instantiate does not carry the template's over.
                info.SmokeColor = SmokeColour;
                TintBand(root.transform, SmokeColour);
            }
            it.Tag = info;
            // A launcher aims along its tube: breech to muzzle.
            if (IsLauncher(o))
            {
                it.Muzzle = root.transform.Find("Muzzle");
                it.Breech = root.transform.Find("Breech");
            }
            if (Config.Dbg1) MelonLogger.Msg($"[SpawnProps] {o} put down at {root.transform.position}");
        }

        // ── Using it ────────────────────────────────────────────────────────────

        /// <summary>The interaction key on a prop (user null), or a body using one it holds.</summary>
        private static bool UseProp(Ordnance o, FruitBody user, FruitWorldItem it, FruitBody target, Vector3? point)
        {
            if (it == null || !it.Valid || !(it.Tag is PropInfo info) || info.Spent) return false;
            try
            {
                return IsLauncher(o) ? FireProp(o, info, it, user, target, point) : ArmProp(o, info, it, user);
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[SpawnProps] using a placed {o} failed: {e}");
                return false;
            }
        }

        /// <summary>
        /// The prop becomes the live charge, in place: the GameObject it is, its rigidbody,
        /// colliders and right-click menu stay, and BombsAway ticks it from here as any thrown
        /// one. Smoke, grenade and flashbang drop the pin and spoon and start their fuse; C4
        /// goes off; a claymore deploys and sticks where it rests.
        /// </summary>
        private static bool ArmProp(Ordnance o, PropInfo info, FruitWorldItem it, FruitBody user)
        {
            var go = it.Root;
            var rb = it.Rb;
            string who = user != null ? user.Name : "the player";

            RaycastHit ground = default;
            if (o == Ordnance.Claymore)
            {
                // It arms where it stands, on what it stands on: not in a hand.
                if (it.Holder != null) return false;
                int mask = Config.WorldLayerMask & ~(1 << IgnoreRaycastLayer);
                var up = go.transform.up.y > 0.3f ? go.transform.up : Vector3.up;
                if (!Physics.Raycast(go.transform.position + up * 0.1f, -up, out ground, 0.6f, mask, QueryTriggerInteraction.Ignore))
                    return false;
            }

            FruitPropUse.Detach(go);
            it.OnUse = null;
            it.Affords &= ~FruitAffords.Use;
            info.Spent = true;

            var ep = ParamsFor(o, out string name, out _);
            // The interaction key is the player's (no user). A body that arms it is who everything
            // it goes on to do is blamed on, wherever it ends up.
            ep.Instigator = user != null ? user.Id : 0;
            go.name = name;

            // The grenade's pin and spoon fly off it, as they leave the hand in a throw.
            Collider own = go.GetComponent<Collider>();
            Vector3 v0 = rb != null ? rb.linearVelocity : Vector3.zero;
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                var part = go.transform.GetChild(i);
                if (!OrdnanceModels.IsLoosePart(part.name)) continue;
                bool spoon = part.name == "Spoon";
                Vector3 kick = (go.transform.right * (spoon ? 1.4f : -0.6f) + Vector3.up * (spoon ? 1.2f : 0.8f));
                var box = ReleasePart(part, spoon ? 0.02f : 0.006f, v0 + kick, UnityEngine.Random.insideUnitSphere * (spoon ? 40f : 15f), ofThrow: false);
                if (own != null && box != null) Physics.IgnoreCollision(own, box, true);
                Sfx.Play(spoon ? "GrenadeSpoon" : "GrenadePin", go.transform.position);
            }

            var mats = OrdnanceModels.OwnMaterials(go, out Renderer main);
            info.C4?.Armed();
            info.Clay?.Deployed();

            if (rb == null) rb = go.AddComponent(Il2CppType.Of<Rigidbody>()).TryCast<Rigidbody>();
            var g = new GrenadeState
            {
                SmokeColor = o == Ordnance.Smoke ? info.SmokeColor : Color.white,
                Obj = go,
                GrenadeRenderer = main,
                Rb = rb,
                Params = ep,
                BaseColors = SnapshotBaseColors(mats),
                FlashMats = mats,
                Blink = info.C4?.Led,
                Clay = info.Clay,
                ThrowDir = go.transform.forward,
                Landed = true,
            };
            g.Owned.AddRange(mats);

            if (o == Ordnance.Claymore)
            {
                // Sat upright on what it stands on, facing the way it was put down.
                var fwd = Vector3.ProjectOnPlane(go.transform.forward, ground.normal);
                var rot = SurfaceRotation(ground.normal, fwd.sqrMagnitude > 1e-4f ? fwd : go.transform.forward);
                var pos = ground.point + rot * StickOffset(DetonationMode.Proximity);
                StickAt(g, pos, rot, ground.collider);
                // Stuck down and watching: not something to pick up any more.
                FruitItems.Unmark(rb);
            }
            else if (!ep.Sticky) ConfigurePhysicalBody(g);
            else if (rb != null) rb.isKinematic = false;

            if (o == Ordnance.C4) g.RemoteTriggered = true;   // its use is the detonator

            _grenades.Add(g);
            it.Name = o switch
            {
                Ordnance.Claymore => "armed claymore",
                Ordnance.C4       => "C4 going off",
                Ordnance.Smoke    => "smoke grenade, pin pulled",
                _                 => "live " + OrdnanceLabels[(int)o].ToLowerInvariant(),
            };

            string what = WorldEvents.Label(g);
            string text = o switch
            {
                Ordnance.C4       => $"{who} set off the C4",
                Ordnance.Claymore => $"{who} armed a claymore mine",
                _                 => $"{who} pulled the pin on a {what}",
            };
            WorldEvents.Publish("Armed", text, go.transform.position, 12f, o == Ordnance.Smoke ? 0.4f : 0.8f, what, ep.Instigator);
            if (Config.Dbg1) MelonLogger.Msg($"[SpawnProps] {o} armed by {who}");
            return true;
        }

        /// <summary>
        /// A launcher put down fires along its tube, from its Muzzle and Breech markers: the
        /// held launcher's shot, without the hand. A body using one it holds fires at its target
        /// if that is roughly where the tube points. Once: then it is spent.
        /// </summary>
        private static bool FireProp(Ordnance o, PropInfo info, FruitWorldItem it, FruitBody user, FruitBody target, Vector3? point)
        {
            var root = it.Root.transform;
            var muzzle = root.Find("Muzzle");
            var breech = root.Find("Breech");
            if (muzzle == null || breech == null) return false;

            Vector3 m = muzzle.position, b = breech.position;
            Vector3 dir = (m - b).normalized;
            Vector3? aim = target != null && target.HasPosition ? target.LiveHeadPosition - Vector3.up * 0.3f : point;
            if (aim.HasValue)
            {
                Vector3 to = aim.Value - m;
                if (to.sqrMagnitude > 1f && Vector3.Angle(dir, to) < 35f) dir = to.normalized;
            }

            bool rocket = o == Ordnance.Rocket;
            var body = OrdnanceModels.Prefab(o)?.transform.Find("Body");
            float half = body != null ? body.GetComponent<MeshFilter>().sharedMesh.bounds.extents.z : 0.5f;
            if (SpawnMissile(null, null, rocket, m - dir * half, dir, user != null ? user.Id : 0) == null) return false;

            if (rocket)
            {
                ExplosionVFX.SpawnBackblast(b, -dir, m);
                info.AT4?.Press(1f);
            }
            Sfx.Play(rocket ? "AT4Fire" : "JavLaunch", m);
            // A loose tube kicks back; one in a hand is held.
            if (it.Holder == null && it.Rb != null && !it.Rb.isKinematic)
                it.Rb.AddForceAtPosition(-dir * (rocket ? 25f : 40f), b, ForceMode.Impulse);

            info.Spent = true;
            FruitPropUse.Detach(it.Root);
            it.OnUse = null;
            it.Affords &= ~(FruitAffords.Use | FruitAffords.Explosive);
            it.Name = rocket ? "spent AT-4 tube" : "spent Javelin tube";
            if (Config.Dbg1) MelonLogger.Msg($"[SpawnProps] placed {o} fired by {(user != null ? user.Name : "the player")}");
            return true;
        }
    }
}
