using System;
using System.Collections.Generic;
using BusSim.Obstacles;
using UnityEngine;

namespace BusSim.Spawning
{
    /// <summary>Density, type weights and allowed categories for one difficulty level (FR6).</summary>
    [CreateAssetMenu(menuName = "BusSim/Difficulty Profile", fileName = "DifficultyProfile")]
    public class DifficultyProfile : ScriptableObject
    {
        [Serializable]
        public struct WeightOverride
        {
            public ObstacleDefinition definition;
            [Min(0f)] public float weight;
        }

        public string profileName = "Normal";
        [Tooltip("0 easy, 1 normal, 2 hard. Types with a higher minDifficulty are excluded.")]
        [Range(0, 2)] public int difficultyLevel = 1;
        [Min(0.1f)] public float eventsPerKm = 8f;
        [Tooltip("Minimum distance in metres between consecutive events.")]
        [Min(0f)] public float minGap = 40f;
        [Tooltip("Spacing is base spacing times a random factor in 1 +- this value.")]
        [Range(0f, 0.9f)] public float spacingJitter = 0.5f;
        [Min(0f)] public float startBuffer = 80f;
        [Min(0f)] public float endBuffer = 50f;

        [Tooltip("Types this profile can spawn.")]
        public List<ObstacleDefinition> definitions = new List<ObstacleDefinition>();
        [Tooltip("Empty means every category is allowed.")]
        public List<ObstacleCategory> allowedCategories = new List<ObstacleCategory>();
        public List<WeightOverride> weightOverrides = new List<WeightOverride>();

        public bool Allows(ObstacleDefinition definition)
        {
            return definition != null
                && definition.prefab != null
                && difficultyLevel >= definition.minDifficulty
                && (allowedCategories.Count == 0 || allowedCategories.Contains(definition.category))
                && GetWeight(definition) > 0f;
        }

        public float GetWeight(ObstacleDefinition definition)
        {
            foreach (WeightOverride entry in weightOverrides)
            {
                if (entry.definition == definition)
                {
                    return entry.weight;
                }
            }
            return definition.weight;
        }
    }
}
