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
        public bool Stuck;
        public bool Armed;
        public float ProxScanAccum;
        public bool RemoteTriggered;
        public bool HasAlignTarget;
        public Quaternion AlignTargetRot;
        public Vector3 ThrowDir;
        public LineRenderer[] SightLines;
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
        public float LaunchY;
        public float CruiseAlt;            
        public ExplosionParams Params;
    }

    public enum AttackMode
    {
        Top,
        Direct,
        Unguided
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
        public string MeshName = null;
        public bool Sticky = false;
        public DetonationMode Detonation = DetonationMode.Timer;
        public float FuseTime = 2f;
        public float FlashTime = 2f;
        public KeyCode RemoteKey = Config.DetonateKey;
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
        public int ArcSteps = 12;
        public float FragWoundDelay = 0.15f;
        public float DebrisRaysRatio = 0.04f;
        public bool CameraFX = true;
        public bool IsFullSphere => HSpreadDeg >= 360f && VSpreadDeg >= 360f;

        public static ExplosionParams FromGrenadeConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
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
                FragWoundDelay = Config.FragWoundDelay,

                DebrisRaysRatio = Config.DebrisRaysRatio,
                CameraFX = Config.CamFXEnabled,
                DamageScale = Config.DamageScale,
            };
        }

        public static ExplosionParams FromC4Config(Vector3 origin)
        {
            return new ExplosionParams
            {
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
                FragWoundDelay = Config.FragWoundDelay,
                DebrisRaysRatio = Config.DebrisRaysRatio,
                CameraFX = Config.CamFXEnabled,
                DamageScale = Config.C4DamageScale,
            };
        }

        public static ExplosionParams FromClaymoreConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
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
                FragWoundDelay = Config.FragWoundDelay,
                DebrisRaysRatio = Config.DebrisRaysRatio,
                CameraFX = Config.CamFXEnabled,
                DamageScale = Config.MineDamageScale,
            };
        }

        public static ExplosionParams FromMissileConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
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
                FragWoundDelay = Config.FragWoundDelay,
                DebrisRaysRatio = Config.DebrisRaysRatio,
                CameraFX = Config.CamFXEnabled,
                DamageScale = Config.MissileDamageScale,
            };
        }

        public static ExplosionParams FromMissileHEConfig(Vector3 origin)
        {
            return new ExplosionParams
            {
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
                FragWoundDelay = Config.FragWoundDelay,
                DebrisRaysRatio = Config.DebrisRaysRatio,
                CameraFX = Config.CamFXEnabled,
                DamageScale = Config.MissileHEDamageScale,
            };
        }

    }
}
