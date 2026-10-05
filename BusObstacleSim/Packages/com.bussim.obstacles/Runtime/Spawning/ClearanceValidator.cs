using System;
using System.Collections.Generic;

namespace BusSim.Spawning
{
    /// <summary>
    /// Pure clearance check (FR3). Takes a window of the candidate length plus a margin before and
    /// after, collects every footprint that overlaps the window, merges their lateral intervals and
    /// measures the widest free gap across the road. Conservative: a straight line fits through.
    /// No Unity scene calls.
    /// </summary>
    public static class ClearanceValidator
    {
        /// <summary>Tolerance so a gap of exactly the minimum corridor passes despite float error.</summary>
        public const float Tolerance = 1e-4f;

        public static bool IsValid(
            in Footprint candidate, IReadOnlyList<Footprint> others, float roadWidth,
            float minCorridor, float windowMargin, List<Footprint> scratch = null)
        {
            return LargestGap(candidate, others, roadWidth, windowMargin, out _, scratch) >= minCorridor - Tolerance;
        }

        /// <summary>
        /// Width of the widest free lateral gap inside the candidate's window. gapStart is the t at
        /// which that gap begins. The candidate itself always counts. Entries of `others` equal to
        /// the candidate are counted once only because they occupy the same interval.
        /// </summary>
        public static float LargestGap(
            in Footprint candidate, IReadOnlyList<Footprint> others, float roadWidth,
            float windowMargin, out float gapStart, List<Footprint> scratch = null)
        {
            List<Footprint> blockers = scratch ?? new List<Footprint>();
            blockers.Clear();

            float windowMin = candidate.SMin - windowMargin;
            float windowMax = candidate.SMax + windowMargin;
            blockers.Add(candidate);
            if (others != null)
            {
                for (int i = 0; i < others.Count; i++)
                {
                    Footprint other = others[i];
                    if (other.SMax > windowMin && other.SMin < windowMax)
                    {
                        blockers.Add(other);
                    }
                }
            }

            // Insertion sort by TMin. Lists are tiny, and this avoids a comparer allocation.
            for (int i = 1; i < blockers.Count; i++)
            {
                Footprint key = blockers[i];
                int j = i - 1;
                while (j >= 0 && blockers[j].TMin > key.TMin)
                {
                    blockers[j + 1] = blockers[j];
                    j--;
                }
                blockers[j + 1] = key;
            }

            float half = roadWidth * 0.5f;
            float cursor = -half;
            float best = 0f;
            gapStart = -half;
            for (int i = 0; i < blockers.Count; i++)
            {
                float tMin = Math.Max(blockers[i].TMin, -half);
                float tMax = Math.Min(blockers[i].TMax, half);
                if (tMax <= tMin)
                {
                    continue; // entirely off the carriageway
                }

                if (tMin - cursor > best)
                {
                    best = tMin - cursor;
                    gapStart = cursor;
                }
                cursor = Math.Max(cursor, tMax);
            }

            if (half - cursor > best)
            {
                best = half - cursor;
                gapStart = cursor;
            }
            return best;
        }
    }
}
