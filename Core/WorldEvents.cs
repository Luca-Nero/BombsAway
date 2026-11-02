using System;
using System.Runtime.CompilerServices;
using FruitLib;
using UnityEngine;

namespace BombsAway
{
    /// <summary>
    /// Tells FruitLib's world event bus (FruitEvents, 5.12.0) what BombsAway's ordnance is doing:
    /// thrown, landed, stuck, lit, tripped, launched, inbound. Explosions themselves are reported
    /// by FruitLib. Listeners (NeuroFrukt's minds, the event log) see which bodies were near.
    ///
    /// BombsAway still runs on FruitLib 5.10: everything that names a 5.13 type sits in its own
    /// NoInlining method, reached only once the version check has passed.
    /// </summary>
    internal static class WorldEvents
    {
        private static int _available;   // 0 = not asked yet, 1 = yes, -1 = no

        private static bool Available
        {
            get
            {
                if (_available == 0) _available = Check() ? 1 : -1;
                return _available > 0;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Check()
        {
            try { return FruitVersion.AtLeast(5, 13, 0); }
            catch { return false; }
        }

        /// <summary>Plain words for an ordnance GameObject's name.</summary>
        internal static string Label(GrenadeState g)
        {
            switch (g?.Obj != null ? g.Obj.name : "")
            {
                case "C4":           return "C4 charge";
                case "Claymore":     return "claymore mine";
                case "SmokeGrenade": return "smoke grenade";
                case "Flashbang":    return "flashbang";
                case "Grenade":      return "frag grenade";
                default:             return "explosive";
            }
        }

        /// <summary>Everything BombsAway sets off, the player set off, unless a body did:
        /// <paramref name="instigator"/> is the FruitBody.Id that pulled the pin, threw it or fired
        /// it (<see cref="ExplosionParams.Instigator"/>), and the event's actor is that body.
        /// <paramref name="cause"/> names the thing ("frag grenade") for listeners that phrase it themselves.</summary>
        internal static void Publish(string kind, string text, Vector3 at, float radius, float magnitude, string cause, int instigator = 0)
        {
            if (!Available) return;
            try { PublishImpl(kind, text, at, radius, magnitude, cause, instigator); }
            catch (Exception e) { Report(e); }
        }

        /// <summary>A charge stuck to something: if that is a tracked body, the event names it.</summary>
        internal static void Stuck(GrenadeState g, Vector3 at, Collider host)
        {
            if (!Available) return;
            try { StuckImpl(Label(g), at, host, g.Params != null ? g.Params.Instigator : 0); }
            catch (Exception e) { Report(e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PublishImpl(string kind, string text, Vector3 at, float radius, float magnitude, string cause, int instigator)
            => FruitEvents.Publish("BombsAway", kind, text, at, radius: radius, magnitude: magnitude, actor: ActorOf(instigator), cause: cause);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ActorOf(int instigator) => instigator != 0 ? FruitEvents.BodyActor(instigator) : "player";

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StuckImpl(string label, Vector3 at, Collider host, int instigator)
        {
            int limb = -1;
            var body = host != null ? FruitBodies.OfObject(host.gameObject, out limb) : null;
            if (body != null)
            {
                limb = Mathf.Clamp(limb, 0, FruitBodies.LimbCount - 1);
                FruitEvents.Publish("BombsAway", "StuckOnBody", $"a {label} was stuck onto {body.Name}'s {body.Limbs[limb].Name}",
                                    at, radius: 8f, body: body.Id, magnitude: 0.9f, limb: limb, actor: ActorOf(instigator), cause: label);
            }
            else
                FruitEvents.Publish("BombsAway", "Stuck", $"a {label} stuck to a surface", at, radius: 8f, magnitude: 0.5f,
                                    actor: ActorOf(instigator), cause: label);
        }

        private static bool _reported;

        private static void Report(Exception e)
        {
            if (_reported) return;
            _reported = true;
            MelonLoader.MelonLogger.Warning($"[BombsAway] publishing world events failed; reported once: {e.Message}");
        }
    }
}
