using System.Collections.Generic;
using UnityEngine;

namespace KeyboardWanderer.World
{
    /// <summary>
    /// 바이옴 장식 오브젝트의 생성, 재사용, 플레이어 가림 투명도를 담당한다.
    /// 장식의 종류와 배치 좌표는 외부에서 전달받고 월드 규칙이나 서버 상태는 알지 못한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererBiomeDecorationRenderer : MonoBehaviour
    {
        private sealed class DecorationInstance
        {
            public SpriteRenderer Renderer;
            public GameObject PrefabKey;
            public Color BaseColor;
        }

        [SerializeField] private Transform decorationRoot;
        [SerializeField] private Transform poolRoot;

        private readonly List<DecorationInstance> _active = new List<DecorationInstance>();
        private readonly Stack<DecorationInstance> _genericPool = new Stack<DecorationInstance>();
        private readonly Dictionary<GameObject, Stack<DecorationInstance>> _prefabPools =
            new Dictionary<GameObject, Stack<DecorationInstance>>();

        public bool IsReady => decorationRoot != null && poolRoot != null;
        public int ActiveCount => _active.Count;

        /// <summary>장식을 풀에서 꺼내거나 프리팹으로 만든 뒤 지정한 월드 위치에 표시한다.</summary>
        public void Spawn(GameObject authoredPrefab, string objectName, Sprite sprite, Vector3 worldPosition,
            Color tint, int sortingOrder, float scale)
        {
            if (!IsReady || sprite == null)
                return;

            DecorationInstance instance = Acquire(authoredPrefab);
            if (instance == null || instance.Renderer == null)
                return;
            GameObject decoration = instance.Renderer.gameObject;
            decoration.name = objectName;
            decoration.transform.SetParent(decorationRoot, false);
            decoration.transform.position = worldPosition;
            decoration.transform.localScale = Vector3.one * scale;
            decoration.SetActive(true);
            instance.Renderer.sprite = sprite;
            instance.Renderer.color = tint;
            instance.Renderer.sortingOrder = sortingOrder;
            instance.BaseColor = tint;
            _active.Add(instance);
        }

        /// <summary>플레이어 앞을 덮는 장식만 부드럽게 투명하게 만든다.</summary>
        public void UpdateOcclusion(Vector2 playerPosition, int playerSortingOrder, float deltaTime)
        {
            float blend = 1f - Mathf.Exp(-12f * deltaTime);
            for (int i = 0; i < _active.Count; i++)
            {
                DecorationInstance instance = _active[i];
                SpriteRenderer renderer = instance?.Renderer;
                if (renderer == null)
                    continue;
                Bounds bounds = renderer.bounds;
                float clearance = Mathf.Max(bounds.extents.x, bounds.extents.y) + 0.75f;
                bool coversPlayer = renderer.sortingOrder >= playerSortingOrder &&
                                    Vector2.Distance(renderer.transform.position, playerPosition) <= clearance;
                Color target = instance.BaseColor;
                if (coversPlayer)
                    target.a = Mathf.Min(target.a, 0.16f);
                renderer.color = Color.Lerp(renderer.color, target, blend);
            }
        }

        /// <summary>현재 장식을 숨기고 원본 프리팹 종류별 풀로 돌려보낸다.</summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                DecorationInstance instance = _active[i];
                if (instance?.Renderer == null)
                    continue;
                instance.Renderer.sprite = null;
                instance.Renderer.gameObject.SetActive(false);
                instance.Renderer.transform.SetParent(poolRoot, false);
                PoolFor(instance.PrefabKey).Push(instance);
            }
            _active.Clear();
        }

#if UNITY_EDITOR
        public void Configure(Transform activeRoot, Transform inactivePoolRoot)
        {
            decorationRoot = activeRoot;
            poolRoot = inactivePoolRoot;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private DecorationInstance Acquire(GameObject authoredPrefab)
        {
            Stack<DecorationInstance> pool = PoolFor(authoredPrefab);
            while (pool.Count > 0)
            {
                DecorationInstance pooled = pool.Pop();
                if (pooled?.Renderer != null)
                    return pooled;
            }

            SpriteRenderer renderer;
            if (authoredPrefab != null)
            {
                GameObject created = Instantiate(authoredPrefab, decorationRoot);
                renderer = created.GetComponent<SpriteRenderer>();
                if (renderer == null)
                {
                    Destroy(created);
                    return null;
                }
            }
            else
            {
                renderer = new GameObject("Pooled Decoration", typeof(SpriteRenderer))
                    .GetComponent<SpriteRenderer>();
                renderer.transform.SetParent(decorationRoot, false);
            }
            return new DecorationInstance { Renderer = renderer, PrefabKey = authoredPrefab };
        }

        private Stack<DecorationInstance> PoolFor(GameObject prefab)
        {
            if (prefab == null)
                return _genericPool;
            if (_prefabPools.TryGetValue(prefab, out Stack<DecorationInstance> pool))
                return pool;
            pool = new Stack<DecorationInstance>();
            _prefabPools.Add(prefab, pool);
            return pool;
        }
    }
}
