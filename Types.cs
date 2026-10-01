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
    }

    /// <summary>The Javelin's two attacks. The AT-4 has none: it flies where it is pointed.</summary>
    public enum AttackMode
    {
        Top,
        Direct
    }

    public enum WarheadMode { HEAT, HE }

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
        public string MeshName = null;
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
        /// <summary>Shaped-charge jet (FruitBallistics 5.4): rays, full cone angle, free metres through walls.</summary>
        public int JetRays = 0;
        public float JetConeDeg = 3f;
        public float JetPenetration = 0f;
        public int JetPower = 0;
        public int JetSpallCount = 0;
        public int ArcSteps = 12;
        public float DebrisRaysRatio = 0.04f;

        public static ExplosionParams FromGrenadeConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
                Kind = "Grenade",
                FragPower = Config.FragPower,
                ChargeKgTNT = Config.ChargeKgTNT,
                MeshName = "TAG19_mesh",
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
                MeshName = UnityEngine.Random.Range(1, 10000000) == 1 ? "Car46_mesh" : "C4_mesh",
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
                MeshName = "Claymore_mesh",
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
                MeshName = "Javelin_mesh",
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
                MeshName = "Javelin_mesh",
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

    }
}
