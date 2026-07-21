using KeyboardWanderer.Gameplay;

namespace KeyboardWanderer.World
{
    /// <summary>노션 payload의 fx_size 5단계. 서버가 아직 보내지 않으므로 클라에서 파생한다.</summary>
    public enum SkillFxSize
    {
        Tile = 0,
        Small = 1,
        Medium = 2,
        Large = 3,
        Screen = 4
    }

    /// <summary>한 이펙트 인스턴스가 어떤 클립을 어떤 크기로 재생할지의 확정 값.</summary>
    public readonly struct SkillEffectInstance
    {
        public readonly string ClipId;
        public readonly SkillFxSize Size;

        public SkillEffectInstance(string clipId, SkillFxSize size)
        {
            ClipId = clipId;
            Size = size;
        }

        public bool HasClip => !string.IsNullOrEmpty(ClipId);
    }

    /// <summary>
    /// DELETE 공격 이펙트의 속성/스타일. 노션 payload의 fx_type에 대응한다.
    /// (e.g. FIRE, ICE, VOID, PHYSICAL — 확장으로 THUNDER/WATER/PLANT 포함)
    /// </summary>
    public enum SkillFxType
    {
        Physical = 0,
        Fire = 1,
        Ice = 2,
        Thunder = 3,
        Void = 4,
        Water = 5,
        Plant = 6
    }

    /// <summary>카탈로그 클립 id 상수. Editor 빌더와 매핑이 공유한다.</summary>
    public static class SkillEffectClips
    {
        public const string Explosion = "explosion";
        public const string SpiritDouble = "spirit_double";
        public const string Spark = "spark";
        public const string Boost = "boost";
        public const string CircleSpark = "circle_spark";
        public const string Aura = "aura";
        public const string SmokeCircular = "smoke_circular";

        // DELETE fx_type 속성별 이펙트
        public const string Flam = "flam";
        public const string Ice = "ice";
        public const string Thunder = "thunder";
        public const string Water = "water";
        public const string Plant = "plant";
    }

    /// <summary>
    /// 노션 "스킬 이펙트 Payload 형식" 문서에 맞춰 각 skill_type에 이펙트를 매핑한다.
    /// server payload(fx_size/fx_type/result)가 붙기 전까지 fx_size는 dice 판정에서 파생한다.
    /// </summary>
    public static class SkillEffectMapping
    {
        /// <summary>공격/대상 위에 재생할 이펙트. DELETE는 fx_type(속성)에 따라 이펙트가 달라진다.</summary>
        public static SkillEffectInstance ForTarget(AbilityKind skill, SkillFxSize size,
            SkillFxType fxType = SkillFxType.Physical)
        {
            switch (skill)
            {
                // COPY: 분신(복제) → 두 영혼 이미지
                case AbilityKind.Copy: return new SkillEffectInstance(SkillEffectClips.SpiritDouble, size);
                // DELETE: 유일한 공격 스킬 → fx_type 속성별 이펙트
                case AbilityKind.Delete: return new SkillEffectInstance(AttackClipForType(fxType), size);
                // CONNECT: 두 대상 연결 → 전기 스파크(양쪽에 재생)
                case AbilityKind.Connect: return new SkillEffectInstance(SkillEffectClips.Spark, size);
                // RESTORE: 복원·부활 → 상승하는 회복 오라
                case AbilityKind.Restore: return new SkillEffectInstance(SkillEffectClips.Boost, size);
                // UNDO: 2턴 시간 역행 → 시전자 발밑 마법진(고정 크기)
                case AbilityKind.Undo: return new SkillEffectInstance(SkillEffectClips.CircleSpark, SkillFxSize.Medium);
                // SEARCH: 조사·스캔 → 은은한 오라(고정 소형)
                case AbilityKind.Search: return new SkillEffectInstance(SkillEffectClips.Aura, SkillFxSize.Small);
                // SELECT_ALL: 영역전개 → 대상마다 폭발
                case AbilityKind.SelectAll: return new SkillEffectInstance(SkillEffectClips.Explosion, size);
                default: return default;
            }
        }

        /// <summary>DELETE fx_type 속성 → 이펙트 클립. (노션 fx_type: FIRE/ICE/VOID/PHYSICAL …)</summary>
        public static string AttackClipForType(SkillFxType fxType)
        {
            switch (fxType)
            {
                case SkillFxType.Fire: return SkillEffectClips.Flam;
                case SkillFxType.Ice: return SkillEffectClips.Ice;
                case SkillFxType.Thunder: return SkillEffectClips.Thunder;
                case SkillFxType.Water: return SkillEffectClips.Water;
                case SkillFxType.Plant: return SkillEffectClips.Plant;
                case SkillFxType.Void: return SkillEffectClips.SmokeCircular;
                default: return SkillEffectClips.Explosion; // PHYSICAL 및 미지정
            }
        }

        /// <summary>서버 fx_type 문자열 → SkillFxType.</summary>
        public static SkillFxType ParseFxType(string value)
        {
            switch ((value ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "FIRE": return SkillFxType.Fire;
                case "ICE": return SkillFxType.Ice;
                case "THUNDER":
                case "LIGHTNING": return SkillFxType.Thunder;
                case "WATER": return SkillFxType.Water;
                case "PLANT":
                case "NATURE": return SkillFxType.Plant;
                case "VOID":
                case "DARK": return SkillFxType.Void;
                default: return SkillFxType.Physical;
            }
        }

        /// <summary>로컬 규칙 엔진 판정 결과 → fx_size 티어.</summary>
        public static SkillFxSize SizeFromOutcome(RuleOutcome outcome)
        {
            switch (outcome)
            {
                case RuleOutcome.CriticalSuccess: return SkillFxSize.Large;
                case RuleOutcome.Success: return SkillFxSize.Medium;
                case RuleOutcome.PartialSuccess: return SkillFxSize.Small;
                default: return SkillFxSize.Tile;
            }
        }

        /// <summary>서버 outcome 문자열 → fx_size 티어.</summary>
        public static SkillFxSize SizeFromOutcome(string outcome)
        {
            string value = (outcome ?? string.Empty).ToLowerInvariant();
            if (value.Contains("critical_success")) return SkillFxSize.Large;
            if (value == "success") return SkillFxSize.Medium;
            if (value.Contains("partial")) return SkillFxSize.Small;
            return SkillFxSize.Tile;
        }

        /// <summary>fx_size 티어 → 이펙트가 차지할 대략적인 월드 유닛(타일) 크기.</summary>
        public static float WorldSize(SkillFxSize size)
        {
            switch (size)
            {
                case SkillFxSize.Tile: return 1.1f;
                case SkillFxSize.Small: return 1.7f;
                case SkillFxSize.Medium: return 2.5f;
                case SkillFxSize.Large: return 3.6f;
                case SkillFxSize.Screen: return 12f;
                default: return 1.6f;
            }
        }
    }
}
