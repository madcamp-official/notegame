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
    /// Fixed product contract for 《넙죽이와 붕괴한 코드 왕국》. A seed may bind content to
    /// existing slots, but cannot replace Codria, Nupjukyi, the Administrator Keyboard, the three
    /// access levels, or the six campaign-region axes.
    /// </summary>
    public static class CampaignCatalog
    {
        public const string RulesVersion = "codria-campaign.v4";
        public const string CampaignId = "WORLD_CODRIA";
        public const string CampaignTitle = "넙죽이와 붕괴한 코드 왕국";
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
            "도착의 의미를 해석하는 안내자", "지역의 생존을 먼저 지키려는 대표",
            "서로 다른 해법 사이에서 흔들리는 동료", "감춰진 기록을 보존한 증언자",
            "과거 선택의 대가를 돌려주는 귀환자", "마지막 선택을 함께 책임질 목격자"
        };

        private static readonly EndingCandidateState[] EndingPool =
        {
            new EndingCandidateState("SAFE_PASSAGE", "안전한 통로", "닻과 보호막, 통로를 잇고 위협을 내려놓는다.", 6),
            new EndingCandidateState("SHARED_GUARDIANSHIP", "공동의 수호", "보호막과 목격자의 기록을 연결해 책임을 나눈다.", 5),
            new EndingCandidateState("FREE_WORLD", "스스로 걷는 세계", "자유의 핵을 깨우고 세계가 직접 다음 길을 고르게 한다.", 5),
            new EndingCandidateState("MEMORY_REWEAVE", "기억 다시 잇기", "기억과 닻을 엮어 상처 난 역사를 새로운 토대로 삼는다.", 5),
            new EndingCandidateState("THREAT_SEAL", "위협의 봉인", "위협을 제거하고 목격자의 증언으로 봉인의 조건을 남긴다.", 4)
        };

        public static CampaignBlueprint Create(long worldSeed)
        {
            Theme theme = Themes[Index(worldSeed, 11, Themes.Length)];
            string playerName = PlayerNames[Index(worldSeed, 23, PlayerNames.Length)];
            string worldName = theme.World;
            string signature = SeedSignature(worldSeed);
            string title = worldName + "의 " + playerName;
            string premise = playerName + "는(은) 키보드 모양의 세계 편집 유물과 함께 " + worldName +
                "에 떨어진다. " + theme.Crisis + ". 이미 생성된 여섯 바이옴을 탐험해 세 개의 핵심 표식을 모으고, " +
                "관계와 기록 속에 감춰진 진실을 확인한 뒤 마지막 수렴지에서 무엇을 남길지 결정해야 한다.";

            string[] npcNames = PickDistinct(worldSeed, NpcNamePool, 6, 37);
            string[] endingCodes = PickDistinctEndingCodes(worldSeed);
            var endings = new List<EndingCandidateState>();
            for (int i = 0; i < endingCodes.Length; i++)
            {
                EndingCandidateState candidate = FindEnding(endingCodes[i]);
                endings.Add(candidate.Clone());
            }
            endings.Add(new EndingCandidateState(FallbackEndingCode, "마지막 피난처",
                "시간이 끝나기 전에 남은 존재를 지키고 변화의 확산을 멈춘다.", 0));

            string[] questSeeds =
            {
                npcNames[0] + "가 " + theme.Motif + "의 첫 흔적을 숨긴 이유를 찾는다.",
                npcNames[2] + "와 " + npcNames[3] + "의 상반된 기억 중 무엇이 지역을 살릴지 검증한다.",
                theme.Return + "는 징조를 따라 마지막 표식의 대가를 선택한다."
            };

            return new CampaignBlueprint(
                CampaignId + "-" + theme.Id + "-" + signature,
                title,
                worldName,
                playerName,
                "player.ninja-green",
                premise,
                new[]
                {
                    new CampaignBeatState("arrival", "도착 · " + theme.Motif,
                        npcNames[0] + " 또는 낯선 키보드 유물을 조사해 이 세계에서 가능한 편집의 범위를 확인하세요.", AbilityKind.Interact,
                        true, ArrivalCatalystRole),
                    new CampaignBeatState("adaptation", "지역의 위기 · 첫 번째 표식",
                        theme.Crisis + ". 지역의 단서를 복사하거나 조사해 MILESTONE_TOKEN_1을 확보하세요.", AbilityKind.Copy,
                        true, LocalStakesRole, MilestoneTokenIds[0]),
                    new CampaignBeatState("expansion", "관계의 충돌 · 두 번째 표식",
                        npcNames[1] + "와 " + npcNames[2] + "의 해법을 연결하거나 중재해 MILESTONE_TOKEN_2를 확보하세요.", AbilityKind.Connect,
                        true, RelationshipConflictRole, MilestoneTokenIds[1]),
                    new CampaignBeatState("truth", "숨은 진실 · " + theme.Truth,
                        "주 기록 또는 증언을 확인해 소문과 확정 사실을 분리하세요.", AbilityKind.Interact,
                        true, HiddenTruthRole),
                    new CampaignBeatState("backflow", "돌아온 결과 · 세 번째 표식",
                        theme.Return + ". 손상된 흔적을 복원하거나 조사해 MILESTONE_TOKEN_3을 확보하세요.", AbilityKind.Restore,
                        true, ConsequenceReturnRole, MilestoneTokenIds[2]),
                    new CampaignBeatState("finale", "마지막 수렴 · 남길 가치",
                        "닻·보호막·통로·목격자·자유·위협·기억을 공간적으로 편집해 결말 레시피 하나를 완성하세요.", AbilityKind.Connect,
                        true, FinalConvergenceRole)
                },
                endings,
                Rotate(worldSeed, RolePool, 59),
                npcNames,
                questSeeds,
                FinaleComponentIds,
                new[]
                {
                    playerName + "는(은) " + worldName + "에 도착한 외부 여행자다.",
                    "키보드 유물은 이미 존재하는 대상의 상태와 관계만 편집할 수 있다.",
                    "160x160 월드와 여섯 바이옴은 런 시작 시 한 번 생성되며 턴 중 다시 만들어지지 않는다.",
                    "마지막 수렴지에는 세 개의 MILESTONE_TOKEN이 필요하다."
                },
                new[] { npcNames[4] + "의 소문: " + theme.Truth + "." },
                new[]
                {
                    "플레이 도중 지도 재생성", "월드에 없는 장소나 통로 생성", "죽은 핵심 NPC의 무비용 부활",
                    "주사위·좌표·지표의 LLM 직접 변경", "세 표식 없이 마지막 수렴지 조기 진입", "확정 세계 사실의 소급 변경"
                });
        }

        private static EndingCandidateState FindEnding(string code)
        {
            for (int i = 0; i < EndingPool.Length; i++)
                if (string.Equals(EndingPool[i].Code, code, StringComparison.Ordinal)) return EndingPool[i];
            return EndingPool[0];
        }

        private static string[] PickDistinctEndingCodes(long seed)
        {
            var indices = new int[EndingPool.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            Shuffle(seed, indices, 71);
            return new[]
            {
                EndingPool[indices[0]].Code,
                EndingPool[indices[1]].Code,
                EndingPool[indices[2]].Code
            };
        }

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
                int value = values[i];
                values[i] = values[swap];
                values[swap] = value;
            }
        }

        private static int Index(long seed, int salt, int length)
        {
            ulong value = Mix(unchecked((ulong)seed) ^ ((ulong)(uint)salt * 0x9e3779b97f4a7c15UL));
            return (int)(value % (ulong)length);
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xbf58476d1ce4e5b9UL;
            value ^= value >> 27;
            value *= 0x94d049bb133111ebUL;
            return value ^ (value >> 31);
        }

        private static string SeedSignature(long seed)
        {
            return Mix(unchecked((ulong)seed)).ToString("x8", CultureInfo.InvariantCulture).Substring(0, 8);
        }
    }
}
