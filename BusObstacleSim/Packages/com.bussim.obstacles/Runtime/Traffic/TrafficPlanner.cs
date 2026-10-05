using System;
using System.Collections.Generic;

namespace BusSim.Traffic
{
    /// <summary>
    /// Plans all ambient traffic for a run from the run seed, with its own random stream so that
    /// switching traffic on never changes the obstacle plan. Pure C#, no scene access (FR7).
    /// </summary>
    public static class TrafficPlanner
    {
        private const int StreamMix = 0x5EED7AF;
        private const float KmhToMetresPerSecond = 1f / 3.6f;
        private const int Lanes = 2;
        private const int PlacementAttempts = 12;
        private const float ChaserJitter = 0.6f;

        /// <summary>Single-junction convenience overload.</summary>
        public static List<TrafficSpawn> Plan(int runSeed, TrafficSettings settings, float roadLength, float junctionS, bool hasJunction, int modelCount)
        {
            return Plan(runSeed, settings, roadLength, hasJunction ? new[] { junctionS } : new float[0], modelCount);
        }

        public static List<TrafficSpawn> Plan(int runSeed, TrafficSettings settings, float roadLength, IReadOnlyList<float> junctions, int modelCount)
        {
            List<TrafficSpawn> plan = new List<TrafficSpawn>();
            if (settings == null || modelCount <= 0)
            {
                return plan;
            }

            Random rng = new Random(runSeed ^ StreamMix);
            float from = settings.startBuffer;
            float to = roadLength - settings.endBuffer;
            float span = Math.Max(0f, to - from);

            AddFlow(plan, rng, settings, TrafficKind.SameDirection, settings.sameDirectionPerKm, span, from,
                settings.sameDirectionMinKmh, settings.sameDirectionMaxKmh, modelCount);
            AddFlow(plan, rng, settings, TrafficKind.Oncoming, settings.oncomingPerKm, span, from,
                settings.oncomingMinKmh, settings.oncomingMaxKmh, modelCount);

            for (int j = 0; j < junctions.Count; j++)
            {
                for (int i = 0; i < settings.sideRoadEntries; i++)
                {
                    float release = junctions[j] - Lerp(settings.entryReleaseMin, settings.entryReleaseMax, rng.NextDouble());
                    plan.Add(new TrafficSpawn
                    {
                        Kind = TrafficKind.SideRoadEntry,
                        S = Math.Max(from, release),
                        Lane = 0,
                        SpeedMetresPerSecond = settings.sideRoadKmh * KmhToMetresPerSecond,
                        ModelIndex = rng.Next(modelCount),
                        AcceptedGapSeconds = Lerp(settings.acceptedGapMin, settings.acceptedGapMax, rng.NextDouble()),
                        JunctionIndex = j
                    });
                }
            }

            // Chasers: released behind the driven vehicle as it passes their s, evenly spread over the run.
            for (int i = 0; i < settings.chasers; i++)
            {
                float slot = (i + 0.5f + (float)(rng.NextDouble() - 0.5) * ChaserJitter) / Math.Max(settings.chasers, 1);
                plan.Add(new TrafficSpawn
                {
                    Kind = TrafficKind.Chaser,
                    S = from + slot * span,
                    Lane = rng.Next(Lanes),
                    SpeedMetresPerSecond = Lerp(settings.aggressiveMinKmh, settings.aggressiveMaxKmh, rng.NextDouble()) * KmhToMetresPerSecond,
                    ModelIndex = rng.Next(modelCount),
                    Aggressive = true
                });
            }

            plan.Sort((a, b) => a.S.CompareTo(b.S));
            for (int i = 0; i < plan.Count; i++)
            {
                TrafficSpawn spawn = plan[i];
                spawn.Index = i;
                plan[i] = spawn;
            }
            return plan;
        }

        private static void AddFlow(List<TrafficSpawn> plan, Random rng, TrafficSettings settings, TrafficKind kind,
            float perKm, float span, float from, float minKmh, float maxKmh, int modelCount)
        {
            int count = (int)Math.Round(perKm * span / 1000f);
            List<TrafficSpawn> placed = new List<TrafficSpawn>();
            for (int i = 0; i < count; i++)
            {
                for (int attempt = 0; attempt < PlacementAttempts; attempt++)
                {
                    float s = from + (float)rng.NextDouble() * span;
                    int lane = rng.Next(Lanes);
                    if (TooClose(placed, s, lane, settings.minSpacing))
                    {
                        continue;
                    }

                    bool aggressive = rng.NextDouble() < settings.aggressiveShare;
                    float speedKmh = aggressive
                        ? Lerp(settings.aggressiveMinKmh, settings.aggressiveMaxKmh, rng.NextDouble())
                        : Lerp(minKmh, maxKmh, rng.NextDouble());
                    TrafficSpawn spawn = new TrafficSpawn
                    {
                        Kind = kind,
                        S = s,
                        Lane = lane,
                        SpeedMetresPerSecond = speedKmh * KmhToMetresPerSecond,
                        ModelIndex = rng.Next(modelCount),
                        Aggressive = aggressive
                    };
                    placed.Add(spawn);
                    break;
                }
            }
            plan.AddRange(placed);
        }

        private static bool TooClose(List<TrafficSpawn> placed, float s, int lane, float spacing)
        {
            foreach (TrafficSpawn other in placed)
            {
                if (other.Lane == lane && Math.Abs(other.S - s) < spacing)
                {
                    return true;
                }
            }
            return false;
        }

        private static float Lerp(float a, float b, double t)
        {
            return a + (b - a) * (float)t;
        }
    }
}
