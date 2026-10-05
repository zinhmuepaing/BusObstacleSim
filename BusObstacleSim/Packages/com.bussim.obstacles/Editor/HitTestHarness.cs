using System.Collections.Generic;
using System.Text;
using BusSim.Obstacles;
using BusSim.Spawning;
using BusSim.Vehicle;
using UnityEditor;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>
    /// M8b acceptance tool. In Play mode, drives the car into one obstacle at a chosen speed with
    /// physics stepped by hand, and reports whether the obstacle was hit, logged exactly once per body,
    /// moved, and whether the car got through. Results go to the console.
    /// </summary>
    public static class HitTestHarness
    {
        private const float CarStartS = 10f;
        private const float ObstacleS = 150f;
        private const float EndAfterObstacle = 25f;
        private const float RunOnAfterHitSeconds = 3f;
        private const float MaxCaseSeconds = 40f;
        private const float StartClearance = 0.1f;
        private const float MovedThreshold = 0.5f;
        private const float ToppledUpright = 0.7f;
        private const float KmhPerMetrePerSecond = 3.6f;
        // A 2.2 tonne handbraked van, hit by a braking car at 20 km/h, slides about 0.15 m. Visibly shifted is enough.
        private const float HeavyMinMove = 0.1f;
        // The cyclist rides away at 4 m/s, so it starts close to give the car time to catch it.
        private const float CyclistS = 60f;
        private const float TraceBefore = 6f;
        private const float TraceAfter = 6f;
        private const int TraceEvery = 3;
        private const int EventIndexBase = 900;
        private const int FixedSeed = 7;

        /// <summary>Set to an obstacle id to log car and body positions around the impact in that case.</summary>
        public static string TraceCaseId;

        public struct Case
        {
            public string Id;
            public float SpeedKmh;
            public float T;
            public float TriggerOverride;
            public float ObstacleS;
            public float MinMove;
            public bool SingleBody;
            public bool MustMove;
            public bool MustToppleOrMove;
        }

        private sealed class Result
        {
            public string Line;
            public bool Pass;
        }

        public static void RunSuiteDeferred(string label, float[] speeds)
        {
            EditorApplication.delayCall += () => HarnessLog.Write($"Hit suite {label}", RunSuite(speeds));
        }

        /// <summary>
        /// Pool soak: several full-speed runs back to back with collisions. Rigidbody count and pooled
        /// instance count must stop growing once the pool is warm, otherwise bodies are leaking.
        /// </summary>
        public static void RunSoakDeferred(int runs, float secondsPerRun)
        {
            EditorApplication.delayCall += () => HarnessLog.Write("Pool soak", RunSoak(runs, secondsPerRun));
        }

        public static string RunSoak(int runs, float secondsPerRun)
        {
            if (!Application.isPlaying)
            {
                return "Enter Play mode first.";
            }

            DifficultyProfile hard = AssetDatabase.LoadAssetAtPath<DifficultyProfile>($"{ObstacleContentBuilder.DifficultyFolder}/Hard.asset");
            StringBuilder report = new StringBuilder();
            List<int> bodyCounts = new List<int>();
            List<int> pooled = new List<int>();
            for (int i = 0; i < runs; i++)
            {
                string line = SimulationHarness.Run(hard, 100 + i, false, secondsPerRun, false);
                ObstacleSpawner spawner = Object.FindAnyObjectByType<ObstacleSpawner>();
                int bodies = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include).Length;
                bodyCounts.Add(bodies);
                pooled.Add(spawner.PooledInstanceCount);
                report.AppendLine($"run {i + 1}: rigidbodies in scene {bodies}, pooled instances {spawner.PooledInstanceCount}, collisions {spawner.Collisions.Count}");
            }

            // Counts may rise while new obstacle types are first met, so compare the last two thirds of the runs.
            int from = runs / 3;
            bool flatBodies = true;
            bool flatPool = true;
            for (int i = from + 1; i < runs; i++)
            {
                flatBodies &= bodyCounts[i] == bodyCounts[from];
                flatPool &= pooled[i] == pooled[from];
            }
            report.AppendLine($"SOAK RESULT: rigidbody count {(flatBodies ? "flat" : "GROWING")}, pooled instances {(flatPool ? "flat" : "GROWING")} -> {(flatBodies && flatPool ? "PASS" : "FAIL")}");
            return report.ToString();
        }

        public static string RunSuite(float[] speeds)
        {
            if (!Application.isPlaying)
            {
                return "Enter Play mode first.";
            }

            float lane = Object.FindAnyObjectByType<ObstacleSpawner>().Road.Settings.GetLaneCentreT(0);
            List<Case> cases = new List<Case>
            {
                new Case { Id = "CONE_CLUSTER", T = lane, MustMove = true },
                new Case { Id = "DEBRIS_BRANCH", T = lane, SingleBody = true, MustMove = true },
                new Case { Id = "DEBRIS_CARGO", T = lane, SingleBody = true, MustMove = true },
                new Case { Id = "STALLED_CAR", T = lane, SingleBody = true, MustMove = true, MinMove = HeavyMinMove },
                new Case { Id = "DOUBLE_PARKED_VAN", T = lane, SingleBody = true, MustMove = true, MinMove = HeavyMinMove },
                new Case { Id = "BUSSTOP_BLOCK", T = lane, SingleBody = true, MustMove = true, MinMove = HeavyMinMove },
                new Case { Id = "ROADWORK_BARRIER", T = lane },
                new Case { Id = "CYCLIST_EDGE", T = lane, SingleBody = true, MustToppleOrMove = true, ObstacleS = CyclistS },
                new Case { Id = "PED_JAYWALK_ADULT", T = -4.5f, TriggerOverride = 1.5f, SingleBody = true, MustToppleOrMove = true }
            };

            StringBuilder report = new StringBuilder();
            int passed = 0;
            int total = 0;
            foreach (float speed in speeds)
            {
                foreach (Case template in cases)
                {
                    Case c = template;
                    c.SpeedKmh = speed;
                    Result result = RunCase(c);
                    report.AppendLine(result.Line);
                    total++;
                    passed += result.Pass ? 1 : 0;
                }
            }
            report.AppendLine($"SUITE RESULT: {passed} of {total} cases passed.");
            return report.ToString();
        }

        private static Result RunCase(Case c)
        {
            CarController car = Object.FindAnyObjectByType<CarController>();
            CarAutopilot autopilot = car.GetComponent<CarAutopilot>();
            Rigidbody carBody = car.GetComponent<Rigidbody>();
            ObstacleSpawner spawner = Object.FindAnyObjectByType<ObstacleSpawner>();
            ObstacleDefinition definition = AssetDatabase.LoadAssetAtPath<ObstacleDefinition>(
                $"{ObstacleContentBuilder.ObstacleDataFolder}/{c.Id}.asset");
            if (c.TriggerOverride > 0f)
            {
                definition = Object.Instantiate(definition);
                definition.triggerTimeToArrival = c.TriggerOverride;
            }

            spawner.ResetRun(FixedSeed, SimulationHarness.MakeProfile("hit test", 1f, 2));
            PhysicsSetup.Apply();
            float laneT = spawner.Road.Settings.GetLaneCentreT(0);
            spawner.Road.GetFrame(CarStartS, out Vector3 centre, out Vector3 forward, out Vector3 right);
            car.PlaceAt(centre + right * laneT + Vector3.up * StartClearance, Quaternion.LookRotation(forward, Vector3.up));

            float obstacleS = c.ObstacleS > 0f ? c.ObstacleS : ObstacleS;
            float moveThreshold = c.MinMove > 0f ? c.MinMove : MovedThreshold;
            SpawnEvent spawnEvent = new SpawnEvent
            {
                Definition = definition, S = obstacleS, T = c.T, YawDegrees = 0f,
                EventIndex = EventIndexBase, Seed = FixedSeed
            };
            ObstacleBehaviour obstacle = spawner.ForceSpawn(spawnEvent);
            Rigidbody[] bodies = obstacle.GetComponentsInChildren<Rigidbody>(true);
            Vector3[] startPositions = new Vector3[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
            {
                startPositions[i] = bodies[i].position;
            }

            SimulationMode previousMode = Physics.simulationMode;
            RigidbodyInterpolation previousInterpolation = carBody.interpolation;
            Physics.simulationMode = SimulationMode.Script;
            carBody.interpolation = RigidbodyInterpolation.None;

            float dt = Time.fixedDeltaTime;
            float originalTargetKmh = autopilot.TargetSpeedKmh;
            float speedAtHit = 0f;
            float minSpeedAfter = float.PositiveInfinity;
            float firstHitTime = -1f;
            float carFrontS = 0f;
            float runTime = 0f;
            StringBuilder trace = c.Id == TraceCaseId ? new StringBuilder() : null;
            int traceStep = 0;
            try
            {
                autopilot.enabled = true;
                autopilot.SetBrakeForObstacles(false);
                autopilot.SetSteerAroundStatics(false); // these tests are meant to hit the obstacle
                autopilot.SetTargetSpeedKmh(c.SpeedKmh);
                int maxSteps = Mathf.CeilToInt(MaxCaseSeconds / dt);
                for (int step = 0; step < maxSteps; step++)
                {
                    spawner.Step(dt);
                    autopilot.Tick(dt);
                    car.Tick(dt);
                    Physics.Simulate(dt);
                    runTime += dt;

                    carFrontS = autopilot.BusS + car.FrontOffset;
                    if (trace != null && carFrontS > obstacleS - TraceBefore && carFrontS < obstacleS + TraceAfter && traceStep++ % TraceEvery == 0)
                    {
                        Vector3 bp = bodies.Length > 0 ? bodies[0].position : Vector3.zero;
                        trace.AppendLine($"  t={runTime:F2} carFront={carFrontS:F2} speed={car.SpeedKmh:F1} carY={carBody.position.y:F2} pitch={Mathf.Asin(car.transform.forward.y) * Mathf.Rad2Deg:F1} bodyPos=({bp.x:F2},{bp.y:F2},{bp.z:F2}) hits={spawner.Collisions.Count}");
                    }
                    if (spawner.Collisions.Count > 0 && firstHitTime < 0f)
                    {
                        firstHitTime = runTime;
                        speedAtHit = car.SpeedKmh;
                        autopilot.SetTargetSpeedKmh(0f); // the driver brakes after the impact
                        for (int i = 0; i < bodies.Length; i++)
                        {
                            startPositions[i] = bodies[i].position; // movement counts from the moment of impact
                        }
                    }
                    if (firstHitTime >= 0f)
                    {
                        minSpeedAfter = Mathf.Min(minSpeedAfter, car.SpeedKmh);
                        if (runTime - firstHitTime > RunOnAfterHitSeconds)
                        {
                            break;
                        }
                    }
                    if (carFrontS > obstacle.CurrentS + definition.footprintLength * 0.5f + EndAfterObstacle)
                    {
                        break;
                    }
                }
            }
            finally
            {
                autopilot.SetTargetSpeedKmh(originalTargetKmh);
                autopilot.SetBrakeForObstacles(true);
                autopilot.SetSteerAroundStatics(true);
                autopilot.enabled = false;
                car.ClearDrive();
                carBody.interpolation = previousInterpolation;
                Physics.simulationMode = previousMode;
            }

            float maxMove = 0f;
            for (int i = 0; i < bodies.Length; i++)
            {
                maxMove = Mathf.Max(maxMove, Vector3.Distance(bodies[i].position, startPositions[i]));
            }
            float upright = Vector3.Dot(obstacle.transform.up, Vector3.up);
            int hits = spawner.Collisions.Count;
            float halfLength = definition.footprintLength * 0.5f;
            bool carGotThrough = carFrontS > obstacle.CurrentS + halfLength;
            bool moved = maxMove > moveThreshold;
            bool toppled = upright < ToppledUpright;

            List<string> problems = new List<string>();
            if (hits == 0)
            {
                problems.Add("no hit logged");
            }
            if (c.SingleBody && hits != 1)
            {
                problems.Add($"expected exactly 1 collision record, got {hits}");
            }
            if (carGotThrough && !moved && !toppled)
            {
                problems.Add("car passed through without moving the obstacle");
            }
            if (c.MustMove && !moved)
            {
                problems.Add($"obstacle moved only {maxMove:F2} m");
            }
            if (c.MustToppleOrMove && !moved && !toppled)
            {
                problems.Add("obstacle neither displaced nor toppled");
            }

            string speedLoss = firstHitTime >= 0f ? $"{speedAtHit:F1} -> min {minSpeedAfter:F1} km/h" : "no hit";
            string verdict = problems.Count == 0 ? "PASS" : "FAIL (" + string.Join("; ", problems) + ")";
            return new Result
            {
                Pass = problems.Count == 0,
                Line = (trace != null ? trace.ToString() : string.Empty) + $"{c.Id} @ {c.SpeedKmh:F0} km/h: hits={hits} moved={maxMove:F2} m upright={upright:F2} carSpeed {speedLoss} carFront s={carFrontS:F0} -> {verdict}"
            };
        }
    }
}
