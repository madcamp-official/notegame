using System;
using KeyboardWanderer.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Action Bar 오브젝트가 자신의 스킬 버튼, 단축키 문구, 선택 표시를 직접 소유한다.
    /// 루트 UI는 어떤 Button이 F 조사인지 알 필요 없이 AbilityKind 상태만 전달한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererSkillBarView : MonoBehaviour
    {
        [Serializable]
        public struct AbilityBinding
        {
            public AbilityKind Ability;
            public Button Button;
            public KeyboardWandererButtonStateView StateView;
        }

        [SerializeField] private NinjaAdventureAssetManifest assetManifest;
        [SerializeField] private AbilityBinding[] abilities = Array.Empty<AbilityBinding>();
        [SerializeField] private TMP_Text copyLabel;
        [SerializeField] private TMP_Text deleteLabel;
        [SerializeField] private TMP_Text undoLabel;
        [SerializeField] private Image copyKeycap;

        private bool _bound;

        public bool IsReady
        {
            get
            {
                if (assetManifest == null || abilities == null || abilities.Length != 8 ||
                    copyLabel == null || deleteLabel == null || undoLabel == null || copyKeycap == null)
                    return false;
                for (int i = 0; i < abilities.Length; i++)
                {
                    if (abilities[i].Button == null || abilities[i].StateView == null)
                        return false;
                }
                return true;
            }
        }

        /// <summary>각 스킬 버튼 클릭을 AbilityKind 선택 이벤트로 한 번만 연결한다.</summary>
        public void Bind(Action<AbilityKind> onAbilitySelected)
        {
            if (_bound || onAbilitySelected == null)
                return;
            for (int i = 0; i < abilities.Length; i++)
            {
                AbilityKind ability = abilities[i].Ability;
                abilities[i].Button.onClick.AddListener(() => onAbilitySelected(ability));
                TMP_Text[] labels = abilities[i].Button.GetComponentsInChildren<TMP_Text>(true);
                for (int j = 0; j < labels.Length; j++)
                    if (labels[j] != null && labels[j].name.IndexOf("Skill Label", StringComparison.OrdinalIgnoreCase) >= 0)
                        labels[j].text = SkillLabel(ability, false);
            }
            _bound = true;
        }

        public void SetState(AbilityKind ability, bool interactable, bool selected)
        {
            for (int i = 0; i < abilities.Length; i++)
            {
                if (abilities[i].Ability != ability)
                    continue;
                if (abilities[i].Button.interactable != interactable)
                    abilities[i].Button.interactable = interactable;
                abilities[i].StateView.SetSelected(selected);
                return;
            }
        }

        /// <summary>Copy가 Paste 단계로 바뀌는 상태까지 Action Bar 내부에서 표현한다.</summary>
        public void PresentShortcuts(bool pasteMode)
        {
            if (!IsReady)
                return;
            copyLabel.text = SkillLabel(AbilityKind.Copy, pasteMode);
            deleteLabel.text = SkillLabel(AbilityKind.Delete, false);
            undoLabel.text = SkillLabel(AbilityKind.Undo, false);
            copyKeycap.sprite = pasteMode ? assetManifest.KeyV : assetManifest.KeyC;
        }

        private static string SkillLabel(AbilityKind ability, bool pasteMode)
        {
            switch (ability)
            {
                case AbilityKind.Move: return "W  이동";
                case AbilityKind.Copy: return pasteMode ? "Ctrl V  배치" : "E  복제";
                case AbilityKind.Delete: return "R  공격";
                case AbilityKind.Connect: return "C  연결";
                case AbilityKind.Restore: return "X  복구";
                case AbilityKind.Undo: return "Z  되돌리기";
                case AbilityKind.Search: return "F  조사";
                case AbilityKind.SelectAll: return "Ctrl A  범위 공격";
                default: return ability.ToString();
            }
        }

#if UNITY_EDITOR
        public void Configure(NinjaAdventureAssetManifest manifest, AbilityBinding[] bindings,
            TMP_Text copy, TMP_Text delete, TMP_Text undo, Image keycap)
        {
            assetManifest = manifest;
            abilities = bindings ?? Array.Empty<AbilityBinding>();
            copyLabel = copy;
            deleteLabel = delete;
            undoLabel = undo;
            copyKeycap = keycap;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
