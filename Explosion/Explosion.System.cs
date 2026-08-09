using Il2Cpp;
using Il2CppEffectors;
using Il2CppEffectors.ReceiveMethods.Index;
using Il2CppInterop.Runtime;
using Il2CppLVA.Organs.EffectorsPerception.Collectors;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using Color = UnityEngine.Color;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // ExplosionSystem
    // ══════════════════════════════════════════════════════════════════════════════
    internal static class ExplosionSystem
    {
        // ── Noise map  ───────────────────────────────────────────────────────────
        private const int NoiseMapSize = 256;
        private static float[] _noiseMap;

        private static readonly Il2CppSystem.Type LimbReceiverType = Il2CppType.Of<LimbEffectorReceiver>();
        private static readonly Dictionary<Rigidbody, Vector3> _shockImpulses = new Dictionary<Rigidbody, Vector3>();
        private static readonly Dictionary<Rigidbody, Vector3> _fragImpulses = new Dictionary<Rigidbody, Vector3>();
        private static readonly HashSet<LimbEffectorReceiver> _limbsInRange = new HashSet<LimbEffectorReceiver>();
        private static readonly Dictionary<int, LimbEffectorReceiver> _limbCache = new Dictionary<int, LimbEffectorReceiver>();

        public static void Init()
        {
            _noiseMap = new float[NoiseMapSize * NoiseMapSize];
            float scale = 0.12f;
            for (int y = 0; y < NoiseMapSize; y++)
                for (int x = 0; x < NoiseMapSize; x++)
                    _noiseMap[y * NoiseMapSize + x] = Mathf.PerlinNoise(x * scale, y * scale);
        }

        private static float NoiseAt(int x, int y, int offX, int offY)
        {
            int xi = ((x + offX) % NoiseMapSize + NoiseMapSize) % NoiseMapSize;
            int yi = ((y + offY) % NoiseMapSize + NoiseMapSize) % NoiseMapSize;
            return _noiseMap[yi * NoiseMapSize + xi];
        }

        // ── Shared overlap query ─────────────────────────────────────────────

        internal static Collider[] OverlapSphereShared(Vector3 pos, float radius, int mask,
            QueryTriggerInteraction q, out int count)
        {
            var result = Physics.OverlapSphere(pos, radius, mask, q);
            count = result.Length;
            return result;
        }

        // ── Ballistic ground-intersection solve ─────────────────────────────────

        internal static float BallisticGroundTime(float gy, float vy, float dy, float maxTime)
        {
            float a = 0.5f * gy, b = vy, c = dy;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return maxTime;
            float sq = Mathf.Sqrt(disc);
            float t1 = (-b + sq) / (2f * a);
            float t2 = (-b - sq) / (2f * a);
            float tHit = -1f;
            if (t1 > 0.001f && t2 > 0.001f) tHit = Mathf.Min(t1, t2);
            else if (t1 > 0.001f) tHit = t1;
            else if (t2 > 0.001f) tHit = t2;
            return tHit > 0f ? Mathf.Min(tHit, maxTime) : maxTime;
        }

        // ── Elliptical cone half-angle at a given azimuth ────────────────────────

        internal static float EllipticalHalfAngle(float azimuth, float tanH, float tanV)
        {
            float cosAz = Mathf.Cos(azimuth);
            float sinAz = Mathf.Sin(azimuth);
            return Mathf.Atan2(1f,
                Mathf.Sqrt((cosAz * cosAz) / (tanH * tanH) + (sinAz * sinAz) / (tanV * tanV)));
        }

        // ── Cone attenuation ──────────────────────────────────────────────────
        private static float ConeAttenuation(Vector3 worldDir, ExplosionParams p)
        {
            if (p.IsFullSphere) return 1f;

            Vector3 fwd = p.Forward.normalized;
            Vector3 d = worldDir.normalized;

            float cosAngle = Vector3.Dot(d, fwd);

            Quaternion look = Quaternion.LookRotation(fwd);
            Quaternion inv = Quaternion.Inverse(look);
            Vector3 local = inv * d;

            float hHalf = p.HSpreadDeg * 0.5f * Mathf.Deg2Rad;
            float vHalf = p.VSpreadDeg * 0.5f * Mathf.Deg2Rad;

            float azimuth = Mathf.Atan2(local.y, local.x);
            float tanH = Mathf.Tan(Mathf.Min(hHalf, 1.5f));
            float tanV = Mathf.Tan(Mathf.Min(vHalf, 1.5f));
            float halfAngle = EllipticalHalfAngle(azimuth, tanH, tanV);

            float cosInner = Mathf.Cos(halfAngle);
            float cosOuter = Mathf.Cos(Mathf.Min(halfAngle * 1.15f, Mathf.PI));
            if (cosAngle >= cosInner) return 1f;
            if (cosAngle <= cosOuter) return 0f;
            return Mathf.InverseLerp(cosOuter, cosInner, cosAngle);
        }

        // ── Limb resolution ──────────────────────────────────────────────────
        internal static bool IsLimb(GameObject obj) => ResolveLimbUncached(obj) != null;

        private static LimbEffectorReceiver ResolveLimbUncached(GameObject obj)
        {
            var comp = obj.GetComponentInParent(LimbReceiverType);
            return comp == null ? null : comp.TryCast<LimbEffectorReceiver>();
        }

        private static LimbEffectorReceiver ResolveLimbCached(GameObject obj)
        {
            int id = obj.GetInstanceID();
            if (_limbCache.TryGetValue(id, out var cached)) return cached;
            var receiver = ResolveLimbUncached(obj);
            _limbCache[id] = receiver;
            return receiver;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Detonate — main entry point
        // ══════════════════════════════════════════════════════════════════════
        public static void Detonate(ExplosionParams p)
        {
            FruitLib.FruitPerfMon.Begin("BA Detonate");
            try { DetonateInternal(p); }
            finally { FruitLib.FruitPerfMon.End("BA Detonate"); }
        }

        private static void DetonateInternal(ExplosionParams p)
        {
            Vector3 origin = p.Origin;
            float qScale = Config.QualityScale;
            _limbCache.Clear();

            if (p.CameraFX) CameraFX.AddTrauma(origin);

            int shockCount = ApplyShockwave(origin, p);

            int woundBudget = Mathf.Max(1, Config.MaxWoundsPerExplosion);
            int opWounds;
            (woundBudget, opWounds) = ApplyOverpressure(origin, p, qScale, woundBudget);

            // ── Ground detection + VFX ────────────────────────────────────
            float groundY = origin.y - 50f;
            bool hasGround = Physics.Raycast(origin, Vector3.down, out RaycastHit groundHit, 200f,
                Config.WorldLayerMask, QueryTriggerInteraction.Ignore);
            if (hasGround)
            {
                groundY = groundHit.point.y;
                ExplosionVFX.Spawn(origin, groundHit);
            }
            else
            {
                ExplosionVFX.SpawnAerial(origin);
            }

            var (rays, fragWounds) = ApplyFragmentation(origin, groundY, p, qScale, woundBudget);

            // ── Apply all impulses last, after wounds have registered ─────
            foreach (var kvp in _fragImpulses)
                if (kvp.Key != null) kvp.Key.linearVelocity += kvp.Value;
            foreach (var kvp in _shockImpulses)
                if (kvp.Key != null) kvp.Key.linearVelocity += kvp.Value;

            if (Config.Dbg1) MelonLogger.Msg($"Detonate at {origin} | " +
                $"cone={p.HSpreadDeg:F0}x{p.VSpreadDeg:F0}° fwd={p.Forward} qScale={qScale:F2} | " +
                $"shockwave: {shockCount} rb | wounds: {opWounds + fragWounds}/{rays} | " +
                $"frag impulse: {_fragImpulses.Count} rb");
        }

        // ── Shockwave — physics push, accumulated and applied after all wounds ──
        private static int ApplyShockwave(Vector3 origin, ExplosionParams p)
        {
            _shockImpulses.Clear();
            var overlaps = OverlapSphereShared(origin, p.BlastRadius,
                Config.FragLayerMask, QueryTriggerInteraction.Ignore, out int count);
            for (int i = 0; i < count; i++)
            {
                var col = overlaps[i];
                if (col == null) continue;
                var rb = col.GetComponentInParent<Rigidbody>();
                if (rb == null || _shockImpulses.ContainsKey(rb)) continue;
                Vector3 dir = (rb.transform.position - origin).normalized;
                float dist = Vector3.Distance(rb.transform.position, origin);
                float coneAtt = ConeAttenuation(dir, p);
                if (coneAtt < 0.01f) continue;
                float falloff = (1f - Mathf.Clamp01(dist / p.BlastRadius)) * coneAtt;
                _shockImpulses[rb] = dir * p.BlastForce * falloff
                                  + Vector3.up * p.BlastUpward * falloff;
            }
            return _shockImpulses.Count;
        }

        // ── Overpressure — close-range wound sampling around every limb in range ──

        private static (int woundBudget, int woundCount) ApplyOverpressure(
            Vector3 origin, ExplosionParams p, float qScale, int woundBudget)
        {
            int noiseOffX = SharedRng.Instance.Next(0, NoiseMapSize);
            int noiseOffY = SharedRng.Instance.Next(0, NoiseMapSize);

            _limbsInRange.Clear();
            var overlaps = OverlapSphereShared(origin, p.OverpressureRadius,
                Config.FragLayerMask, QueryTriggerInteraction.Ignore, out int count);
            for (int i = 0; i < count; i++)
            {
                var col = overlaps[i];
                if (col == null) continue;
                var receiver = ResolveLimbCached(col.gameObject);
                if (receiver != null) _limbsInRange.Add(receiver);
            }

            float goldenAngleOP = Mathf.PI * (3f - Mathf.Sqrt(5f));
            int opPoints = Mathf.Max(1, Mathf.RoundToInt(p.OverpressureWoundPoints * qScale));
            int woundCount = 0;

            foreach (var receiver in _limbsInRange)
            {
                if (woundBudget <= 0) break;
                if (receiver.gameObject == null) continue;
                Vector3 limbCenter = receiver.gameObject.transform.position;
                float limbDist = Vector3.Distance(origin, limbCenter);

                Vector3 limbDir = (limbCenter - origin).normalized;
                float coneAtt = ConeAttenuation(limbDir, p);
                if (coneAtt < 0.05f) continue;

                float limbScale = Mathf.Pow(
                    1f - Mathf.Clamp01(limbDist / p.OverpressureRadius),
                    p.OverpressureFalloffExp) * coneAtt;
                if (limbScale < 0.05f) continue;

                for (int i = 0; i < opPoints; i++)
                {
                    if (woundBudget <= 0) break;

                    float t = opPoints > 1 ? (float)i / (opPoints - 1) : 0.5f;
                    float fy = Mathf.Lerp(-1f, 1f, t);
                    float frXZ = Mathf.Sqrt(Mathf.Max(0f, 1f - fy * fy));
                    float fAngle = i * goldenAngleOP;
                    Vector3 fibDir = new Vector3(frXZ * Mathf.Cos(fAngle), fy,
                                                 frXZ * Mathf.Sin(fAngle));

                    Vector3 rayOrigin = limbCenter + fibDir * (p.OverpressureRadius * 0.6f);
                    Vector3 rayDir = (limbCenter - rayOrigin).normalized;

                    if (!Physics.Raycast(rayOrigin, rayDir, out RaycastHit opHit,
                                         p.OverpressureRadius * 1.2f,
                                         Config.FragLayerMask, QueryTriggerInteraction.Ignore)) continue;
                    if (opHit.collider == null) continue;

                    var hitReceiver = ResolveLimbCached(opHit.collider.gameObject);
                    if (hitReceiver == null || hitReceiver.Pointer != receiver.Pointer) continue;

                    Vector3 inward = (origin - opHit.point).normalized;
                    if (inward.sqrMagnitude < 0.001f) continue;

                    Vector3 nudged = opHit.point + inward * 0.15f;
                    ApplyWound(nudged, nudged - inward * 10f,
                        opHit.collider.gameObject, hitReceiver, p,
                        limbScale * p.DamageScale, true, noiseOffX, noiseOffY);
                    woundBudget--;
                    woundCount++;
                }
            }

            return (woundBudget, woundCount);
        }

        // ── Fragmentation rays ────────────────────────────────────────────────
        private static (int rays, int woundCount) ApplyFragmentation(
            Vector3 origin, float groundY, ExplosionParams p, float qScale, int woundBudget)
        {
            _fragImpulses.Clear();

            float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
            float gy = Physics.gravity.y;
            int woundCount = 0;

            int rays = Mathf.Max(1, Mathf.RoundToInt(p.FragRayCount * qScale));
            Quaternion coneRot;

            float yBottom = -1f, yTop = 1f;
            float hHalfRad = 0f, vHalfRad = 0f, tanHCone = 0f, tanVCone = 0f;

            if (p.IsFullSphere)
            {
                coneRot = Quaternion.identity;
            }
            else
            {
                coneRot = Quaternion.LookRotation(p.Forward.normalized);
                hHalfRad = Mathf.Min(p.HSpreadDeg * 0.5f, 180f) * Mathf.Deg2Rad;
                vHalfRad = Mathf.Min(p.VSpreadDeg * 0.5f, 180f) * Mathf.Deg2Rad;
                tanHCone = Mathf.Tan(hHalfRad);
                tanVCone = Mathf.Tan(vHalfRad);
            }

            int maxDebris = p.DebrisRaysRatio > 0f
                ? Mathf.Min(Mathf.RoundToInt(rays * p.DebrisRaysRatio),
                            Mathf.Max(1, Mathf.RoundToInt(Config.DebrisMaxPerExplosion * qScale)))
                : 0;
            int debrisInterval = maxDebris > 0 ? Mathf.Max(1, rays / maxDebris) : int.MaxValue;
            int debrisSpawned = 0;

            for (int r = 0; r < rays; r++)
            {
                Vector3 dir;

                if (p.IsFullSphere)
                {
                    float t = rays > 1 ? (float)r / (rays - 1) : 0.5f;
                    float y = Mathf.Lerp(yBottom, yTop, t);
                    float rXZ = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                    float angle = r * goldenAngle;
                    dir = new Vector3(rXZ * Mathf.Cos(angle), y, rXZ * Mathf.Sin(angle)).normalized;
                }
                else
                {
                    float t = rays > 1 ? (float)r / (rays - 1) : 0.5f;
                    float sqrtT = Mathf.Sqrt(t);
                    float azimuth = r * goldenAngle;

                    float halfAngle = EllipticalHalfAngle(azimuth, tanHCone, tanVCone);
                    float theta = sqrtT * halfAngle;
                    float sinTheta = Mathf.Sin(theta);

                    Vector3 localDir = new Vector3(
                        sinTheta * Mathf.Cos(azimuth),
                        sinTheta * Mathf.Sin(azimuth),
                        Mathf.Cos(theta)).normalized;
                    dir = coneRot * localDir;
                }

                Vector3 vel = dir * p.FragSpeed;
                Vector3 p0 = origin + dir * 0.04f;

                float flightTime = BallisticGroundTime(gy, vel.y, p0.y - groundY, p.FragMaxTime);

                Vector3 landing = p0 + vel * flightTime
                    + new Vector3(0f, 0.5f * gy * flightTime * flightTime, 0f);

                float arcLen = Vector3.Distance(p0, landing);
                int steps = Mathf.Clamp(Mathf.CeilToInt(arcLen / 0.75f), 2, Mathf.Max(2, p.ArcSteps));

                bool didHit = false;
                RaycastHit hit = default;
                Vector3 prev = p0;

                for (int s = 1; s <= steps; s++)
                {
                    float ft = flightTime * ((float)s / steps);
                    Vector3 pt = p0 + vel * ft + new Vector3(0f, 0.5f * gy * ft * ft, 0f);
                    Vector3 seg = pt - prev;
                    if (Physics.SphereCast(prev, 0.04f, seg.normalized, out hit, seg.magnitude,
                        Config.FragLayerMask, QueryTriggerInteraction.Ignore))
                    { didHit = true; break; }
                    prev = pt;
                }

                LimbEffectorReceiver receiver = null;
                if (didHit && hit.collider != null)
                    receiver = ResolveLimbCached(hit.collider.gameObject);

                if (Config.DebugDrawRays)
                {
                    Vector3 hitOrLanding = didHit ? hit.point : landing;
                    Color color = didHit ? (receiver != null ? Color.green : Color.red) : Color.grey;
                    DrawBallisticArc(p0, vel, gy, flightTime, hitOrLanding, color);
                }

                if (maxDebris > 0 && debrisSpawned < maxDebris && r % debrisInterval == 0)
                {
                    ExplosionVFX.SpawnDebrisArc(p0, vel, gy,
                        didHit ? hit.distance / p.FragSpeed : flightTime);
                    debrisSpawned++;
                }

                if (!didHit || hit.collider == null) continue;

                float hitDist = Vector3.Distance(origin, hit.point);
                if (hitDist < 0.02f) continue;

                var fragRb = hit.collider.GetComponentInParent<Rigidbody>();
                if (receiver == null && fragRb == null)
                    continue; 

                if (fragRb != null)
                {
                    if (!_fragImpulses.ContainsKey(fragRb)) _fragImpulses[fragRb] = Vector3.zero;
                    _fragImpulses[fragRb] += dir * p.FragImpulse;
                }

                if (receiver == null) continue;

                if (woundBudget > 0)
                {
                    ApplyWound(hit.point, origin, hit.collider.gameObject, receiver, p, p.DamageScale);
                    woundBudget--;
                    woundCount++;
                }
            }

            return (rays, woundCount);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static System.Collections.IEnumerator DelayedWound(
            Vector3 point, Vector3 origin, GameObject obj, float delay,
            ExplosionParams p, float damageScale = 1f, bool useNoise = false,
            int noiseOffX = 0, int noiseOffY = 0)
        {
            yield return new WaitForSeconds(delay);
            if (obj == null) yield break;
            var receiver = ResolveLimbUncached(obj);
            if (receiver == null) yield break;
            ApplyWound(point, origin, obj, receiver, p, damageScale, useNoise, noiseOffX, noiseOffY);
        }

        private static int ConeRadius(int step) => Config.WConeRadius(step);
        private static float ConeSignal(int step) => Config.WConeSignal(step);

        private static void ApplyWound(Vector3 worldHitPos, Vector3 origin,
                                       GameObject hitObject, LimbEffectorReceiver receiver,
                                       ExplosionParams p,
                                       float damageScale = 1f,
                                       bool useNoise = false,
                                       int noiseOffX = 0, int noiseOffY = 0)
        {
            try
            {
                if (hitObject == null || receiver == null) return;

                var rb = hitObject.GetComponentInParent<Rigidbody>();
                if (rb == null) return;

                var birComp = rb.GetComponent(Il2CppType.Of<bir>());
                if (birComp == null) return;

                var biwReceiver = birComp.TryCast<biw>();
                if (biwReceiver == null) return;

                var voxelMesh = receiver.wtb;
                if (voxelMesh == null) return;

                var fragDir = (worldHitPos - origin).normalized;

                float noiseThreshold = useNoise
                    ? Mathf.Lerp(0.62f, 0.20f, Mathf.Clamp01(damageScale))
                    : -1f;

                int totalVoxels = 0;
                var allVoxelSets = new List<(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnityEngine.Vector3Int> voxels, float signal)>();

                for (int step = 0; step < 3; step++)
                {
                    Vector3 samplePos = worldHitPos + fragDir * (Config.WChunkStep * step);
                    var chunk = ct.diz(voxelMesh, samplePos, fragDir, 0);
                    if (chunk == null) break;

                    var voxels = new dh(chunk.pla, ConeRadius(step)).dki();
                    if (voxels == null || voxels.Length == 0) continue;

                    float signal = -10000f * ConeSignal(step) * damageScale;
                    allVoxelSets.Add((voxels, signal));
                    totalVoxels += voxels.Length;
                }

                if (totalVoxels == 0) return;

                var builder = new bjd(totalVoxels, false);
                foreach (var (voxels, signal) in allVoxelSets)
                    foreach (var v in voxels)
                    {
                        if (useNoise && NoiseAt(v.x, v.z, noiseOffX, noiseOffY) < noiseThreshold)
                            continue;
                        builder.jbq(new IndexEffectorSignal(fp.ecz(v), signal, (InfluenceProcessType)1));
                    }

                biwReceiver.cyn(new bjb<bit>(builder));
                builder.Dispose();

                if (damageScale >= 1f)
                {
                    Vector3 exitProbe = worldHitPos + fragDir * (Config.WChunkStep * 3);
                    var exitChunk = ct.diz(voxelMesh, exitProbe, fragDir, 0);
                    if (exitChunk == null)
                    {
                        float penetratedScale = damageScale * Config.WPenetrationScale;
                        Vector3 continueFrom = exitProbe + fragDir * 0.05f;
                        if (Physics.Raycast(continueFrom, fragDir, out RaycastHit nextHit, 5f,
                            Config.FragLayerMask, QueryTriggerInteraction.Ignore))
                            if (nextHit.collider != null && IsLimb(nextHit.collider.gameObject))
                                MelonCoroutines.Start(DelayedWound(
                                    nextHit.point, worldHitPos,
                                    nextHit.collider.gameObject,
                                    p.FragWoundDelay, p, penetratedScale));
                    }
                }
            }
            catch (Exception e) { MelonLogger.Warning($"[WOUND] {e.Message}"); }
        }

        private static void DrawBallisticArc(
            Vector3 p0, Vector3 vel, float gy,
            float totalTime, Vector3 endPoint, Color color)
        {
            int steps = Mathf.Max(2, Config.ArcDebugSteps);
            var go = new GameObject("FragArc");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = steps + 1;
            for (int i = 0; i < steps; i++)
            {
                float ft = totalTime * ((float)i / steps);
                lr.SetPosition(i, p0 + vel * ft + new Vector3(0f, 0.5f * gy * ft * ft, 0f));
            }
            lr.SetPosition(steps, endPoint);
            lr.startWidth = 0.012f;
            lr.endWidth = 0.004f;
            lr.material = new Material(Config.FindSpriteShader());
            lr.startColor = color;
            lr.endColor = new Color(color.r, color.g, color.b, 0f);
            GameObject.Destroy(go, Config.DebugRayDuration);
        }
    }
}
