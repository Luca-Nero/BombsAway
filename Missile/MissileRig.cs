using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The Javelin missile's moving parts: eight mid-body wings that wrap the hull when folded,
    /// four tail fins that fold forward flat along it, and the nozzle glow. The prefab holds the
    /// DEPLOYED pose. A launch starts folded, as it left the tube; the wings and fins spring out
    /// during the soft-launch coast, and the glow lights with the flight motor. Built by
    /// Assets/FRUKT/JavelinMissile/javelin_missile_build.py.
    /// </summary>
    internal sealed class MissileRig
    {
        public const float WingWrap = 90f;   // folded: turned about its root line, lying along the hull
        public const float FinFold = 90f;    // folded: swung forward about its trailing root

        // Deploy timing, seconds after launch: wings first, then the tail fins.
        public const float WingsAt = 0.08f, WingsTime = 0.16f;
        public const float FinsAt = 0.16f, FinsTime = 0.14f;

        public readonly Transform[] Wings = new Transform[8];
        public readonly Transform[] Fins = new Transform[4];
        public Renderer Glow;
        public Transform Nozzle;

        private readonly Quaternion[] _wingHome = new Quaternion[8], _finHome = new Quaternion[4];
        private readonly Vector3[] _finAxis = new Vector3[4];

        public static MissileRig Bind(Transform root)
        {
            var r = new MissileRig();
            for (int i = 0; i < 8; i++)
            {
                r.Wings[i] = root.Find("Wing" + i);
                if (r.Wings[i] == null) return null;
                r._wingHome[i] = r.Wings[i].localRotation;
            }
            for (int i = 0; i < 4; i++)
            {
                r.Fins[i] = root.Find("Fin" + i);
                if (r.Fins[i] == null) return null;
                r._finHome[i] = r.Fins[i].localRotation;
                Vector3 radial = new Vector3(r.Fins[i].localPosition.x, r.Fins[i].localPosition.y, 0f).normalized;
                r._finAxis[i] = Vector3.Cross(radial, Vector3.forward).normalized;   // positive: tip swings toward the nose
            }
            var glow = root.Find("Glow");
            r.Nozzle = glow;
            r.Glow = glow != null ? glow.GetComponent<Renderer>() : null;
            return r;
        }

        /// <summary>0 = folded as in the tube, 1 = deployed.</summary>
        public void Wings01(float t)
        {
            // All the same way round, so folded they lap over one another like the real ones.
            for (int i = 0; i < 8; i++)
                Wings[i].localRotation = Quaternion.AngleAxis(WingWrap * (1f - t), Vector3.forward) * _wingHome[i];
        }

        public void Fins01(float t)
        {
            for (int i = 0; i < 4; i++)
                Fins[i].localRotation = Quaternion.AngleAxis(FinFold * (1f - t), _finAxis[i]) * _finHome[i];
        }

        public void Folded() { Wings01(0f); Fins01(0f); }

        /// <summary>Deploy progress at <paramref name="sinceLaunch"/>: each set snaps out past its stop and settles.</summary>
        public void DeployAt(float sinceLaunch)
        {
            Wings01(ClaymoreRig.BackOutUnclamped((sinceLaunch - WingsAt) / WingsTime));
            Fins01(ClaymoreRig.BackOutUnclamped((sinceLaunch - FinsAt) / FinsTime));
        }

        public static float DeployDone => Mathf.Max(WingsAt + WingsTime, FinsAt + FinsTime);
    }
}
