using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BusSim.Obstacles;
using BusSim.Spawning;
using BusSim.TestRig;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>
    /// Acceptance-test tool. In Play mode, steps physics, the test bus, its autopilot and the
    /// spawner in lockstep (no render loop needed) and reports per-event behaviour: trigger time
    /// to arrival, time on the carriageway, closest approach and contacts with the bus.
    /// </summary>
    public static class SimulationHarness
    {
        private const float BusHalfWidth = 1.25f;
        private const float BusLength = 12f;
        private const float StoppedKmh = 0.5f;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private sealed class EventStats
        {
            public SpawnEvent Event;
            public bool Triggered;
            public float TriggerTimeToArrival = float.PositiveInfinity;
            public float SpeedAtTriggerKmh;
            public float LongestBlockSeconds;
            public float MinGapInPath = float.PositiveInfinity;
            public int ContactSteps;
        }

        /// <summary>
        /// Same as Run but scheduled on the next editor update, with the report written to the
        /// console. Lets a remote caller return immediately instead of waiting for a long drive.
        /// </summary>
        public static void RunDeferred(DifficultyProfile profile, int seed, bool brakeForObstacles, float maxSeconds, bool listEvents, string label)
        {
            UnityEditor.EditorApplication.delayCall += () =>
                Debug.Log($"[Harness {label}]\n{Run(profile, seed, brakeForObstacles, maxSeconds, listEvents)}");
        }

        /// <summary>Profile built in memory from definition ids, for single-type acceptance runs.</summary>
        public static DifficultyProfile MakeProfile(string profileName, float eventsPerKm, int level, params string[] ids)
        {
            DifficultyProfile profile = ScriptableObject.CreateInstance<DifficultyProfile>();
            profile.profileName = profileName;
            profile.eventsPerKm = eventsPerKm;
            profile.difficultyLevel = level;
            foreach (string id in ids)
            {
                ObstacleDefinition definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ObstacleDefinition>(
                    $"{ObstacleContentBuilder.ObstacleDataFolder}/{id}.asset");
                if (definition != null)
                {
                    profile.definitions.Add(definition);
                }
            }
            return profile;
        }

        /// <summary>
        /// Restarts the spawner with this profile and seed, drives the whole road and returns a report.
        /// </summary>
        public static string Run(DifficultyProfile profile, int seed, bool brakeForObstacles, float maxSeconds, bool listEvents)
        {
            if (!Application.isPlaying)
            {
                return "Enter Play mode first.";
            }

            TestBusController bus = Object.FindAnyObjectByType<TestBusController>();
            TestBusAutopilot autopilot = bus.GetComponent<TestBusAutopilot>();
            Rigidbody body = bus.GetComponent<Rigidbody>();
            ObstacleSpawner spawner = Object.FindAnyObjectByType<ObstacleSpawner>();
            MethodInfo busStep = typeof(TestBusController).GetMethod("FixedUpdate", Any);
            MethodInfo autopilotStep = typeof(TestBusAutopilot).GetMethod("FixedUpdate", Any);

            ResetBus(spawner, body);
            spawner.ResetRun(seed, profile);

            Dictionary<int, EventStats> stats = new Dictionary<int, EventStats>();
            List<ObstacleBehaviour> active = new List<ObstacleBehaviour>();
            System.Action<SpawnEvent, ObstacleBehaviour> onActivated = (spawnEvent, behaviour) =>
            {
                stats[spawnEvent.EventIndex] = new EventStats { Event = spawnEvent };
                active.Add(behaviour);
            };
            System.Action<SpawnEvent, ObstacleBehaviour> onReleased = (spawnEvent, behaviour) =>
            {
                Capture(stats[spawnEvent.EventIndex], behaviour);
                active.Remove(behaviour);
            };
            spawner.ObstacleActivated += onActivated;
            spawner.ObstacleReleased += onReleased;

            SimulationMode previousMode = Physics.simulationMode;
            RigidbodyInterpolation previousInterpolation = body.interpolation;
            Physics.simulationMode = SimulationMode.Script;
            body.interpolation = RigidbodyInterpolation.None;
            float dt = Time.fixedDeltaTime;
            int steps = 0;
            int brakingSteps = 0;
            try
            {
                autopilot.enabled = true;
                autopilot.SetBrakeForObstacles(brakeForObstacles);
                int maxSteps = Mathf.CeilToInt(maxSeconds / dt);
                for (steps = 1; steps <= maxSteps; steps++)
                {
                    autopilotStep.Invoke(autopilot, null);
                    busStep.Invoke(bus, null);
                    Physics.Simulate(dt);
                    spawner.Step(dt);
                    if (autopilot.BrakingForObstacle)
                    {
                        brakingSteps++;
                    }

                    foreach (ObstacleBehaviour behaviour in active)
                    {
                        EventStats entry = stats[behaviour.Event.EventIndex];
                        Capture(entry, behaviour);
                        if (entry.Triggered && entry.SpeedAtTriggerKmh <= 0f)
                        {
                            entry.SpeedAtTriggerKmh = spawner.VehicleSpeed * 3.6f;
                        }
                        Measure(entry, behaviour, spawner);
                    }

                    if (autopilot.Finished && bus.SpeedKmh < StoppedKmh)
                    {
                        break;
                    }
                }
            }
            finally
            {
                autopilot.enabled = false;
                bus.ClearInputOverride();
                body.interpolation = previousInterpolation;
                Physics.simulationMode = previousMode;
                spawner.ObstacleActivated -= onActivated;
                spawner.ObstacleReleased -= onReleased;
            }

            return Report(spawner, autopilot, stats, steps * dt, brakingSteps * dt, listEvents);
        }

        private static void ResetBus(ObstacleSpawner spawner, Rigidbody body)
        {
            const float startS = BusLength * 0.5f + 2f;
            float laneT = spawner.Road.Settings.GetLaneCentreT(0);
            spawner.Road.GetFrame(startS, out Vector3 centre, out Vector3 forward, out Vector3 right);
            Vector3 position = centre + right * laneT + Vector3.up * 0.05f;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            body.position = position;
            body.rotation = rotation;
            body.transform.SetPositionAndRotation(position, rotation);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private static void Capture(EventStats entry, ObstacleBehaviour behaviour)
        {
            if (behaviour.HasTriggered && !entry.Triggered)
            {
                entry.Triggered = true;
                entry.TriggerTimeToArrival = behaviour.TriggerTimeToArrival;
            }
            entry.LongestBlockSeconds = Mathf.Max(entry.LongestBlockSeconds, behaviour.LongestBlockSeconds);
        }

        private static void Measure(EventStats entry, ObstacleBehaviour behaviour, ObstacleSpawner spawner)
        {
            (float s, float t) = spawner.Road.ProjectToRoad(behaviour.transform.position);
            float halfWidth = entry.Event.Definition.footprintWidth * 0.5f;
            float halfLength = entry.Event.Definition.footprintLength * 0.5f;
            bool lateralOverlap = Mathf.Abs(t - spawner.VehicleT) < BusHalfWidth + halfWidth;
            if (!lateralOverlap)
            {
                return;
            }

            float gap = (s - halfLength) - spawner.VehicleFrontS;
            float busRearS = spawner.VehicleFrontS - BusLength;
            if (s + halfLength > busRearS)
            {
                entry.MinGapInPath = Mathf.Min(entry.MinGapInPath, gap);
                if (gap < 0f)
                {
                    entry.ContactSteps++;
                }
            }
        }

        private static string Report(ObstacleSpawner spawner, TestBusAutopilot autopilot, Dictionary<int, EventStats> stats,
            float simulatedSeconds, float brakingSeconds, bool listEvents)
        {
            int contacts = 0;
            int blockViolations = 0;
            int dynamicCount = 0;
            int triggered = 0;
            foreach (EventStats entry in stats.Values)
            {
                if (entry.ContactSteps > 0)
                {
                    contacts++;
                }
                if (entry.Event.Definition.isDynamic)
                {
                    dynamicCount++;
                    if (entry.Triggered)
                    {
                        triggered++;
                    }
                    if (entry.LongestBlockSeconds > entry.Event.Definition.maxBlockSeconds)
                    {
                        blockViolations++;
                    }
                }
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Plan: {spawner.Plan.Summary()}");
            builder.AppendLine($"Drive: bus reached s={autopilot.BusS:F0} in {simulatedSeconds:F1}s simulated, braking for obstacles {brakingSeconds:F1}s, max |t| {autopilot.MaxAbsT:F2}");
            builder.AppendLine($"Result: activated {stats.Count}, dynamic {dynamicCount} (triggered {triggered}), obstacles touched by bus {contacts}, maxBlockSeconds violations {blockViolations}");
            if (listEvents)
            {
                List<int> keys = new List<int>(stats.Keys);
                keys.Sort();
                foreach (int key in keys)
                {
                    EventStats entry = stats[key];
                    string gap = float.IsPositiveInfinity(entry.MinGapInPath) ? "never in path" : $"{entry.MinGapInPath:F1} m";
                    builder.Append($"  #{key} {entry.Event.Definition.id} s={entry.Event.S:F0} t={entry.Event.T:F2}");
                    if (entry.Event.Definition.isDynamic)
                    {
                        builder.Append($" TTA@trigger={entry.TriggerTimeToArrival:F2}s speed@trigger={entry.SpeedAtTriggerKmh:F1}km/h longestBlock={entry.LongestBlockSeconds:F2}s/{entry.Event.Definition.maxBlockSeconds}s");
                    }
                    builder.AppendLine($" minGapInPath={gap} contactSteps={entry.ContactSteps}");
                }
            }
            return builder.ToString();
        }
    }
}
