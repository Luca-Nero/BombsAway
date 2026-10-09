using FruitLib;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;

namespace BombsAway
{
    // Missile is the Javelin (the name stays: it is the item id in saved toolbars); Rocket the AT-4.
    // Appended, never reordered: the names are item ids in saved toolbars.
    public enum Ordnance { Grenade, C4, Claymore, Missile, Rocket, Smoke, Flash, Binoculars }

    // ══════════════════════════════════════════════════════════════════════════════
    // One inventory item per ordnance type, on the mod's own "Bombs Away" shelf. LMB launches.
    // Javelin: RMB raises the CLU and seeks, locking after LockTime; LMB fires at a lock.
    // AT-4: LMB fires where it points, then it reloads.
    // ══════════════════════════════════════════════════════════════════════════════

    public partial class Core
    {
        private static readonly string[] OrdnanceLabels = { "Grenade", "C4", "Claymore", "Javelin", "AT-4", "Smoke", "Flashbang", "Binoculars" };
        private static readonly Color[] OrdnanceColours =
        {
            new Color(0.29f, 0.33f, 0.13f),   // army green
            new Color(0.76f, 0.69f, 0.50f),   // tan
            new Color(0.45f, 0.40f, 0.25f),   // olive drab
            new Color(0.55f, 0.57f, 0.52f),   // launcher grey
            new Color(0.82f, 0.59f, 0.00f),   // hazard yellow
            new Color(0.22f, 0.23f, 0.17f),   // olive drab can
            new Color(0.47f, 0.48f, 0.50f),   // steel
            new Color(0.17f, 0.17f, 0.18f),   // charcoal
        };

        /// <summary>A launcher: fires rather than throws, and is never placed.</summary>
        private static bool IsLauncher(Ordnance o) => o == Ordnance.Missile || o == Ordnance.Rocket;

        /// <summary>Not a weapon at all: never thrown, placed or fired (the binoculars).</summary>
        private static bool IsTool(Ordnance o) => o == Ordnance.Binoculars;

        public static Ordnance Selected = Ordnance.Grenade;
        public static bool     Equipped;

        // Card text per ordnance, in enum order.
        private static readonly string[] OrdnanceDescriptions =
        {
            "Fragmentation grenade. Throw it, or place it on a surface up close.",
            "Plastic explosive. Sticks to whatever it hits; right click detonates.",
            "Directional mine. Place it facing the way the blast should go.",
            "Guided anti-tank missile. Right mouse raises the sight and locks on; left click launches. Its key again: TOP / DIR.",
            "Unguided anti-tank rocket. Left click fires where it points.",
            "Smoke grenade. Burns for a while and hides what is behind it. Its warhead key changes the colour.",
            "Stun grenade. A blinding bang: everyone near it loses their footing for a few seconds.",
            "Laser rangefinder binoculars. Right mouse to look; hold left mouse on a target to lase it and call in a fire mission.",
        };
        private static readonly string[] OrdnanceUse = { "throw / place", "stick / detonate", "place", "lock-on launch", "point and shoot", "throw / place", "throw / place", "lase / call fire" };
        private static readonly Sprite[] _icons = new Sprite[OrdnanceLabels.Length];

        /// <summary>The embedded icon, rendered isometric from the bundled model (Assets/FRUKT).</summary>
        private static string IconFile(Ordnance o) => o switch
        {
            Ordnance.Missile => "Icons/Javelin.png",
            Ordnance.Rocket  => "Icons/AT4.png",
            _                => $"Icons/{o}.png",
        };

        // Unity null check, not ??=: UnloadUnusedAssets on a scene change can destroy these.
        private static Sprite IconFor(Ordnance o)
        {
            int i = (int)o;
            if (_icons[i] == null)
            {
                _icons[i] = FruitIcons.Load(System.Reflection.Assembly.GetExecutingAssembly(), IconFile(o));
                if (_icons[i] == null) _icons[i] = FruitIcons.Solid(OrdnanceColours[i]);
            }
            return _icons[i];
        }

        /// <summary>This item is in hand and gameplay input is live.</summary>
        private static bool Holding(Ordnance o) =>
            Equipped && Selected == o && !FruitMenu.BlocksGameplayInput;

        /// <summary>RMB held with the missile out (and all there): scanning for a lock.</summary>
        private static bool SlotScanning => Holding(Ordnance.Missile) && Input.GetMouseButton(1) && HeldReady(Ordnance.Missile);

