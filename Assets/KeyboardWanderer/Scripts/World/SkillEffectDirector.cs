using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KeyboardWanderer.World
{
    /// <summary>
    /// 스킬 이펙트 스프라이트 애니메이션을 대상 위에 잠깐 재생하고 스스로 정리한다.
    /// 게임 규칙·서버 응답은 모르고, 확정된 클립 id·위치·크기(SkillEffectInstance)만 받는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkillEffectDirector : MonoBehaviour
    {
        private const int WorldSortingOrder = 10000;

        private SkillEffectCatalog _catalog;
        private Transform _worldRoot;

        public bool IsReady => _catalog != null && _catalog.Clips != null && _catalog.Clips.Count > 0;

        public void Initialize(SkillEffectCatalog catalog, Transform worldEffectsRoot)
        {
            _catalog = catalog;
            _worldRoot = worldEffectsRoot != null ? worldEffectsRoot : transform;
        }

        /// <summary>대상마다 이펙트를 재생한다.</summary>
        public void Play(SkillEffectInstance perTarget, IReadOnlyList<Vector3> targetWorldPositions)
        {
            if (!IsReady || !perTarget.HasClip || targetWorldPositions == null)
                return;
            for (int i = 0; i < targetWorldPositions.Count; i++)
                PlayAt(perTarget, targetWorldPositions[i]);
        }

        public void PlayAt(SkillEffectInstance instance, Vector3 worldPosition)
        {
            SkillEffectCatalog.Clip clip = _catalog != null ? _catalog.Find(instance.ClipId) : null;
            if (clip == null || clip.Frames == null || clip.Frames.Length == 0)
                return;
            float units = SkillEffectMapping.WorldSize(instance.Size) * Mathf.Max(0.01f, clip.ScaleMultiplier);
            worldPosition.z = -5f; // 엔티티보다 앞에 그린다.
            var go = new GameObject("SkillFx_" + instance.ClipId);
            go.transform.SetParent(_worldRoot != null ? _worldRoot : transform, false);
            go.transform.position = worldPosition;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = WorldSortingOrder;
            StartCoroutine(Animate(go, renderer, clip, units));
        }

        private IEnumerator Animate(GameObject go, SpriteRenderer renderer, SkillEffectCatalog.Clip clip,
            float targetUnits)
        {
            float frameTime = 1f / Mathf.Max(1f, clip.Fps);
            ApplyScale(renderer.transform, clip.Frames[0], targetUnits);
            for (int i = 0; i < clip.Frames.Length; i++)
            {
                renderer.sprite = clip.Frames[i];
                yield return new WaitForSecondsRealtime(frameTime);
            }
            Destroy(go);
        }

        private static void ApplyScale(Transform target, Sprite frame, float targetUnits)
        {
            if (target == null || frame == null)
                return;
            float largest = Mathf.Max(frame.bounds.size.x, frame.bounds.size.y);
            float scale = largest > 0.001f ? targetUnits / largest : targetUnits;
            target.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
