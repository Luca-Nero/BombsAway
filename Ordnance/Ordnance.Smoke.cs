using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // The smoke grenade. It is thrown or set down like the frag (pin, spoon, fuse), but when
    // the fuse runs out it lights instead of going off: FX_SmokeGrenade (Unity: VoxelFxRecipes)
    // rides on the can's top, venting a plume of smoke for SmokeDuration with the can's hiss
    // looping (SmokeCloud.cs, drawn by the prefab's Volume box, which is taken off the can).
    // Then it stops; the smoke drifts off and clears on its own, and the spent can lies there
    // for SmokeCanisterLife before it goes.
    // Nothing sets it off as a blast: shot or chained, it only lights.
    //
    // The warhead key cycles the colour while it is in hand. The can's band (its own part,
    // painted near white) shows it, tinted through a property block so nothing is instanced.
    // The change isn't a cut: a terminal beside the can reloads the dye while the new colour
    // fights the old one for the band, texel by texel (SmokeTerminal.cs).
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
        private static readonly SmokeTerminal _smokeTerm = new SmokeTerminal();

        private static Color SmokeColour => SmokeColours[_smokeColour].Colour;
        private static string SmokeColourName => SmokeColours[_smokeColour].Name;

        /// <summary>The warhead key with the smoke grenade in hand: the next colour.</summary>
        private static void CycleSmokeColour()
        {
            // From what holds the band now, even part way through the last change.
            Color from = _smokeTerm.Running ? _smokeTerm.Colour : SmokeColour;
            _smokeColour = (_smokeColour + 1) % SmokeColours.Length;
            // While the can is still being spawned its parts are on other materials: the
            // colour simply changes, as it did before the terminal.
            if (_held != null && _heldKind == Ordnance.Smoke && Spawning)
            {
                TintBand(_held.transform, SmokeColour);
                Sfx.PlayHeld("CluClick");
            }
            else if (_held != null && _heldKind == Ordnance.Smoke)
            {
                var band = BandRenderer(_held.transform);
                _smokeTerm.Begin(from, SmokeColour, SmokeColourName, band);
                if (!_smokeTerm.PaintBand(band)) TintBand(_held.transform, SmokeColour);
            }
            else Sfx.PlayHeld("CluClick");
        }

        /// <summary>Every LateUpdate: the terminal beside the can, and the band while it is fought over.</summary>
        private static void TickSmokeTerminal()
        {
            if (!_smokeTerm.Active) return;
            bool inHand = _held != null && _heldKind == Ordnance.Smoke;
            if (!inHand) _smokeTerm.Close();
            var cam = Camera.main;
            int layer = cam != null ? ViewmodelCamera.Layer(cam) : -1;
            if (layer >= 0) ViewmodelCamera.Sync();
            _smokeTerm.Tick(cam, inHand ? _held.transform : null, layer >= 0 ? layer : (_held != null ? _held.layer : 0));
            if (inHand && !_smokeTerm.PaintBand(BandRenderer(_held.transform))) TintBand(_held.transform, SmokeColour);
        }

        private static Renderer BandRenderer(Transform root)
        {
            var band = root != null ? root.Find("Band") : null;
            return band != null ? band.GetComponent<Renderer>() : null;
        }

        /// <summary>Colours a smoke can's band (the part named Band).</summary>
        private static void TintBand(Transform root, Color colour)
        {
            var r = BandRenderer(root);
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
                var volume = fx.transform.Find("Volume");
                g.Cloud = SmokeCloud.Start(volume != null ? volume.gameObject : null, fx.transform, Config.SmokeDuration, g.SmokeColor);
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
                // Burnt out: no more smoke, no more hiss. The smoke drifts off and clears on
                // its own.
                g.Cloud?.StopFeeding();
                if (g.Hiss != null) { Object.Destroy(g.Hiss.gameObject); g.Hiss = null; }
                g.CanGoneAt = Time.time + Config.SmokeCanisterLife;
                return;
            }

            if (Time.time < g.CanGoneAt) return;
            if (g.SmokeFx != null) Object.Destroy(g.SmokeFx);
            if (g.Obj != null) Object.Destroy(g.Obj);
            ReleaseMaterials(g.Owned);
            g.SmokeFx = null;
            g.Obj = null;
            g.Dead = true;
        }
    }
}