        /// <summary>The mod's own shelf in the inventory window (FruitLib adds it after the game's four).</summary>
        private const string CategoryName = "Bombs Away";

        private static void RegisterLoadout()
        {
            FruitInventory.AddCategory(CategoryName,
                FruitIcons.Load(System.Reflection.Assembly.GetExecutingAssembly(), "Icons/Category.png"),
                "Explosives and launchers.");

            for (int i = 0; i < OrdnanceLabels.Length; i++)
            {
                var o = (Ordnance)i;   // one per closure
                FruitInventory.AddItem(new FruitItem
                {
                    Id           = "BombsAway:" + o,
                    Name         = OrdnanceLabels[i],
                    Description  = OrdnanceDescriptions[i],
                    Category     = CategoryName,
                    Icon         = IconFor(o),
                    OnSelected   = item => OnOrdnanceSelected(o, item.Slot, item.Held),
                    OnScroll     = (item, notches) => OnOrdnanceScroll(o, notches),
                    OnMiddle     = item => OnOrdnanceMiddle(o),
                    OnDeselected = item => OnOrdnanceDeselected(o, item.Slot),
                }
                .AddStat("use", OrdnanceUse[i]));
            }
        }

        private static int _selectedSlot = -1;
        private static float _selectedAt;

        /// <summary>Scroll up for the CLU's narrow field of view, down for the wide one (middle click toggles).
        /// The binoculars step their zoom levels the same way (middle click cycles them).</summary>
        private static void OnOrdnanceScroll(Ordnance o, float notches)
        {
            if (notches == 0f) return;
            if (o == Ordnance.Binoculars)
            {
                if (BinocularView.Step(notches > 0f ? 1 : -1)) BinoZoomChanged();
                return;
            }
            if (o != Ordnance.Missile) return;
            bool nfov = notches > 0f;
            if (nfov == _cluNfov) return;
            _cluNfov = nfov;
            Sfx.PlayHeld("CluClick");
            if (Config.Dbg1) MelonLogger.Msg($"[CLU] {(nfov ? "NFOV" : "WFOV")}");
        }

        private static void OnOrdnanceMiddle(Ordnance o)
        {
            if (o == Ordnance.Missile) OnOrdnanceScroll(o, _cluNfov ? -1f : 1f);
            else if (o == Ordnance.Binoculars && BinocularView.Cycle()) BinoZoomChanged();
        }

        private static void BinoZoomChanged()
        {
            Sfx.PlayHeld("CluClick");
            if (Config.Dbg1) MelonLogger.Msg($"[Binoculars] zoom x{BinocularView.Level:0.#}");
        }

        private static void ToggleAttackMode()
        {
            MissileAttackMode = MissileAttackMode == AttackMode.Top ? AttackMode.Direct : AttackMode.Top;
            if (Config.Dbg1) MelonLogger.Msg($"[Missile] Attack mode: {MissileAttackMode}");
        }

        /// <summary>
        /// The launcher's own toolbar key, pressed again while it is in hand. The game ignores a
        /// select of the item already selected (GAItemsSelectRequestsProcessor.TrySelectRequest),
        /// so the key is free to mean TOP / DIR. The press that selected it is not one.
        /// </summary>
        private static bool MissileSlotRepressed()
        {
            if (_selectedSlot < 0 || _selectedSlot > 9 || Time.unscaledTime - _selectedAt < 0.25f) return false;
            var key = _selectedSlot == 9 ? KeyCode.Alpha0 : KeyCode.Alpha1 + _selectedSlot;
            return Input.GetKeyDown(key);
        }

        private static void OnOrdnanceSelected(Ordnance o, int slot, GameObject held)
        {
            _selectedSlot = slot;
            _selectedAt = Time.unscaledTime;
            // Switching straight from one ordnance to another can deliver the new select before
            // the old deselect, so selecting over a held one is a swap. The placement tick
            // restarts the hologram for the new type on its own (_holoFor != Selected).
            bool swap = Equipped && Selected != o;
            Sfx.PlayHeld("Equip" + o);
            Selected = o;
            Equipped = true;
            if (HasHeldModel(o)) AttachHeld(held != null ? held.transform : null, o);
            else                 DetachHeld(despawn: Config.SpawnTerminal);
            if (Config.Dbg1) MelonLogger.Msg($"[Loadout] {o} equipped (slot {slot + 1}){(swap ? " over another ordnance" : "")}");
        }

