using System;
using System.Collections.Generic;
using BusSim.Obstacles;
using BusSim.Road;
using UnityEngine;

namespace BusSim.Spawning
{
    /// <summary>
    /// Plans the run from the seed at start, then activates pooled obstacles as the vehicle comes
    /// within spawnAhead, ticks them and releases them once the vehicle is despawnBehind past.
    /// Knows the vehicle only as a Transform, so it works with any vehicle.
    /// </summary>
    public class ObstacleSpawner : MonoBehaviour, IVehicleState, ICollisionSink
    {
        private struct ActiveObstacle
        {
            public SpawnEvent Event;
            public GameObject Instance;
            public ObstacleBehaviour Behaviour;
        }

        private const int CollisionCapacity = 64;

        [Header("Scene references")]
        [SerializeField] private RoadSampler road;
        [SerializeField] private Transform vehicle;
        [SerializeField] private DifficultyProfile profile;
        [Tooltip("Optional. Zone-only obstacle types need zones.")]
        [SerializeField] private RoadZones zones;

        [Header("Seed")]
        [SerializeField] private int seed = 12345;
        [Tooltip("Pick a new seed from the clock each run. The seed is always logged.")]
        [SerializeField] private bool randomSeedEachRun = true;

        [Header("Activation (metres)")]
        [SerializeField, Min(1f)] private float spawnAhead = 150f;
        [SerializeField, Min(0f)] private float despawnBehind = 50f;
        [Tooltip("Distance from the vehicle pivot to its front bumper.")]
        [SerializeField, Min(0f)] private float vehicleFrontOffset = 2.1f;
        [Tooltip("Higher values follow speed changes faster.")]
        [SerializeField, Min(0.1f)] private float speedSmoothingRate = 5f;

        [Header("Clearance (FR3)")]
        [SerializeField, Min(0.1f)] private float minClearCorridor = 2.5f;
        [SerializeField, Min(0f)] private float clearanceWindowMargin = 15f;
        [SerializeField, Min(1)] private int maxPlacementAttempts = 8;

        [Header("Pool")]
        [SerializeField, Min(0)] private int prewarmPerPrefab = 3;

        private readonly List<ActiveObstacle> active = new List<ActiveObstacle>();
        private readonly List<CollisionRecord> collisions = new List<CollisionRecord>(CollisionCapacity);
        private readonly SpawnPlanner planner = new SpawnPlanner();
        private ObjectPool pool;
        private int nextEventIndex;
        private float previousVehicleS;
        private bool hasVehicleSample;

        public event Action<SpawnPlan> PlanBuilt;
        public event Action<SpawnEvent, ObstacleBehaviour> ObstacleActivated;
        public event Action<SpawnEvent, ObstacleBehaviour> ObstacleReleased;
        public event Action<CollisionRecord> ObstacleHit;

        public SpawnPlan Plan { get; private set; }
        public RoadSampler Road => road;
        public DifficultyProfile Profile => profile;
        public int Seed => seed;
        public float SpawnAhead => spawnAhead;
        public float DespawnBehind => despawnBehind;
        public float MinClearCorridor => minClearCorridor;
        public float ClearanceWindowMargin => clearanceWindowMargin;
        public int ActiveCount => active.Count;

        /// <summary>How many obstacle instances the pool has ever created. Stays flat when pooling works.</summary>
        public int PooledInstanceCount => pool != null ? pool.CreatedCount : 0;

        /// <summary>Footprints of the static obstacles currently in the world, so ambient traffic can queue behind them.</summary>
        public void CollectStaticFootprints(List<Footprint> into)
        {
            into.Clear();
            foreach (ActiveObstacle entry in active)
            {
                if (!entry.Event.Definition.isDynamic && !entry.Behaviour.WasHit)
                {
                    into.Add(entry.Event.Footprint);
                }
            }
        }

        /// <summary>Every vehicle-obstacle collision this run, in order.</summary>
        public IReadOnlyList<CollisionRecord> Collisions => collisions;

        public void OnCollision(in CollisionRecord record)
        {
            collisions.Add(record);
            ObstacleHit?.Invoke(record);
        }

        public Transform VehicleTransform => vehicle;
        public float VehicleS { get; private set; }
        public float VehicleFrontS => VehicleS + vehicleFrontOffset;
        public float VehicleT { get; private set; }
        public float VehicleSpeed { get; private set; }

        public void Configure(RoadSampler roadSampler, Transform vehicleTransform, DifficultyProfile difficulty, RoadZones roadZones)
        {
            road = roadSampler;
            vehicle = vehicleTransform;
            profile = difficulty;
            zones = roadZones;
        }

        /// <summary>Sets the vehicle's front offset and the narrowest corridor the planner may leave.</summary>
        public void SetVehicleGeometry(float frontOffset, float requiredCorridor)
        {
            vehicleFrontOffset = frontOffset;
            minClearCorridor = requiredCorridor;
        }

        public void SetSeed(int newSeed, bool randomEachRun)
        {
            seed = newSeed;
            randomSeedEachRun = randomEachRun;
        }

        public void SetProfile(DifficultyProfile difficulty)
        {
            profile = difficulty;
        }

