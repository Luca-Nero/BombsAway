using System.Collections.Generic;
using Il2CppData.CustomTypes.LimitedValue.Dependecies;
using Il2CppEffectors;
using Il2CppLVA.Limbs.Variants.Human;
using Il2CppLVA.Puppeteers.Variants.Humanoid;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The flashbang. It works on two senses, for the player and for every body alike:
    ///
    /// Sight: 1 within FlashFullRange, then inverse square; times the eye's cone (full inside
    /// FlashFocusAngle of where the eyes point, fading through FlashPeripheralAngle, nothing
    /// behind); times FlashOcclusion if the bang can't see the eyes. That is the direct light.
    /// Light bounced off walls (a bang in a room blinds whichever way you face) comes in as a
    /// second term, <see cref="Reflected"/>, which ignores the cone - not modelled yet.
    ///
    /// Hearing: the same falloff, cover counting for less (FlashHearingOcclusion), and which way
    /// the head points doesn't matter.
    ///
    /// The player: sight whites the screen out, hearing rings the ears (a pure tone, its pitch
    /// picked per bang, fading out over FlashRingTime x hearing).
    ///
    /// Bodies: the stun is sight plus FlashHearingStun of the hearing on top. A humanoid's
    /// puppeteer drives two creature values through BaseChannelWriters, GeneralMuscleForce and
    /// Balance (HumanoidReferences.Parameters), and rewrites them every tick, so a stunned body
    /// is held: every physics tick and frame both are pushed down (never raised) to their
    /// targets for FlashStunTime x stun, then eased back over FlashRecoverTime. Balance drops to
    /// FlashBalance percent, so the body goes down; muscle only to FlashMuscle percent, so the
    /// arms can still move: a blinded body puts its hands over its eyes, a deafened one over its
    /// ears. The hands are placed the way the game's own wound covering places them
    /// (ArmPainPointCovering): HandEffectorPlacer.SetLocalPosition with a point in the head's
    /// local space, where +Z is the face and +Y the crown.
    /// </summary>
    internal static partial class Flashbang
    {
        private sealed class Stunned
        {
            public HumanoidPuppeteer Puppeteer;
            public BaseChannelWriter Muscle, Balance;
            public Transform Head;
            public float Stun;
            public float HoldUntil, RecoverUntil;
            public bool Eyes;          // hands over the eyes (blinded) or the ears (only deafened)
            public bool Covering;
            public float NextCoverCheck;
            // The head's shape in its own space (from its collider): the hand targets and elbows hang off it.
            public Vector3 HeadCenter, HeadExtent;
            public string HeadShape;
            public int PlacedLeft, PlacedRight;   // flights started (diagnostics)
            public float LeftSide = -1f;          // the head-local X side the left hand is on
            public Rigidbody[] Hands;             // the physical hands (diagnostics), found on first use
            // What the hands are placed against: the IK rig's head (see PickFrame), with the turn
            // that maps the physical head's axes (which all the offsets are written in) onto it.
            public Transform Frame;
            public Quaternion FrameQ = Quaternion.identity;
            public string FrameNote = "";
        }

        private static readonly List<Stunned> _stunned = new List<Stunned>();

        /// <summary>Bodies held right now (the HUD's debug readout).</summary>
        public static int Count => _stunned.Count;

        public static void Bang(Vector3 origin)
        {
            PlayerEffect(origin);

            HumanoidPuppeteer[] all;
            try { all = Object.FindObjectsOfType<HumanoidPuppeteer>(); }
            catch (System.Exception e) { MelonLogger.Warning($"[Flash] finding bodies failed: {e.Message}"); return; }

            int hit = 0;
            foreach (var pp in all)
            {
                if (pp == null) continue;
                try
                {
                    var pars = pp.HumanoidReferences?.Parameters;
                    if (pars == null) continue;
                    var muscle = pars.GeneralMuscleForceChannel;
                    var balance = pars.BalanceChannel;
                    if (muscle == null && balance == null) continue;

                    var root = pp.transform.root;
                    if (!Measure(root, origin, out float distance, out bool anyVisible, out Rigidbody head)) continue;

                    float hearing = Falloff(distance) * (anyVisible ? 1f : Config.FlashHearingOcclusion);
                    float sight = 0f;
                    if (head != null)
                    {
                        Vector3 eye = head.worldCenterOfMass;
                        bool eyesVisible = InSight(origin, head, root);
                        sight = Sight(origin, eye, head.transform.forward, eyesVisible, closeGate: true);
                        if (Config.DebugDrawExplosions)
                        {
                            ExplosionDebugDraw.Segment(eye, eye + head.transform.forward * 0.6f, new Color(0.25f, 0.9f, 0.85f), 0.012f);
                            ExplosionDebugDraw.Segment(origin, eye, Color.Lerp(new Color(0.3f, 0.3f, 0.35f), Color.white, sight), 0.008f);
                        }
                    }
                    float stun = Mathf.Clamp01(sight + (1f - sight) * hearing * Config.FlashHearingStun);
                    if (stun < Config.FlashMinStrength) continue;

                    float now = Time.time;
                    var s = Find(pp);
                    if (s == null) { s = new Stunned { Puppeteer = pp, Muscle = muscle, Balance = balance }; _stunned.Add(s); }
                    var headT = head != null ? head.transform : null;
                    if (headT != null && s.Head != headT) MeasureHead(s, headT);
                    s.Head = headT;
                    s.Stun = Mathf.Max(s.Stun, stun);
                    s.HoldUntil = Mathf.Max(s.HoldUntil, now + Mathf.Max(0.3f, Config.FlashStunTime * stun));
                    s.RecoverUntil = s.HoldUntil + Mathf.Max(0.05f, Config.FlashRecoverTime);
                    bool eyes = sight >= Config.FlashCoverAt;
                    if (eyes || hearing >= Config.FlashCoverAt)
                    {
                        if (eyes) s.Eyes = true;   // once blinded, a second bang heard from behind doesn't move the hands to the ears
                        s.NextCoverCheck = 0f;
                    }
                    hit++;
                    if (Config.Dbg1)
                        MelonLogger.Msg($"[Flash] body at {distance:F1} m: sight {sight:F2}, hearing {hearing:F2}, stun {stun:F2}, held {s.HoldUntil - now:F1}s, hands {(eyes ? "eyes" : hearing >= Config.FlashCoverAt ? "ears" : "-")}");
                }
                catch (System.Exception e) { MelonLogger.Warning($"[Flash] stunning a body failed: {e.Message}"); }
            }
            if (Config.Dbg1) MelonLogger.Msg($"[Flash] {hit} of {all.Length} bodies stunned");
        }

        // ── Exposure ────────────────────────────────────────────────────────────

        /// <summary>1 within FlashFullRange, then the inverse square.</summary>
        private static float Falloff(float distance)
        {
            float full = Mathf.Max(0.1f, Config.FlashFullRange);
            return distance <= full ? 1f : (full / distance) * (full / distance);
        }

        /// <summary>
        /// How blinding the bang is to an eye at <paramref name="eye"/> looking along <paramref name="facing"/>.
        /// With <paramref name="closeGate"/> (bodies), a bang within FlashCloseRange that can see the
        /// head blinds it whichever way it faces: a body standing over the grenade looks past it
        /// (the cone points over it), yet the flash fills its view. Eased out over the metre beyond.
        /// </summary>
        private static float Sight(Vector3 origin, Vector3 eye, Vector3 facing, bool inSight, bool closeGate = false)
        {
            Vector3 to = origin - eye;
            float d = to.magnitude;
            float cone = Cone(Vector3.Angle(facing, d > 0.01f ? to / d : facing));
            if (closeGate && inSight)
            {
                float close = Mathf.Max(0f, Config.FlashCloseRange);
                cone = Mathf.Lerp(1f, cone, Mathf.SmoothStep(0f, 1f, d - close));
            }
            float direct = Falloff(d) * cone * (inSight ? 1f : Config.FlashOcclusion);
            return Mathf.Clamp01(direct + Reflected(origin, eye));
        }

        /// <summary>
        /// The eye's field: 1 inside the focus cone, easing down to FlashPeripheralStrength at the
        /// edge of the peripheral one, then out over its last 10 degrees.
        /// </summary>
        private static float Cone(float angle)
        {
            float focus = Mathf.Clamp(Config.FlashFocusAngle, 1f, 179f);
            float peripheral = Mathf.Clamp(Config.FlashPeripheralAngle, focus + 1f, 180f);
            if (angle <= focus) return 1f;
            if (angle >= peripheral) return 0f;
            float t = Mathf.SmoothStep(0f, 1f, (angle - focus) / (peripheral - focus));
            return Mathf.Lerp(1f, Config.FlashPeripheralStrength, t) * Mathf.Clamp01((peripheral - angle) / 10f);
        }

        /// <summary>
        /// Light reaching the eye off walls, floor and ceiling: it arrives from every side, so it
        /// is added to the direct term without the cone, and is what makes a bang in a room blind
        /// you facing away. Not modelled yet (0). The plan: rays from the bang measure how
        /// enclosed it is (the share that hits within a few metres, and how near); that share x
        /// a wall albedo x the falloff to the eye, only if the eye is in the same space (it sees
        /// some of the lit surfaces). Shared by the player and bodies, so both pick it up at once.
        /// </summary>
        private static float Reflected(Vector3 origin, Vector3 eye) => 0f;

        /// <summary>
        /// Distance from the bang to the body's nearest limb, whether any limb is in its line of
        /// sight (for hearing), and the head (for the eyes and the hands).
        /// </summary>
        private static bool Measure(Transform root, Vector3 origin, out float distance, out bool visible, out Rigidbody head)
        {
            distance = float.MaxValue; visible = false; head = null;
            Rigidbody nearest = null, top = null;
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>())
            {
                if (rb == null || !ExplosionSystem.IsLimb(rb.gameObject)) continue;
                float d = Vector3.Distance(origin, rb.worldCenterOfMass);
                if (d < distance) { distance = d; nearest = rb; }
                if (top == null || rb.worldCenterOfMass.y > top.worldCenterOfMass.y) top = rb;
                if (head == null && IsHead(rb)) head = rb;
            }
            if (nearest == null) return false;
            visible = InSight(origin, nearest, root) || (top != nearest && InSight(origin, top, root));
            return true;
        }

        private static bool IsHead(Rigidbody rb)
        {
            try
            {
                var receiver = rb.GetComponent<LimbEffectorReceiver>();
                var limb = receiver != null ? receiver.m_limbReferences?.Limb : null;
                return limb != null && limb.TryCast<Head>() != null;
            }
            catch { return false; }
        }

        /// <summary>The first thing on the line from the bang to the limb belongs to the body.</summary>
        private static bool InSight(Vector3 origin, Rigidbody limb, Transform root)
        {
            // From just above the can: one lying on the floor starts its ray at the floor's surface.
            origin += Vector3.up * 0.1f;
            Vector3 to = limb.worldCenterOfMass - origin;
            float d = to.magnitude;
            if (d < 0.05f) return true;
            // The allocating overload: NonAlloc returns nothing in this build.
            var hits = Physics.RaycastAll(origin, to / d, d, Config.FragLayerMask | Config.WorldLayerMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Collider first = null;
            foreach (var h in hits)
                if (h.collider != null && h.distance > 0.05f && h.distance < best) { best = h.distance; first = h.collider; }
            return first == null || first.transform.IsChildOf(root);
        }

        private static Stunned Find(HumanoidPuppeteer pp)
        {
            foreach (var s in _stunned) if (s.Puppeteer != null && s.Puppeteer.Pointer == pp.Pointer) return s;
            return null;
        }

        // ── Holding bodies ──────────────────────────────────────────────────────

        private static int _lastFrame = -1;

        /// <summary>Holds every stunned body down; called each frame, late frame and physics tick.</summary>
        public static void Tick()
        {
            bool newFrame = Time.frameCount != _lastFrame;
            _lastFrame = Time.frameCount;
            if (newFrame) TickRing();
            if (_stunned.Count == 0) return;

            float now = Time.time;
            float balanceFloor = Mathf.Clamp01(Config.FlashBalance / 100f);
            float muscleFloor = Mathf.Clamp01(Config.FlashMuscle / 100f);
            for (int i = _stunned.Count - 1; i >= 0; i--)
            {
                var s = _stunned[i];
                if (s.Puppeteer == null || now >= s.RecoverUntil) { Uncover(s); _stunned.RemoveAt(i); continue; }

                // Back up the way a body gathers itself: slowly at first, then all at once.
                float back = now < s.HoldUntil ? 0f : (now - s.HoldUntil) / Mathf.Max(0.05f, s.RecoverUntil - s.HoldUntil);
                back *= back;
                float balance = Mathf.Lerp(Mathf.Lerp(1f, balanceFloor, s.Stun), 1f, back);
                float muscle = Mathf.Lerp(Mathf.Lerp(1f, muscleFloor, s.Stun), 1f, back);
                try { Hold(s.Muscle, muscle); Hold(s.Balance, balance); }
                catch { Uncover(s); _stunned.RemoveAt(i); continue; }

                if (!newFrame) continue;
                if (now < s.HoldUntil) Cover(s, now);
                else Uncover(s);
            }
        }

        /// <summary>Pushes the channel down to <paramref name="fraction"/> of its maximum, never up.</summary>
        private static void Hold(BaseChannelWriter channel, float fraction)
        {
            if (channel == null) return;
            float want = channel.MaxValue * fraction;
            var slot = channel.Slot;
            if (slot != null && slot.Value <= want) return;
            channel.Set(want);
        }

        // ── Hands over the eyes or ears ─────────────────────────────────────────

        /// <summary>
        /// Puts both hands on the face, and puts them back if something else (the game's own wound
        /// covering) moved them: checked a few times a second, re-placed only when a hand has come
        /// away and isn't already flying somewhere.
        /// </summary>
        private static void Cover(Stunned s, float now)
        {
            if (!Config.FlashCoverFace || s.Head == null || now < s.NextCoverCheck) return;
            if (s.Covering && s.Frame == null) return;
            s.NextCoverCheck = now + 0.4f;
            try
            {
                var ik = s.Puppeteer.HumanoidReferences?.IK;
                if (ik == null) return;
                // Which side of the head is the left one (the head's local X sign), decided once when
                // the hands go up - from the shoulders, which can't cross, else from the hands. Read
                // off the hands every check, a body flailing as it fell swapped the sides and sent
                // each hand across the face to the other one.
                if (!s.Covering) { PickFrame(s, ik); s.LeftSide = LeftSide(s, ik); }
                float leftSide = s.LeftSide;
                if (Place(ik.LeftHandEffectorPlacer, ik.LeftHandEffector, s, Target(s, s.Eyes, leftSide), !s.Covering)) s.PlacedLeft++;
                if (Place(ik.RightHandEffectorPlacer, ik.RightHandEffector, s, Target(s, s.Eyes, -leftSide), !s.Covering)) s.PlacedRight++;
                if (!s.Covering) HoldArms(s, ik, leftSide);
                s.Covering = true;
            }
            catch (System.Exception e)
            {
                s.Head = null;   // don't try again for this body
                if (Config.Dbg1) MelonLogger.Warning($"[Flash] covering the face failed: {e.Message}");
            }
        }

        private static float LeftSide(Stunned s, IKReferences ik)
        {
            var refs = ik.FullBodyBiped != null ? ik.FullBodyBiped.references : null;
            Transform l = refs?.leftUpperArm, r = refs?.rightUpperArm;
            if (l == null || r == null) { l = ik.LeftHandEffector; r = ik.RightHandEffector; }
            float lx = FrameLocal(s, l.position).x, rx = FrameLocal(s, r.position).x;
            return lx == rx ? -1f : Mathf.Sign(lx - rx);
        }

        // ── The frame the hands are placed in ───────────────────────────────────
        //
        // The game poses an IK rig and the physical body copies the rig's joint angles. Placed
        // against the physical head, a target is wherever that head is - and once the body lies
        // or hangs, the rig is somewhere else (0.66 m away lying, in testing), so the rig reached
        // across the gap with straight arms and the body copied the straight arms. Placed against
        // the rig's own head, the rig's pose is "hands on my face" wherever it is, and that pose
        // is what the body copies.
        //
        // All offsets are written in the physical head's axes (+Z face, +Y crown). The rig's head
        // bone may be built with other axes, so the turn between them is measured once, from a
        // body whose rig and head agree (closer than 0.2 m), rounded to the nearest quarter turn
        // (what is left is pose lag), and kept for every body after.

        private static Quaternion _rigToBody = Quaternion.identity;
        private static bool _rigToBodyKnown;

        private static void PickFrame(Stunned s, IKReferences ik)
        {
            var refs = ik.FullBodyBiped != null ? ik.FullBodyBiped.references : null;
            var rig = refs?.head;
            if (rig == null || !Config.FlashCoverOnRig)
            {
                s.Frame = s.Head; s.FrameQ = Quaternion.identity;
                s.FrameNote = rig == null ? "body head (no rig head)" : "body head (FlashCoverOnRig off)";
                return;
            }
            float gap = Vector3.Distance(rig.position, s.Head.position);
            if (!_rigToBodyKnown && gap < 0.2f)
            {
                var raw = Quaternion.Inverse(rig.rotation) * s.Head.rotation;
                _rigToBody = QuarterTurns(raw);
                _rigToBodyKnown = true;
                MelonLogger.Msg($"[Flash] rig head vs body head: {Quaternion.Angle(Quaternion.identity, raw):F0} deg apart, " +
                                $"taken as {Quaternion.Angle(Quaternion.identity, _rigToBody):F0} deg (gap {gap:F2} m)");
            }
            s.Frame = rig;
            s.FrameQ = _rigToBody;
            s.FrameNote = _rigToBodyKnown ? $"rig head, axes turned {Quaternion.Angle(Quaternion.identity, _rigToBody):F0} deg"
                                          : "rig head, axes not measured yet";
        }

        /// <summary>A point given in physical-head axes (from the head's pivot), in the world.</summary>
        private static Vector3 FramePoint(Stunned s, Vector3 local) => s.Frame.TransformPoint(s.FrameQ * local);

        /// <summary>A world point in physical-head axes, relative to the frame.</summary>
        private static Vector3 FrameLocal(Stunned s, Vector3 world) => Quaternion.Inverse(s.FrameQ) * s.Frame.InverseTransformPoint(world);

        /// <summary>The frame's rotation in physical-head axes (+Z face, +Y crown).</summary>
        private static Quaternion FrameRotation(Stunned s) => s.Frame.rotation * s.FrameQ;

        /// <summary>The nearest of the 24 quarter-turn rotations.</summary>
        private static Quaternion QuarterTurns(Quaternion q)
        {
            Vector3 f = MainAxis(q * Vector3.forward), u = MainAxis(q * Vector3.up);
            if (Mathf.Abs(Vector3.Dot(f, u)) > 0.5f) return Quaternion.identity;
            return Quaternion.LookRotation(f, u);
        }

        private const float Palm = 0.03f;   // a hand's half thickness: targets sit this far off the head's surface

        /// <summary>
        /// A palm over an eye (in front of the face, a quarter up from the head's middle, a little
        /// to its side) or over an ear (beside the head, level with its middle). Head space,
        /// measured from the head's collider rather than its pivot, which sits low (at the neck).
        /// </summary>
        private static Vector3 Target(Stunned s, bool eyes, float side)
        {
            Vector3 c = s.HeadCenter, e = s.HeadExtent;
            return eyes ? c + new Vector3(0.45f * e.x * side, 0.25f * e.y, e.z + Palm)
                        : c + new Vector3((e.x + Palm) * side, 0.1f * e.y, 0f);
        }

        /// <summary>
        /// The head's middle and half size in its own space, from its collider. Without one, the
        /// numbers the targets used before (pivot-relative) are kept.
        /// </summary>
        private static void MeasureHead(Stunned s, Transform head)
        {
            s.HeadCenter = Vector3.zero; s.HeadExtent = new Vector3(0.1f, 0.14f, 0.09f); s.HeadShape = "none (defaults)";
            try
            {
                foreach (var col in head.GetComponents<Collider>())
                {
                    if (col == null || col.isTrigger) continue;
                    var box = col.TryCast<BoxCollider>();
                    if (box != null) { s.HeadCenter = box.center; s.HeadExtent = box.size * 0.5f; s.HeadShape = "box"; return; }
                    var mesh = col.TryCast<MeshCollider>();
                    if (mesh != null && mesh.sharedMesh != null)
                    {
                        var b = mesh.sharedMesh.bounds;
                        s.HeadCenter = b.center; s.HeadExtent = b.extents; s.HeadShape = "mesh"; return;
                    }
                    // Sphere and capsule radius getters are stripped in this build: their world box, brought
                    // into head space (exact for a sphere, roughly right for an upright capsule).
                    var wb = col.bounds;
                    var scale = head.lossyScale;
                    s.HeadCenter = head.InverseTransformPoint(wb.center);
                    s.HeadExtent = new Vector3(wb.extents.x / Mathf.Max(1e-4f, Mathf.Abs(scale.x)),
                                               wb.extents.y / Mathf.Max(1e-4f, Mathf.Abs(scale.y)),
                                               wb.extents.z / Mathf.Max(1e-4f, Mathf.Abs(scale.z)));
                    s.HeadShape = col.GetIl2CppType().Name + " (world box)";
                    return;
                }
                // Nothing on the head's own object: the first solid collider under it.
                foreach (var col in head.GetComponentsInChildren<Collider>())
                {
                    if (col == null || col.isTrigger) continue;
                    var wb = col.bounds;
                    var scale = head.lossyScale;
                    s.HeadCenter = head.InverseTransformPoint(wb.center);
                    s.HeadExtent = new Vector3(wb.extents.x / Mathf.Max(1e-4f, Mathf.Abs(scale.x)),
                                               wb.extents.y / Mathf.Max(1e-4f, Mathf.Abs(scale.y)),
                                               wb.extents.z / Mathf.Max(1e-4f, Mathf.Abs(scale.z)));
                    s.HeadShape = col.GetIl2CppType().Name + " on " + col.name + " (world box)";
                    return;
                }
            }
            catch (System.Exception e) { s.HeadShape = "failed: " + e.Message; }
            finally
            {
                if (Config.Dbg1) MelonLogger.Msg($"[Flash] head {s.HeadShape}: centre {s.HeadCenter}, half size {s.HeadExtent}");
            }
        }

        /// <summary>Starts the hand's flight to <paramref name="local"/> (physical-head axes, on the frame); true if it did.</summary>
        private static bool Place(HandEffectorPlacer placer, Transform effector, Stunned s, Vector3 local, bool first)
        {
            if (placer == null || s.Frame == null) return false;
            if (!first)
            {
                if (placer.InFlight) return false;
                if (effector != null && Vector3.Distance(effector.position, FramePoint(s, local)) < 0.08f) return false;
            }
            placer.SetLocalPosition(s.FrameQ * local, s.Frame, first ? 1.6f : 1f, true,
                new Il2CppSystem.Nullable<Vector3>(), new Il2CppSystem.Nullable<Quaternion>(), null);
            return true;
        }

        private static void Uncover(Stunned s)
        {
            if (!s.Covering) return;
            s.Covering = false;
            ReleaseArms(s);
            try
            {
                var ik = s.Puppeteer != null ? s.Puppeteer.HumanoidReferences?.IK : null;
                if (ik == null) return;
                ik.LeftHandEffectorPlacer?.RemovePosition();
                ik.RightHandEffectorPlacer?.RemovePosition();
            }
            catch { }
        }

        public static void Clear()
        {
            foreach (var s in _stunned) Uncover(s);
            _stunned.Clear();
            DropArms();
            StopRing();
            _blindPeak = 0f;
        }

        // ── The player ──────────────────────────────────────────────────────────

        // The blind: full for a hold, then clearing slowly (the way shooters do it: you are
        // properly blind for a few seconds, then the world comes back through the glare).
        private static float _blindStart, _blindHold, _blindFade, _blindPeak;
        private static Texture2D _white;

        /// <summary>Sight blinds you (white-out or black-out), hearing rings the ears.</summary>
        private static void PlayerEffect(Vector3 origin)
        {
            if (!Config.FlashPlayer) return;   // off: only the bang itself
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            Vector3 to = origin - eye;
            float d = to.magnitude;
            var hits = Physics.RaycastAll(eye, to / Mathf.Max(0.01f, d), Mathf.Max(0f, d - 0.3f), Config.WorldLayerMask, QueryTriggerInteraction.Ignore);
            bool open = hits.Length == 0;
            float sight = Sight(origin, eye, cam.transform.forward, open);
            float hearing = Falloff(d) * (open ? 1f : Config.FlashHearingOcclusion);

            if (sight >= 0.08f)
            {
                float now = Time.time;
                float hold = Mathf.Max(0f, Config.FlashBlindTime) * sight;
                float fade = Mathf.Max(0.3f, Config.FlashBlindFade * (0.4f + 0.6f * sight));
                // A second bang while still blind: never brighter-then-dimmer, never shorter.
                float left = BlindAlpha(now), peak = Mathf.Clamp01(sight * 1.6f);
                if (left <= 0f || now + hold + fade > _blindStart + _blindHold + _blindFade)
                {
                    _blindStart = now; _blindHold = hold; _blindFade = fade;
                    _blindPeak = Mathf.Max(peak, left);
                }
                else _blindPeak = Mathf.Max(_blindPeak, peak);
            }
            CameraFX.AddKick(0.5f * hearing);
            if (hearing >= 0.15f) StartRing(hearing);
            if (Config.Dbg1) MelonLogger.Msg($"[Flash] you at {d:F1} m: sight {sight:F2}, hearing {hearing:F2}");
        }

        /// <summary>The blind's strength now: the peak through the hold, then easing out over the fade.</summary>
        private static float BlindAlpha(float now)
        {
            float t = now - _blindStart;
            if (_blindPeak <= 0f || t < 0f || t >= _blindHold + _blindFade) return 0f;
            if (t <= _blindHold) return _blindPeak;
            float a = 1f - (t - _blindHold) / _blindFade;
            return _blindPeak * a * a * (3f - 2f * a);
        }

        /// <summary>The white-out (or black-out), drawn over everything (called from OnGUI).</summary>
        public static void DrawOverlay()
        {
            float alpha = BlindAlpha(Time.time);
            if (alpha <= 0f) return;
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); _white.hideFlags = HideFlags.HideAndDontSave; }
            float c = Config.FlashBlackout ? 0f : 1f;
            var prev = GUI.color;
            GUI.color = new Color(c, c, c, alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
            GUI.color = prev;
        }

        // The ringing: one looping sine (FlashRingLoop, 4 kHz in the bundle), its pitch picked per
        // bang, full for the first moment and then fading out with the square of the time left.
        private static AudioSource _ring;
        private static float _ringBase, _ringLevel, _ringStart, _ringLength;

        private static void StartRing(float hearing)
        {
            float now = Time.time;
            float level = Mathf.Clamp01(hearing);
            if (_ring == null)
            {
                _ring = Sfx.PlayHeld("FlashRingLoop", 1f);
                if (_ring == null) return;
                _ringBase = _ring.volume;
                _ringLevel = 0f;
            }
            // A new bang while still ringing: a fresh tone, at least as loud as what is left.
            _ring.pitch = Random.Range(Config.FlashRingPitchMin, Config.FlashRingPitchMax);
            _ringLevel = Mathf.Max(level, RingEnvelope(now) * _ringLevel);
            _ringStart = now;
            _ringLength = Mathf.Max(1.5f, Config.FlashRingTime * _ringLevel);
            _ring.volume = _ringBase * _ringLevel;
        }

        private static float RingEnvelope(float now)
        {
            if (_ringLength <= 0f) return 0f;
            float t = (now - _ringStart) / _ringLength;
            if (t >= 1f) return 0f;
            const float hold = 0.12f;
            if (t <= hold) return 1f;
            float u = 1f - (t - hold) / (1f - hold);
            return u * u;
        }

        private static void TickRing()
        {
            if (_ring == null) return;
            float env = RingEnvelope(Time.time);
            if (env <= 0f) { StopRing(); return; }
            _ring.volume = _ringBase * _ringLevel * env;
        }

        private static void StopRing()
        {
            if (_ring != null) Object.Destroy(_ring.gameObject);
            _ring = null;
            _ringLength = 0f;
        }
    }
}
