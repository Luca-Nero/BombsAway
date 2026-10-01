using System.Collections.Generic;
using System.Text;
using Il2CppEffectors;
using Il2CppLVA.Limbs.Variants.Human;
using Il2CppLVA.Puppeteers.Variants.Humanoid;
using UnityEngine;
using Color = UnityEngine.Color;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// DebugDrawFlashArms: what every stunned body's head, hands and elbows are doing, live, with
    /// labels. Three skeletons are in play and the point is to see where they disagree:
    ///
    /// the physical body (rigidbodies: what you see), the IK rig the game poses (bones the
    /// muscles pull the physical body toward), and the IK targets (the hand effectors and elbow
    /// bend goals the flashbang and the game move). Targets are placed in the physical head's
    /// space, the IK rig reaches for them, and the physical body follows the rig with whatever
    /// muscle it has left.
    ///
    /// Drawn from OnGUI (repaint), so after the frame's IK and physics; on top of the world.
    /// </summary>
    internal static partial class Flashbang
    {
        private static readonly Color CHead = new Color(1f, 1f, 1f, 0.7f);
        private static readonly Color CRig = new Color(0.75f, 0.75f, 0.8f, 0.9f);
        private static readonly Color CGap = new Color(1f, 0.25f, 0.25f, 1f);
        private static readonly Color CTarget = new Color(1f, 0.9f, 0.1f, 1f);
        private static readonly Color CEffector = new Color(0.2f, 0.95f, 1f, 1f);
        private static readonly Color CReach = new Color(1f, 0.55f, 0.1f, 1f);
        private static readonly Color CPhys = new Color(1f, 0.3f, 0.9f, 1f);
        private static readonly Color CBend = new Color(0.65f, 0.4f, 1f, 1f);
        private static readonly Color CFlat = new Color(1f, 0.6f, 0.75f, 1f);
        private static readonly Color CFrame = new Color(1f, 0.9f, 0.1f, 0.5f);

        private static readonly List<(Vector3 At, string Text, Color Colour)> _labels = new List<(Vector3, string, Color)>();

        /// <summary>From OnGUI: the lines (on repaint) and the labels and legend.</summary>
        public static void DrawDiagnostics()
        {
            var ev = Event.current;
            if (ev == null || ev.type != EventType.Repaint) return;

            LiveLines.Begin();
            _labels.Clear();
            if (Config.DebugDrawFlashArms)
            {
                int n = 0;
                foreach (var s in _stunned)
                {
                    n++;
                    if (s.Puppeteer == null || s.Head == null) continue;
                    try { DrawBody(s, n); }
                    catch (System.Exception e) { _labels.Add((s.Head.position, $"#{n} draw failed: {e.Message}", CGap)); }
                }
            }
            LiveLines.End();
            if (!Config.DebugDrawFlashArms) return;

            var cam = Camera.main;
            if (cam != null)
                foreach (var (at, text, colour) in _labels)
                {
                    var p = cam.WorldToScreenPoint(at);
                    if (p.z <= 0f) continue;
                    Label(new Rect(p.x + 6f, Screen.height - p.y - 8f, 460f, 60f), text, colour);
                }
            Legend();
        }

        private static void DrawBody(Stunned s, int n)
        {
            var head = s.Head;
            var ik = s.Puppeteer.HumanoidReferences?.IK;
            var refs = ik != null && ik.FullBodyBiped != null ? ik.FullBodyBiped.references : null;

            // The physical head: its axes (Z = face), its collider box and middle.
            LiveLines.Axes(head.position, head.rotation, 0.15f);
            LiveLines.Box(head, s.HeadCenter, s.HeadExtent, CHead);
            Vector3 middle = head.TransformPoint(s.HeadCenter);
            LiveLines.Cross(middle, 0.02f, CHead);

            // The rig's head, and how far the rig has drifted from the physical body.
            float gap = 0f;
            if (refs != null && refs.head != null)
            {
                LiveLines.Axes(refs.head.position, refs.head.rotation, 0.1f, 0.45f, 0.004f);
                LiveLines.Line(refs.head.position, head.position, CGap, 0.004f);
                gap = Vector3.Distance(refs.head.position, head.position);
            }
            float hold = s.HoldUntil - Time.time;
            _labels.Add((middle + head.up * (s.HeadExtent.y + 0.1f),
                $"#{n} {(s.Covering ? (s.Eyes ? "EYES" : "EARS") : "no cover")}  stun {s.Stun:F2}  hold {Mathf.Max(0f, hold):F1}s  head {s.HeadShape}  rig-body head gap {gap:F2} m\nhands placed on: {s.FrameNote}",
                CHead));

            // The head as the hands see it: the same box, on the frame they are placed against.
            if (s.Frame != null && s.Frame != head)
            {
                var corners = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    corners[i] = FramePoint(s, s.HeadCenter + new Vector3((i & 1) == 0 ? -s.HeadExtent.x : s.HeadExtent.x,
                                                                          (i & 2) == 0 ? -s.HeadExtent.y : s.HeadExtent.y,
                                                                          (i & 4) == 0 ? -s.HeadExtent.z : s.HeadExtent.z));
                for (int i = 0; i < 8; i++)
                    for (int bit = 1; bit < 8; bit <<= 1)
                        if ((i & bit) == 0) LiveLines.Line(corners[i], corners[i | bit], CFrame, 0.004f);
                var fr = FrameRotation(s);
                LiveLines.Line(FramePoint(s, s.HeadCenter), FramePoint(s, s.HeadCenter) + fr * Vector3.forward * 0.2f, CFrame, 0.006f);
            }

            if (ik == null) return;
            if (s.Hands == null) s.Hands = FindHands(s.Puppeteer.transform.root);
            DrawArm(s, head, "L", s.LeftSide, ik.LeftHandEffectorPlacer, ik.LeftHandEffector, ik.LeftArmBendGoal,
                    refs?.leftUpperArm, refs?.leftForearm, refs?.leftHand, s.PlacedLeft);
            DrawArm(s, head, "R", -s.LeftSide, ik.RightHandEffectorPlacer, ik.RightHandEffector, ik.RightArmBendGoal,
                    refs?.rightUpperArm, refs?.rightForearm, refs?.rightHand, s.PlacedRight);
        }

        private static void DrawArm(Stunned s, Transform head, string name, float side, HandEffectorPlacer placer,
                                    Transform effector, Transform bendGoal, Transform upper, Transform fore, Transform hand, int placed)
        {
            var sb = new StringBuilder(name);
            if (s.Frame == null) return;
            Vector3 target = FramePoint(s, Target(s, s.Eyes, side));
            if (s.Covering) LiveLines.Cross(target, 0.03f, CTarget, 0.008f);

            // The IK target the hand is flown to, and its way to the flashbang's target.
            if (effector != null)
            {
                LiveLines.Cross(effector.position, 0.025f, CEffector);
                if (s.Covering)
                {
                    LiveLines.Line(effector.position, target, CTarget, 0.003f);
                    sb.Append($"  eff>tgt {Vector3.Distance(effector.position, target):F2}");
                }
                if (placer != null) sb.Append(placer.InFlight ? "  FLYING" : "  settled").Append($"  flights {placed}");
            }

            // The rig's arm, and how far its hand is from where it was told to be.
            if (upper != null && fore != null && hand != null)
            {
                LiveLines.Line(upper.position, fore.position, CRig, 0.008f);
                LiveLines.Line(fore.position, hand.position, CRig, 0.008f);
                if (effector != null)
                {
                    LiveLines.Line(hand.position, effector.position, CReach, 0.004f);
                    sb.Append($"  rig>eff {Vector3.Distance(hand.position, effector.position):F2}");
                }
            }

            // The elbow: where the bend goal is, where the flashbang wants it.
            if (bendGoal != null)
            {
                LiveLines.Cross(bendGoal.position, 0.025f, CBend);
                if (fore != null) LiveLines.Line(fore.position, bendGoal.position, CBend, 0.003f);
                if (s.Covering) LiveLines.Cross(FramePoint(s, Elbow(s, s.Eyes, side)), 0.015f, CBend, 0.003f);
            }

            // The hand's long axis and flat side, now (from the rig's hand) and wanted (at the target).
            if (hand != null && effector != null && placer != null && _arms.TryGetValue(placer.Pointer, out var a) && a.Along != Vector3.zero)
            {
                LiveLines.Line(hand.position, hand.position + hand.rotation * a.Along * 0.12f, CReach, 0.006f);
                if (a.Flat != Vector3.zero) LiveLines.Line(hand.position, hand.position + hand.rotation * a.Flat * 0.08f, CFlat, 0.006f);
                if (s.Covering)
                {
                    var want = HandRotation(a);
                    LiveLines.Line(target, target + want * a.Along * 0.12f, CTarget, 0.004f);
                    if (a.Flat != Vector3.zero) LiveLines.Line(target, target + want * a.Flat * 0.08f, CFlat, 0.004f);
                    sb.Append($"  turn off {Quaternion.Angle(hand.rotation, want):F0}deg");
                }
                if (a.ReleasedAt >= 0f) sb.Append("  RELEASING");
            }

            // The physical hand on this side, and how far it trails the rig's.
            Rigidbody phys = PhysicalHand(s, head, side);
            if (phys != null)
            {
                LiveLines.Cross(phys.worldCenterOfMass, 0.025f, CPhys);
                if (hand != null)
                {
                    LiveLines.Line(hand.position, phys.worldCenterOfMass, CPhys, 0.003f);
                    sb.Append($"  body>rig {Vector3.Distance(phys.worldCenterOfMass, hand.position):F2}");
                }
            }

            _labels.Add((effector != null ? effector.position : target, sb.ToString(), CEffector));
        }

        private static Rigidbody[] FindHands(Transform root)
        {
            var list = new List<Rigidbody>();
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>())
            {
                if (rb == null || !ExplosionSystem.IsLimb(rb.gameObject)) continue;
                try
                {
                    var receiver = rb.GetComponent<LimbEffectorReceiver>();
                    var limb = receiver != null ? receiver.m_limbReferences?.Limb : null;
                    if (limb != null && limb.TryCast<Hand>() != null) list.Add(rb);
                }
                catch { }
            }
            return list.ToArray();
        }

        /// <summary>The physical hand furthest toward <paramref name="side"/> of the head.</summary>
        private static Rigidbody PhysicalHand(Stunned s, Transform head, float side)
        {
            Rigidbody best = null;
            float bestX = float.MinValue;
            foreach (var rb in s.Hands)
            {
                if (rb == null) continue;
                float x = head.InverseTransformPoint(rb.worldCenterOfMass).x * side;
                if (x > bestX) { bestX = x; best = rb; }
            }
            return best;
        }

        private static void Legend()
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); _white.hideFlags = HideFlags.HideAndDontSave; }
            string[] rows =
            {
                "FLASH ARMS (DebugDrawFlashArms)",
                "axes red/green/blue  physical head X / Y / Z (blue = face); faint = rig head",
                "white box + cross    head collider, its middle",
                "faint yellow box     the head the hands are placed against (rig head), stick = its face",
                "red line             rig head to physical head (rig and body apart)",
                "yellow cross         hand target (eye or ear); thin yellow = effector's way there",
                "cyan cross           hand IK effector (what the rig reaches for)",
                "grey lines           rig arm: shoulder > elbow > hand",
                "orange line          rig hand to effector (reach error); orange stick = hand long axis",
                "pink stick           hand flat side; yellow/pink sticks at target = wanted",
                "purple cross + line  elbow bend goal; small purple = wanted elbow",
                "magenta cross        physical hand; magenta line = how far it trails the rig",
            };
            float y = 10f;
            var bg = new Rect(8f, 8f, 600f, rows.Length * 20f + 6f);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(bg, _white);
            GUI.color = prev;
            foreach (var r in rows) { Label(new Rect(12f, y, 600f, 22f), r, Color.white); y += 20f; }
        }

        private static void Label(Rect r, string text, Color c)
        {
            var prev = GUI.color;
            GUI.color = Color.black;
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text);   // (string, GUIStyle) is stripped
            GUI.color = c;
            GUI.Label(r, text);
            GUI.color = prev;
        }
    }
}
