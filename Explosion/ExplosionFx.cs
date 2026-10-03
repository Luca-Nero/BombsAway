using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// Explosion effects from the bundle: one prefab per explosive ("FX_" + its kind, e.g.
    /// FX_Grenade), authored and tuned in the Unity project (Assets/BombsAway/FX, the voxel kit
    /// in FX/_Kit). This only places and plays them; what they look and sound like lives in the
    /// prefab. Its children are read by name:
    ///   Ground*  only when the blast is within GroundReach of the ground under it (more for a
    ///            MOAB's low air burst); moved onto the ground there and turned to its normal
    ///   Aim*     turned to the blast's forward (a claymore's fan, a HEAT jet, C4 off its
    ///            surface); AimBack* the other way (the claymore's rear puff)
    ///   Sound*   AudioSources (Play On Awake off): played through Sfx, late by the distance over
    ///            the speed of sound; of those named SoundVariant only one, picked at random
    /// The root sits at the blast, +Y up. Particle collision layers are set in the prefab (the
    /// game's build strips the collision module's scripting API, and startDelay with it), and
    /// an effect is destroyed once none of its systems is alive and its sounds have played.
    /// A kind with no prefab falls back to the old code-built effect (Explosion.Vfx.cs).
    /// </summary>
    internal static class ExplosionFx
    {
        private const float GroundReach = 1.5f;
        private const float SpeedOfSound = 343f;

        private const float MaxLife = 40f;

        private sealed class Live { public GameObject Go; public ParticleSystem Root; public float Born, SoundsDone; }
        private static readonly List<Live> _live = new List<Live>();

        /// <summary>Plays the kind's effect; false if the bundle has none (the caller falls back).</summary>
        public static bool Play(string kind, Vector3 origin, Vector3 forward, bool hasGround, RaycastHit ground)
        {
            string name = "FX_" + kind;
            var prefab = OrdnanceModels.Asset(name);
            if (prefab == null) return false;

            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            var go = Object.Instantiate(prefab, origin, Quaternion.LookRotation(flat, Vector3.up));
            go.name = name;

            var root = go.GetComponent<ParticleSystem>();
            if (root != null) root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // A MOAB bursts a couple of metres up, and its dust, stem and shock ring are on the ground all the same.
            bool onGround = hasGround && origin.y - ground.point.y <= (kind == "Moab" ? 15f : GroundReach);
            var cam = Camera.main;
            float delay = cam != null ? Vector3.Distance(cam.transform.position, origin) / SpeedOfSound : 0f;

            // Of the SoundVariant children one plays, picked at random; other Sound* all play (layers).
            var variants = new List<AudioSource>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == "SoundVariant") { var a = t.GetComponent<AudioSource>(); if (a != null) variants.Add(a); }
            if (variants.Count > 0) Sfx.PlaySource(variants[Random.Range(0, variants.Count)], delay);

            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t == go.transform) continue;
                string n = t.name;
                if (n.StartsWith("Ground"))
                {
                    if (!onGround) { t.gameObject.SetActive(false); continue; }
                    t.SetPositionAndRotation(ground.point + ground.normal * t.localPosition.y,
                                             Quaternion.FromToRotation(Vector3.up, ground.normal) * Quaternion.LookRotation(flat, Vector3.up));
                }
                else if (n.StartsWith("Aim") && forward.sqrMagnitude > 1e-4f)
                {
                    Vector3 f = n.StartsWith("AimBack") ? -forward : forward;
                    t.rotation = Quaternion.LookRotation(f, Mathf.Abs(f.y) > 0.95f ? Vector3.forward : Vector3.up);
                }
                else if (n.StartsWith("Sound") && n != "SoundVariant")
                    Sfx.PlaySource(t.GetComponent<AudioSource>(), delay);   // authored with Play On Awake off
            }

            root?.Play(true);

            float sounds = 0f;
            foreach (var a in go.GetComponentsInChildren<AudioSource>(true))
                if (a.clip != null) sounds = Mathf.Max(sounds, a.clip.length / Mathf.Max(0.05f, Mathf.Abs(a.pitch)));   // pitched down plays longer
            _live.Add(new Live { Go = go, Root = root, Born = Time.time, SoundsDone = Time.time + delay + sounds * 1.1f + 0.3f });   // jitter and slow motion
            if (Config.Dbg2) MelonLogger.Msg($"[FX] {name} at {origin} ground={onGround} sound in {delay:F2}s");
            return true;
        }

        /// <summary>Every frame: effects whose particles are all gone and whose sounds have played go too.</summary>
        public static void Tick()
        {
            float now = Time.time;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var l = _live[i];
                bool done = l.Go == null
                    || now - l.Born > MaxLife
                    || (now - l.Born > 0.5f && now >= l.SoundsDone && (l.Root == null || !l.Root.IsAlive(true)));
                if (!done) continue;
                if (l.Go != null) Object.Destroy(l.Go);
                _live.RemoveAt(i);
            }
        }

        public static void Clear()
        {
            foreach (var l in _live) if (l.Go != null) Object.Destroy(l.Go);
            _live.Clear();
        }
    }
}
