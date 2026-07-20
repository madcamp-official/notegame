using KeyboardWanderer.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// 현재 선택한 스킬과 대상, 실행 가능 여부를 한 곳에 보여 주는 HUD 컴포넌트다.
    /// 스킬 아이콘은 검증된 Ninja Adventure manifest에서 가져오고,
    /// 컨트롤러는 계산된 문구와 대상 Sprite만 전달한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererSelectionView : MonoBehaviour
    {
        private static readonly Color ReadyColor = new Color(0.83f, 0.65f, 0.29f, 1f);
        private static readonly Color BlockedColor = new Color(0.55f, 0.26f, 0.20f, 1f);
        private static readonly Color DimmedIconColor = new Color(0.48f, 0.48f, 0.48f, 0.72f);
        private static readonly Color MissingTargetColor = new Color(0.65f, 0.61f, 0.52f, 0.58f);

        [Header("Authoring references")]
        [SerializeField] private NinjaAdventureAssetManifest assetManifest;
        [SerializeField] private TMP_Text headingText;
        [SerializeField] private TMP_Text detailText;
        [SerializeField] private Image skillIcon;
        [SerializeField] private Image targetIcon;
        [SerializeField] private Image readinessBar;

        public bool IsReady => assetManifest != null && headingText != null && detailText != null &&
                               skillIcon != null && targetIcon != null && readinessBar != null;

        /// <summary>
        /// 선택 상태가 바뀔 때만 호출되어 프리팹에 배치된 표시 요소를 갱신한다.
        /// 대상이 없을 때는 상호작용 손 아이콘을 흐리게 보여 대상 선택이 필요함을 알린다.
        /// </summary>
        public void Present(AbilityKind ability, Sprite targetSprite, bool available, string heading, string detail)
        {
            if (!IsReady)
                return;

            Sprite abilitySprite = SkillIcon(ability);
            skillIcon.sprite = abilitySprite;
            skillIcon.enabled = abilitySprite != null;
            skillIcon.color = available ? Color.white : DimmedIconColor;

            bool hasTarget = targetSprite != null;
            targetIcon.sprite = hasTarget ? targetSprite : assetManifest.InteractIcon;
            targetIcon.enabled = targetIcon.sprite != null;
            targetIcon.color = hasTarget ? Color.white : MissingTargetColor;

            readinessBar.color = available ? ReadyColor : BlockedColor;
            headingText.color = available ? ReadyColor : Color.white;
            if (headingText.text != heading)
                headingText.text = heading ?? string.Empty;
            if (detailText.text != detail)
                detailText.text = detail ?? string.Empty;
        }

#if UNITY_EDITOR
        /// <summary>에디터 authoring 도구가 직렬화 참조를 한 번에 연결할 때 사용한다.</summary>
        public void Configure(NinjaAdventureAssetManifest manifest, TMP_Text heading, TMP_Text detail,
            Image abilityImage, Image targetImage, Image stateBar)
        {
            assetManifest = manifest;
            headingText = heading;
            detailText = detail;
            skillIcon = abilityImage;
            targetIcon = targetImage;
            readinessBar = stateBar;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private Sprite SkillIcon(AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.Copy: return assetManifest.CopyIcon;
                case AbilityKind.Delete: return assetManifest.DeleteIcon;
                case AbilityKind.Connect: return assetManifest.ConnectIcon;
                case AbilityKind.Restore: return assetManifest.RestoreIcon;
                case AbilityKind.Undo: return assetManifest.UndoIcon;
                case AbilityKind.Search: return assetManifest.SearchIcon;
                case AbilityKind.SelectAll: return assetManifest.SelectAllIcon;
                case AbilityKind.Move: return assetManifest.MoveIcon;
                default: return assetManifest.InteractIcon;
            }
        }
    }
}
