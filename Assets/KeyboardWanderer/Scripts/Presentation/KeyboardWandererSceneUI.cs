using System;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Presentation;
using UnityEngine;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// 씬의 UI View를 컨트롤러에 연결하는 얇은 진입점이다.
    /// 텍스트, 버튼, 화면 상태의 실제 소유권은 각 자식 오브젝트 컴포넌트에 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererSceneUI : MonoBehaviour
    {
        [Header("화면과 상위 패널")]
        [SerializeField] private KeyboardWandererScreenFlowView screenFlowView;
        [SerializeField] private KeyboardWandererTitleView titleView;
        [SerializeField] private KeyboardWandererGameHudView gameHudView;
        [SerializeField] private KeyboardWandererSettingsView settingsView;
        [SerializeField] private KeyboardWandererPauseView pauseView;
        [SerializeField] private KeyboardWandererEndingView endingView;

        [Header("게임 HUD 하위 패널")]
        [SerializeField] private KeyboardWandererDialogueView dialogueView;
        [SerializeField] private KeyboardWandererTutorialView tutorialView;
        [SerializeField] private KeyboardWandererSkillBarView skillBarView;
        [SerializeField] private KeyboardWandererSelectionView selectionView;
        [SerializeField] private KeyboardWandererMinimapView minimapView;
        private KeyboardWandererInventoryQuestView _inventoryQuestView;

        private bool _bound;

        public bool IsReady => screenFlowView != null && screenFlowView.IsReady &&
                               titleView != null && titleView.IsReady &&
                               gameHudView != null && gameHudView.IsReady &&
                               settingsView != null && settingsView.IsReady &&
                               pauseView != null && pauseView.IsReady &&
                               endingView != null && endingView.IsReady &&
                               dialogueView != null && dialogueView.IsReady &&
                               tutorialView != null && tutorialView.IsReady &&
                               skillBarView != null && skillBarView.IsReady &&
                               selectionView != null && selectionView.IsReady &&
                               minimapView != null && minimapView.IsReady;

        public int TutorialPageCount => tutorialView == null ? 0 : tutorialView.PageCount;

        private void OnValidate()
        {
#if UNITY_EDITOR
            AutoWire();
#endif
        }

        /// <summary>각 화면 오브젝트가 소유한 버튼 이벤트를 한 번만 연결한다.</summary>
        public void Bind(KeyboardWandererDemoController controller)
        {
            if (_bound || controller == null || !IsReady)
                return;
            titleView.Bind(controller.UiStartNewRun, controller.UiContinueRun, controller.UiOpenSettings);
            gameHudView.Bind(controller.UiSubmit);
            settingsView.Bind(controller.UiSetMusicVolume, controller.UiSetSfxVolume,
                controller.UiSetGmEnabled, controller.UiCloseSettings, controller.UiDeleteSave);
            pauseView.Bind(controller.UiResume, controller.UiOpenSettingsFromPause, controller.UiShowTitle);
            endingView.Bind(controller.UiStartNewRun, controller.UiShowTitle);
            dialogueView.Bind(controller.UiAdvanceDialogue, controller.UiSelectNarrativeChoice, controller.UiSubmitPlayerMessage);
            skillBarView.Bind(controller.UiSetAbility);
            _bound = true;
        }

        public void InitializeInventoryQuestOverlay(NinjaAdventureAssetManifest manifest,
            Action<string> itemSelected, Action<bool> visibilityChanged)
        {
            if (gameHudView == null || dialogueView == null) return;
            _inventoryQuestView = gameHudView.GetComponent<KeyboardWandererInventoryQuestView>();
            if (_inventoryQuestView == null)
                _inventoryQuestView = gameHudView.gameObject.AddComponent<KeyboardWandererInventoryQuestView>();
            _inventoryQuestView.Initialize(gameHudView.transform, dialogueView.StoryText?.font,
                manifest, itemSelected, visibilityChanged);
        }

        public void PresentInventoryAndQuests(RunPresentationModel run)
        {
            _inventoryQuestView?.Present(run?.Inventory, run?.Quests);
        }

        public bool ToggleInventory() => _inventoryQuestView != null && _inventoryQuestView.ToggleInventory();
        public bool ToggleQuests() => _inventoryQuestView != null && _inventoryQuestView.ToggleQuests();
        public bool CloseInventoryQuestOverlay() => _inventoryQuestView != null && _inventoryQuestView.Close();
        public bool IsInventoryQuestOverlayOpen => _inventoryQuestView != null && _inventoryQuestView.IsOpen;

        public bool InsertInventoryItemIntoDialogue(string itemName)
        {
            return dialogueView != null && dialogueView.InsertItemReference(itemName);
        }

        public void Show(bool title, bool settings, bool playing, bool paused, bool ended)
        {
            screenFlowView?.Present(title, settings, playing, paused, ended);
        }

        public void PresentTitle(string heading, string subtitle, string seed, string premise,
            string status, Sprite character, bool canStart, bool canContinue)
        {
            titleView?.Present(heading, subtitle, seed, premise, status, character, canStart, canContinue);
        }

        public void PresentHud(string location, string title, string objective, string actionHint)
        {
            gameHudView?.Present(location, title, objective, actionHint);
        }

        public void PresentQuestStatus(string questActionHint, string statusLabels, string statusValues)
        {
            gameHudView?.PresentQuestStatus(questActionHint, statusLabels, statusValues);
        }

        public void PresentConfirm(string label, bool interactable)
        {
            gameHudView?.PresentConfirm(label, interactable);
        }

        public void SetAbilityState(AbilityKind ability, bool interactable, bool selected)
        {
            skillBarView?.SetState(ability, interactable, selected);
        }

        public void SetMinimap(Sprite sprite, string status)
        {
            minimapView?.Present(sprite, status);
        }

        public void PresentSelection(AbilityKind ability, Sprite targetSprite, bool available,
            string heading, string detail)
        {
            selectionView?.Present(ability, targetSprite, available, heading, detail);
        }

        public void SetStoryVisible(bool visible)
        {
            dialogueView?.SetVisible(visible);
        }

        public void PresentDialogue(bool visible, string speaker, string story, string actionLabel, bool interactable,
            Sprite portrait = null, bool showLargeSubject = false)
        {
            dialogueView?.Present(visible, speaker, story, actionLabel, interactable, portrait, showLargeSubject);
        }

        public void PresentDialogueChoices(bool visible, NarrativeChoiceOption[] choices, bool interactable)
        {
            dialogueView?.PresentChoices(visible, choices, interactable);
            // Skills are narrative choices now. The persistent HUD shortcut bar
            // must never imply that they are available between story prompts.
            if (skillBarView != null && skillBarView.gameObject.activeSelf)
                skillBarView.gameObject.SetActive(false);
        }

        public void ReleaseDialogueChoiceInputLock()
        {
            dialogueView?.ReleaseChoiceInputLock();
        }

        public bool IsDialogueInputFocused => dialogueView != null && dialogueView.IsFreeformFocused;

        public void MoveDialogueChoiceSelection(int direction)
        {
            dialogueView?.MoveChoiceSelection(direction);
        }

        public void ConfirmDialogueChoiceSelection()
        {
            dialogueView?.ConfirmChoiceSelection();
        }

        public void PresentTutorial(int page, string objective)
        {
            tutorialView?.Present(page, objective);
        }

        public void PresentSettings(float musicVolume, float sfxVolume, bool gmEnabled)
        {
            settingsView?.Present(musicVolume, sfxVolume, gmEnabled);
        }

        public void SetCopyPasteMode(bool pasteMode)
        {
            skillBarView?.PresentShortcuts(pasteMode);
        }

        public void SetOutcomeEmote(string outcome)
        {
            gameHudView?.PresentOutcome(outcome);
        }

        public void PresentEnding(string heading, string body)
        {
            endingView?.Present(heading, body);
        }

#if UNITY_EDITOR
        public void AutoWire()
        {
            screenFlowView = GetComponent<KeyboardWandererScreenFlowView>();
            if (screenFlowView != null)
                screenFlowView.Configure(FindObject("Title Screen"), FindObject("Game HUD"),
                    FindObject("Settings Screen"), FindObject("Pause Screen"), FindObject("Ending Screen"));
            titleView = FindComponent<KeyboardWandererTitleView>("Title Screen");
            gameHudView = FindComponent<KeyboardWandererGameHudView>("Game HUD");
            settingsView = FindComponent<KeyboardWandererSettingsView>("Settings Screen");
            pauseView = FindComponent<KeyboardWandererPauseView>("Pause Screen");
            endingView = FindComponent<KeyboardWandererEndingView>("Ending Screen");
            dialogueView = FindComponent<KeyboardWandererDialogueView>("Story Panel");
            tutorialView = FindComponent<KeyboardWandererTutorialView>("Story Panel");
            skillBarView = FindComponent<KeyboardWandererSkillBarView>("Action Bar");
            selectionView = FindComponent<KeyboardWandererSelectionView>("Selection Panel");
            minimapView = FindComponent<KeyboardWandererMinimapView>("Minimap Panel");
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private T FindComponent<T>(string objectName) where T : Component
        {
            GameObject item = FindObject(objectName);
            return item != null ? item.GetComponent<T>() : null;
        }

        private GameObject FindObject(string objectName)
        {
            Transform[] items = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < items.Length; i++)
            {
                if (string.Equals(items[i].name, objectName, StringComparison.Ordinal))
                    return items[i].gameObject;
            }
            return null;
        }
#endif
    }
}
