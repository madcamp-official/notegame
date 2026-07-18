using System;
using System.IO;
using System.Linq;
using KeyboardWanderer.Demo;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KeyboardWanderer.Editor
{
    public static class KeyboardWandererAnimationBuilder
    {
        private const string OutputFolder = "Assets/KeyboardWanderer/Animations/Generated";
        private const string IsMoving = "IsMoving";
        private const string IsAttacking = "IsAttacking";

        [MenuItem("Keyboard Wanderer/Rebuild Generated Animations")]
        public static void RebuildGeneratedAnimations()
        {
            if (Application.isPlaying)
                throw new UnityException("Exit Play Mode before rebuilding animation assets.");

            EnsureFolder(OutputFolder);

            AnimationClip playerIdle = BuildClip("PlayerIdle", LoadRow(
                "Assets/NinjaAdventure/Actor/Character/NinjaGreen/SeparateAnim/Idle.png", 0), 8f, true);
            AnimationClip playerWalk = BuildClip("PlayerWalk", LoadRow(
                "Assets/NinjaAdventure/Actor/Character/NinjaGreen/SeparateAnim/Walk.png", 0), 10f, true);
            AnimationClip playerAttack = BuildClip("PlayerAttack", LoadRow(
                "Assets/NinjaAdventure/Actor/Character/NinjaGreen/SeparateAnim/Attack.png", 0), 12f, false);
            AnimationClip slimeIdle = BuildClip("SlimeIdle", LoadRow(
                "Assets/NinjaAdventure/Actor/Monster/Slime/Slime.png", 3), 8f, true);
            AnimationClip villagerIdle = BuildClip("VillagerIdle", LoadRow(
                "Assets/NinjaAdventure/Actor/Character/Villager/SeparateAnim/Walk.png", 3), 8f, true);

            BuildPlayerController(playerIdle, playerWalk, playerAttack);
            BuildSingleStateController("Slime", slimeIdle);
            BuildSingleStateController("Villager", villagerIdle);
            NinjaAdventureManifestBuilder.RebuildManifest();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Keyboard Wanderer generated animations rebuilt: Player, Slime, Villager.");
        }

        private static Sprite[] LoadRow(string path, int rowFromBottom)
        {
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .ToArray();
            if (sprites.Length == 0)
                throw new UnityException("No sliced sprites found at " + path);

            float[] rows = sprites.Select(sprite => sprite.rect.y).Distinct().OrderBy(value => value).ToArray();
            float row = rows[Mathf.Clamp(rowFromBottom, 0, rows.Length - 1)];
            Sprite[] frames = sprites
                .Where(sprite => Mathf.Approximately(sprite.rect.y, row))
                .OrderBy(sprite => sprite.rect.x)
                .Take(4)
                .ToArray();
            if (frames.Length == 0)
                throw new UnityException("No animation frames found at " + path + " row " + rowFromBottom);
            return frames;
        }

        private static AnimationClip BuildClip(string name, Sprite[] frames, float frameRate, bool loop)
        {
            string path = OutputFolder + "/" + name + ".anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = name };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            clip.frameRate = frameRate;

            var keys = new ObjectReferenceKeyframe[frames.Length + 1];
            for (int i = 0; i < frames.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / frameRate, value = frames[i] };
            keys[frames.Length] = new ObjectReferenceKeyframe
            {
                time = frames.Length / frameRate,
                value = frames[frames.Length - 1]
            };
            var binding = new EditorCurveBinding
            {
                path = string.Empty,
                type = typeof(SpriteRenderer),
                propertyName = "m_Sprite"
            };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void BuildPlayerController(
            AnimationClip idleClip,
            AnimationClip walkClip,
            AnimationClip attackClip)
        {
            AnimatorController controller = NewController("Player");
            controller.AddParameter(IsMoving, AnimatorControllerParameterType.Bool);
            controller.AddParameter(IsAttacking, AnimatorControllerParameterType.Bool);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idle = machine.AddState("Idle");
            AnimatorState walk = machine.AddState("Walk");
            AnimatorState attack = machine.AddState("Attack");
            idle.motion = idleClip;
            walk.motion = walkClip;
            attack.motion = attackClip;
            machine.defaultState = idle;

            AddTransition(idle, walk, AnimatorConditionMode.If, IsMoving);
            AddTransition(walk, idle, AnimatorConditionMode.IfNot, IsMoving);
            AnimatorStateTransition attackTransition = machine.AddAnyStateTransition(attack);
            ConfigureTransition(attackTransition);
            attackTransition.canTransitionToSelf = false;
            attackTransition.AddCondition(AnimatorConditionMode.If, 0f, IsAttacking);

            AnimatorStateTransition attackToWalk = attack.AddTransition(walk);
            ConfigureTransition(attackToWalk);
            attackToWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, IsAttacking);
            attackToWalk.AddCondition(AnimatorConditionMode.If, 0f, IsMoving);
            AnimatorStateTransition attackToIdle = attack.AddTransition(idle);
            ConfigureTransition(attackToIdle);
            attackToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, IsAttacking);
            attackToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, IsMoving);
            EditorUtility.SetDirty(controller);
        }

        private static void BuildSingleStateController(string name, AnimationClip clip)
        {
            AnimatorController controller = NewController(name);
            AnimatorState idle = controller.layers[0].stateMachine.AddState("Idle");
            idle.motion = clip;
            controller.layers[0].stateMachine.defaultState = idle;
            EditorUtility.SetDirty(controller);
        }

        private static AnimatorController NewController(string name)
        {
            string path = OutputFolder + "/" + name + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
                AssetDatabase.DeleteAsset(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        private static void AddTransition(
            AnimatorState from,
            AnimatorState to,
            AnimatorConditionMode mode,
            string parameter)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            ConfigureTransition(transition);
            transition.AddCondition(mode, 0f, parameter);
        }

        private static void ConfigureTransition(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
                return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
