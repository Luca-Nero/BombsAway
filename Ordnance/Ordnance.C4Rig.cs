using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The C4 detonator's moving parts, on any copy of the bundled model: the one in hand
    /// (animated from SAFE to ARMED), a charge in the world and the placement hologram (both
    /// ARMED). The prefab holds the geometry with the lever straight up and the antenna
    /// collapsed; every pose here is relative to that. Built by Assets/FRUKT/C4/c4_build.py.
    /// </summary>
    internal sealed class C4Rig
    {
        public const float CoverOpen = 100f;     // degrees about the hinge on the far edge
        public const float LeverThrow = 35f;     // SAFE leans toward the player, ARMED away
        public const float MidTravel = 0.042f;   // antenna segments, metres up out of the socket
        public const float TipTravel = 0.078f;

        public Transform Cover, Lever, AntMid, AntTip;
        public Renderer Led, Screen;

        private Quaternion _coverHome, _leverHome;
        private Vector3 _midHome, _tipHome, _coverAxis;

        /// <summary>The parts under <paramref name="root"/>, or null if this is not the C4 model.</summary>
        public static C4Rig Bind(Transform root)
        {
            var r = new C4Rig
            {
                Cover = root.Find("Cover"), Lever = root.Find("Lever"),
                AntMid = root.Find("AntMid"), AntTip = root.Find("AntTip"),
            };
            if (r.Cover == null || r.Lever == null || r.AntMid == null || r.AntTip == null) return null;
            var led = root.Find("Led"); var screen = root.Find("Screen");
            r.Led = led != null ? led.GetComponent<Renderer>() : null;
            r.Screen = screen != null ? screen.GetComponent<Renderer>() : null;

            r._coverHome = r.Cover.localRotation;
            r._leverHome = r.Lever.localRotation;
            r._midHome = r.AntMid.localPosition;
            r._tipHome = r.AntTip.localPosition;
            // Parts are only offset from the root: hinge -> middle of the cover, in root space.
            Vector3 arm = r.Cover.GetComponent<MeshFilter>().sharedMesh.bounds.center;
            r._coverAxis = Vector3.Cross(arm, Vector3.up).normalized;   // positive: its free edge rises
            return r;
        }

        public void CoverAngle(float deg) => Cover.localRotation = Quaternion.AngleAxis(deg, _coverAxis) * _coverHome;

        /// <summary>Tilt from upright: negative toward the player (SAFE), positive away (ARMED).</summary>
        public void LeverAngle(float deg) => Lever.localRotation = Quaternion.AngleAxis(deg, Vector3.right) * _leverHome;

        public void Antenna(float mid, float tip)
        {
            AntMid.localPosition = _midHome + Vector3.up * (MidTravel * mid);
            AntTip.localPosition = _tipHome + Vector3.up * (TipTravel * tip);
        }

        public void Lit(bool led, bool screen)
        {
            if (Led != null) Led.enabled = led;
            if (Screen != null) Screen.enabled = screen;
        }

        public void Safe()  { CoverAngle(0f);        LeverAngle(-LeverThrow); Antenna(0f, 0f); Lit(false, false); }
        public void Armed() { CoverAngle(CoverOpen); LeverAngle(LeverThrow);  Antenna(1f, 1f); Lit(true, true); }

        /// <summary>Overshoots and settles: a flicked cover hitting its stop, a spring-loaded antenna.</summary>
        public static float BackOut(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            x = Mathf.Clamp01(x) - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }
    }
}
