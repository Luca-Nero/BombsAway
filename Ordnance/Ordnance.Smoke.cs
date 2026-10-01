using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // The smoke grenade. It is thrown or set down like the frag (pin, spoon, fuse), but when
    // the fuse runs out it lights instead of going off: FX_SmokeGrenade (Unity: VoxelFxRecipes)
    // rides on the can, its jet venting out of the top for SmokeDuration with the can's hiss
    // looping, while the can feeds a cloud that spreads over the ground (SmokeCloud.cs, drawn
    // into the prefab's Volume box, which is taken off the can). Then it stops; the cloud
    // clears on its own, and the spent can lies there for SmokeCanisterLife before it goes.
    // Nothing sets it off as a blast: shot or chained, it only lights.
    //
    // The warhead key cycles the colour while it is in hand. The can's band (its own part,
    // painted near white) shows it, tinted through a property block so nothing is instanced.
    // ══════════════════════════════════════════════════════════════════════════════

    public partial class Core
    {
        private static readonly (string Name, Color Colour)[] SmokeColours =
        {
            ("WHITE",  new Color(0.95f, 0.95f, 0.93f)),
            ("RED",    new Color(0.86f, 0.20f, 0.16f)),
            ("GREEN",  new Color(0.30f, 0.72f, 0.28f)),
            ("VIOLET", new Color(0.58f, 0.32f, 0.78f)),
            ("YELLOW", new Color(0.95f, 0.80f, 0.22f)),
        };

        private static int _smokeColour;
        private static MaterialPropertyBlock _bandBlock;

        private static Color SmokeColour => SmokeColours[_smokeColour].Colour;
        private static string SmokeColourName => SmokeColours[_smokeColour].Name;

        /// <summary>The warhead key with the smoke grenade in hand: the next colour.</summary>
        private static void CycleSmokeColour()
        {
            _smokeColour = (_smokeColour + 1) % SmokeColours.Length;
            Sfx.PlayHeld("CluClick");
            if (_held != null && _heldKind == Ordnance.Smoke) TintBand(_held.transform, SmokeColour);
        }

        /// <summary>Colours a smoke can's band (the part named Band).</summary>
        private static void TintBand(Transform root, Color colour)
        {
            if (root == null) return;
            var band = root.Find("Band");
            var r = band != null ? band.GetComponent<Renderer>() : null;
            if (r == null) return;
            if (_bandBlock == null) _bandBlock = new MaterialPropertyBlock();
            _bandBlock.Clear();
            _bandBlock.SetColor("_BaseColor", colour);
            r.SetPropertyBlock(_bandBlock);
        }

        /// <summary>The fuse ran out: smoke out of the top of the can, hiss, for SmokeDuration.</summary>
        private static void LightSmoke(GrenadeState g)
        {
            if (g.Obj == null || g.SmokeUntil >= 0f) return;
            g.SmokeUntil = Time.time + Config.SmokeDuration;
            var can = g.Obj.transform;

            var prefab = OrdnanceModels.Asset("FX_SmokeGrenade");
            if (prefab != null)
            {
                var fx = Object.Instantiate(prefab);
                fx.transform.SetParent(can, false);
                // Out of the vents in the lid: the top of the can, emitting along its up.
                var bounds = OrdnanceModels.LocalBounds(OrdnanceModels.Prefab(Ordnance.Smoke) ?? g.Obj, skipLooseParts: true);
                fx.transform.localPosition = new Vector3(0f, bounds.max.y * 0.8f, 0f);
                fx.transform.localRotation = Quaternion.identity;
                float density = Mathf.Max(0.05f, Config.SmokeDensity);
                ParticleSystem jet = null;
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.startColor = g.SmokeColor;
                    if (ps.name == "Jet") jet = ps;
                }
                var volume = fx.transform.Find("Volume");
                // The rate's getter is stripped in this build, so the authored rate
                // (VoxelFxRecipes.SmokeGrenade) is restated here to scale it.
                if (jet != null && density != 1f) { var em = jet.emission; em.rateOverTime = 12f * density; }
                g.Cloud = SmokeCloud.Start(volume != null ? volume.gameObject : null, jet, can, Config.SmokeDuration, g.SmokeColor);
                g.SmokeFx = fx;
            }

            Sfx.Play("SmokeIgnite", can.position, can);
            g.Hiss = Sfx.Play("SmokeHissLoop", can.position, can);
            if (Config.Dbg1) MelonLoader.MelonLogger.Msg($"[Smoke] lit ({SmokeColourName}) for {Config.SmokeDuration:F0}s");
        }

        /// <summary>Burning, then burnt out: the cloud is left to thin, and later the can goes.</summary>
        private static void TickSmoke(GrenadeState g)
        {
            if (g.CanGoneAt < 0f)
            {
                if (Time.time < g.SmokeUntil) return;
                // Burnt out: no more smoke, no more hiss. The cloud stays where it is and
                // clears on its own; the jet's last puffs drift off.
                g.Cloud?.StopFeeding();
                if (g.SmokeFx != null)
                    foreach (var ps in g.SmokeFx.GetComponentsInChildren<ParticleSystem>(true))
                        ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                if (g.Hiss != null) { Object.Destroy(g.Hiss.gameObject); g.Hiss = null; }
                g.CanGoneAt = Time.time + Config.SmokeCanisterLife;
                return;
            }

            if (Time.time < g.CanGoneAt) return;
            var jetLeft = g.SmokeFx != null ? g.SmokeFx.transform.Find("Jet") : null;
            var jetPs = jetLeft != null ? jetLeft.GetComponent<ParticleSystem>() : null;
            if (jetPs != null && jetPs.IsAlive(true))
                return;   // the jet's last puffs are still hanging off it

            g.Cloud?.DropJet();
            if (g.SmokeFx != null) Object.Destroy(g.SmokeFx);
            if (g.Obj != null) Object.Destroy(g.Obj);
            ReleaseMaterials(g.Owned);
            g.SmokeFx = null;
            g.Obj = null;
            g.Dead = true;
        }
    }
}
