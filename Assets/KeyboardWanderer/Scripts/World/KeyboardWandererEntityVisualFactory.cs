using System.Collections.Generic;
using KeyboardWanderer.Demo;
using UnityEngine;

namespace KeyboardWanderer.World
{
    /// <summary>
    /// Entity Visual 프리팹의 생성과 풀 반환을 담당한다.
    /// 엔티티 규칙, 체력 수치, 애니메이션 선택은 알지 못하고 오브젝트 수명만 관리한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererEntityVisualFactory : MonoBehaviour
    {
        [SerializeField] private KeyboardWandererEntityView entityPrefab;
        [SerializeField] private Transform activeRoot;
        [SerializeField] private Transform poolRoot;

        private readonly Stack<KeyboardWandererEntityView> _pool =
            new Stack<KeyboardWandererEntityView>();

        public bool IsReady => entityPrefab != null && activeRoot != null && poolRoot != null;
        public int PooledCount => _pool.Count;

        /// <summary>재사용 가능한 View를 꺼내 요청한 엔티티 표시용으로 준비한다.</summary>
        public KeyboardWandererEntityView Acquire(string objectName, bool hostile, Sprite whiteSprite)
        {
            if (!IsReady)
                return null;
            while (_pool.Count > 0 && _pool.Peek() == null)
                _pool.Pop();
            KeyboardWandererEntityView view = _pool.Count > 0 ? _pool.Pop() : Instantiate(entityPrefab);
            view.transform.SetParent(activeRoot, false);
            view.gameObject.SetActive(true);
            view.name = objectName;
            view.Prepare(whiteSprite, hostile);
            return view;
        }

        /// <summary>사용이 끝난 View를 비활성 풀 루트로 돌려보낸다.</summary>
        public void Release(KeyboardWandererEntityView view)
        {
            if (view == null)
                return;
            view.gameObject.SetActive(false);
            view.transform.SetParent(poolRoot, false);
            _pool.Push(view);
        }

#if UNITY_EDITOR
        public void Configure(KeyboardWandererEntityView prefab, Transform runtimeRoot, Transform inactivePoolRoot)
        {
            entityPrefab = prefab;
            activeRoot = runtimeRoot;
            poolRoot = inactivePoolRoot;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
