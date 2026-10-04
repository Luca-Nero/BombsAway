using System.Collections.Generic;
using HarmonyLib;
using Il2CppLVA.Puppeteers.Variants.Humanoid;
using MelonLoader;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The arms while a body covers its face: the elbow and the hand's orientation.
    ///
    /// HandEffectorPlacer.SetLocalPosition only flies the hand's IK effector to the target. Its
    /// initialBendPosition / initialHandEffectorRotation arguments are start values that the game
    /// blends back to the arm's defaults over the flight (Ghidra: SetLocalPosition sets the blend
    /// start, OnPreSolve sums progress toward the saved rest). The default elbow hangs low and
    /// inside, so the forearm swept across the chest and face on the way up and stayed through the
    /// head at the ear; the hand arrived in whatever orientation it had.
    ///
    /// So both are held here, right after the game's own per-solve update (a postfix on
    /// HandEffectorPlacer.OnPreSolve, which runs before each IK solve):
    ///
    /// Elbow: the arm's IK bend goal is pulled to a point in head space - out to the side and up
    /// for the ears (the flared elbows of someone clamping their ears), in front and below for
    /// the eyes - eased in over ElbowIn and back to where it was over Release.
    ///
    /// Hand: the effector's rotation turns the hand to lie along the head (the ear) or up the face
    /// (the eyes) instead of pointing into it. The hand's long axis is the forearm's direction in
    /// hand space; its flat side the thinnest axis of a BoxCollider on the hand bone, if there is
    /// one (otherwise only the long axis is turned, by the smallest rotation).
    /// </summary>
    internal static partial class Flashbang
    {
        private const float ElbowIn = 0.35f, TurnIn = 0.6f, Release = 0.45f;

        private sealed class ArmHold
        {
            public Stunned Body;
            public float Side;              // which side of the head (head-local X sign)
            public Transform BendGoal, Effector, Hand;
            public Vector3 BendRest;        // the bend goal's local position before we moved it
            public Vector3 Along, Flat;     // hand-local: long axis, and the flat side's normal (zero if unknown)
            public float Since, ReleasedAt = -1f;
        }

        private static readonly Dictionary<System.IntPtr, ArmHold> _arms = new Dictionary<System.IntPtr, ArmHold>();

        /// <summary>Starts holding both arms of a body that has just started covering its face.</summary>
        private static void HoldArms(Stunned s, IKReferences ik, float leftSide)
        {
            var refs = ik.FullBodyBiped != null ? ik.FullBodyBiped.references : null;
            Arm(s, ik.LeftHandEffectorPlacer, ik.LeftArmBendGoal, ik.LeftHandEffector,
                refs?.leftHand, refs?.leftForearm, leftSide, "left");
            Arm(s, ik.RightHandEffectorPlacer, ik.RightArmBendGoal, ik.RightHandEffector,
                refs?.rightHand, refs?.rightForearm, -leftSide, "right");
        }

        private static void Arm(Stunned s, HandEffectorPlacer placer, Transform bendGoal, Transform effector,
                                Transform hand, Transform forearm, float side, string name)
        {
            if (placer == null || bendGoal == null || effector == null) return;
            var key = placer.Pointer;
            if (_arms.TryGetValue(key, out var held) && held.ReleasedAt >= 0f)
            {
                // Covered again while still letting go: carry on from here, keep the true rest.
                held.Body = s; held.Side = side; held.ReleasedAt = -1f; held.Since = Time.time;
                return;
            }
            held = new ArmHold
            {
                Body = s, Side = side, BendGoal = bendGoal, Effector = effector, Hand = hand,
                BendRest = bendGoal.localPosition, Since = Time.time,
            };
            string how = "no hand bone: elbow only";
            if (hand != null && forearm != null)
            {
                held.Along = MainAxis(hand.InverseTransformDirection(hand.position - forearm.position));
                var box = hand.GetComponent<BoxCollider>();
                if (box != null)
                {
                    held.Flat = ThinAxis(box.size, held.Along);
                    how = $"along {held.Along}, flat {held.Flat} (box {box.size})";
                }
                else how = $"along {held.Along}, no box collider: long axis only";
            }
            _arms[key] = held;
            if (Config.Dbg1) MelonLogger.Msg($"[Flash] {name} arm held: {how}");
        }

        /// <summary>Lets go of a body's arms: the elbows ease back to where they were.</summary>
        private static void ReleaseArms(Stunned s)
        {
            float now = Time.time;
            foreach (var a in _arms.Values)
                if (a.Body == s && a.ReleasedAt < 0f) a.ReleasedAt = now;
        }

        /// <summary>Scene change or reset: every elbow straight back.</summary>
        private static void DropArms()
        {
            foreach (var a in _arms.Values)
                try { if (a.BendGoal != null) a.BendGoal.localPosition = a.BendRest; } catch { }
            _arms.Clear();
        }

        /// <summary>After the game's own update of one arm's IK targets: the elbow and the hand, held.</summary>
        internal static void ShapeArm(HandEffectorPlacer placer)
        {
            if (!_arms.TryGetValue(placer.Pointer, out var a)) return;
            float now = Time.time;
            if (a.BendGoal == null || a.Effector == null || a.Body.Frame == null)
            {
                Forget(placer.Pointer, a);
                return;
            }

            float w;
            if (a.ReleasedAt >= 0f)
            {
                w = 1f - Mathf.Clamp01((now - a.ReleasedAt) / Release);
                if (w <= 0f) { Forget(placer.Pointer, a); return; }
            }
            else w = 1f;
            float elbowW = w * Mathf.SmoothStep(0f, 1f, (now - a.Since) / ElbowIn);
            float turnW = w * Mathf.SmoothStep(0f, 1f, (now - a.Since) / TurnIn);

            // Elbow: from where it rests (or where the game's blend has it) toward the pose.
            var parent = a.BendGoal.parent;
            Vector3 rest = parent != null ? parent.TransformPoint(a.BendRest) : a.BendRest;
            Vector3 from = a.ReleasedAt >= 0f ? rest : a.BendGoal.position;
            a.BendGoal.position = Vector3.Lerp(from, FramePoint(a.Body, Elbow(a.Body, a.Body.Eyes, a.Side)), elbowW);

            // Hand: only while covering; letting go, the game fades the effector's weight anyway.
            if (a.ReleasedAt < 0f && a.Hand != null && a.Along != Vector3.zero && turnW > 0f)
                a.Effector.rotation = Quaternion.Slerp(a.Effector.rotation, HandRotation(a), turnW);
        }

        private static readonly List<System.IntPtr> _armsGone = new List<System.IntPtr>();

        /// <summary>
        /// Once a frame: entries whose arm can no longer reach ShapeArm. Only the postfix forgets an
        /// arm, so a body deleted mid-cover (its placer gone, the hook never called again) would keep
        /// its entry until the scene changes, and a new placer at the same address would inherit it.
        /// </summary>
        private static void PruneArms()
        {
            float now = Time.time;
            _armsGone.Clear();
            foreach (var kv in _arms)
            {
                var a = kv.Value;
                if (a.BendGoal == null || a.Effector == null || a.Body.Puppeteer == null
                    || (a.ReleasedAt >= 0f && now - a.ReleasedAt > Release + 1f))
                    _armsGone.Add(kv.Key);
            }
            foreach (var key in _armsGone) Forget(key, _arms[key]);
        }

        private static void Forget(System.IntPtr key, ArmHold a)
        {
            try { if (a.BendGoal != null) a.BendGoal.localPosition = a.BendRest; } catch { }
            _arms.Remove(key);
        }

        /// <summary>
        /// Where the elbow points, in head space from the head's middle (+X the hand's side, +Y the
        /// crown, +Z the face). Ears: out to the side, about shoulder height, a little forward.
        /// Eyes: in front of the chest, a little out.
        /// </summary>
        private static Vector3 Elbow(Stunned s, bool eyes, float side)
            => s.HeadCenter + (eyes ? new Vector3(0.22f * side, -0.40f, 0.24f) : new Vector3(0.36f * side, -0.22f, 0.06f));

        /// <summary>
        /// The hand's world rotation on the face. Ears: lying along the side of the head, pointing
        /// up and back. Eyes: flat on the face, pointing up and in toward the other eye.
        /// </summary>
        private static Quaternion HandRotation(ArmHold a)
        {
            var frame = FrameRotation(a.Body);
            Vector3 up = frame * Vector3.up, fwd = frame * Vector3.forward, right = frame * Vector3.right * a.Side;
            Vector3 along = a.Body.Eyes ? (up * 0.9f - right * 0.4f) : (up * 0.85f - fwd * 0.4f);
            Vector3 flat = a.Body.Eyes ? fwd : right;
            along.Normalize();
            var current = a.Hand.rotation;

            if (a.Flat == Vector3.zero)
                return Quaternion.FromToRotation(current * a.Along, along) * current;

            // Two ways to lay a flat hand on the head; take the one nearer to how it is now, so
            // the wrist doesn't flip over.
            flat = Vector3.ProjectOnPlane(flat, along).normalized;
            var basis = Quaternion.Inverse(Quaternion.LookRotation(a.Along, a.Flat));
            var r1 = Quaternion.LookRotation(along, flat) * basis;
            var r2 = Quaternion.LookRotation(along, -flat) * basis;
            return Quaternion.Angle(current, r1) <= Quaternion.Angle(current, r2) ? r1 : r2;
        }

        /// <summary>The principal axis (±X, ±Y, ±Z) nearest to <paramref name="v"/>.</summary>
        private static Vector3 MainAxis(Vector3 v)
        {
            float x = Mathf.Abs(v.x), y = Mathf.Abs(v.y), z = Mathf.Abs(v.z);
            if (x >= y && x >= z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (y >= z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        /// <summary>The thinnest of the box's axes other than <paramref name="along"/>; zero if it is no thinner than the other.</summary>
        private static Vector3 ThinAxis(Vector3 size, Vector3 along)
        {
            Vector3 best = Vector3.zero, other = Vector3.zero;
            float bestSize = float.MaxValue, otherSize = 0f;
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var sizes = new[] { Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z) };
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(Vector3.Dot(axes[i], along)) > 0.5f) continue;
                if (sizes[i] < bestSize) { other = best; otherSize = bestSize; best = axes[i]; bestSize = sizes[i]; }
                else { other = axes[i]; otherSize = sizes[i]; }
            }
            return otherSize > bestSize * 1.15f ? best : Vector3.zero;
        }

        /// <summary>Runs after the game's own per-solve update of each hand's IK targets.</summary>
        [HarmonyPatch(typeof(HandEffectorPlacer), nameof(HandEffectorPlacer.OnPreSolve))]
        private static class ArmHook
        {
            private static void Postfix(HandEffectorPlacer __instance)
            {
                if (_arms.Count == 0 || __instance == null) return;
                try { ShapeArm(__instance); }
                catch (System.Exception e)
                {
                    _arms.Remove(__instance.Pointer);
                    if (Config.Dbg1) MelonLogger.Warning($"[Flash] holding an arm failed: {e.Message}");
                }
            }
        }
    }
}
