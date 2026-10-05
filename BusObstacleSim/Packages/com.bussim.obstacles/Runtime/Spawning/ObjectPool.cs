using System.Collections.Generic;
using UnityEngine;

namespace BusSim.Spawning
{
    /// <summary>Pools instances per prefab. Instances are deactivated, not destroyed, on release.</summary>
    public sealed class ObjectPool
    {
        private readonly Dictionary<GameObject, Stack<GameObject>> free = new Dictionary<GameObject, Stack<GameObject>>();
        private readonly Transform root;

        public ObjectPool(Transform root)
        {
            this.root = root;
        }

        public int CreatedCount { get; private set; }

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
            return Object.Instantiate(prefab, root);
        }
    }
}
