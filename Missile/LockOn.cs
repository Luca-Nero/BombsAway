using System.Collections.Generic;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    public partial class Core
    {
        private static readonly HashSet<Rigidbody> _scanSeen = new HashSet<Rigidbody>();

        // Scratch lists for one scan: the targets inside the cone, the collider that found each,
        // and how central each is. Parallel, kept as fields so a scan allocates nothing.
        private static readonly List<Rigidbody> _scanRbs = new List<Rigidbody>();
        private static readonly List<Collider> _scanCols = new List<Collider>();
        private static readonly List<float> _scanDots = new List<float>();

        // A crowd of limbs behind one wall would otherwise cost a ray each, ten times a second.
        private const int MaxLosTestsPerScan = 24;

        // The overlap is throttled: the cone test doesn't need to run every frame.
        private const float ScanInterval = 0.1f;
        private static float _scanNextTime;
        private static Rigidbody _scanResult;

        private static Rigidbody ScanForTarget()
        {
            float now = Time.unscaledTime;
            if (now < _scanNextTime) return _scanResult;
            _scanNextTime = now + ScanInterval;
            _scanResult = ScanForTargetNow();
            return _scanResult;
        }

        private static Rigidbody ScanForTargetNow()
        {
            var cam = CameraCache.Main;
            if (cam == null) return null;

            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;
            float cosThreshold = Mathf.Cos(Config.MissileLockAngle * Mathf.Deg2Rad);

            _scanSeen.Clear();
            _scanRbs.Clear();
            _scanCols.Clear();
            _scanDots.Clear();

            // First the cheap cone test on everything in range; the rays come after, only for
            // what is already in the cone.
            var overlaps = ExplosionSystem.OverlapSphereShared(camPos, Config.MissileLockRange,
                Config.FragLayerMask, QueryTriggerInteraction.Ignore, out int count);
            for (int i = 0; i < count; i++)
            {
                var col = overlaps[i];
                if (col == null) continue;
                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                if (!_scanSeen.Add(rb)) continue;

                Vector3 toTarget = (rb.transform.position - camPos).normalized;
                float dot = Vector3.Dot(toTarget, camFwd);
                if (dot <= cosThreshold) continue;

                _scanRbs.Add(rb);
                _scanCols.Add(col);
                _scanDots.Add(dot);
            }

            // Best-centred first, stopping at the first one that can actually be seen: a target
            // behind a wall must not take the focus (and so the lock) from the one in front of it.
            for (int tests = 0; _scanRbs.Count > 0 && tests < MaxLosTestsPerScan; tests++)
            {
                int bi = 0;
                for (int i = 1; i < _scanDots.Count; i++)
                    if (_scanDots[i] > _scanDots[bi]) bi = i;

                var rb = _scanRbs[bi];
                var col = _scanCols[bi];
                _scanRbs.RemoveAt(bi);
                _scanCols.RemoveAt(bi);
                _scanDots.RemoveAt(bi);

                if (rb == null || col == null) continue;  // destroyed while scanning
                // Smoke hides it from the day and night sights; thermal sees through.
                if (HasLineOfSight(camPos, rb, col) && !CluObscured(rb.worldCenterOfMass)) return rb;
            }

            return null;
        }

        /// <summary>Nothing solid between the camera and the target but the target itself.
        /// Tries its centre of mass first, then the middle of the collider that put it in range,
        /// so a limb peeking round a corner still counts.</summary>
        private static bool HasLineOfSight(Vector3 from, Rigidbody target, Collider col)
        {
            return RayReaches(from, target.worldCenterOfMass, target)
                || RayReaches(from, col.bounds.center, target);
        }

        private static bool RayReaches(Vector3 from, Vector3 to, Rigidbody target)
        {
            Vector3 d = to - from;
            float dist = d.magnitude;
            if (dist < 0.01f) return true;

            // The allocating raycast is the one that works in this build (NonAlloc returns nothing).
            if (!Physics.Raycast(from, d / dist, out RaycastHit hit, dist,
                    Config.FragLayerMask, QueryTriggerInteraction.Ignore))
                return true;

            var hitCol = hit.collider;
            if (hitCol == null) return true;
            var hitRb = hitCol.attachedRigidbody;
            if (hitRb == null) return false;   // static world
            if (hitRb == target) return true;

            // Another limb of the same ragdoll is still the same person, not cover.
            return hitRb.transform.root == target.transform.root
                && ExplosionSystem.IsLimb(hitCol.gameObject)
                && ExplosionSystem.IsLimb(target.gameObject);
        }
    }
}
