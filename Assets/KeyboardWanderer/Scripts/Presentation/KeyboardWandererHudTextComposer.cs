using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;

namespace KeyboardWanderer.Presentation
{
    /// <summary>
    /// 정규화된 런 상태를 플레이어가 읽는 HUD 문장으로 변환한다.
    /// 컨트롤러가 서버 DTO, 조사 목표 탐색, 한글 키 이름 조합을 직접 담당하지 않게 한다.
    /// </summary>
    public static class KeyboardWandererHudTextComposer
    {
        public static string SecondaryObjectives(RunPresentationModel run)
        {
            var objectives = new List<string>();
            if (run?.OpenLoops != null)
            {
                for (int i = 0; i < run.OpenLoops.Count && objectives.Count < 2; i++)
                {
                    string value = run.OpenLoops[i];
                    if (!string.IsNullOrWhiteSpace(value) &&
                        !string.Equals(value, run.StoryObjective, StringComparison.Ordinal))
                        objectives.Add("• " + value.Trim());
                }
            }
            if (objectives.Count == 0)
                objectives.Add("• 현재 보조 목표 없음");
            if (run?.EndingBoard != null && run.EndingBoard.Count > 0)
            {
                RunPresentationEnding nearest = run.EndingBoard[0];
                string missing = nearest.IsEligible
                    ? "모든 조건 충족"
                    : nearest.MissingConditions.Count > 0 ? nearest.MissingConditions[0] : "조건 확인 필요";
                objectives.Add("◆ 가장 가까운 결말 · " + nearest.Title + " " + nearest.SatisfiedCount +
                               "/" + nearest.TotalCount + " · " + missing);
            }
            return string.Join("\n", objectives);
        }

        public static AbilityKind[] RecommendedActions(RunPresentationModel run, bool encounterMoveRequired)
        {
            var values = new List<AbilityKind>();
            AbilityKind objectiveSkill = run?.ObjectiveAbility ?? AbilityKind.Copy;
            bool objectiveOutOfRange = run?.ObjectiveTargetPosition.HasValue == true &&
                                       run.Core.PlayerPosition.ManhattanDistance(run.ObjectiveTargetPosition.Value) >
                                       ObjectiveAbilityRange(objectiveSkill);
            if (!encounterMoveRequired && objectiveOutOfRange)
                values.Add(AbilityKind.Move);
            if (!values.Contains(objectiveSkill))
                values.Add(objectiveSkill);
            if (!encounterMoveRequired && !values.Contains(AbilityKind.Move))
                values.Add(AbilityKind.Move);
            AbilityKind contextual = encounterMoveRequired ? AbilityKind.Delete : AbilityKind.Connect;
            if (!values.Contains(contextual))
                values.Add(contextual);
            if (values.Count < 2)
                values.Add(AbilityKind.Restore);
            return values.ToArray();
        }

        private const string QuestNumberColor = "#F2C14E";
        private const string QuestDetailColor = "#B8A382";

        /// <summary>
        /// 퀘스트 패널 본문을 번호 목록으로 만든다. 1번은 주 목표와 위치 세부 항목,
        /// 이후 번호는 보조 목표, ◆는 가장 가까운 결말 진행도다. 진행·자원 수치는 상태 패널이 맡는다.
        /// </summary>
        public static string ObjectiveHud(RunPresentationModel run)
        {
            if (run == null)
                return string.Empty;
            var lines = new List<string>();
            string title = string.IsNullOrWhiteSpace(run.StoryObjective)
                ? "다음 이야기를 기다리는 중"
                : run.StoryObjective.Trim();
            lines.Add("<color=" + QuestNumberColor + ">1</color>  " + title);
            if (run.ObjectiveTargetPosition.HasValue)
            {
                GridCoord target = run.ObjectiveTargetPosition.Value;
                int distance = run.Core.PlayerPosition.ManhattanDistance(target);
                string name = string.IsNullOrWhiteSpace(run.ObjectiveTargetName)
                    ? "표시된 대상"
                    : run.ObjectiveTargetName.Trim();
                lines.Add("<color=" + QuestDetailColor + ">     • " + name + " · " +
                          DirectionLabel(run.Core.PlayerPosition, target) + " " + distance + "칸</color>");
                lines.Add("<color=" + QuestDetailColor + ">     • 미니맵 분홍 표식을 따라가세요</color>");
            }
            int number = 2;
            if (run.OpenLoops != null)
            {
                for (int i = 0; i < run.OpenLoops.Count && number <= 3; i++)
                {
                    string value = run.OpenLoops[i];
                    if (string.IsNullOrWhiteSpace(value) ||
                        string.Equals(value, run.StoryObjective, StringComparison.Ordinal))
                        continue;
                    lines.Add("<color=" + QuestNumberColor + ">" + number + "</color>  " + value.Trim());
                    number++;
                }
            }
            if (run.EndingBoard != null && run.EndingBoard.Count > 0)
            {
                RunPresentationEnding nearest = run.EndingBoard[0];
                lines.Add("<color=" + QuestNumberColor + ">◆</color>  결말 · " + nearest.Title +
                          "  <color=" + QuestDetailColor + ">" + nearest.SatisfiedCount + "/" +
                          nearest.TotalCount + "</color>");
            }
            return string.Join("\n", lines);
        }

