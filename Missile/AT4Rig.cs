using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The AT-4 launcher's moving parts (Assets/FRUKT/AT4Launcher/at4_launcher_build.py). The
    /// prefab holds the READY pose (pin in its boss, lever cocked forward along the tube, safety
    /// down, sights up); <see cref="Safe"/> gives the transport pose it comes up in:
    /// lever swung up and back over its hub, safety flipped up, both sights folded flat.
    /// Model space: +Z to the muzzle, +Y up, +X the operator's right; AngleAxis(+a, right)
    /// turns up toward the muzzle.
    /// </summary>
    internal sealed class AT4Rig
    {
        public const float CockUp = 120f;      // uncocked: the lever stands up and back
        public const float SafetyUp = 90f;     // on: flipped up off the tube
        public const float SightFold = 90f;    // folded: rear lies back, front lies forward
        public const float ButtonTravel = 0.004f;

        public Transform Pin, Cock, Safety, Button, SightRear, SightFront, SightDot;
        private Quaternion _cock, _safety, _rear, _front, _dot;
        private Vector3 _pin, _button;

        public static AT4Rig Bind(Transform root)
        {
            var r = new AT4Rig
            {
                Pin = root.Find("Pin"), Cock = root.Find("Cock"), Safety = root.Find("Safety"), Button = root.Find("Button"),
                SightRear = root.Find("SightRear"), SightFront = root.Find("SightFront"), SightDot = root.Find("SightDot"),
            };
            if (r.Pin == null || r.Cock == null || r.Safety == null || r.Button == null
                || r.SightRear == null || r.SightFront == null || r.SightDot == null) return null;
            r._pin = r.Pin.localPosition;
            r._button = r.Button.localPosition;
            r._cock = r.Cock.localRotation;
            r._safety = r.Safety.localRotation;
            r._rear = r.SightRear.localRotation;
            r._front = r.SightFront.localRotation;
            r._dot = r.SightDot.localRotation;
            return r;
        }

        /// <summary>As it comes up: pin in, uncocked, safety on, sights down.</summary>
        public void Safe()
        {
            PinOut(0f);
            CockAngle(-CockUp);
            SafetyAngle(-SafetyUp);
            Sights(0f, 0f);
            Press(0f);
        }

        /// <summary>The pin drawn out of its boss toward the operator's right, <paramref name="metres"/>.</summary>
        public void PinOut(float metres) { if (Pin != null) Pin.localPosition = _pin + Vector3.right * metres; }

        /// <summary>Lever angle from cocked: negative stands it up and back.</summary>
        public void CockAngle(float deg) => Cock.localRotation = Quaternion.AngleAxis(deg, Vector3.right) * _cock;

        public void SafetyAngle(float deg) => Safety.localRotation = Quaternion.AngleAxis(deg, Vector3.right) * _safety;

        /// <summary>0 folded, 1 up; each may overshoot its stop.</summary>
        public void Sights(float rear, float front)
        {
            SightRear.localRotation = Quaternion.AngleAxis(-SightFold * (1f - rear), Vector3.right) * _rear;
            var f = Quaternion.AngleAxis(SightFold * (1f - front), Vector3.right);
            SightFront.localRotation = f * _front;
            SightDot.localRotation = f * _dot;   // the dot rides the post, on the same hinge
        }

        /// <summary>The firing button, 0 out to 1 pressed home.</summary>
        public void Press(float t) => Button.localPosition = _button + Vector3.down * ButtonTravel * t;
    }
}
