using System;
using System.Collections.Generic;
using System.Globalization;

namespace KeyboardWanderer.Gameplay
{
    public sealed class AdminAccessBinding
    {
        public string AccessId { get; }
        public int Level { get; }
        public string SelectedRegionAxis { get; }
        public ActionContext SelectedContext { get; }
        public AbilityKind SelectedSkill { get; }
        public IReadOnlyList<string> CandidateRegionAxes { get; }
        public IReadOnlyList<ActionContext> CandidateContexts { get; }
        public IReadOnlyList<AbilityKind> CandidateSkills { get; }

        public AdminAccessBinding(string accessId, int level, string selectedRegionAxis,
            ActionContext selectedContext, AbilityKind selectedSkill,
            IEnumerable<string> candidateRegionAxes, IEnumerable<ActionContext> candidateContexts,
            IEnumerable<AbilityKind> candidateSkills)
        {
            AccessId = accessId ?? string.Empty;
            Level = Math.Max(1, Math.Min(3, level));
            SelectedRegionAxis = selectedRegionAxis ?? string.Empty;
            SelectedContext = selectedContext;
            SelectedSkill = selectedSkill;
            CandidateRegionAxes = new List<string>(candidateRegionAxes ?? Array.Empty<string>());
            CandidateContexts = new List<ActionContext>(candidateContexts ?? Array.Empty<ActionContext>());
            CandidateSkills = new List<AbilityKind>(candidateSkills ?? Array.Empty<AbilityKind>());
        }
    }

    public sealed class CampaignBlueprint
    {
        public string Id { get; }
        public string Title { get; }
        public string WorldName { get; }
        public string PlayerName { get; }
        public string PlayerAssetId { get; }
        public string Premise { get; }
        public List<CampaignBeatState> Beats { get; }
        public List<EndingCandidateState> Endings { get; }
        public List<string> NpcRoles { get; }
        public List<string> NpcNames { get; }
        public List<string> QuestSeeds { get; }
        public List<string> FinaleComponents { get; }
        public List<string> InitialFacts { get; }
        public List<string> InitialRumors { get; }
        public List<string> ForbiddenEvents { get; }
        public List<AdminAccessBinding> AdminAccessBindings { get; }