        /// <summary>Builds the plan for a seed. Works in edit mode for gizmo previews and tests.</summary>
        public SpawnPlan BuildPlan(int planSeed)
        {
            SpawnPlanner.Settings settings = new SpawnPlanner.Settings
            {
                RoadLength = road.Length,
                RoadWidth = road.Settings.RoadWidth,
                MinCorridor = minClearCorridor,
                WindowMargin = clearanceWindowMargin,
                MaxAttempts = maxPlacementAttempts
            };
            Plan = planner.Plan(planSeed, profile, zones != null ? zones.Zones : null, settings);
            return Plan;
        }

        /// <summary>Releases every active obstacle and starts a fresh run with this seed and profile.</summary>
        public void ResetRun(int newSeed, DifficultyProfile difficulty)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Release(i);
            }
            if (difficulty != null)
            {
                profile = difficulty;
            }
            seed = newSeed;
            randomSeedEachRun = false;
            hasVehicleSample = false;
            VehicleSpeed = 0f;
            Start();
        }

        [ContextMenu("Preview Plan (current seed)")]
        public void PreviewPlan()
        {
            BuildPlan(seed);
            Debug.Log($"BusSim spawner preview: {Plan.Summary()}", this);
        }

        private void Start()
        {
            if (road == null || profile == null)
            {
                Debug.LogError("BusSim spawner: road and difficulty profile must be assigned.", this);
                enabled = false;
                return;
            }

            PhysicsSetup.Apply();
            collisions.Clear();
            if (randomSeedEachRun)
            {
                seed = Environment.TickCount & int.MaxValue;
            }

            BuildPlan(seed);
            Debug.Log($"BusSim run: profile {profile.profileName}, {Plan.Summary()}", this);

            pool?.Clear();
            pool = new ObjectPool(transform);
            PrewarmPool();
            nextEventIndex = 0;
            PlanBuilt?.Invoke(Plan);
        }

        private void PrewarmPool()
        {
            foreach (SpawnEvent spawnEvent in Plan.Events)
            {
                pool.Prewarm(spawnEvent.Definition.prefab, prewarmPerPrefab);
            }
        }

        private void FixedUpdate()
        {
            Step(Time.fixedDeltaTime);
        }

        /// <summary>One spawner update. Public so tests and tools can drive it without a render loop.</summary>
        public void Step(float deltaTime)
        {
            if (Plan == null || vehicle == null || pool == null)
            {
                return;
            }

            UpdateVehicleState(deltaTime);
            ActivateUpcoming();

            for (int i = active.Count - 1; i >= 0; i--)
            {
                ActiveObstacle entry = active[i];
                entry.Behaviour.Tick(deltaTime);

                float passedS = entry.Behaviour.CurrentS + entry.Event.Definition.footprintLength * 0.5f;
                bool passed = VehicleS - despawnBehind > passedS;
                if (passed || entry.Behaviour.IsFinished || entry.Behaviour.IsSettledOrLost)
                {
                    Release(i);
                }
            }
        }

        private void UpdateVehicleState(float deltaTime)
        {
            (float s, float t) = road.ProjectToRoad(vehicle.position);
            if (hasVehicleSample && deltaTime > 0f)
            {
                float rawSpeed = (s - previousVehicleS) / deltaTime;
                float blend = 1f - Mathf.Exp(-speedSmoothingRate * deltaTime);
                VehicleSpeed = Mathf.Lerp(VehicleSpeed, rawSpeed, blend);
            }
            previousVehicleS = s;
            hasVehicleSample = true;
            VehicleS = s;
            VehicleT = t;
        }

        private void ActivateUpcoming()
        {
            List<SpawnEvent> events = Plan.Events;
            while (nextEventIndex < events.Count && VehicleS + spawnAhead >= events[nextEventIndex].S)
            {
                SpawnEvent spawnEvent = events[nextEventIndex];
                nextEventIndex++;

                float endS = spawnEvent.S + spawnEvent.Definition.footprintLength * 0.5f;
                if (VehicleS - despawnBehind > endS)
                {
                    continue; // already behind the vehicle, for example after a teleport
                }

                ActivateEvent(spawnEvent);
            }
        }

        /// <summary>Activates one event straight away, outside the plan. For tests and tools.</summary>
        public ObstacleBehaviour ForceSpawn(SpawnEvent spawnEvent)
        {
            return ActivateEvent(spawnEvent);
        }

        private ObstacleBehaviour ActivateEvent(SpawnEvent spawnEvent)
        {
            GameObject instance = pool.Get(spawnEvent.Definition.prefab);
            ObstacleBehaviour behaviour = instance.GetComponent<ObstacleBehaviour>();
            if (behaviour == null)
            {
                Debug.LogError($"BusSim: prefab of {spawnEvent.Definition.id} has no ObstacleBehaviour.", spawnEvent.Definition.prefab);
                pool.Release(spawnEvent.Definition.prefab, instance);
                return null;
            }

            behaviour.Init(new ObstacleContext(road, this, spawnEvent, this));
            behaviour.Activate();
            active.Add(new ActiveObstacle { Event = spawnEvent, Instance = instance, Behaviour = behaviour });
            ObstacleActivated?.Invoke(spawnEvent, behaviour);
            return behaviour;
        }

        private void Release(int index)
        {
            ActiveObstacle entry = active[index];
            entry.Behaviour.Finish();
            ObstacleReleased?.Invoke(entry.Event, entry.Behaviour);
            pool.Release(entry.Event.Definition.prefab, entry.Instance);
            active.RemoveAt(index);
        }
    }
}
