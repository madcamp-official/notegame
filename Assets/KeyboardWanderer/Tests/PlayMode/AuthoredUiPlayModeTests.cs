using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Presentation;
using KeyboardWanderer.Runtime;
using KeyboardWanderer.World;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KeyboardWanderer.Tests.PlayMode
{
    public sealed class AuthoredUiPlayModeTests
    {
        private KeyboardWandererSceneUI _sceneUi;
        private KeyboardWandererDemoController _controller;

        [UnitySetUp]
        public IEnumerator LoadAuthoredScene()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;
            _sceneUi = Object.FindAnyObjectByType<KeyboardWandererSceneUI>(FindObjectsInactive.Include);
            _controller = Object.FindAnyObjectByType<KeyboardWandererDemoController>(FindObjectsInactive.Include);
            Assert.That(_sceneUi, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            Assert.That(_controller.GetComponent<KeyboardWandererInputRouter>(), Is.Not.Null,
                "게임 루트에 키보드·포인터 입력 전용 InputRouter가 있어야 합니다.");
            Assert.That(_controller.GetComponent<KeyboardWandererSelectionController>(), Is.Not.Null,
                "게임 루트에 선택 상태 전용 SelectionController가 있어야 합니다.");
            Assert.That(_controller.GetComponent<KeyboardWandererAbilityAvailability>(), Is.Not.Null,
                "게임 루트에 로컬·서버 공통 스킬 판정 컴포넌트가 있어야 합니다.");
            Assert.That(Object.FindAnyObjectByType<KeyboardWandererMinimapRenderer>(FindObjectsInactive.Include),
                Is.Not.Null, "Authored World에 미니맵 렌더링 전용 컴포넌트가 있어야 합니다.");
            Assert.That(Object.FindAnyObjectByType<KeyboardWandererPathPlanner>(FindObjectsInactive.Include),
                Is.Not.Null, "Authored World에 이동 경로 계산 전용 컴포넌트가 있어야 합니다.");
            Assert.That(_controller.GetComponent<KeyboardWandererRunSessionController>(), Is.Not.Null,
                "게임 루트에 새 게임·이어하기 전용 RunSessionController가 있어야 합니다.");
            Assert.That(_controller.GetComponent<KeyboardWandererSettingsController>(), Is.Not.Null,
                "게임 루트에 사용자 설정 저장 전용 SettingsController가 있어야 합니다.");
            Assert.That(Object.FindAnyObjectByType<KeyboardWandererVisualAssetLibrary>(FindObjectsInactive.Include),
                Is.Not.Null, "Authored World에 시각 에셋 생성 전용 VisualAssetLibrary가 있어야 합니다.");
        }

        [Test]
        public void AuthoredUi_UsesSerializedTmpMinimapAndButtonStateViews()
        {
            Assert.That(_sceneUi.IsReady, Is.True);
            Assert.That(_sceneUi.GetComponentsInChildren<Text>(true), Is.Empty);

            Assert.That(_sceneUi.GetComponent<KeyboardWandererScreenFlowView>()?.IsReady, Is.True,
                "Authored UI 루트가 화면 활성 상태만 소유해야 합니다.");
            Assert.That(Find(_sceneUi.transform, "Title Screen")
                ?.GetComponent<KeyboardWandererTitleView>()?.IsReady, Is.True);
            Assert.That(Find(_sceneUi.transform, "Game HUD")
                ?.GetComponent<KeyboardWandererGameHudView>()?.IsReady, Is.True);
            Assert.That(Find(_sceneUi.transform, "Pause Screen")
                ?.GetComponent<KeyboardWandererPauseView>()?.IsReady, Is.True);
            Assert.That(Find(_sceneUi.transform, "Ending Screen")
                ?.GetComponent<KeyboardWandererEndingView>()?.IsReady, Is.True);

            TMP_Text[] texts = _sceneUi.GetComponentsInChildren<TMP_Text>(true);
            Assert.That(texts.Length, Is.GreaterThan(20));
            Assert.That(texts, Has.All.Matches<TMP_Text>(text =>
                text.font != null && text.font.name.Contains("NeoDunggeunmoPro")));

            Transform minimap = Find(_sceneUi.transform, "Minimap Map");
            Assert.That(minimap, Is.Not.Null);
            Assert.That(minimap.GetComponent<Image>(), Is.Not.Null);
            Assert.That(Find(_sceneUi.transform, "Runtime Minimap"), Is.Null);
            Assert.That(Find(_sceneUi.transform, "Minimap Panel")
                .GetComponent<KeyboardWandererMinimapView>()?.IsReady, Is.True,
                "미니맵 패널이 자체 표시 컴포넌트와 직렬화 참조를 소유해야 합니다.");

            var dialogueView = Find(_sceneUi.transform, "Story Panel")
                .GetComponent<KeyboardWandererDialogueView>();
            Assert.That(dialogueView, Is.Not.Null,
                "대화 상태를 소유하는 컴포넌트가 Story Panel에 있어야 합니다.");
            Assert.That(dialogueView.IsReady, Is.True,
                "대화 텍스트와 Next 버튼은 Inspector 참조로 연결되어야 합니다.");
            Assert.That(Find(_sceneUi.transform, "Story Panel")
                ?.GetComponent<KeyboardWandererTutorialView>()?.IsReady, Is.True,
                "튜토리얼 문구는 Story Panel 컴포넌트에서 편집할 수 있어야 합니다.");

            var inventoryHudView = Find(_sceneUi.transform, "Inventory Panel")
                ?.GetComponent<KeyboardWandererInventoryHudView>();
            Assert.That(inventoryHudView, Is.Not.Null,
                "Game HUD에 상시 노출되는 소지품 요약 Inventory Panel이 있어야 합니다.");
            Assert.That(inventoryHudView.IsReady, Is.True,
                "Inventory Panel의 아이콘 슬롯은 Inspector 참조로 연결되어야 합니다.");
            Assert.That(Find(_sceneUi.transform, "Story Header"), Is.Null,
                "좌측 최상단 캐릭터 상태창은 Game HUD에 남아 있으면 안 됩니다.");

            var skillBarView = Find(_sceneUi.transform, "Action Bar")
                ?.GetComponent<KeyboardWandererSkillBarView>();
            Assert.That(skillBarView, Is.Not.Null,
                "Action Bar가 스킬 버튼 상태를 직접 소유해야 합니다.");
            Assert.That(skillBarView.IsReady, Is.True,
                "Action Bar의 버튼, 상태 표시, 단축키 참조는 Inspector에서 연결되어야 합니다.");

            var settingsView = Find(_sceneUi.transform, "Settings Screen")
                ?.GetComponent<KeyboardWandererSettingsView>();
            Assert.That(settingsView, Is.Not.Null,
                "Settings Screen이 슬라이더와 GM 토글을 직접 소유해야 합니다.");
            Assert.That(settingsView.IsReady, Is.True,
                "Settings Screen의 설정 컨트롤은 Inspector에서 연결되어야 합니다.");

            Button delete = Find(_sceneUi.transform, "Delete Skill Button").GetComponent<Button>();
            var stateView = delete.GetComponent<KeyboardWandererButtonStateView>();
            Outline outline = delete.targetGraphic.GetComponent<Outline>();
            Assert.That(stateView, Is.Not.Null);
            Assert.That(outline, Is.Not.Null);

            int componentCount = delete.GetComponents<Component>().Length;
            _sceneUi.SetAbilityState(AbilityKind.Delete, true, true);
            Assert.That(outline.enabled, Is.True);
            Assert.That(delete.transform.localScale.x, Is.GreaterThan(1f));
            _sceneUi.SetAbilityState(AbilityKind.Delete, false, false);
            Assert.That(outline.enabled, Is.False);
            Assert.That(delete.GetComponents<Component>().Length, Is.EqualTo(componentCount));
        }

        [UnityTest]
        public IEnumerator NewRunButtonAndDialogue_ChangeVisibleAuthoredState()
        {
            // 머신의 PlayerPrefs 상태와 무관하게 튜토리얼이 대화 패널을 가리지 않도록 고정한다.
            PlayerPrefs.SetInt("keyboard-wanderer.tutorial-v1-complete", 1);
            Button newRun = Find(_sceneUi.transform, "New Run Button").GetComponent<Button>();
            newRun.onClick.Invoke();
            yield return null;

            GameObject gameHud = Find(_sceneUi.transform, "Game HUD").gameObject;
            Assert.That(gameHud.activeSelf || (bool)GetField(_controller, "_serverPending"), Is.True,
                "New Run button did not start either a server request or a local run.");
            if (!gameHud.activeSelf)
            {
                _controller.StopAllCoroutines();
                Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7303), false);
                yield return null;
            }
            Assert.That(gameHud.activeSelf, Is.True);
            // Search가 목표 스킬인 seed에서는 스토리 대화 페이지가 서사를 대신하므로 스킬을 고정한다.
            _controller.GetComponent<KeyboardWandererSelectionController>().ResetSelection(AbilityKind.Move);
            SetField(_controller, "_lastOutcome", "SUCCESS");
            SetField(_controller, "_lastNarrative", "첫 번째 이야기");
            SetField(_controller, "_lastDialogue", new[] { "두 번째 대화", "세 번째 대화" });
            ((DialoguePresenter)GetField(_controller, "_dialoguePresenter")).Reset();
            Invoke(_controller, "UpdateAuthoredUi");

            TMP_Text story = Find(_sceneUi.transform, "Story Text").GetComponent<TMP_Text>();
            string first = story.text;
            Find(_sceneUi.transform, "Next Dialogue Button").GetComponent<Button>().onClick.Invoke();
            Invoke(_controller, "UpdateAuthoredUi");
            Assert.That(story.text, Is.Not.EqualTo(first));
            Assert.That(story.text, Is.EqualTo("첫 번째 이야기"));
        }

        [UnityTest]
        public IEnumerator Minimap_RedrawsWhenEnemySelectionChanges()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7306);
            Invoke(_controller, "StartRun", service, false);
            yield return null;
            KeyboardWandererMinimapRenderer renderer =
                Object.FindAnyObjectByType<KeyboardWandererMinimapRenderer>(FindObjectsInactive.Include);
            Assert.That(renderer, Is.Not.Null);
            string before = renderer.Signature;
            EntityView enemy = service.CurrentView.Entities.First(entity => entity.IsHostile);

            _controller.GetComponent<KeyboardWandererSelectionController>().SelectPrimary(enemy.EntityId);
            yield return null;

            Assert.That(renderer.Signature, Is.Not.EqualTo(before));
            Assert.That(renderer.Signature, Does.Contain(enemy.EntityId.ToString("N")));
        }

        [UnityTest]
        public IEnumerator AuthoredHud_CapturesSixteenByNineAndFourByThree()
        {
            if (System.Environment.GetEnvironmentVariable("KEYBOARD_WANDERER_CAPTURE_SCREENSHOTS") != "1")
                Assert.Ignore("Set KEYBOARD_WANDERER_CAPTURE_SCREENSHOTS=1 for graphical screenshot validation.");

            Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7305), false);
            yield return null;
            Camera camera = Object.FindAnyObjectByType<Camera>();
            Canvas canvas = _sceneUi.GetComponentInParent<Canvas>();
            Assert.That(camera, Is.Not.Null);
            Assert.That(canvas, Is.Not.Null);
            CaptureAtResolution(camera, canvas, 1600, 900, "/tmp/KeyboardWanderer-16x9.png");
            CaptureAtResolution(camera, canvas, 1024, 768, "/tmp/KeyboardWanderer-4x3.png");
        }

        private static void CaptureAtResolution(Camera camera, Canvas canvas, int width, int height, string path)
        {
            if (File.Exists(path)) File.Delete(path);
            RenderMode originalMode = canvas.renderMode;
            Camera originalCanvasCamera = canvas.worldCamera;
            bool originalOverrideSorting = canvas.overrideSorting;
            int originalSortingOrder = canvas.sortingOrder;
            RenderTexture originalTarget = camera.targetTexture;
            RenderTexture originalActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 2000;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = originalTarget;
                canvas.worldCamera = originalCanvasCamera;
                canvas.renderMode = originalMode;
                canvas.overrideSorting = originalOverrideSorting;
                canvas.sortingOrder = originalSortingOrder;
                RenderTexture.active = originalActive;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(target);
            }
            Assert.That(File.Exists(path), Is.True, "Screenshot was not written: " + path);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(1024));
        }

        private static Transform Find(Transform root, string name)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform item in transforms)
                if (item.name == name)
                    return item;
            return null;
        }

        private static object Invoke(object target, string methodName, params object[] values)
        {
            MethodInfo method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(target, values);
        }

        private static object GetField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(target);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
