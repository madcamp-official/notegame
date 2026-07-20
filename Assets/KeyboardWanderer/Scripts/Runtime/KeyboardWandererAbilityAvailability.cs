using System;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Presentation;
using UnityEngine;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// 현재 런 상태와 선택 상태를 바탕으로 키보드 스킬을 사용할 수 있는지 판정한다.
    /// 로컬 저장 모델이나 서버 DTO를 직접 읽지 않아 두 실행 경로가 같은 UI 규칙을 사용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererAbilityAvailability : MonoBehaviour
    {
        /// <summary>스킬을 실행할 때 필요한 집중력 수치를 반환한다.</summary>
        public int FocusCost(AbilityKind skill)
        {
            switch (skill)
            {
                case AbilityKind.Move:
                case AbilityKind.Interact:
                    return 0;
                case AbilityKind.Copy:
                case AbilityKind.Delete:
                case AbilityKind.Search:
                    return 1;
                case AbilityKind.Connect:
                case AbilityKind.Restore:
                    return 2;
                case AbilityKind.Undo:
                case AbilityKind.SelectAll:
                    return 3;
                default:
                    return int.MaxValue;
            }
        }

        /// <summary>
        /// 화면 출처와 관계없이 동일한 거리, 대상 종류, 집중력 규칙으로 스킬 가능 여부를 계산한다.
        /// localReversibleTurns는 로컬 Undo에서만 사용하며 서버 실행에서는 현재 턴을 기준으로 판단한다.
        /// </summary>
        public bool CanUse(AbilityKind skill, RunPresentationModel run,
            KeyboardWandererSelectionController selection, Guid? lastRestorableTarget,
            int localReversibleTurns)
        {
            if (run == null || selection == null || ReferenceEquals(run, RunPresentationModel.Empty))
                return false;
            if (skill == AbilityKind.Move)
                return true;

            int focusCost = FocusCost(skill);
            if (focusCost == int.MaxValue || run.Focus < focusCost)
                return false;

            if (skill == AbilityKind.Undo)
                return run.IsServerAuthoritative ? run.Core.Turn >= 2 : localReversibleTurns >= 2;
            if (skill == AbilityKind.SelectAll)
                return run.ActiveEnemiesWithin(4) > 0;

            if (!selection.SelectedTarget.HasValue)
                return true;
            RunPresentationEntity target = run.FindEntity(selection.SelectedTarget);
            if (target == null)
                return false;
            if (!string.IsNullOrWhiteSpace(target.RequiredSkillId) &&
                !string.Equals(target.RequiredSkillId, skill.ToString(), StringComparison.OrdinalIgnoreCase))
                return false;

            int distance = run.DistanceFromPlayer(target);
            switch (skill)
            {
                case AbilityKind.Copy:
                    return target.CanCopy && distance <= 4 &&
                           (!selection.SelectedCoord.HasValue ||
                            run.Core.PlayerPosition.ManhattanDistance(selection.SelectedCoord.Value) <= 4);
                case AbilityKind.Delete:
                    return target.CanDelete && run.AdminAccess >= target.DeleteRequiredAdminAccess &&
                           target.IsActive && target.Health > 0 && distance <= 3;
                case AbilityKind.Search:
                    return distance <= 6;
                case AbilityKind.Connect:
                    return CanConnect(run, selection, target, distance);
                case AbilityKind.Restore:
                    return target.CanRestore && distance <= 4 &&
                           (lastRestorableTarget == target.Id ||
                            EntityCapabilityCatalog.IsAdministratorAccessCandidate(target.AssetId));
                case AbilityKind.Interact:
                    return target.CanInteract && distance <= 2;
                default:
                    return false;
            }
        }

        private static bool CanConnect(RunPresentationModel run,
            KeyboardWandererSelectionController selection, RunPresentationEntity first, int firstDistance)
        {
            if (!first.CanConnect)
                return false;
            if (!selection.SelectedSecondaryTarget.HasValue)
                return true;
            RunPresentationEntity second = run.FindEntity(selection.SelectedSecondaryTarget);
            if (second == null || !second.CanConnect)
                return false;
            int secondDistance = run.DistanceFromPlayer(second);
            return (firstDistance <= 4 || secondDistance <= 4) &&
                   first.Position.ManhattanDistance(second.Position) <= 12;
        }
    }
}
