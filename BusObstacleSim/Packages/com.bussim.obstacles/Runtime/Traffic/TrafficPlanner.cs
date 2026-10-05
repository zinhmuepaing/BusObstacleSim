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

        public static List<TrafficSpawn> Plan(int runSeed, TrafficSettings settings, float roadLength, float junctionS, bool hasJunction, int modelCount)
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

            if (hasJunction)
            {
                for (int i = 0; i < settings.sideRoadEntries; i++)
                {
                    float release = junctionS - Lerp(settings.entryReleaseMin, settings.entryReleaseMax, rng.NextDouble());
                    plan.Add(new TrafficSpawn
                    {
                        Kind = TrafficKind.SideRoadEntry,
                        S = Math.Max(from, release),
                        Lane = 0,
                        SpeedMetresPerSecond = settings.sideRoadKmh * KmhToMetresPerSecond,
                        ModelIndex = rng.Next(modelCount),
                        AcceptedGapSeconds = Lerp(settings.acceptedGapMin, settings.acceptedGapMax, rng.NextDouble())
                    });
                }
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

                    TrafficSpawn spawn = new TrafficSpawn
                    {
                        Kind = kind,
                        S = s,
                        Lane = lane,
                        SpeedMetresPerSecond = Lerp(minKmh, maxKmh, rng.NextDouble()) * KmhToMetresPerSecond,
                        ModelIndex = rng.Next(modelCount)
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