        private static void OnOrdnanceDeselected(Ordnance o, int slot)
        {
            // The late deselect of the ordnance just swapped out: the new one is already in hand.
            if (!Equipped || Selected != o) return;
            Equipped = false;
            DetachHeld(despawn: Config.SpawnTerminal);
            // Stop the hologram now, not next frame: the tool being switched to may start its
            // own placement on the same shared service before our Update runs again.
            StopHologram();
            if (Config.Dbg1) MelonLogger.Msg($"[Loadout] {o} holstered (slot {slot + 1})");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // A scene reload drops the held item without firing OnDeselected.
            Equipped = false;
            DetachHeld();
            AdsBlur.ResetForScene();
            ViewmodelCamera.Reset();
            ResetChargesForScene();
            ResetPlacementForScene();
            BinocularView.OnScene();
            LookSpeed.OnScene();
            FireMission.OnScene();
            _rocketReadyAt = 0f;
        }

        /// <summary>The scene's objects are gone, so the charges tracking them (and any chain
        /// detonation queued for them) go too, along with the materials they made.</summary>
        private static void ResetChargesForScene()
        {
            ClearAllCharges();
            TestBench.OnScene();
            ExplosionVFX.ResetForScene();
            Sfx.Clear();
            Breeze.OnScene();
        }

        /// <summary>
        /// Every charge and missile in the world goes, without going off: the World menu's
        /// RESET BOMBS, and RESET MAP / RESET ALL, which put the map back in place without a
        /// scene load - so nothing else would clear what the map no longer has room for. What
        /// they set going goes with them: missions and their radio traffic, sounds and shakes
        /// still on their way, explosion effects, and the flashbang's stun and ringing.
        /// </summary>
        internal static void ClearAllCharges()
        {
            SmokeCloud.ClearAll();
            Shockwave.Clear();
            foreach (var g in _grenades)
            {
                if (g.Obj != null) GameObject.Destroy(g.Obj);
                ReleaseMaterials(g.Owned);
                g.Obj = null;
                g.Dead = true;
            }
            foreach (var m in _missiles)
            {
                if (m.Obj != null) GameObject.Destroy(m.Obj);
                ReleaseMaterials(m.Owned);
                m.Obj = null;
                m.Dead = true;
            }
            _grenades.Clear();
            _missiles.Clear();
            FireMission.Clear();
            RadioLog.Clear();
            Thermobaric.Clear();
            ClearPending();
            ClearLooseParts();
            ExplosionFx.Clear();
            Sfx.Stop();
            CameraFX.Clear();
            Flashbang.Clear();
        }

        private static void OnMapReset()
        {
            ClearAllCharges();
            TestBench.OnScene();   // the bench's walls are ours, not the map's
        }

        /// <summary>LMB for the equipped ordnance.</summary>
        private static void TickLoadout()
        {
            if (!Equipped || FruitMenu.BlocksGameplayInput) return;

            if (!Input.GetMouseButtonDown(0)) return;
            switch (Selected)
            {
                // Close to a surface: place where the hologram shows. Otherwise throw. Either
                // way the held model plays its sequence first (grenade: pin and spoon, C4: arming,
                // claymore: deploying; Ordnance.Held.cs); without one it happens at once.
                case Ordnance.Grenade:
                case Ordnance.C4:
                case Ordnance.Claymore:
                case Ordnance.Smoke:
                case Ordnance.Flash:
                    if (ThrowAnimating) break;
                    if (_placeValid) { if (!BeginThrow(_placeHit)) PlaceOrdnance(Selected, _placeHit); }
                    else if (!BeginThrow(null)) ThrowOrdnance(Selected);
                    break;
                // Fires at a lock; the seeker (Missile/Seeker.cs) does the locking, so this works at the eye too.
                case Ordnance.Missile:  TryLaunchMissile(); break;
                case Ordnance.Rocket:   TryFireRocket();    break;
            }
        }

        private static float _rocketReadyAt;

        /// <summary>The AT-4 is loaded again (it fired over RocketReloadTime ago).</summary>
        private static bool RocketReady => Time.time >= _rocketReadyAt;

        /// <summary>LMB with the AT-4: straight out where it points, then a reload.</summary>
        private static void TryFireRocket()
        {
            if (!RocketReady || !RocketInHand) return;
            _rocketReadyAt = Time.time + Config.RocketReloadTime;
            SpawnMissile(null, null, rocket: true);
        }
    }
}
