using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The AT-4 rocket's moving parts: four stamped fins hinged at their leading root, which in
    /// the tube lie folded back behind the nozzle like a shut umbrella and flick out as it
    /// leaves, and the tracer in the nozzle. The prefab holds the DEPLOYED pose. Built by
    /// Assets/FRUKT/AT4Rocket/at4_rocket_build.py.
    /// </summary>
    internal sealed class RocketRig
    {
        public const float FinFold = 90f;                       // folded: swung aft about the leading root
        public const float FinsAt = 0.01f, FinsTime = 0.07f;    // it leaves at full speed: they open at once

        public readonly Transform[] Fins = new Transform[4];
        public Renderer Tracer;
        public Transform Nozzle;

        private readonly Quaternion[] _finHome = new Quaternion[4];
        private readonly Vector3[] _finAxis = new Vector3[4];

        public static RocketRig Bind(Transform root)
        {
            var r = new RocketRig();
            for (int i = 0; i < 4; i++)
            {
                r.Fins[i] = root.Find("Fin" + i);
                if (r.Fins[i] == null) return null;
                r._finHome[i] = r.Fins[i].localRotation;
                Vector3 radial = new Vector3(r.Fins[i].localPosition.x, r.Fins[i].localPosition.y, 0f).normalized;
                r._finAxis[i] = Vector3.Cross(radial, Vector3.forward).normalized;   // positive: tip toward the nose
            }
            r.Nozzle = root.Find("Glow");
            r.Tracer = r.Nozzle != null ? r.Nozzle.GetComponent<Renderer>() : null;
            return r;
        }

        /// <summary>0 = folded as in the tube, 1 = deployed.</summary>
        public void Fins01(float t)
        {
            for (int i = 0; i < 4; i++)
                Fins[i].localRotation = Quaternion.AngleAxis(-FinFold * (1f - t), _finAxis[i]) * _finHome[i];
        }

        public void Folded() => Fins01(0f);

        /// <summary>The fins' spring throws them past their stops, then they settle.</summary>
        public void DeployAt(float sinceLaunch) => Fins01(ClaymoreRig.BackOutUnclamped((sinceLaunch - FinsAt) / FinsTime));

        public static float DeployDone => FinsAt + FinsTime;
    }
}