        public CampaignBlueprint(string id, string title, string worldName, string playerName,
            string playerAssetId, string premise, IEnumerable<CampaignBeatState> beats,
            IEnumerable<EndingCandidateState> endings, IEnumerable<string> npcRoles,
            IEnumerable<string> npcNames, IEnumerable<string> questSeeds,
            IEnumerable<string> finaleComponents, IEnumerable<string> initialFacts,
            IEnumerable<string> initialRumors, IEnumerable<string> forbiddenEvents,
            IEnumerable<AdminAccessBinding> adminAccessBindings = null)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            WorldName = worldName ?? string.Empty;
            PlayerName = playerName ?? string.Empty;
            PlayerAssetId = playerAssetId ?? string.Empty;
            Premise = premise ?? string.Empty;
            Beats = new List<CampaignBeatState>(beats ?? Array.Empty<CampaignBeatState>());
            Endings = new List<EndingCandidateState>(endings ?? Array.Empty<EndingCandidateState>());
            NpcRoles = new List<string>(npcRoles ?? Array.Empty<string>());
            NpcNames = new List<string>(npcNames ?? Array.Empty<string>());
            QuestSeeds = new List<string>(questSeeds ?? Array.Empty<string>());
            FinaleComponents = new List<string>(finaleComponents ?? Array.Empty<string>());
            InitialFacts = new List<string>(initialFacts ?? Array.Empty<string>());
            InitialRumors = new List<string>(initialRumors ?? Array.Empty<string>());
            ForbiddenEvents = new List<string>(forbiddenEvents ?? Array.Empty<string>());
            AdminAccessBindings = new List<AdminAccessBinding>(adminAccessBindings ?? Array.Empty<AdminAccessBinding>());
        }
    }

    /// <summary>
    /// Fixed product contract for 《Ninja Adventure》. A seed may bind content to
    /// existing slots, but cannot replace Codria, Nupjukyi, the Administrator Keyboard, the three
    /// access levels, or the six campaign-region axes.
    /// </summary>
    public static class CampaignCatalog
    {
        public const string RulesVersion = "codria-campaign.v4";
        public const string CampaignId = "WORLD_CODRIA";
        public const string CampaignTitle = "Ninja Adventure";
        public const string WorldId = "WORLD_CODRIA";
        public const string WorldName = "코드리아";
        public const string ProtagonistId = "PROTAGONIST_NUPJUKYI";
        public const string ProtagonistName = "넙죽이";
        // Temporary visual binding only. Product identity remains PROTAGONIST_NUPJUKYI / 넙죽이.
        public const string ProtagonistAssetId = "player.ninja-green.v1";
        public const string AdministratorKeyboardId = "ARTIFACT_ADMIN_KEYBOARD";
        public const string AdministratorKeyboardName = "관리자 키보드";
        public const string FallbackEndingCode = "ENDING_EMERGENCY_WITHDRAWAL";

        public const string BugForestAxis = "REGION_BUG_FOREST";
        public const string BufferVillageAxis = "REGION_BUFFER_VILLAGE";
        public const string DeadlockCityAxis = "REGION_DEADLOCK_CITY";
        public const string DataGrandLibraryAxis = "REGION_DATA_GRAND_LIBRARY";
        public const string LegacyCitadelAxis = "REGION_LEGACY_CITADEL";
        public const string RootSystemAxis = "REGION_ROOT_SYSTEM";

        // Compatibility names retained for the generator; their values are now product region axes.
        public const string ArrivalCatalystRole = BugForestAxis;
        public const string LocalStakesRole = BufferVillageAxis;
        public const string RelationshipConflictRole = DeadlockCityAxis;
        public const string HiddenTruthRole = DataGrandLibraryAxis;
        public const string ConsequenceReturnRole = LegacyCitadelAxis;
        public const string FinalConvergenceRole = RootSystemAxis;

        public static readonly string[] RegionAxisIds =
        {
            BugForestAxis, BufferVillageAxis, DeadlockCityAxis, DataGrandLibraryAxis,
            LegacyCitadelAxis, RootSystemAxis
        };

        public static readonly string[] RoleIds = RegionAxisIds;

        public static readonly string[] AdminAccessLevelIds =
        {
            "ADMIN_ACCESS_LEVEL_1", "ADMIN_ACCESS_LEVEL_2", "ADMIN_ACCESS_LEVEL_3"
        };

        public static readonly string[] FinaleComponentIds =
        {
            "FINAL_ANCHOR", "FINAL_SAFEGUARD", "FINAL_MEMORY", "FINAL_FREEDOM",
            "FINAL_THREAT", "FINAL_PASSAGE", "FINAL_WITNESS"
        };

        private static readonly string[] NpcNamePool =
        {
            "바이트", "루프", "모나드", "세마", "래치", "스택", "캐시", "패치",
            "링크", "포인터", "로그", "커밋"
        };

        private static readonly string[] RolePool =
        {
            "붕괴를 기록하는 디버거", "마을의 버퍼를 지키는 조정자",
            "교착된 파벌 사이의 중재자", "손실된 데이터를 보존한 사서",
            "레거시 책임을 증언하는 유지보수자", "최종 배치를 함께 책임질 동료"
        };

        private static readonly EndingCandidateState[] EndingPool =
        {
            new EndingCandidateState("ENDING_REWEAVE_TOGETHER", "함께 다시 잇기", "관계와 세계의 상처를 함께 엮는다.", 8),
            new EndingCandidateState("ENDING_OPEN_FRONTIER", "열린 변경", "코드리아가 위험과 선택권을 함께 품게 한다.", 8),
            new EndingCandidateState("ENDING_KEEP_THE_PROMISE", "약속을 지키는 이", "주민과 맺은 약속의 책임을 받아들인다.", 8),
            new EndingCandidateState("ENDING_CUT_THE_CYCLE", "되풀이 끊기", "오래된 통제 순환을 끊고 다음 가능성을 연다.", 8),
            new EndingCandidateState("ENDING_PRESERVE_THE_SCARS", "상처를 기억하기", "완전한 복구 대신 상처와 증언을 보존한다.", 8),
            new EndingCandidateState("ENDING_WALK_BETWEEN_WORLDS", "세계 사이를 걷기", "두 세계를 잇는 통로와 책임을 선택한다.", 8),
            new EndingCandidateState(FallbackEndingCode, "긴급 이탈", "생존자를 우선해 위험한 수렴점에서 이탈한다.", 0)
        };

        public static CampaignBlueprint Create(long worldSeed)
        {
            string signature = SeedSignature(worldSeed);
            string[] npcNames = PickDistinct(worldSeed, NpcNamePool, 6, 37);
            AdminAccessBinding[] bindings = CreateAccessBindings(worldSeed);
            string premise = "현생에서 개발자였던 내가 이세계에서 키보드워리어?!";

            var beats = new List<CampaignBeatState>
            {
                Beat("arrival", "코드리아 추락", "관리자 키보드를 조사해 편집 범위를 확인하세요.",
                    AbilityKind.Search, BugForestAxis, "", ActionContext.Investigation),
                Beat("collapse", "붕괴 징후", "현재 지역의 붕괴 원인을 조사하세요.",
                    AbilityKind.Search, bindings[0].SelectedRegionAxis, "", ActionContext.Investigation),
                AccessBeat(bindings[0]),
                AccessBeat(bindings[1]),
                Beat("truth", "관리자 통제의 내부 오류", "데이터 대도서관의 기록으로 붕괴 원인을 확인하세요.",
                    AbilityKind.Search, DataGrandLibraryAxis, "", ActionContext.Investigation),
                Beat("debt-backflow", "기술 부채의 역류", "레거시 성채 주민과 연결해 과거 편집의 책임을 회수하세요.",
                    AbilityKind.Connect, LegacyCitadelAxis, "", ActionContext.Negotiation),
                AccessBeat(bindings[2]),
                Beat("root-entry", "루트 시스템 진입", "세 권한과 내부 오류 단서로 루트 게이트를 여세요.",
                    AbilityKind.Connect, RootSystemAxis, "", ActionContext.Deployment),
                Beat("final-deployment", "최종 배치", "통제와 자율, 오류와 공존 사이의 최종 배치를 실행하세요.",
                    AbilityKind.Connect, RootSystemAxis, "", ActionContext.Deployment)
            };

            return new CampaignBlueprint(
                CampaignId + ":" + signature,
                CampaignTitle,
                WorldName,
                ProtagonistName,
                ProtagonistAssetId,
                premise,
                beats,
                EndingPool,
                Rotate(worldSeed, RolePool, 59),
                npcNames,
                new[]
                {
                    "버그 숲과 버퍼 마을 중 먼저 도울 지역을 선택한다.",
                    "교착 도시의 충돌을 삭제할지 연결할지 결정한다.",
                    "레거시 성채에서 기술 부채의 책임을 인수하거나 협력을 구한다."
                },
                FinaleComponentIds,
                new[]
                {
                    "세계 ID는 WORLD_CODRIA이며 이름은 코드리아다.",
                    "주인공 ID는 PROTAGONIST_NUPJUKYI이며 이름은 넙죽이다.",
                    "관리자 키보드는 이미 존재하는 객체와 관계만 편집한다.",
                    "160x160 월드 geometry는 런 시작 시 봉인되어 턴·복구·재개 이후에도 불변이다.",
                    "ROOT_SYSTEM 진입에는 관리자 권한 3단계와 내부 오류 단서가 필요하다."
                },
                new[] { npcNames[4] + "의 소문: 루트 시스템의 통제가 붕괴를 스스로 증폭시키고 있다." },
                new[]
                {
                    "플레이 중 geometry 재생성", "LLM의 좌표·D20·권한·결말 변경",
                    "세 관리자 권한 없이 ROOT_SYSTEM 진입", "자연어 입력을 규칙 권위로 사용",
                    "기술 부채 원장을 남기지 않는 강제 편집"
                },
                bindings);
        }

        private static CampaignBeatState Beat(string id, string title, string objective,
            AbilityKind trigger, string regionAxis, string accessId = "",
            ActionContext requiredContext = ActionContext.None)
        {
            return new CampaignBeatState(id, title, objective, trigger, true, regionAxis, accessId,
                requiredContext);
        }

        private static CampaignBeatState AccessBeat(AdminAccessBinding binding)
        {
            return Beat("admin-access-" + binding.Level, "관리자 권한 " + Roman(binding.Level),
                RegionLabel(binding.SelectedRegionAxis) + "에서 " + ContextLabel(binding.SelectedContext) +
                " 문맥의 " + binding.SelectedSkill + " 스킬로 " + binding.AccessId + "을 획득하세요.",
                binding.SelectedSkill,
                binding.SelectedRegionAxis, binding.AccessId, binding.SelectedContext);
        }

        private static AdminAccessBinding[] CreateAccessBindings(long seed)
        {
            string[][] candidates =
            {
                new[] { BufferVillageAxis, BugForestAxis },
                new[] { DeadlockCityAxis, BufferVillageAxis },
                new[] { LegacyCitadelAxis, DataGrandLibraryAxis }
            };
            ActionContext[][] contexts =
            {
                new[] { ActionContext.Negotiation, ActionContext.Investigation },
                new[] { ActionContext.Combat, ActionContext.Negotiation },
                new[] { ActionContext.Deployment, ActionContext.Investigation }
            };
            AbilityKind[][] skills =
            {
                new[] { AbilityKind.Connect, AbilityKind.Search },
                new[] { AbilityKind.Delete, AbilityKind.Connect },
                new[] { AbilityKind.Restore, AbilityKind.Search }
            };
            var result = new AdminAccessBinding[3];
            for (int i = 0; i < result.Length; i++)
            {
                int selected = Index(seed, 101 + i * 19, candidates[i].Length);
                result[i] = new AdminAccessBinding(AdminAccessLevelIds[i], i + 1,
                    candidates[i][selected], contexts[i][selected], skills[i][selected],
                    candidates[i], contexts[i], skills[i]);
            }
            return result;
        }

        public static string RegionLabel(string axis)
        {
            switch (axis)
            {
                case BugForestAxis: return "버그 숲";
                case BufferVillageAxis: return "버퍼 마을";
                case DeadlockCityAxis: return "교착 도시";
                case DataGrandLibraryAxis: return "데이터 대도서관";
                case LegacyCitadelAxis: return "레거시 성채";
                case RootSystemAxis: return "루트 시스템";
                default: return axis ?? string.Empty;
            }
        }

        public static string ContextLabel(ActionContext context)
        {
            switch (context)
            {
                case ActionContext.Combat: return "전투";
                case ActionContext.Investigation: return "조사";
                case ActionContext.Negotiation: return "협상";
                case ActionContext.Deployment: return "배치";
                default: return "안전 이동";
            }
        }

        private static string Roman(int level) { return level == 1 ? "I" : level == 2 ? "II" : "III"; }

        private static string[] PickDistinct(long seed, string[] source, int count, int salt)
        {
            var indices = new int[source.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            Shuffle(seed, indices, salt);
            var result = new string[Math.Min(count, source.Length)];
            for (int i = 0; i < result.Length; i++) result[i] = source[indices[i]];
            return result;
        }

        private static string[] Rotate(long seed, string[] source, int salt)
        {
            var result = new string[source.Length];
            int start = Index(seed, salt, source.Length);
            for (int i = 0; i < result.Length; i++) result[i] = source[(start + i) % source.Length];
            return result;
        }

        private static void Shuffle(long seed, int[] values, int salt)
        {
            ulong state = Mix(unchecked((ulong)seed) ^ (ulong)(uint)salt);
            for (int i = values.Length - 1; i > 0; i--)
            {
                state = Mix(state + 0x9e3779b97f4a7c15UL);
                int swap = (int)(state % (ulong)(i + 1));
                int value = values[i]; values[i] = values[swap]; values[swap] = value;
            }
        }

        private static int Index(long seed, int salt, int length)
        {
            ulong value = Mix(unchecked((ulong)seed) ^ ((ulong)(uint)salt * 0x9e3779b97f4a7c15UL));
            return (int)(value % (ulong)length);
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30; value *= 0xbf58476d1ce4e5b9UL;
            value ^= value >> 27; value *= 0x94d049bb133111ebUL;
            return value ^ (value >> 31);
        }

        private static string SeedSignature(long seed)
        {
            return Mix(unchecked((ulong)seed)).ToString("x8", CultureInfo.InvariantCulture).Substring(0, 8);
        }
    }
}
