using System;
using System.Collections.Generic;
using System.Globalization;

namespace KeyboardWanderer.Gameplay
{
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

        public CampaignBlueprint(string id, string title, string worldName, string playerName,
            string playerAssetId, string premise, IEnumerable<CampaignBeatState> beats,
            IEnumerable<EndingCandidateState> endings, IEnumerable<string> npcRoles,
            IEnumerable<string> npcNames, IEnumerable<string> questSeeds,
            IEnumerable<string> finaleComponents, IEnumerable<string> initialFacts,
            IEnumerable<string> initialRumors, IEnumerable<string> forbiddenEvents)
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
        }
    }

    /// <summary>
    /// Creates the offline campaign contract from the run seed. Geometry is generated once by the
    /// region generator; this catalog varies story flavour and finale options without asking an LLM
    /// to invent or replace map topology during play.
    /// </summary>
    public static class CampaignCatalog
    {
        public const string RulesVersion = "seeded-campaign.v4";
        public const string CampaignId = "seeded-keyboard-wanderer";
        public const string CampaignTitle = "키보드 방랑자";
        public const string FallbackEndingCode = "LAST_RESORT";

        public const string ArrivalCatalystRole = "ARRIVAL_CATALYST";
        public const string LocalStakesRole = "LOCAL_STAKES";
        public const string RelationshipConflictRole = "RELATIONSHIP_CONFLICT";
        public const string HiddenTruthRole = "HIDDEN_TRUTH";
        public const string ConsequenceReturnRole = "CONSEQUENCE_RETURN";
        public const string FinalConvergenceRole = "FINAL_CONVERGENCE";

        public static readonly string[] RoleIds =
        {
            ArrivalCatalystRole, LocalStakesRole, RelationshipConflictRole,
            HiddenTruthRole, ConsequenceReturnRole, FinalConvergenceRole
        };

        public static readonly string[] MilestoneTokenIds =
        {
            "MILESTONE_TOKEN_1", "MILESTONE_TOKEN_2", "MILESTONE_TOKEN_3"
        };

        public static readonly string[] FinaleComponentIds =
        {
            "finale.anchor", "finale.safeguard", "finale.passage", "finale.witness",
            "finale.freedom", "finale.threat", "finale.memory"
        };

        private sealed class Theme
        {
            public readonly string Id;
            public readonly string World;
            public readonly string Crisis;
            public readonly string Truth;
            public readonly string Return;
            public readonly string Motif;

            public Theme(string id, string world, string crisis, string truth, string consequence,
                string motif)
            {
                Id = id;
                World = world;
                Crisis = crisis;
                Truth = truth;
                Return = consequence;
                Motif = motif;
            }
        }

        private static readonly Theme[] Themes =
        {
            new Theme("tide", "잠든 조수의 군도", "밤마다 섬의 길이 물속으로 사라진다",
                "도시의 안전을 위해 지운 기억이 조류를 붙잡고 있었다", "잊힌 항해자들의 선택이 파도와 함께 돌아온다", "푸른 종과 소금빛 기록"),
            new Theme("ember", "꺼지지 않는 잿불령", "마을의 불씨가 서로의 온기를 빼앗기 시작했다",
                "보호 의식은 오래전 추방된 수호자의 약속을 훔쳐 만들어졌다", "버려진 약속이 산불의 형상으로 귀환한다", "재의 정원과 붉은 실"),
            new Theme("frost", "거울서리 고원", "얼음 거울이 주민들의 미래를 하나씩 고정한다",
                "예언은 발견된 것이 아니라 두려움으로 반복 작성된 기록이었다", "거부한 미래들이 눈보라 속에서 되돌아온다", "은빛 거울과 유리 새"),
            new Theme("root", "속삭이는 뿌리숲", "숲의 길이 방문자의 가장 소중한 관계를 삼킨다",
                "고대 나무는 침입자가 아니라 주민들의 비밀을 대신 기억해 왔다", "잘라 낸 관계들이 덩굴의 목소리로 돌아온다", "씨앗 문자와 초록 등불"),
            new Theme("clock", "멈춘 종탑도시", "해 질 무렵마다 같은 한 시간이 반복된다",
                "반복은 재난이 아니라 단 한 사람을 기다리기 위한 공동의 선택이었다", "미뤄 둔 작별들이 시계탑 아래 모여든다", "황동 톱니와 종이 새"),
            new Theme("echo", "메아리 동굴왕국", "말하지 않은 생각이 괴물의 목소리로 증폭된다",
                "괴물은 외부의 적이 아니라 감춘 진실이 만든 공동의 메아리다", "침묵시킨 증언들이 수정 벽에서 깨어난다", "수정 북과 검은 물"),
            new Theme("ruin", "별비 내리는 폐허원", "밤하늘의 파편이 오래된 건축물을 다시 움직인다",
                "폐허는 멸망의 흔적이 아니라 다음 시대를 시험하는 거대한 질문이었다", "과거의 답들이 수호상으로 돌아온다", "별가루 지도와 돌 열쇠"),
            new Theme("marsh", "달그림자 습지", "그림자를 잃은 이들이 서로의 이름을 잊어 간다",
                "그림자는 저주가 아니라 약속을 보관하던 두 번째 기억이었다", "파기된 맹세가 검은 연꽃으로 피어난다", "달빛 병과 연꽃 인장")
        };

        private static readonly string[] PlayerNames =
        {
            "초록 방랑자", "낯선 편집자", "열쇠를 든 여행자", "기억을 잇는 자",
            "길 위의 기록자", "균열을 걷는 자", "새벽의 조율자", "무명의 복원자"
        };

        private static readonly string[] NpcNamePool =
        {
            "마루", "세라", "도윤", "리오", "나린", "페이", "온", "루카",
            "하람", "이브", "소미", "테오", "아린", "로안", "유나", "카이"
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
