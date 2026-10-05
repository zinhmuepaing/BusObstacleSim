using System.Collections.Generic;
using UnityEngine;

namespace BusSim.Spawning
{
    /// <summary>Pools instances per prefab. Instances are deactivated, not destroyed, on release.</summary>
    public sealed class ObjectPool
    {
        private readonly Dictionary<GameObject, Stack<GameObject>> free = new Dictionary<GameObject, Stack<GameObject>>();
        private readonly Transform root;
        private readonly List<GameObject> all = new List<GameObject>();

        public ObjectPool(Transform root)
        {
            this.root = root;
        }

        public int CreatedCount { get; private set; }

        /// <summary>Destroys every instance this pool ever made. Call before replacing the pool.</summary>
        public void Clear()
        {
            foreach (GameObject instance in all)
            {
                if (instance != null)
                {
                    // Immediate, so the old instances are gone before the next run builds its own.
                    Object.DestroyImmediate(instance);
                }
            }
            all.Clear();
            free.Clear();
            CreatedCount = 0;
        }

        public void Prewarm(GameObject prefab, int count)
        {
            Stack<GameObject> stack = GetStack(prefab);
            while (stack.Count < count)
            {
                GameObject instance = Create(prefab);
                instance.SetActive(false);
                stack.Push(instance);
            }
        }

        public GameObject Get(GameObject prefab)
        {
            Stack<GameObject> stack = GetStack(prefab);
            GameObject instance = stack.Count > 0 ? stack.Pop() : Create(prefab);
            instance.SetActive(true);
            return instance;
        }

        public void Release(GameObject prefab, GameObject instance)
        {
            instance.SetActive(false);
            GetStack(prefab).Push(instance);
        }

        private Stack<GameObject> GetStack(GameObject prefab)
        {
            if (!free.TryGetValue(prefab, out Stack<GameObject> stack))
            {
                stack = new Stack<GameObject>();
                free.Add(prefab, stack);
            }
            return stack;
        }

        private GameObject Create(GameObject prefab)
        {
            CreatedCount++;
            GameObject instance = Object.Instantiate(prefab, root);
            all.Add(instance);
            return instance;
        }
    }
}
