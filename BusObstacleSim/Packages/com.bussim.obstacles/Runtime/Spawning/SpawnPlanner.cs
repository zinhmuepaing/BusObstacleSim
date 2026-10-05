using System;
using System.Collections.Generic;
using BusSim.Obstacles;
using BusSim.Road;

namespace BusSim.Spawning
{
    /// <summary>
    /// Builds the whole run's spawn plan from a seed (FR2, FR3, FR7). Pure C#, no scene access.
    /// Static obstacles are only accepted if they and every static obstacle whose window they touch
    /// still leave the minimum clear corridor.
    /// </summary>
    public sealed class SpawnPlanner
    {
        public struct Settings
        {
            public float RoadLength;
            public float RoadWidth;
            public float MinCorridor;
            public float WindowMargin;
            public int MaxAttempts;
        }

        private const uint SeedMixA = 73856093u;
        private const uint SeedMixB = 19349663u;

        private readonly List<ObstacleDefinition> candidates = new List<ObstacleDefinition>();
        private readonly List<float> candidateWeights = new List<float>();
        private readonly List<Footprint> statics = new List<Footprint>();
        private readonly List<Footprint> scratch = new List<Footprint>();

        /// <summary>Per-event seed so behaviour randomness replays with the run seed.</summary>
        public static int DeriveEventSeed(int runSeed, int eventIndex)
        {
            unchecked
            {
                return (int)(((uint)runSeed * SeedMixA) ^ ((uint)(eventIndex + 1) * SeedMixB));
            }
        }

        public SpawnPlan Plan(int seed, DifficultyProfile profile, IReadOnlyList<RoadZone> zones, Settings settings)
        {
            Random rng = new Random(seed);
            float sEnd = settings.RoadLength - profile.endBuffer;
            SpawnPlan plan = new SpawnPlan(seed, Math.Max(0f, sEnd - profile.startBuffer));

            candidates.Clear();
            foreach (ObstacleDefinition definition in profile.definitions)
            {
                if (profile.Allows(definition) && !candidates.Contains(definition))
                {
                    candidates.Add(definition);
                }
            }
            if (candidates.Count == 0)
            {
                return plan;
            }

            statics.Clear();
            float baseSpacing = 1000f / profile.eventsPerKm;
            float s = profile.startBuffer;
            float pendingGap = 0f;
            int eventIndex = 0;

            while (true)
            {
                double factor = 1.0 + (rng.NextDouble() * 2.0 - 1.0) * profile.spacingJitter;
                s += Math.Max(Math.Max(profile.minGap, pendingGap), (float)(baseSpacing * factor));
                if (s >= sEnd)
                {
                    break;
                }

                bool placed = false;
                for (int attempt = 0; attempt < settings.MaxAttempts && !placed; attempt++)
                {
                    ObstacleDefinition definition = PickDefinition(rng, profile, zones, s, sEnd);
                    if (definition == null)
                    {
                        break;
                    }

                    float t = PickT(rng, definition);
                    Footprint footprint = new Footprint(s, t, definition.footprintWidth, definition.footprintLength);
                    if (!definition.isDynamic && !KeepsCorridor(footprint, settings))
                    {
                        plan.RejectedFootprints.Add(footprint);
                        continue;
                    }

                    float yaw = (float)((rng.NextDouble() * 2.0 - 1.0) * definition.yawJitterDegrees);
                    plan.Events.Add(new SpawnEvent
                    {
                        Definition = definition,
                        S = s,
                        T = t,
                        YawDegrees = yaw,
                        EventIndex = eventIndex,
                        Seed = DeriveEventSeed(seed, eventIndex)
                    });
                    eventIndex++;
                    if (!definition.isDynamic)
                    {
                        statics.Add(footprint);
                    }
                    pendingGap = definition.minGapAfter;
                    placed = true;
                }

                if (!placed)
                {
                    plan.SkippedEvents++;
                }
            }
            return plan;
        }

        /// <summary>The candidate, and every accepted static whose window it enters, keep the corridor.</summary>
        private bool KeepsCorridor(in Footprint candidate, Settings settings)
        {
            if (!ClearanceValidator.IsValid(candidate, statics, settings.RoadWidth, settings.MinCorridor, settings.WindowMargin, scratch))
            {
                return false;
            }

            statics.Add(candidate);
            bool valid = true;
            for (int i = 0; i < statics.Count - 1 && valid; i++)
            {
                Footprint other = statics[i];
                bool touches = candidate.SMax > other.SMin - settings.WindowMargin
                    && candidate.SMin < other.SMax + settings.WindowMargin;
                if (touches)
                {
                    valid = ClearanceValidator.IsValid(other, statics, settings.RoadWidth, settings.MinCorridor, settings.WindowMargin, scratch);
                }
            }
            statics.RemoveAt(statics.Count - 1);
            return valid;
        }

        private ObstacleDefinition PickDefinition(Random rng, DifficultyProfile profile, IReadOnlyList<RoadZone> zones, float s, float sEnd)
        {
            candidateWeights.Clear();
            float total = 0f;
            foreach (ObstacleDefinition definition in candidates)
            {
                float halfLength = definition.footprintLength * 0.5f;
                bool fits = s + halfLength <= sEnd && ZoneAllows(definition, zones, s - halfLength, s + halfLength);
                float weight = fits ? profile.GetWeight(definition) : 0f;
                candidateWeights.Add(weight);
                total += weight;
            }
            if (total <= 0f)
            {
                return null;
            }

            double pick = rng.NextDouble() * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                pick -= candidateWeights[i];
                if (pick < 0.0 && candidateWeights[i] > 0f)
                {
                    return candidates[i];
                }
            }
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                if (candidateWeights[i] > 0f)
                {
                    return candidates[i];
                }
            }
            return null;
        }

        private static bool ZoneAllows(ObstacleDefinition definition, IReadOnlyList<RoadZone> zones, float sMin, float sMax)
        {
            if (definition.requiredZone == ZoneType.None)
            {
                return true;
            }
            if (zones == null)
            {
                return false;
            }
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].type == definition.requiredZone && zones[i].Contains(sMin, sMax))
                {
                    return true;
                }
            }
            return false;
        }

        private static float PickT(Random rng, ObstacleDefinition definition)
        {
            if (definition.placeAtRangeEndsOnly)
            {
                return rng.NextDouble() < 0.5 ? definition.minT : definition.maxT;
            }
            return (float)(definition.minT + (definition.maxT - definition.minT) * rng.NextDouble());
        }
    }
}
