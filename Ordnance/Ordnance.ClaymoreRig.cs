using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The claymore's moving parts, on any copy of the bundled model: in hand (folded, then
    /// deployed by the sequence), in the world and as the hologram (deployed). The prefab holds
    /// the DEPLOYED pose (legs down, sensor head up), so its bounds are the standing mine; the
    /// folded pose is relative to it. Built by Assets/FRUKT/Claymore/claymore_build.py.
    /// </summary>
    internal sealed class ClaymoreRig
    {
        public const float LegFold = 90f;           // folded flat under the body, tips inward
        public const float SensorTravel = 0.024f;   // metres the head sinks into its slot when stowed
        public const float LaserSpread = 30f;       // outer lasers fan out this far (degrees)

        public Transform LegL, LegR, Sensor;
        public readonly Transform[] Lenses = new Transform[3];   // Lens0 on the mine's right (+X) .. Lens2 on its left
        public readonly Renderer[] LensGlow = new Renderer[3];

        private Quaternion _legLHome, _legRHome;
        private Vector3 _legLAxis, _legRAxis, _sensorHome;
        private readonly Vector3[] _lensHome = new Vector3[3];

        public static ClaymoreRig Bind(Transform root)
        {
            var r = new ClaymoreRig { LegL = root.Find("LegL"), LegR = root.Find("LegR"), Sensor = root.Find("Sensor") };
            if (r.LegL == null || r.LegR == null || r.Sensor == null) return null;
            for (int i = 0; i < 3; i++)
            {
                r.Lenses[i] = root.Find("Lens" + i);
                if (r.Lenses[i] == null) return null;
                r.LensGlow[i] = r.Lenses[i].GetComponent<Renderer>();
                r._lensHome[i] = r.Lenses[i].localPosition;
            }
            r._legLHome = r.LegL.localRotation;
            r._legRHome = r.LegR.localRotation;
            r._legLAxis = FoldAxis(r.LegL);
            r._legRAxis = FoldAxis(r.LegR);
            r._sensorHome = r.Sensor.localPosition;
            return r;
        }

        /// <summary>Positive rotation about it swings the leg's tips from down toward the middle.</summary>
        private static Vector3 FoldAxis(Transform leg)
        {
            Vector3 inward = new Vector3(-Mathf.Sign(leg.localPosition.x), 0f, 0f);
            return Vector3.Cross(Vector3.down, inward).normalized;
        }

        /// <summary>0 = folded, 1 = standing.</summary>
        public void Legs(float left, float right)
        {
            LegL.localRotation = Quaternion.AngleAxis(LegFold * (1f - left), _legLAxis) * _legLHome;
            LegR.localRotation = Quaternion.AngleAxis(LegFold * (1f - right), _legRAxis) * _legRHome;
        }

        /// <summary>0 = sunk in its slot, 1 = up. The lenses ride with it.</summary>
        public void Head(float up)
        {
            Vector3 drop = Vector3.down * (SensorTravel * (1f - up));
            Sensor.localPosition = _sensorHome + drop;
            for (int i = 0; i < 3; i++) Lenses[i].localPosition = _lensHome[i] + drop;
        }

        /// <summary>C4Rig.BackOut, but 0 before the start: for parts that begin later in a stage.</summary>
        public static float BackOutUnclamped(float x) => x <= 0f ? 0f : C4Rig.BackOut(x);

        public void Lit(int i, bool on) { if (LensGlow[i] != null) LensGlow[i].enabled = on; }
        public void Lit(bool on) { for (int i = 0; i < 3; i++) Lit(i, on); }

        public void Stowed()   { Legs(0f, 0f); Head(0f); Lit(false); }
        public void Deployed() { Legs(1f, 1f); Head(1f); Lit(true); }

        /// <summary>Where laser <paramref name="i"/> starts (just off its lens) and which way it points, in world space.</summary>
        public void Laser(int i, Transform root, out Vector3 origin, out Vector3 dir)
        {
            float yaw = Mathf.Sign(_lensHome[i].x) * (Mathf.Abs(_lensHome[i].x) > 1e-4f ? LaserSpread : 0f);
            dir = root.TransformDirection(Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward);
            origin = Lenses[i].position + dir * 0.002f;
        }
    }
}
