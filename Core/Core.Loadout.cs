using FruitLib;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;

namespace BombsAway
{
    public enum Ordnance { Grenade, C4, Claymore, Missile }

    // ══════════════════════════════════════════════════════════════════════════════
    // One inventory item per ordnance type, on the Weapons shelf. LMB launches.
    // Missile: hold RMB to scan, LMB while holding locks the tracked target.
    // ══════════════════════════════════════════════════════════════════════════════

    public partial class Core
    {
        private static readonly string[] OrdnanceLabels = { "Grenade", "C4", "Claymore", "Javelin" };
        private static readonly Color[] OrdnanceColours =
        {
            new Color(0.29f, 0.33f, 0.13f),   // army green
            new Color(0.76f, 0.69f, 0.50f),   // tan
            new Color(0.45f, 0.40f, 0.25f),   // olive drab
            new Color(0.55f, 0.57f, 0.52f),   // launcher grey
        };

        public static Ordnance Selected = Ordnance.Grenade;
        public static bool     Equipped;

        // Card text per ordnance, in enum order.
        private static readonly string[] OrdnanceDescriptions =
        {
            "Fragmentation grenade. Throw it, or place it on a surface up close.",
            "Plastic explosive. Sticks to whatever it hits; right click detonates.",
            "Directional mine. Place it facing the way the blast should go.",
            "Anti-tank missile. Hold right mouse to scan, left click to lock and launch.",
        };
        private static readonly string[] OrdnanceUse = { "throw / place", "stick / detonate", "place", "lock-on launch" };
        private static readonly Sprite[] _icons = new Sprite[4];

        // Unity null check, not ??=: UnloadUnusedAssets on a scene change can destroy these.
        private static Sprite IconFor(Ordnance o)
        {
            int i = (int)o;
            if (_icons[i] == null) _icons[i] = FruitIcons.Solid(OrdnanceColours[i]);
            return _icons[i];
        }

        /// <summary>This item is in hand and gameplay input is live.</summary>
        private static bool Holding(Ordnance o) =>
            Equipped && Selected == o && !FruitMenu.BlocksGameplayInput;

        /// <summary>RMB held with the missile out: scanning for a lock.</summary>
        private static bool SlotScanning => Holding(Ordnance.Missile) && Input.GetMouseButton(1);

        private static void RegisterLoadout()
        {
            for (int i = 0; i < OrdnanceLabels.Length; i++)
            {
                var o = (Ordnance)i;   // one per closure
                FruitInventory.AddItem(new FruitItem
                {
                    Id           = "BombsAway:" + o,
                    Name         = OrdnanceLabels[i],
                    Description  = OrdnanceDescriptions[i],
                    Category     = nameof(FruitItemCategory.Weapon),
                    Icon         = IconFor(o),
                    OnSelected   = item => OnOrdnanceSelected(o, item.Slot),
                    OnDeselected = item => OnOrdnanceDeselected(o, item.Slot),
                }
                .AddStat("use", OrdnanceUse[i]));
            }
        }

        private static void OnOrdnanceSelected(Ordnance o, int slot)
        {
            // Switching straight from one ordnance to another can deliver the new select before
            // the old deselect, so selecting over a held one is a swap. The placement tick
            // restarts the hologram for the new type on its own (_holoFor != Selected).
            bool swap = Equipped && Selected != o;
            Selected = o;
            Equipped = true;
            if (Config.Dbg1) MelonLogger.Msg($"[Loadout] {o} equipped (slot {slot + 1}){(swap ? " over another ordnance" : "")}");
        }

        private static void OnOrdnanceDeselected(Ordnance o, int slot)
        {
            // The late deselect of the ordnance just swapped out: the new one is already in hand.
            if (!Equipped || Selected != o) return;
            Equipped = false;
            // Stop the hologram now, not next frame: the tool being switched to may start its
            // own placement on the same shared service before our Update runs again.
            StopHologram();
            if (Config.Dbg1) MelonLogger.Msg($"[Loadout] {o} holstered (slot {slot + 1})");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // A scene reload drops the held item without firing OnDeselected.
            Equipped = false;
            ResetChargesForScene();
            ResetPlacementForScene();
            PlacementProbe.OnScene();
        }

        /// <summary>The scene's objects are gone, so the charges tracking them (and any chain
        /// detonation queued for them) go too, along with the materials they made.</summary>
        private static void ResetChargesForScene()
        {
            foreach (var g in _grenades) ReleaseMaterials(g.Owned);
            foreach (var m in _missiles) ReleaseMaterials(m.Owned);
            _grenades.Clear();
            _missiles.Clear();
            ClearPending();
            TestBench.OnScene();
            ExplosionVFX.ResetForScene();
        }

        /// <summary>LMB for the equipped ordnance.</summary>
        private static void TickLoadout()
        {
            if (!Equipped || FruitMenu.BlocksGameplayInput) return;

            if (!Input.GetMouseButtonDown(0)) return;
            switch (Selected)
            {
                // Close to a surface: place where the hologram shows. Otherwise throw.
                case Ordnance.Grenade:
                case Ordnance.C4:
                case Ordnance.Claymore:
                    if (_placeValid) PlaceOrdnance(Selected, _placeHit);
                    else             ThrowOrdnance(Selected);
                    break;
                // LMB while RMB is held locks instead (UpdateBody's lock-on block).
                case Ordnance.Missile:  if (!SlotScanning) TryLaunchMissile(); break;
            }
        }
    }
}