        /// <summary>퀘스트 패널 하단의 추천 행동 한 줄.</summary>
        public static string QuestActionHint(RunPresentationModel run, bool encounterMoveRequired)
        {
            if (run == null)
                return string.Empty;
            AbilityKind[] recommendations = RecommendedActions(run, encounterMoveRequired);
            var labels = new string[recommendations.Length];
            for (int i = 0; i < recommendations.Length; i++)
                labels[i] = AbilityPlayerLabel(recommendations[i]);
            return "추천  " + string.Join(" > ", labels);
        }

        /// <summary>상태 패널 왼쪽 컬럼. <see cref="StatusValues"/>와 줄 순서가 일치해야 한다.</summary>
        public static string StatusLabels()
        {
            return "권한\n의미 턴\nFOCUS\nXP\nGOLD";
        }

        /// <summary>상태 패널 오른쪽 컬럼. <see cref="StatusLabels"/>와 줄 순서가 일치해야 한다.</summary>
        public static string StatusValues(RunPresentationModel run)
        {
            if (run == null)
                return string.Empty;
            return run.AdminAccess + " / 3\n" +
                   run.Core.Turn + " / " + run.TurnLimit + "\n" +
                   run.Focus + " / " + run.MaxFocus + "\n" +
                   run.Experience + "\n" +
                   run.Gold;
        }

        /// <summary>현재 목표가 스킬 사거리 밖일 때 이동 방향을 한 줄로 안내한다.</summary>
        public static string ObjectiveRouteHint(RunPresentationModel run)
        {
            if (run?.ObjectiveTargetPosition.HasValue != true)
                return string.Empty;
            GridCoord target = run.ObjectiveTargetPosition.Value;
            int distance = run.Core.PlayerPosition.ManhattanDistance(target);
            if (distance <= ObjectiveAbilityRange(run.ObjectiveAbility))
                return string.Empty;
            return "현재 목표 " + run.ObjectiveTargetName + " · " +
                   DirectionLabel(run.Core.PlayerPosition, target) + " " + distance + "칸\n";
        }

        public static int ObjectiveAbilityRange(AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.Search: return 6;
                case AbilityKind.Delete: return 3;
                case AbilityKind.Copy:
                case AbilityKind.Connect:
                case AbilityKind.Restore: return 4;
                default: return 0;
            }
        }

        public static string ObjectiveActionText(string targetName, AbilityKind ability)
        {
            string name = string.IsNullOrWhiteSpace(targetName) ? "표시된 대상" : targetName.Trim();
            switch (ability)
            {
                case AbilityKind.Search: return WithObjectParticle(name) + " 찾아 Ctrl F로 조사하세요.";
                case AbilityKind.Delete: return WithObjectParticle(name) + " 찾아 Delete로 삭제하세요.";
                case AbilityKind.Copy: return WithObjectParticle(name) + " 찾아 Ctrl C로 복제하세요.";
                case AbilityKind.Connect: return name + "와 연결할 대상을 찾아 Ctrl K로 연결하세요.";
                case AbilityKind.Restore: return WithObjectParticle(name) + " Ctrl R로 복구하세요.";
                default: return name + "에게 이동하세요.";
            }
        }

        public static string DirectionLabel(GridCoord from, GridCoord to)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            string vertical = dy > 0 ? "북" : dy < 0 ? "남" : string.Empty;
            string horizontal = dx > 0 ? "동" : dx < 0 ? "서" : string.Empty;
            string direction = vertical + horizontal;
            return string.IsNullOrEmpty(direction) ? "현재 위치" : direction + "쪽";
        }

        public static string AbilityPlayerLabel(AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.Move: return "W 이동";
                case AbilityKind.Copy: return "Ctrl C 복제";
                case AbilityKind.Delete: return "Delete 단일 삭제";
                case AbilityKind.Connect: return "Ctrl K 연결";
                case AbilityKind.Restore: return "Ctrl R 복구";
                case AbilityKind.Undo: return "Ctrl Z 2턴 역행";
                case AbilityKind.Search: return "Ctrl F 조사";
                case AbilityKind.SelectAll: return "Ctrl A 범위 공격";
                default: return ability.ToString();
            }
        }

        public static string ShortNarrative(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "확정된 짧은 서사가 없습니다.";
            string value = text.Trim();
            int sentences = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character != '.' && character != '!' && character != '?')
                    continue;
                sentences++;
                if (sentences == 4)
                    return value.Substring(0, i + 1);
            }
            return value;
        }

        private static string WithObjectParticle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "대상을";
            string text = value.Trim();
            char last = text[text.Length - 1];
            if (last >= '\uAC00' && last <= '\uD7A3')
                return text + ((last - '\uAC00') % 28 == 0 ? "를" : "을");
            return text + "을(를)";
        }
    }
}
