using System;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using UnityEngine;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// 플레이어가 현재 선택한 스킬, 좌표, 대상과 복사 단계를 소유한다.
    /// 월드 규칙 판정이나 화면 렌더링은 담당하지 않고 선택 상태의 수명만 관리한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererSelectionController : MonoBehaviour
    {
        [SerializeField, Tooltip("Play Mode에서 현재 선택한 스킬입니다.")]
        private AbilityKind currentAbility = AbilityKind.Move;

        public AbilityKind Ability
        {
            get => currentAbility;
            internal set => currentAbility = value;
        }

        public GridCoord? SelectedCoord { get; internal set; }
        public Guid? SelectedTarget { get; internal set; }
        public Guid? SelectedSecondaryTarget { get; internal set; }
        public bool CopySourceCaptured { get; internal set; }
        public string Feedback { get; internal set; } = string.Empty;
        public int PoiCursor { get; internal set; } = -1;
        public string PoiLabel { get; internal set; } = "POI 탐색";

        /// <summary>새 게임이나 재개 시 선택 상태를 지정한 기본 스킬로 초기화한다.</summary>
        public void ResetSelection(AbilityKind ability)
        {
            currentAbility = ability;
            SelectedCoord = null;
            SelectedTarget = null;
            SelectedSecondaryTarget = null;
            CopySourceCaptured = false;
            Feedback = string.Empty;
            PoiCursor = -1;
            PoiLabel = "POI 탐색";
        }

        /// <summary>스킬을 바꾸고 이전 스킬에서 남은 대상과 좌표를 제거한다.</summary>
        public bool ChangeAbility(AbilityKind ability, Guid? restoreTarget = null, GridCoord? restoreCoord = null)
        {
            if (currentAbility == ability)
                return false;
            currentAbility = ability;
            CopySourceCaptured = false;
            SelectedTarget = ability == AbilityKind.Restore ? restoreTarget : null;
            SelectedSecondaryTarget = null;
            SelectedCoord = ability == AbilityKind.Restore ? restoreCoord : null;
            Feedback = string.Empty;
            return true;
        }

        /// <summary>월드에서 첫 번째 대상을 선택하고 필요하면 대상 좌표도 함께 기록한다.</summary>
        public void SelectPrimary(Guid? target, GridCoord? coord = null)
        {
            SelectedTarget = target;
            SelectedSecondaryTarget = null;
            if (coord.HasValue)
                SelectedCoord = coord;
        }

        /// <summary>Connect 스킬의 두 번째 대상을 기록한다.</summary>
        public void SelectSecondary(Guid? target)
        {
            SelectedSecondaryTarget = target;
        }

        /// <summary>이동 또는 배치 목적 좌표를 기록한다.</summary>
        public void SelectDestination(GridCoord? coord)
        {
            SelectedCoord = coord;
        }

        /// <summary>Move 상태에서 상호작용 가능한 오브젝트를 직접 눌렀을 때의 선택을 만든다.</summary>
        public void SelectInteraction(Guid target, GridCoord coord, string feedback)
        {
            currentAbility = AbilityKind.Interact;
            SelectedTarget = target;
            SelectedSecondaryTarget = null;
            SelectedCoord = coord;
            CopySourceCaptured = false;
            Feedback = feedback ?? string.Empty;
        }

        /// <summary>통행 가능한 이동 목적지를 선택한다.</summary>
        public void SelectMovement(GridCoord coord)
        {
            currentAbility = AbilityKind.Move;
            SelectedCoord = coord;
            SelectedTarget = null;
            SelectedSecondaryTarget = null;
            CopySourceCaptured = false;
            Feedback = string.Empty;
        }

        /// <summary>Copy의 원본 오브젝트를 선택해 Ctrl V 배치 단계로 전환한다.</summary>
        public void SelectCopySource(Guid target, string feedback)
        {
            SelectedTarget = target;
            SelectedSecondaryTarget = null;
            SelectedCoord = null;
            CopySourceCaptured = true;
            Feedback = feedback ?? string.Empty;
        }

        /// <summary>Copy 원본을 놓을 목적 좌표를 선택한다.</summary>
        public void SelectCopyDestination(GridCoord coord, string feedback)
        {
            SelectedCoord = coord;
            Feedback = feedback ?? string.Empty;
        }

        /// <summary>Connect 대상을 순서대로 기록하고 두 번째 대상까지 선택됐는지 반환한다.</summary>
        public bool SelectConnectTarget(Guid target, GridCoord coord)
        {
            if (!SelectedTarget.HasValue || SelectedTarget.Value == target)
            {
                SelectedTarget = target;
                SelectedSecondaryTarget = null;
            }
            else
            {
                SelectedSecondaryTarget = target;
            }
            SelectedCoord = coord;
            return SelectedSecondaryTarget.HasValue;
        }

        /// <summary>Delete, Search 같은 단일 대상 스킬의 좌표와 대상을 기록한다.</summary>
        public void SelectSingleTarget(Guid? target, GridCoord coord, string feedback)
        {
            SelectedCoord = coord;
            SelectedTarget = target;
            SelectedSecondaryTarget = null;
            Feedback = feedback ?? string.Empty;
        }

        /// <summary>선택이 거부된 이유만 바꾸고 기존 선택은 유지한다.</summary>
        public void Reject(string feedback)
        {
            Feedback = feedback ?? string.Empty;
        }

        /// <summary>POI 순환으로 찾은 좌표와 표시 문구를 함께 기록한다.</summary>
        public void SelectPoi(int cursor, GridCoord coord, string label)
        {
            PoiCursor = cursor;
            PoiLabel = label ?? string.Empty;
            SelectedCoord = coord;
            SelectedTarget = null;
            SelectedSecondaryTarget = null;
        }

        /// <summary>월드 이동이 끝난 뒤 사건 배치 좌표만 남기고 대상은 비운다.</summary>
        public void ClearToCoord(GridCoord? coord)
        {
            SelectedCoord = coord;
            SelectedTarget = null;
            SelectedSecondaryTarget = null;
            Feedback = string.Empty;
        }

        /// <summary>행동이 끝난 뒤 대상 선택을 비우고 필요한 경우 이동 좌표만 유지한다.</summary>
        public void ClearAfterAction(GridCoord? retainedCoord, bool retainCoord)
        {
            SelectedTarget = null;
            SelectedSecondaryTarget = null;
            SelectedCoord = retainCoord ? retainedCoord : null;
            Feedback = string.Empty;
        }

        /// <summary>복구 가능한 대상이 사라졌을 때 Restore 선택도 함께 비운다.</summary>
        public void ClearRestoreSelection()
        {
            if (currentAbility != AbilityKind.Restore)
                return;
            SelectedTarget = null;
            SelectedCoord = null;
            Feedback = string.Empty;
        }
    }
}
