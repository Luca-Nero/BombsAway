using MelonLoader;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// A whole ragdoll as one target: everything under the limb's root. Aims at its middle, or
    /// for a top attack its highest point, and carries the limb nearest that middle for whatever
    /// still wants a Rigidbody.
    /// </summary>
    internal sealed class TargetBody
    {
        public Transform Root;
        public Rigidbody Core;
        private Renderer[] _renderers;

        /// <summary>The body a limb belongs to, or null if its root is not one body (a map container).</summary>
        public static TargetBody Of(Rigidbody limb)
        {
            if (limb == null) return null;
            var root = limb.transform.root;
            var raw = root.GetComponentsInChildren<Renderer>();
            var body = new TargetBody { Root = root, _renderers = new Renderer[raw.Length] };
            for (int i = 0; i < raw.Length; i++) body._renderers[i] = raw[i];
            if (!body.TryBounds(out Bounds b) || b.size.y > 6f) return null;

            // The limb whose centre of mass is nearest the body's middle: the torso, usually.
            body.Core = limb;
            float best = float.MaxValue;
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>())
            {
                if (rb == null || !ExplosionSystem.IsLimb(rb.gameObject)) continue;
                float d = (rb.worldCenterOfMass - b.center).sqrMagnitude;
                if (d < best) { best = d; body.Core = rb; }
            }
            return body;
        }

        public bool Alive => Root != null;

        public bool TryBounds(out Bounds b)
        {
            b = default;
            if (Root == null || _renderers == null) return false;
            bool any = false;
            foreach (var r in _renderers)
            {
                if (r == null || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        public Vector3 Centre => TryBounds(out Bounds b) ? b.center : (Core != null ? Core.worldCenterOfMass : Vector3.zero);
        public float Top => TryBounds(out Bounds b) ? b.max.y : Centre.y;
    }

    public partial class Core
    {
        // ══════════════════════════════════════════════════════════════════════════
        // The Javelin's seeker. While seeking (right mouse, the launcher up) the target
        // under the reticle is tracked for LockTime seconds, then it locks; the left mouse
        // fires only at a lock. A target counts as the same one for as long as it stays in
        // the lock cone, so a ragdoll's limbs swapping places in the scan don't restart it.
        // With LockWholeBody the lock is the whole ragdoll (its middle, or its top for a
        // top attack); otherwise the limb. A lock is lost when the launcher comes down from
        // the eye, and with LockBreaks also when its target strays past LockBreakAngle from
        // where you look, or out of range. Once a missile is fired at it, the lock is pinned:
        // it holds, sight or no sight, until that missile is down (and then goes, unless
        // PersistentLock keeps it for another shot).
        //
        // Smoke: the DAY and NIGHT views can't see through it, the thermal ones can. In a day or
        // night view a target behind smoke can't be tracked, and a lock not yet fired on is lost
        // once smoke has hidden it for ObscuredGrace.
        // ══════════════════════════════════════════════════════════════════════════

        private static float _lockProgress;          // 0..1 toward a lock on the candidate
        private static HomingMissileState _lockMissile;   // the missile fired at the lock, while it flies
        private static float _seekTick;                   // until the seeker's next tone
        private static TargetBody _candidateBody;    // whole-body mode: the candidate's body
        private static TargetBody _lockedBody;       // whole-body mode: the locked body
        private static float _lockHiddenFor;              // seconds smoke has hidden the lock

        private const float ObscuredGrace = 0.4f;

        /// <summary>The CLU sees heat (WHOT / BHOT), so smoke doesn't hide anything from it.</summary>
        private static bool CluThermal => _cluView == CluView.WHot || _cluView == CluView.BHot;

        /// <summary>Smoke hides <paramref name="point"/> from <paramref name="eye"/> (for a day or night sight).</summary>
        internal static bool SmokeHides(Vector3 eye, Vector3 point)
            => SmokeCloud.Count > 0 && SmokeCloud.Transmittance(eye, point) < Config.SmokeLockClear;

        /// <summary>Smoke hides <paramref name="point"/> from the CLU in its current view.</summary>
        private static bool CluObscured(Vector3 point)
        {
            if (CluThermal || SmokeCloud.Count == 0) return false;
            var cam = CameraCache.Main;
            return cam != null && SmokeHides(cam.transform.position, point);
        }

        private static void TickSeeker(float dt, bool seeking)
        {
            if (_lockedTarget != null)
            {
                bool gone = _lockedTarget == null || (_lockedBody != null && !_lockedBody.Alive);
                if (gone || (!LockPinned && Config.LockBreaks && !InView(LockedPoint(), Config.LockBreakAngle, 1.15f)))
                    BreakLock(gone ? "target gone" : "strayed");
            }
            // Smoke between the CLU and a lock that has no missile on it yet: lost after a moment.
            if (_lockedTarget != null && !LockPinned && seeking && CluObscured(LockedPoint()))
            {
                _lockHiddenFor += dt;
                if (_lockHiddenFor >= ObscuredGrace) BreakLock("hidden by smoke");
            }
            else _lockHiddenFor = 0f;

            if (!seeking)
            {
                // Lowering the launcher drops the lock: it is only held while you look through the
                // CLU, or while the missile fired at it is still on its way.
                if (_lockedTarget != null && !LockPinned) BreakLock("left the sight");
                ClearCandidate();
                return;
            }

            // Keep the current candidate while it stays in the cone; otherwise take the scan's best.
            Rigidbody cand = _focusedTarget != null && InView(CandidatePoint(), Config.MissileLockAngle, 1f)
                ? _focusedTarget : ScanForTarget();
            // Whichever it is, smoke in front of it (day or night view) means nothing to track:
            // checked every frame, as the scan's result is reused between scans.
            if (cand != null && CluObscured(cand == _focusedTarget ? CandidatePoint() : cand.worldCenterOfMass)) cand = null;

            if (cand != null && _lockedTarget != null && SameTarget(cand, _lockedTarget))
            {
                ClearCandidate();   // already locked on this one
                return;
            }
            if (cand == null || !SameTarget(cand, _focusedTarget))
            {
                _focusedTarget = cand;
                _candidateBody = cand != null && Config.LockWholeBody ? TargetBody.Of(cand) : null;
                _lockProgress = 0f;
            }
            if (_focusedTarget == null) return;

            _lockProgress += dt / Mathf.Max(0.1f, Config.LockTime);
            // The seeker's tone: ticks that come faster as it closes on a lock.
            _seekTick -= dt;
            if (_seekTick <= 0f) { Sfx.PlayHeld("CluSeek"); _seekTick = Mathf.Lerp(0.45f, 0.08f, _lockProgress); }
            if (_lockProgress < 1f) return;

            _lockedBody = _candidateBody;
            _lockedTarget = _candidateBody != null && _candidateBody.Core != null ? _candidateBody.Core : _focusedTarget;
            Sfx.PlayHeld("CluLock");
            _lockMissile = null;   // a new lock: the missile in the air keeps the old target
            if (Config.Dbg1) MelonLogger.Msg($"[Seeker] Locked: '{(_lockedBody != null ? _lockedBody.Root.name : _lockedTarget.gameObject.name)}'");
            ClearCandidate();
        }

        /// <summary>A missile fired at the current lock is still flying.</summary>
        private static bool LockPinned => _lockMissile != null && !_lockMissile.Dead;

        /// <summary>Every frame, whatever is in hand: the pinned missile came down, so the lock goes with it.</summary>
        private static void TickLockPin()
        {
            if (_lockMissile == null || !_lockMissile.Dead) return;
            _lockMissile = null;
            if (_lockedTarget != null && !PersistentLock) BreakLock("missile down");
            // Persistent: the lock stays, now under the usual rules (it drops if not sighted).
        }

        private static void ClearCandidate()
        {
            _focusedTarget = null;
            _candidateBody = null;
            _lockProgress = 0f;
        }

        private static void BreakLock(string why)
        {
            if (Config.Dbg1) MelonLogger.Msg($"[Seeker] Lock broken: {why}");
            _lockedTarget = null;
            _lockedBody = null;
            _lockMissile = null;
            _lockHiddenFor = 0f;
            HideLockIndicator();
        }

        /// <summary>Same target: the same limb, or in whole-body mode any limb of the same ragdoll.</summary>
        private static bool SameTarget(Rigidbody a, Rigidbody b)
        {
            if (a == null || b == null) return a == b;
            if (a == b) return true;
            return Config.LockWholeBody && a.transform.root == b.transform.root;
        }

        private static Vector3 CandidatePoint() =>
            _candidateBody != null ? _candidateBody.Centre : _focusedTarget != null ? _focusedTarget.worldCenterOfMass : Vector3.zero;

        private static Vector3 LockedPoint() =>
            _lockedBody != null ? _lockedBody.Centre : _lockedTarget != null ? _lockedTarget.worldCenterOfMass : Vector3.zero;

        /// <summary>Within <paramref name="angle"/> of the view and the lock range (times <paramref name="rangeSlack"/>).</summary>
        private static bool InView(Vector3 point, float angle, float rangeSlack)
        {
            var cam = CameraCache.Main;
            if (cam == null) return false;
            Vector3 to = point - cam.transform.position;
            if (to.magnitude > Config.MissileLockRange * rangeSlack) return false;
            return Vector3.Angle(cam.transform.forward, to) <= angle;
        }

        /// <summary>What the CLU's track gates frame: the lock, else the candidate; a body's root or a limb.</summary>
        private static Transform GateTarget()
        {
            if (_lockedTarget != null) return _lockedBody != null ? _lockedBody.Root : _lockedTarget.transform;
            if (_focusedTarget != null) return _candidateBody != null ? _candidateBody.Root : _focusedTarget.transform;
            return null;
        }
    }
}
