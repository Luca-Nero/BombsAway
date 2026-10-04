using System.Collections.Generic;
using UnityEngine;
using Color = UnityEngine.Color;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    internal static class SharedRng
    {
        public static readonly System.Random Instance = new System.Random();
    }

    internal static class CameraCache
    {
        private static Camera _cam;
        public static Camera Main
        {
            get
            {
                if (_cam == null) _cam = Camera.main;
                return _cam;
            }
        }
    }

    internal class GrenadeState
    {
        public GameObject Obj;
        public Renderer GrenadeRenderer;
        public Rigidbody Rb;
        public Rigidbody HostRb;
        public Vector3 LocalOffset;
        public Quaternion LocalRotation;
        public float Timer;
        public bool Dead;
        public float FlashAccum;
        public bool FlashToggle;
        public ExplosionParams Params;
        public Color[] BaseColors;
        public Material[] FlashMats;          // the body's materials, for the fuse flash
        public Renderer Blink;                // a status LED that blinks while it waits (C4)
        public ClaymoreRig Clay;              // the claymore's lenses, where its lasers start
        public List<Material> Owned = new List<Material>();   // every Material made for this charge
        public bool Stuck;
        public bool HasHost;                  // stuck to a rigidbody (HostRb goes null if that is destroyed)
        public bool Armed;
        public float ProxScanAccum;
        public float TripAt = -1f;            // claymore: when a laser was broken (<0 = not tripped)
        public bool RemoteTriggered;
        public Vector3 ThrowDir;

        // Scripted flight (sticky ordnance): see Ordnance.Flight.cs.
        public bool Ballistic;
        public Vector3 Velocity;
        public float FlightTime;
        public float CastRadius;
        public Vector3 SpinAxis;
        public float SpinRate;
        public bool HasLanding;
        public float LandTime;
        public Quaternion LandRot;
        public LineRenderer[] SightLines;

        // Smoke grenade, once lit (Ordnance.Smoke.cs): its effect (the jet), the cloud it feeds, its hiss, until when, and its colour.
        public GameObject SmokeFx;
        public SmokeCloud Cloud;
        public AudioSource Hiss;
        public float SmokeUntil = -1f;        // <0 = not lit
        public float CanGoneAt = -1f;
        public Color SmokeColor = Color.white;
    }

    internal class HomingMissileState
    {
        public GameObject Obj;
        public Rigidbody TargetRb;
        public Renderer TargetRenderer;        
        public Vector3 LastKnownTargetPos;
        public Vector3 PrevLOSDir;              
        public Vector3 Velocity;                
        public float Timer;
        public float MotorTime;                 
        public bool Dead;
        public int Phase;                      
        public bool TopAttack;                   
        public bool Unguided;
        public bool Beam, OnBeam;               // AT-4: converging on the centre line, then riding it
        public Vector3 BeamOrigin, BeamDir;     // that line: the camera ray when it fired
        public float LaunchY;
        public float CruiseAlt;            
        public List<Material> Owned = new List<Material>();   // every Material made for this missile
        public ExplosionParams Params;
        public MissileRig Rig;                  // bundled model: fins and nozzle glow
        public RocketRig RocketRig;             // the AT-4's instead: flick-out fins and tracer
        public TargetBody Body;                 // whole-body lock: aim at the ragdoll, not a limb
        public TrailRenderer Trail;             // exhaust, emitting only while the motor burns
        public AudioSource Motor;               // its roar, while the motor burns
        public bool Thermal;                    // fired from a thermal view: smoke doesn't hide its target
        public bool Hidden;                     // smoke hides the target: flying at where it was last seen
        public float NextSightCheck;
    }

    /// <summary>The Javelin's two attacks. The AT-4 has none: it flies where it is pointed.</summary>
    public enum AttackMode
    {
        Top,
        Direct
    }

    /// <summary>The launchers' warheads: shaped charge, high explosive, thermobaric. Cycled in this order.</summary>
    public enum WarheadMode { HEAT, HE, TBX }

    internal enum DetonationMode
    {
        Timer,
        Remote,
        Proximity,
        Impact
    }

    internal class ExplosionParams
    {
        /// <summary>Which explosive this is. Registered with FruitLib as "BombsAway." + Kind.</summary>
        public string Kind = "Grenade";
        public bool Sticky = false;
        public DetonationMode Detonation = DetonationMode.Timer;
        public float FuseTime = 2f;
        public float FlashTime = 2f;
        public float ProximityRadius = 2f;
        public float ProximityHSpreadDeg = 360f;
        public float ProximityVSpreadDeg = 360f;
        public float ProximityInterval = 0.1f;
        public float ImpactCastRadius = 0.1f;
        public float ImpactCastRange = 0.5f;
        public float ArmDelay = 0.15f;
        public Vector3 Origin;
        public Vector3 Forward = Vector3.up;
        public float HSpreadDeg = 360f;
        public float VSpreadDeg = 360f;
        public float BlastRadius = 6f;
        public float BlastForce = 5f;
        public float BlastUpward = 2f;
        public float OverpressureRadius = 3.5f;
        public float OverpressureFalloffExp = 1f;
        public int OverpressureWoundPoints = 12;
        public int FragRayCount = 2000;
        public float FragSpeed = 15f;
        public float FragMaxTime = 4f;
        public float FragImpulse = 0.8f;
        public float DamageScale = 1f;
        /// <summary>Wound power of one fragment at the charge (FruitBallistics).</summary>
        public int FragPower = 3000;
        /// <summary>The charge as kg of TNT: drives FruitLib's physical overpressure (0 = old radius model).</summary>
        public float ChargeKgTNT = 0f;
        /// <summary>This kind's blast-wave push, times the BlastPushScale setting (the gun runs' bursts throw harder than their charge).</summary>
        public float PushScale = 1f;
        /// <summary>Shaped-charge jet (FruitBallistics 5.4): rays, full cone angle, free metres through walls.</summary>
        public int JetRays = 0;
        public float JetConeDeg = 3f;
        public float JetPenetration = 0f;
        public int JetPower = 0;
        public int JetSpallCount = 0;
        public int ArcSteps = 12;
        public float DebrisRaysRatio = 0.04f;
        /// <summary>Which parts of FruitLib's detonation run (all, unless the kind leaves some out).</summary>
        public FruitLib.ExplosionFeatures Features = FruitLib.ExplosionFeatures.All;
        /// <summary>What still reaches something behind full cover, 0..1 (FruitLib's default 0.15).</summary>
        public float BlastDiffraction = 0.15f;
        /// <summary>How far out the blast wave pushes and injures, m (FruitLib 5.7.0; its defaults 40 / 60). The big bombs reach farther.</summary>
        public float MaxPushRange = 40f, MaxInjuryRange = 60f;
        /// <summary>Share of a fragment's power lost per metre, as exp(-x d) (FruitLib's 0.08): a bomb's heavy fragments carry far.</summary>
        public float FragPowerFalloff = 0.08f;
        /// <summary>This kind's wound budget per detonation; 0 = the MaxWoundsPerExplosion setting. A bomb among a crowd needs far more than a grenade.</summary>
        public int MaxWounds = 0;
        /// <summary>FruitLib 5.8.0's targeted fragments: the case's real fragment count (0 = off; FragRayCount
        /// rays as before). With it on, FragRayCount is only the untargeted scenery rays.</summary>
        public int FragTargeted = 0;
        /// <summary>Most fragment walks one limb takes from one detonation (0 = no limit).</summary>
        public int MaxWalksPerLimb = 0;
        /// <summary>Side-spray belt: thickness in degrees (0 = an even sphere) and its share of the fragments.</summary>
        public float FragBeltDeg = 0f, FragBeltShare = 0.8f;
        /// <summary>The casing's long axis for the belt (a bomb's flight); zero = Forward. Forward stays what effects are aimed by.</summary>
        public Vector3 Axis = Vector3.zero;
        /// <summary>How well the fragments go through things, against steel chunks of their mass (1); the MOAB's case is aluminium.</summary>
        public float FragPenetrationScale = 1f;
        /// <summary>A burst this low over the ground counts as a surface burst, m/kg^(1/3) (FruitLib 5.10.0; 0 = only on contact).</summary>
        public float SurfaceBurstScaledHeight = 0f;

        public static ExplosionParams FromGrenadeConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
                Kind = "Grenade",
                FragPower = Config.FragPower,
                ChargeKgTNT = Config.ChargeKgTNT,
                Sticky = false,

                Detonation = DetonationMode.Timer,
                FuseTime = Config.Fuse,
                FlashTime = 2f,

                Origin = origin,
                Forward = Vector3.up,
                HSpreadDeg = 360f,
                VSpreadDeg = 360f,

                BlastRadius = Config.BlastRadius,
                BlastForce = Config.BlastForce,
                BlastUpward = Config.BlastUpward,

                OverpressureRadius = Config.OverpressureRadius,
                OverpressureFalloffExp = Config.OverpressureFalloffExp,
                OverpressureWoundPoints = Config.OverpressureWoundPoints,

                FragRayCount = Config.FragRayCount,
                FragSpeed = Config.FragSpeed,
                FragMaxTime = Config.FragMaxTime,
                FragImpulse = Config.FragImpulse,
                ArcSteps = Config.ArcDebugSteps,

                DebrisRaysRatio = Config.DebrisRaysRatio,
                DamageScale = Config.DamageScale,
            };
        }

        /// <summary>Smoke grenade: a fuse, then smoke instead of a blast (Ordnance.Smoke.cs). Kind "Smoke" never detonates.</summary>
        public static ExplosionParams FromSmokeConfig(Vector3 origin)
        {
            var p = FromGrenadeConfig(origin);
            p.Kind = "Smoke";
            p.FuseTime = Config.SmokeFuse;
            p.FlashTime = 0f;
            p.FragRayCount = 0;
            p.ChargeKgTNT = 0f;
            return p;
        }

        /// <summary>Flashbang: a fuse, a small blast without fragments, and the stun (Ordnance.Flash.cs).</summary>
        public static ExplosionParams FromFlashConfig(Vector3 origin)
        {
            var p = FromGrenadeConfig(origin);
            p.Kind = "Flash";
            p.FuseTime = Config.FlashFuse;
            p.FlashTime = 0f;
            p.FragRayCount = 0;
            p.FragPower = 0;
            p.ChargeKgTNT = Config.FlashChargeKgTNT;
            p.BlastRadius = 2f;
            p.BlastForce = 0.3f;
            p.BlastUpward = 0.2f;
            p.OverpressureRadius = 0.5f;
            p.OverpressureWoundPoints = 0;
            p.DebrisRaysRatio = 0f;
            p.DamageScale = 0.1f;
            return p;
        }

        public static ExplosionParams FromC4Config(Vector3 origin)
        {
            return new ExplosionParams
            {
                Kind = "C4",
                FragPower = Config.C4FragPower,
                ChargeKgTNT = Config.C4ChargeKgTNT,
                Sticky = true,

                Detonation = DetonationMode.Remote,

                Origin = origin,
                Forward = Vector3.up,
                HSpreadDeg = 360f,
                VSpreadDeg = 360f,

                BlastRadius = Config.C4BlastRadius,
                BlastForce = Config.C4BlastForce,
                BlastUpward = Config.C4BlastUpward,

                OverpressureRadius = Config.C4OverpressureRadius,
                OverpressureFalloffExp = Config.C4OverpressureFalloffExp,
                OverpressureWoundPoints = Config.C4OverpressureWoundPoints,

                FragRayCount = Config.C4FragRayCount,
                FragSpeed = Config.C4FragSpeed,
                FragMaxTime = Config.C4FragMaxTime,
                FragImpulse = Config.C4FragImpulse,

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = Config.DebrisRaysRatio,
                DamageScale = Config.C4DamageScale,
            };
        }

        public static ExplosionParams FromClaymoreConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
                Kind = "Claymore",
                FragPower = Config.MineFragPower,
                ChargeKgTNT = Config.MineChargeKgTNT,
                Sticky = true,

                Detonation = DetonationMode.Proximity,

                Origin = origin,
                Forward = Vector3.forward,
                HSpreadDeg = 60f,
                VSpreadDeg = 40f,

                ProximityRadius = Config.MineProximityRange,
                ProximityHSpreadDeg = 40f,
                ProximityVSpreadDeg = 40f,
                ProximityInterval = 0.1f,

                BlastRadius = Config.MineBlastRadius,
                BlastForce = Config.MineBlastForce,
                BlastUpward = Config.MineBlastUpward,

                OverpressureRadius = Config.MineOverpressureRadius,
                OverpressureFalloffExp = Config.MineOverpressureFalloffExp,
                OverpressureWoundPoints = Config.MineOverpressureWoundPoints,

                FragRayCount = Config.MineFragRayCount,
                FragSpeed = Config.MineFragSpeed,
                FragMaxTime = Config.MineFragMaxTime,
                FragImpulse = Config.MineFragImpulse,

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = Config.DebrisRaysRatio,
                DamageScale = Config.MineDamageScale,
            };
        }

        public static ExplosionParams FromMissileConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
                Kind = "Missile",
                FragPower = Config.MissileFragPower,
                ChargeKgTNT = Config.MissileChargeKgTNT,
                Sticky = false,

                Detonation = DetonationMode.Impact,
                ImpactCastRadius = 0.15f,
                ImpactCastRange = 0.3f,
                ArmDelay = Config.MissileSoftLaunchTime,  // don't detonate during soft launch

                Origin = origin,
                Forward = Vector3.forward,
                HSpreadDeg = 90f,
                VSpreadDeg = 90f,

                BlastRadius = Config.MissileBlastRadius,
                BlastForce = Config.MissileBlastForce,
                BlastUpward = Config.MissileBlastUpward,

                OverpressureRadius = Config.MissileOverpressureRadius,
                OverpressureFalloffExp = Config.MissileOverpressureFalloffExp,
                OverpressureWoundPoints = Config.MissileOverpressureWoundPoints,

                FragRayCount = Config.MissileFragRayCount,
                FragSpeed = Config.MissileFragSpeed,
                FragMaxTime = Config.MissileFragMaxTime,
                FragImpulse = Config.MissileFragImpulse,

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = Config.DebrisRaysRatio,
                DamageScale = Config.MissileDamageScale,

                JetRays = Config.MissileJetRays,
                JetConeDeg = Config.MissileJetConeDeg,
                JetPenetration = Config.MissileJetPenetration,
                JetPower = Config.MissileJetPower,
                JetSpallCount = Config.MissileJetSpallCount,
            };
        }

        public static ExplosionParams FromMissileHEConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
                Kind = "MissileHE",
                FragPower = Config.MissileHEFragPower,
                ChargeKgTNT = Config.MissileHEChargeKgTNT,
                Sticky = false,

                Detonation = DetonationMode.Impact,
                ImpactCastRadius = 0.15f,
                ImpactCastRange = 0.3f,
                ArmDelay = Config.MissileSoftLaunchTime,

                Origin = origin,
                Forward = Vector3.forward,
                HSpreadDeg = 360f,
                VSpreadDeg = 360f,

                BlastRadius = Config.MissileHEBlastRadius,
                BlastForce = Config.MissileHEBlastForce,
                BlastUpward = Config.MissileHEBlastUpward,

                OverpressureRadius = Config.MissileHEOverpressureRadius,
                OverpressureFalloffExp = Config.MissileHEOverpressureFalloffExp,
                OverpressureWoundPoints = Config.MissileHEOverpressureWoundPoints,

                FragRayCount = Config.MissileHEFragRayCount,
                FragSpeed = Config.MissileHEFragSpeed,
                FragMaxTime = Config.MissileHEFragMaxTime,
                FragImpulse = Config.MissileHEFragImpulse,

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = Config.DebrisRaysRatio,
                DamageScale = Config.MissileHEDamageScale,
            };
        }

        /// <summary>
        /// The thermobaric warhead's main charge: the fuel cloud the warhead dispersed, ignited
        /// (Missile/Thermobaric.cs). All blast and no fragments: a longer, wider pressure wave
        /// than the HE warhead's for its weight, and one that fills rooms and spills round
        /// corners (BlastDiffraction well over the usual 0.15), so cover helps far less.
        /// </summary>
        public static ExplosionParams FromMissileTBXConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
                Kind = "MissileTBX",
                ChargeKgTNT = Config.MissileTBXChargeKgTNT,
                FragPower = 1000,
                Sticky = false,

                Detonation = DetonationMode.Impact,
                ImpactCastRadius = 0.15f,
                ImpactCastRange = 0.3f,
                ArmDelay = Config.MissileSoftLaunchTime,

                Origin = origin,
                Forward = Vector3.forward,
                HSpreadDeg = 360f,
                VSpreadDeg = 360f,

                BlastRadius = Config.MissileTBXBlastRadius,
                BlastForce = Config.MissileTBXBlastForce,
                BlastUpward = Config.MissileTBXBlastUpward,

                OverpressureRadius = Config.MissileTBXOverpressureRadius,
                OverpressureFalloffExp = 0.7f,
                OverpressureWoundPoints = Config.MissileTBXOverpressureWoundPoints,

                FragRayCount = 0,
                FragSpeed = 15f,
                FragMaxTime = 1f,
                FragImpulse = 0f,

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = 0f,
                DamageScale = Config.MissileTBXDamageScale,

                Features = FruitLib.ExplosionFeatures.BlastOnly,
                BlastDiffraction = Mathf.Clamp01(Config.MissileTBXDiffraction),
            };
        }

        /// <summary>The warhead the launchers carry now, as its explosion.</summary>
        public static ExplosionParams ForWarhead(WarheadMode w) =>
            w == WarheadMode.HE ? FromMissileHEConfig(Vector3.zero)
            : w == WarheadMode.TBX ? FromMissileTBXConfig(Vector3.zero)
            : FromMissileConfig(Vector3.zero);

        /// <summary>
        /// A 155 mm HE shell (M795: about 10.8 kg of TNT in a thick steel body, ~6.6 kg TNT
        /// equivalent of blast), point-detonating where it lands (FireSupport/FireMission.cs).
        /// </summary>
        public static ExplosionParams FromArtilleryConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
                Kind = "Arty155",
                FragPower = Config.ArtyFragPower,
                ChargeKgTNT = Config.ArtyChargeKgTNT,
                Sticky = false,
                Detonation = DetonationMode.Impact,

                Origin = origin,
                Forward = Vector3.up,
                HSpreadDeg = 360f,
                VSpreadDeg = 360f,

                BlastRadius = Config.ArtyBlastRadius,
                BlastForce = Config.ArtyBlastForce,
                BlastUpward = Config.ArtyBlastUpward,

                OverpressureRadius = Config.ArtyOverpressureRadius,
                OverpressureFalloffExp = 1f,
                OverpressureWoundPoints = Config.ArtyOverpressureWoundPoints,

                FragRayCount = Config.ArtyFragRayCount,
                FragSpeed = Config.ArtyFragSpeed,
                FragMaxTime = Config.ArtyFragMaxTime,
                FragImpulse = Config.ArtyFragImpulse,

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = Config.DebrisRaysRatio,
                DamageScale = Config.ArtyDamageScale,
            };
        }

        /// <summary>
        /// An 81 mm mortar bomb (M821: about 0.7 kg of Comp B in a thin body, ~0.95 kg TNT
        /// equivalent), point-detonating where it lands (FireSupport/FireMission.cs).
        /// </summary>
        public static ExplosionParams FromMortarConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
                Kind = "Mortar81",
                FragPower = Config.MortarFragPower,
                ChargeKgTNT = Config.MortarChargeKgTNT,
                Sticky = false,
                Detonation = DetonationMode.Impact,

                Origin = origin,
                Forward = Vector3.up,
                HSpreadDeg = 360f,
                VSpreadDeg = 360f,

                BlastRadius = Config.MortarBlastRadius,
                BlastForce = Config.MortarBlastForce,
                BlastUpward = Config.MortarBlastUpward,

                OverpressureRadius = Config.MortarOverpressureRadius,
                OverpressureFalloffExp = 1f,
                OverpressureWoundPoints = Config.MortarOverpressureWoundPoints,

                FragRayCount = Config.MortarFragRayCount,
                FragSpeed = Config.MortarFragSpeed,
                FragMaxTime = Config.MortarFragMaxTime,
                FragImpulse = Config.MortarFragImpulse,

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = Config.DebrisRaysRatio,
                DamageScale = Config.MortarDamageScale,
            };
        }

        /// <summary>
        /// One 30 mm HEI round going off where it hits (FireSupport/AirStrike.cs): a small charge
        /// in a naturally fragmenting body, made more devastating than the real one (5.12.0).
        /// Hits come at 65 a second, so the fragment count stays low.
        /// </summary>
        public static ExplosionParams FromGun30Config(Vector3 origin) =>
            GunBurst("Gun30", origin, Config.Gun30HEIChargeKgTNT, Config.Gun30HEIDamageScale, Config.Gun30HEIPushScale,
                     Config.Gun30FragRayCount, Config.Gun30HEIFragPower, Config.Gun30HEIFragKick, Config.Gun30HEIOverpressurePoints,
                     Config.Gun30BlastRadius, Config.Gun30BlastForce, Config.Gun30BlastUpward, Config.Gun30OverpressureRadius,
                     Config.Gun30FragSpeed, Config.Gun30FragMaxTime);

        /// <summary>One 20 mm PGU-28/B round going off where it hits (the F-22's gun run): the 30 mm's, smaller.</summary>
        public static ExplosionParams FromGun20Config(Vector3 origin) =>
            GunBurst("Gun20", origin, Config.Gun20HEIChargeKgTNT, Config.Gun20HEIDamageScale, Config.Gun20HEIPushScale,
                     Config.Gun20FragRayCount, Config.Gun20HEIFragPower, Config.Gun20HEIFragKick, Config.Gun20HEIOverpressurePoints,
                     Config.Gun20BlastRadius, Config.Gun20BlastForce, Config.Gun20BlastUpward, Config.Gun20OverpressureRadius,
                     Config.Gun20FragSpeed, Config.Gun20FragMaxTime);

        /// <summary>
        /// A JDAM's bomb going off (FireSupport/AirStrike.Bombs.cs): <paramref name="kind"/> is
        /// Jdam500, Jdam1000 or Jdam2000 (Mk 82 / 83 / 84, about 95 / 215 / 460 kg TNT). A thick
        /// cast case breaks into heavy fragments that carry far (the Mk 84's are lethal to about
        /// 120 m), and the blast wave pushes and injures past FruitLib's usual reach.
        /// </summary>
        public static ExplosionParams FromJdamConfig(string kind, Vector3 origin)
        {
            // The fragments' arcs are flown at about their real speed, so they go out flat over
            // the arena (a slow, readable arc lobbed them over people and into the ground
            // within ~15 m); the drawn debris is a small share of them.
            float charge, push, fragSpeed, falloff;
            int frags, rays, power, wounds;
            switch (kind)
            {
                case "Jdam500":
                    charge = Config.Jdam500ChargeKgTNT; frags = Config.Jdam500Fragments; rays = Config.Jdam500WorldRays; power = Config.Jdam500FragPower;
                    push = Config.Jdam500PushRange; fragSpeed = 180f; falloff = 0.02f; wounds = Config.Jdam500MaxWounds;
                    break;
                case "Jdam1000":
                    charge = Config.Jdam1000ChargeKgTNT; frags = Config.Jdam1000Fragments; rays = Config.Jdam1000WorldRays; power = Config.Jdam1000FragPower;
                    push = Config.Jdam1000PushRange; fragSpeed = 200f; falloff = 0.017f; wounds = Config.Jdam1000MaxWounds;
                    break;
                default:
                    kind = "Jdam2000";
                    charge = Config.Jdam2000ChargeKgTNT; frags = Config.Jdam2000Fragments; rays = Config.Jdam2000WorldRays; power = Config.Jdam2000FragPower;
                    push = Config.Jdam2000PushRange; fragSpeed = 220f; falloff = 0.015f; wounds = Config.Jdam2000MaxWounds;
                    break;
            }
            float r = Mathf.Pow(Mathf.Max(1f, charge) / 95f, 1f / 3f);   // the old-model fallbacks, by the 500 lb's
            return new ExplosionParams
            {
                Kind = kind,
                FragPower = power,
                ChargeKgTNT = charge,
                Sticky = false,
                Detonation = DetonationMode.Impact,

                Origin = origin,
                Forward = Vector3.up,
                HSpreadDeg = 360f,
                VSpreadDeg = 360f,

                BlastRadius = 30f * r,
                BlastForce = 12f,
                BlastUpward = 4f,

                OverpressureRadius = 25f * r,
                OverpressureFalloffExp = 1f,
                OverpressureWoundPoints = 30,

                // The case's fragments are aimed at the limbs in reach (FruitLib 5.8.0), so every
                // body gets its share however many there are; the rays only dress the scenery.
                FragTargeted = Mathf.Max(0, frags),
                FragRayCount = Mathf.Max(0, rays),
                MaxWalksPerLimb = Mathf.Max(0, Config.JdamWalksPerLimb),
                FragBeltDeg = Mathf.Clamp(Config.JdamFragBeltDeg, 0f, 180f),
                FragBeltShare = Mathf.Clamp01(Config.JdamFragBeltShare),
                FragSpeed = fragSpeed,
                FragMaxTime = 6f,
                FragImpulse = 0.3f,
                FragPowerFalloff = falloff,

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = 0f,   // the flat arcs would fling it off at their speed; FX_Jdam has its own clods
                DamageScale = Config.JdamDamageScale,

                MaxWounds = Mathf.Max(0, wounds),
                MaxPushRange = Mathf.Max(10f, push),
                MaxInjuryRange = Mathf.Max(10f, Config.JdamInjuryRange),
            };
        }

        /// <summary>
        /// A Hydra 70 rocket's M151 warhead going off where it hits (FireSupport/AirStrike.Rockets.cs):
        /// 1.04 kg of Comp B-4 (~1.4 kg TNT) in a 3.9 kg malleable cast-iron body. Bursting radius
        /// about 10 m, its fast fragments lethal past 50 m. As the bombs': the body's fragments are
        /// aimed at the limbs in reach, most of them in a belt square to the rocket's flight.
        /// </summary>
        public static ExplosionParams FromHydraConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
                Kind = "Hydra",
                FragPower = Config.HydraFragPower,
                ChargeKgTNT = Config.HydraChargeKgTNT,
                Sticky = false,
                Detonation = DetonationMode.Impact,

                Origin = origin,
                Forward = Vector3.up,
                HSpreadDeg = 360f,
                VSpreadDeg = 360f,

                BlastRadius = 8f,
                BlastForce = 4.5f,
                BlastUpward = 1.5f,

                OverpressureRadius = 12f,
                OverpressureFalloffExp = 1f,
                OverpressureWoundPoints = 14,

                FragTargeted = Mathf.Max(0, Config.HydraFragments),
                FragRayCount = Mathf.Max(0, Config.HydraWorldRays),
                MaxWalksPerLimb = Mathf.Max(0, Config.HydraWalksPerLimb),
                FragBeltDeg = Mathf.Clamp(Config.HydraFragBeltDeg, 0f, 180f),
                FragBeltShare = Mathf.Clamp01(Config.HydraFragBeltShare),
                FragSpeed = 120f,          // flat arcs, as the bombs' (a slow one lobs them over people)
                FragMaxTime = 3f,
                FragImpulse = 0.2f,
                FragPowerFalloff = 0.035f, // ~17 % of its power left at 50 m

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = 0f,
                DamageScale = Config.HydraDamageScale,
                MaxWounds = Mathf.Max(0, Config.HydraMaxWounds),
            };
        }

        /// <summary>
        /// The GBU-43/B MOAB going off (FireSupport/AirStrike.Bombs.cs): 8,500 kg of H-6, about
        /// 11 t of TNT, in a thin aluminium case, air-burst about 2 m up. A blast weapon above
        /// all: limbs blown apart within about 30 m, skin torn to about 50 m, lungs to about 95 m
        /// (it counts as a surface burst so low), and the wave throws bodies out to MoabPushRange.
        /// The 1.3 t case breaks into light aluminium chunks that carry the lethal zone out to
        /// about 150 m (its reported lethal radius), aimed as the JDAMs' are.
        /// </summary>
        public static ExplosionParams FromMoabConfig(Vector3 origin)
        {
            float r = Mathf.Pow(Mathf.Max(1f, Config.MoabChargeKgTNT) / 95f, 1f / 3f);   // the old-model fallbacks, by the 500 lb's
            return new ExplosionParams
            {
                Kind = "Moab",
                FragPower = Config.MoabFragPower,
                ChargeKgTNT = Config.MoabChargeKgTNT,
                Sticky = false,
                Detonation = DetonationMode.Impact,

                Origin = origin,
                Forward = Vector3.up,
                HSpreadDeg = 360f,
                VSpreadDeg = 360f,

                BlastRadius = 30f * r,
                BlastForce = 12f,
                BlastUpward = 4f,

                OverpressureRadius = 25f * r,
                OverpressureFalloffExp = 1f,
                OverpressureWoundPoints = 12,   // fewer than the JDAMs' 30: limbs torn this far out are many, and most are blown apart anyway
                SurfaceBurstScaledHeight = Mathf.Max(0f, Config.MoabSurfaceBurstHeight),

                FragTargeted = Mathf.Max(0, Config.MoabFragments),
                FragRayCount = Mathf.Max(0, Config.MoabWorldRays),
                MaxWalksPerLimb = Mathf.Max(0, Config.MoabWalksPerLimb),
                FragBeltDeg = Mathf.Clamp(Config.MoabFragBeltDeg, 0f, 180f),
                FragBeltShare = Mathf.Clamp01(Config.MoabFragBeltShare),
                FragSpeed = 200f,
                FragMaxTime = 6f,
                FragImpulse = 0.3f,
                FragPowerFalloff = 0.02f,       // 5 % of its power left at 150 m
                FragPenetrationScale = 0.35f,   // aluminium: about a third of steel's density

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = 0f,
                DamageScale = Config.MoabDamageScale,

                MaxWounds = Mathf.Max(0, Config.MoabMaxWounds),
                MaxPushRange = Mathf.Max(10f, Config.MoabPushRange),
                MaxInjuryRange = Mathf.Max(10f, Config.MoabInjuryRange),
            };
        }

        /// <summary>
        /// One BLU-97/B combined effects bomb going off where it lands (FireSupport/AirStrike.Cluster.cs):
        /// 287 g of cyclotol (~0.35 kg TNT of blast) behind a copper cone, in a scored steel case
        /// that breaks into ~300 fragments, with a zirconium ring. Three effects at once: the jet
        /// punches down along its fall (over 200 mm of armour; through a roof into the room
        /// under it), the case's fragments fly out square to it, lethal to about 20 m, and the
        /// zirconium sets sparks flying (the effect only). The caller sets Forward (and Axis) to the
        /// bomblet's fall.
        /// </summary>
        public static ExplosionParams FromBlu97Config(Vector3 origin)
        {
            return new ExplosionParams
            {
                Kind = "Blu97",
                FragPower = Config.Blu97FragPower,
                ChargeKgTNT = Config.Blu97ChargeKgTNT,
                Sticky = false,
                Detonation = DetonationMode.Impact,

                Origin = origin,
                Forward = Vector3.down,
                HSpreadDeg = 360f,
                VSpreadDeg = 360f,

                BlastRadius = 4f,
                BlastForce = 3f,
                BlastUpward = 1f,

                OverpressureRadius = 5f,
                OverpressureFalloffExp = 1f,
                OverpressureWoundPoints = 6,

                FragTargeted = Mathf.Max(0, Config.Blu97Fragments),
                FragRayCount = Mathf.Max(0, Config.Blu97WorldRays),
                MaxWalksPerLimb = Mathf.Max(0, Config.Blu97WalksPerLimb),
                FragBeltDeg = Mathf.Clamp(Config.Blu97FragBeltDeg, 0f, 180f),
                FragBeltShare = Mathf.Clamp01(Config.Blu97FragBeltShare),
                FragSpeed = 120f,
                FragMaxTime = 2f,
                FragImpulse = 0.2f,
                FragPowerFalloff = 0.07f,       // 5 % of its power left at about 43 m, a quarter at 20 m

                // The shaped charge, down the fall (FruitLib 5.10.0: a jet on a full-sphere spec).
                JetRays = Mathf.Max(0, Config.Blu97JetRays),
                JetConeDeg = 2f,
                JetPenetration = Mathf.Max(0f, Config.Blu97JetPenetration),
                JetPower = Config.Blu97JetPower,
                JetSpallCount = Mathf.Max(0, Config.Blu97JetSpallCount),

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = 0f,
                DamageScale = Config.Blu97DamageScale,
                MaxWounds = Mathf.Max(0, Config.Blu97MaxWounds),
            };
        }

        private static ExplosionParams GunBurst(string kind, Vector3 origin, float charge, float damage, float push,
                                                int frags, int fragPower, float kick, int opPoints,
                                                float blastRadius, float blastForce, float blastUpward, float opRadius,
                                                float fragSpeed, float fragMaxTime)
        {
            return new ExplosionParams
            {
                Kind = kind,
                FragPower = fragPower,
                ChargeKgTNT = charge,
                PushScale = push,
                Sticky = false,
                Detonation = DetonationMode.Impact,

                Origin = origin,
                Forward = Vector3.up,
                HSpreadDeg = 360f,
                VSpreadDeg = 360f,

                BlastRadius = blastRadius,
                BlastForce = blastForce,
                BlastUpward = blastUpward,

                OverpressureRadius = opRadius,
                OverpressureFalloffExp = 1f,
                OverpressureWoundPoints = opPoints,

                FragRayCount = frags,
                FragSpeed = fragSpeed,
                FragMaxTime = fragMaxTime,
                FragImpulse = kick,

                ArcSteps = Config.ArcDebugSteps,
                DebrisRaysRatio = 0f,
                DamageScale = damage,
            };
        }

    }
}
