using System.Collections;
using System.Reflection;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
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
        }

        [Test]
        public void AuthoredUi_UsesSerializedTmpMinimapAndButtonStateViews()
        {
            Assert.That(_sceneUi.IsReady, Is.True);
            Assert.That(_sceneUi.GetComponentsInChildren<Text>(true), Is.Empty);

            TMP_Text[] texts = _sceneUi.GetComponentsInChildren<TMP_Text>(true);
            Assert.That(texts.Length, Is.GreaterThan(20));
            Assert.That(texts, Has.All.Matches<TMP_Text>(text =>
                text.font != null && text.font.name.Contains("NeoDunggeunmoPro")));

            Transform minimap = Find(_sceneUi.transform, "Minimap Map");
            Assert.That(minimap, Is.Not.Null);
            Assert.That(minimap.GetComponent<Image>(), Is.Not.Null);
            Assert.That(Find(_sceneUi.transform, "Runtime Minimap"), Is.Null);

            Button delete = Find(_sceneUi.transform, "Delete Skill Button").GetComponent<Button>();
            var stateView = delete.GetComponent<KeyboardWandererButtonStateView>();
            Outline outline = delete.targetGraphic.GetComponent<Outline>();
            Assert.That(stateView, Is.Not.Null);
            Assert.That(outline, Is.Not.Null);

            int componentCount = delete.GetComponents<Component>().Length;
            _sceneUi.SetButtonState(KeyboardWandererUiButton.Delete, true, true);
            Assert.That(outline.enabled, Is.True);
            Assert.That(delete.transform.localScale.x, Is.GreaterThan(1f));
            _sceneUi.SetButtonState(KeyboardWandererUiButton.Delete, false, false);
            Assert.That(outline.enabled, Is.False);
            Assert.That(delete.GetComponents<Component>().Length, Is.EqualTo(componentCount));
        }

        [UnityTest]
        public IEnumerator NewRunButtonAndDialogue_ChangeVisibleAuthoredState()
        {
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
            SetField(_controller, "_lastOutcome", "SUCCESS");
            SetField(_controller, "_lastNarrative", "첫 번째 이야기");
            SetField(_controller, "_lastDialogue", new[] { "두 번째 대화", "세 번째 대화" });
            SetField(_controller, "_authoredDialogueSignature", string.Empty);
            Invoke(_controller, "UpdateAuthoredUi");

            TMP_Text story = Find(_sceneUi.transform, "Story Text").GetComponent<TMP_Text>();
            string first = story.text;
            Find(_sceneUi.transform, "Next Dialogue Button").GetComponent<Button>().onClick.Invoke();
            Invoke(_controller, "UpdateAuthoredUi");
            Assert.That(story.text, Is.Not.EqualTo(first));
            Assert.That(story.text, Is.EqualTo("첫 번째 이야기"));
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
