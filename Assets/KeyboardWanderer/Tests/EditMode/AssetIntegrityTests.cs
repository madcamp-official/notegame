using KeyboardWanderer.Editor;
using KeyboardWanderer.Editor.Validation;
using KeyboardWanderer.Demo;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace KeyboardWanderer.Tests.EditMode
{
    public sealed class AssetIntegrityTests
    {
        [Test]
        public void Assets_HaveExactlyOneMetaAndUniqueGuids()
        {
            var problems = UnityAssetIntegrityValidator.CollectProblems(Application.dataPath);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void KoreanTmpFont_IsPrewarmedAndKeepsValidAtlasReferences()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                KeyboardWandererFontAssetAuthoring.FontAssetPath);
            Assert.That(font, Is.Not.Null, "기본 한글 TMP 폰트 에셋이 없습니다.");
            Assert.That(font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Dynamic));
            Assert.That(font.isMultiAtlasTexturesEnabled, Is.True);
            Assert.That(font.atlasWidth, Is.EqualTo(KeyboardWandererFontAssetAuthoring.AtlasSize));
            Assert.That(font.atlasHeight, Is.EqualTo(KeyboardWandererFontAssetAuthoring.AtlasSize));
            Assert.That(font.atlasTextureCount, Is.EqualTo(1),
                "현재 게임 문자는 첫 프레임에서 추가 Atlas를 만들지 않도록 하나의 Atlas에 미리 들어가야 합니다.");

            var serializedFont = new SerializedObject(font);
            Assert.That(serializedFont.FindProperty("m_ClearDynamicDataOnBuild").boolValue, Is.False,
                "빌드에서 미리 채운 한글 글리프를 지우면 첫 화면에서 Atlas가 다시 분리됩니다.");

            string requiredCharacters = KeyboardWandererFontAssetAuthoring.CollectRuntimeCharacters();
            Assert.That(requiredCharacters, Is.Not.Empty);
            Assert.That(KeyboardWandererFontAssetAuthoring.HasAllRuntimeCharacters(font), Is.True,
                "게임 문자열에 필요한 글리프가 TMP 폰트에 미리 들어 있어야 합니다.");

            Texture2D[] atlases = font.atlasTextures;
            Assert.That(atlases, Is.Not.Null.And.Not.Empty);
            foreach (var glyph in font.glyphTable)
            {
                Assert.That(glyph.atlasIndex, Is.LessThan((uint)atlases.Length),
                    "글리프가 존재하지 않는 Atlas 인덱스를 참조하고 있습니다.");
                Assert.That(atlases[glyph.atlasIndex], Is.Not.Null,
                    "글리프가 비어 있는 Atlas 슬롯을 참조하고 있습니다.");
            }
        }

        [Test]
        public void AuthoredUi_KeepsComponentizedHudReferencesAndInactiveStartupState()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/KeyboardWanderer/Prefabs/UI/AuthoredUI.prefab");
            Assert.That(prefab, Is.Not.Null);
            KeyboardWandererSceneUI sceneUi = prefab.GetComponent<KeyboardWandererSceneUI>();
            Assert.That(sceneUi, Is.Not.Null);
            Assert.That(sceneUi.IsReady, Is.True,
                "HUD 재생성 후 화면 View 직렬화 참조가 유실되었습니다.");
            Transform hud = Find(prefab.transform, "Game HUD");
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.gameObject.activeSelf, Is.False,
                "타이틀 초기화 전에 Game HUD placeholder가 노출되면 안 됩니다.");
            Assert.That(Find(prefab.transform, "Choice Strip"), Is.Not.Null);
            Assert.That(Find(prefab.transform, "Encounter Subject Stage"), Is.Not.Null);
            Assert.That(Find(prefab.transform, "Encounter Subject"), Is.Not.Null);
        }

        private static Transform Find(Transform root, string name)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
                if (children[i].name == name) return children[i];
            return null;
        }
    }
}
