using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Game HUD 루트가 목표·행동 안내와 실행 버튼을 소유한다.
    /// 스킬, 선택, 대화, 미니맵은 더 작은 하위 View가 각각 담당한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererGameHudView : MonoBehaviour
    {
        [SerializeField] private NinjaAdventureAssetManifest assetManifest;
        [SerializeField] private TMP_Text location;
        [SerializeField] private TMP_Text sceneTitle;
        [SerializeField] private TMP_Text objective;
        [SerializeField] private TMP_Text actionHint;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Image outcomeEmote;

        [Header("퀘스트 패널(선택)")]
        [SerializeField] private TMP_Text questHint;

        private bool _bound;

        public bool IsReady => assetManifest != null && objective != null && actionHint != null &&
                               confirmLabel != null &&
                               confirmButton != null && outcomeEmote != null;

        public void Bind(Action onConfirm)
        {
            if (_bound || !IsReady)
                return;
            if (onConfirm != null) confirmButton.onClick.AddListener(onConfirm.Invoke);
            _bound = true;
        }

        public void Present(string area, string title, string currentObjective, string guidance)
        {
            if (!IsReady)
                return;
            SetText(location, area);
            SetText(sceneTitle, title);
            SetText(objective, currentObjective);
            SetText(actionHint, guidance);
        }

        /// <summary>퀘스트 추천 행동 한 줄. 리스타일 전 프리팹에서는 필드가 비어 있어도 안전하다.</summary>
        public void PresentQuestStatus(string questActionHint)
        {
            SetText(questHint, questActionHint);
        }

        public void PresentConfirm(string label, bool interactable)
        {
            if (!IsReady)
                return;
            SetText(confirmLabel, label);
            confirmButton.interactable = interactable;
        }

        public void PresentOutcome(string outcome)
        {
            if (!IsReady || assetManifest.Emotes == null || assetManifest.Emotes.Length < 30)
                return;
            int number;
            switch ((outcome ?? string.Empty).ToUpperInvariant())
            {
                case "CRITICALSUCCESS":
                case "CRITICAL_SUCCESS": number = 27; break;
                case "SUCCESS": number = 11; break;
                case "PARTIALSUCCESS":
                case "PARTIAL_SUCCESS": number = 7; break;
                case "FAILURE": number = 13; break;
                case "CRITICALFAILURE":
                case "CRITICAL_FAILURE": number = 22; break;
                default: number = 23; break;
            }
            outcomeEmote.sprite = assetManifest.Emotes[number - 1];
        }

#if UNITY_EDITOR
        public void Configure(NinjaAdventureAssetManifest manifest, TMP_Text area, TMP_Text title,
            TMP_Text currentObjective, TMP_Text guidance, TMP_Text actionLabel,
            Button actionButton, Image emote)
        {
            assetManifest = manifest;
            location = area;
            sceneTitle = title;
            objective = currentObjective;
            actionHint = guidance;
            confirmLabel = actionLabel;
            confirmButton = actionButton;
            outcomeEmote = emote;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        public void ConfigureQuestHint(TMP_Text questActionHint)
        {
            questHint = questActionHint;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private static void SetText(TMP_Text target, string value)
        {
            value ??= string.Empty;
            if (target != null && target.text != value)
                target.text = value;
        }
    }
}
