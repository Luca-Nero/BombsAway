using System.Linq;
using UnityEngine;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // Equipping as a spawn (a prototype, SpawnTerminal in the Effects menu): instead of rising
    // into view, the item is fought into existence texel by texel while a DOS window beside the
    // hand runs SPAWN BA:<ITEM> (SpawnTerminal.cs). It hovers a little over the hand meanwhile,
    // and once whole it drops in on a spring with one small overshoot, and the hand's own kick
    // spring gives it a sway. Only on equipping: a fresh grenade after a throw, or the AT-4's
    // next tube, still rises as before.
    //
    // Putting it away (deselect, or a swap to another item) is the same in reverse: the model is
    // let go of in view and dissolves into bytes while the window runs FREE BA:<ITEM>.
    // ══════════════════════════════════════════════════════════════════════════════

    public partial class Core
    {
        private static readonly SpawnTerminal _spawnTerm = new SpawnTerminal();

        // The settle: the model's lift over its hold (metres, along the camera's up), on a spring
        // once the spawn lets it go. Under-damped: one small overshoot below the hold.
        private const float SettleStiffness = 300f, SettleDamping = 12f;
        private static Vector3 _heldUp;           // the camera's up in the pivot's space, per metre
        private static float _settle, _settleVel;
        private static bool _settleHold;          // hovering while it spawns

        /// <summary>The model in hand is still being spawned: it can't be thrown or fired yet.</summary>
        private static bool Spawning => _spawnTerm.Busy;

        /// <summary>Starts the spawn for the model just put in the hand.</summary>
        private static void BeginSpawnFx()
        {
            if (_held == null) return;
            string item = _heldKind.ToString().ToUpperInvariant();
            _settle = Config.SpawnSettleLift;
            _settleVel = 0f;
            _settleHold = true;
            ApplyRaise();
            _spawnTerm.OnLive = SettleHeld;
            _spawnTerm.Begin(_held, item, Designation(_heldKind), SpawnFacts(_heldKind));
        }

        /// <summary>Whole: it drops into the hand and the hand sways with it.</summary>
        private static void SettleHeld()
        {
            _settleHold = false;
            _kickVel += Random.onUnitSphere * Config.SpawnSettleSway;
        }

        private static void TickSettle(float dt)
        {
            if (_settleHold || (_settle == 0f && _settleVel == 0f)) return;
            dt = Mathf.Min(dt, 0.05f);   // a hitch must not blow the spring up
            _settleVel += (-SettleStiffness * _settle - SettleDamping * _settleVel) * dt;
            _settle += _settleVel * dt;
            if (Mathf.Abs(_settle) < 1e-5f && Mathf.Abs(_settleVel) < 1e-4f) _settle = _settleVel = 0f;
        }

        /// <summary>The model in hand is put away into bytes; false if it couldn't be (destroy it).</summary>
        private static bool DespawnHeld()
        {
            _settleHold = false;
            return _spawnTerm.Despawn(_held, _heldKind.ToString().ToUpperInvariant(), Designation(_heldKind));
        }

        /// <summary>Every LateUpdate: the spawn's window and its fight.</summary>
        private static void TickSpawnTerminal()
        {
            if (!_spawnTerm.Active) return;
            var cam = Camera.main;
            int layer = cam != null ? ViewmodelCamera.Layer(cam) : -1;
            if (layer >= 0) ViewmodelCamera.Sync();
            _spawnTerm.Tick(cam, _heldParent, layer >= 0 ? layer : (_held != null ? _held.layer : 0), _held);
        }

        /// <summary>What the item is, for RESOLVE.</summary>
        private static string Designation(Ordnance o) => o switch
        {
            Ordnance.Grenade    => "M67 FRAG",
            Ordnance.C4         => "M112 DEMO CHARGE",
            Ordnance.Claymore   => "M18A1 CLAYMORE",
            Ordnance.Missile    => "FGM-148 JAVELIN",
            Ordnance.Rocket     => "M136 AT4",
            Ordnance.Smoke      => "M18 SMOKE",
            Ordnance.Flash      => "M84 STUN",
            Ordnance.Binoculars => "LRF BINOCULARS",
            _                   => o.ToString().ToUpperInvariant(),
        };

        /// <summary>Two lines about it, as the mod has it set up right now.</summary>
        private static string[] SpawnFacts(Ordnance o)
        {
            switch (o)
            {
                case Ordnance.Grenade:  return new[] { $"CHARGE {Config.ChargeKgTNT:0.00} KG TNT", $"FUSE {Config.Fuse:0.0} S" };
                case Ordnance.C4:       return new[] { $"CHARGE {Config.C4ChargeKgTNT:0.00} KG TNT", "DET: REMOTE" };
                case Ordnance.Claymore: return new[] { $"CHARGE {Config.MineChargeKgTNT:0.00} KG TNT", Config.MineTripwire ? "TRIP: 3 LASERS" : "TRIP: CONE" };
                case Ordnance.Missile:  return new[] { $"WARHEAD {WarheadText}", $"ATTACK {MissileAttackMode.ToString().ToUpperInvariant()}" };
                case Ordnance.Rocket:   return new[] { $"WARHEAD {WarheadText}", "84 MM  ONE SHOT" };
                case Ordnance.Smoke:    return new[] { $"DYE {SmokeColourName}", $"BURN {Config.SmokeDuration:0} S" };
                case Ordnance.Flash:    return new[] { $"CHARGE {Config.FlashChargeKgTNT:0.00} KG TNT", $"FUSE {Config.FlashFuse:0.0} S" };
                case Ordnance.Binoculars:
                    var zooms = Config.BinoZoomLevels.Split(',').Select(z => z.Trim()).Where(z => z.Length > 0);
                    return new[] { $"RANGE {Config.LaseRange:0} M", $"ZOOM {string.Join("/", zooms)}X" };
                default: return new string[0];
            }
        }
    }
}
