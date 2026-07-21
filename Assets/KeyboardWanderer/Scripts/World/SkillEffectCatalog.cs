using System;
using System.Collections.Generic;
using UnityEngine;

namespace KeyboardWanderer.World
{
    /// <summary>
    /// 스킬 이펙트 애니메이션 프레임 모음. Editor 빌더가 NinjaAdventure FX 시트에서
    /// 이미 슬라이스된 서브 스프라이트를 이 ScriptableObject에 직렬화하고,
    /// 런타임은 클립 id로 프레임 배열을 조회한다. 게임 규칙은 알지 못한다.
    /// </summary>
    [CreateAssetMenu(fileName = "SkillEffectCatalog", menuName = "Codria/Skill Effect Catalog")]
    public sealed class SkillEffectCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Clip
        {
            public string Id;
            public Sprite[] Frames = Array.Empty<Sprite>();
            [Tooltip("초당 프레임 수")]
            public float Fps = 16f;
            [Tooltip("이펙트 크기 미세 보정 배율")]
            public float ScaleMultiplier = 1f;
        }

        [SerializeField] private Clip[] clips = Array.Empty<Clip>();

        private Dictionary<string, Clip> _lookup;

        public IReadOnlyList<Clip> Clips => clips;

        public Clip Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (_lookup == null || _lookup.Count != clips.Length)
            {
                _lookup = new Dictionary<string, Clip>(StringComparer.Ordinal);
                for (int i = 0; i < clips.Length; i++)
                    if (clips[i] != null && !string.IsNullOrEmpty(clips[i].Id))
                        _lookup[clips[i].Id] = clips[i];
            }
            return _lookup.TryGetValue(id, out Clip clip) ? clip : null;
        }

#if UNITY_EDITOR
        public void SetClips(Clip[] value)
        {
            clips = value ?? Array.Empty<Clip>();
            _lookup = null;
        }
#endif
    }
}
