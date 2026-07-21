using System;
using System.Collections.Generic;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Runtime;
using UnityEngine;

namespace KeyboardWanderer.World
{
    /// <summary>엔티티 한 개의 런타임 렌더링·이동·애니메이션 상태다.</summary>
    internal sealed class KeyboardWandererEntityVisualState
    {
        public KeyboardWandererEntityView AuthoredView;
        public GameObject Root;
        public SpriteRenderer Renderer;
        public Animator Animator;
        public GameObject HealthBack;
        public GameObject HealthFill;
        public GameObject RootComponentLabel;
        public Sprite[] IdleFrames;
        public Sprite[] WalkFrames;
        public Sprite[] AttackFrames;
        public Vector3 TargetPosition;
        public readonly Queue<Vector3> MovementPath = new Queue<Vector3>();
        public Vector2 Facing = Vector2.down;
        public bool IsWandering;
        public Color BaseColor;
        public float DesiredSize;
        public bool IsPlayer;
        public bool IsHostile;
        public bool HasMoveX;
        public bool HasMoveY;
        public bool HasMoveSpeed;
    }

    /// <summary>
    /// 모든 엔티티의 걷기, 대기 배회, 공격 프레임, 선택 점멸을 매 프레임 갱신한다.
    /// 게임 규칙과 서버 응답은 알지 못하고 전달받은 시각 상태만 변경한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererEntityAnimationDriver : MonoBehaviour
    {
        private float _nextAnimationAt;
        private int _animationFrame;

        internal event Action PlayerPathCompleted;

        internal void Tick(
            Dictionary<Guid, KeyboardWandererEntityVisualState> visuals,
            KeyboardWandererSelectionController selection,
            bool playerWalking,
            float playerActionUntil,
            float playerWalkSpeed,
            Vector2 mapOrigin,
            float tileSize,
            bool preventPlayerFlip,
            Sprite[] playerWalkRight,
            Sprite[] playerWalkLeft,
            Sprite[] playerWalkUp,
            Sprite[] playerWalkDown,
            Sprite[] playerAttackRight,
            Sprite[] playerAttackLeft,
            Sprite[] playerAttackUp,
            Sprite[] playerAttackDown)
        {
            if (visuals == null || selection == null)
                return;
            if (Time.unscaledTime >= _nextAnimationAt)
            {
                _animationFrame++;
                _nextAnimationAt = Time.unscaledTime + 0.16f;
            }

            float smoothing = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            foreach (KeyValuePair<Guid, KeyboardWandererEntityVisualState> pair in visuals)
            {
                KeyboardWandererEntityVisualState visual = pair.Value;
                if (visual?.Root == null || visual.Renderer == null)
                    continue;
                bool walkingThisFrame = visual.IsPlayer && playerWalking;
                bool usesAnimator = visual.Animator != null && visual.Animator.runtimeAnimatorController != null;
                if (walkingThisFrame)
                {
                    UpdatePlayerMovement(visual, playerWalkSpeed, usesAnimator);
                }
                else if (!visual.IsPlayer)
                {
                    UpdateNonPlayerMovement(visual, playerWalkSpeed, usesAnimator);
                }
                else
                {
                    visual.Root.transform.position = Vector3.Lerp(
                        visual.Root.transform.position, visual.TargetPosition, smoothing);
                }

                int visualY = Mathf.FloorToInt((visual.Root.transform.position.y - mapOrigin.y) / tileSize);
                visual.Renderer.sortingOrder = 500 - visualY * 4;
                bool playerAction = visual.IsPlayer && Time.unscaledTime < playerActionUntil;
                Sprite[] frames = visual.IsPlayer && walkingThisFrame
                    ? DirectionalFrames(visual.Facing, false, visual.WalkFrames,
                        playerWalkRight, playerWalkLeft, playerWalkUp, playerWalkDown,
                        playerAttackRight, playerAttackLeft, playerAttackUp, playerAttackDown)
                    : playerAction
                        ? DirectionalFrames(visual.Facing, true, visual.AttackFrames,
                            playerWalkRight, playerWalkLeft, playerWalkUp, playerWalkDown,
                            playerAttackRight, playerAttackLeft, playerAttackUp, playerAttackDown)
                        : visual.IdleFrames;
                if (visual.IsPlayer && preventPlayerFlip)
                    visual.Renderer.flipX = false;
                UpdateAnimatorOrSprite(visual, frames, walkingThisFrame, playerActionUntil, _animationFrame);
                UpdateSelectionTint(pair.Key, visual, selection);
                UpdateAttachedVisuals(visual);
            }
        }

        internal static void InitializeAmbientWander(
            KeyboardWandererEntityVisualState visual, Guid entityId)
        {
            if (visual == null || visual.Animator == null)
                return;
            CacheAnimatorParameters(visual);
            if (visual.IsPlayer || !visual.HasMoveSpeed) return;
            visual.Facing = Vector2.down;
            if (visual.HasMoveX) visual.Animator.SetFloat("MoveX", 0f);
            if (visual.HasMoveY) visual.Animator.SetFloat("MoveY", -1f);
            visual.Animator.SetFloat("MoveSpeed", 0f);
        }

        private void UpdatePlayerMovement(
            KeyboardWandererEntityVisualState visual, float playerWalkSpeed, bool usesAnimator)
        {
            Vector3 before = visual.Root.transform.position;
            Vector3 remaining = visual.TargetPosition - before;
            if (Mathf.Abs(remaining.x) > Mathf.Abs(remaining.y))
                visual.Facing = new Vector2(Mathf.Sign(remaining.x), 0f);
            else if (Mathf.Abs(remaining.y) > 0.0001f)
                visual.Facing = new Vector2(0f, Mathf.Sign(remaining.y));
            visual.Root.transform.position = Vector3.MoveTowards(
                before, visual.TargetPosition, playerWalkSpeed * Time.unscaledDeltaTime);
            float horizontal = visual.Root.transform.position.x - before.x;
            if (!usesAnimator && Mathf.Abs(horizontal) > 0.0001f)
                visual.Renderer.flipX = horizontal < 0f;
            else if (usesAnimator)
                visual.Renderer.flipX = false;
            if (Vector3.SqrMagnitude(visual.Root.transform.position - visual.TargetPosition) >= 0.0004f)
                return;
            visual.Root.transform.position = visual.TargetPosition;
            if (visual.MovementPath.Count > 0)
                visual.TargetPosition = visual.MovementPath.Dequeue();
            else
                PlayerPathCompleted?.Invoke();
        }

        private static void UpdateAnimatorOrSprite(
            KeyboardWandererEntityVisualState visual, Sprite[] frames,
            bool walkingThisFrame, float playerActionUntil, int animationFrame)
        {
            bool usesAnimator = visual.Animator != null && visual.Animator.runtimeAnimatorController != null;
            if (usesAnimator && visual.IsPlayer)
            {
                visual.Animator.SetFloat("MoveX", visual.Facing.x);
                visual.Animator.SetFloat("MoveY", visual.Facing.y);
                visual.Animator.SetBool("IsMoving", walkingThisFrame);
                visual.Animator.SetBool("IsAttacking", !walkingThisFrame && Time.unscaledTime < playerActionUntil);
                return;
            }
            if (usesAnimator)
            {
                if (visual.HasMoveX) visual.Animator.SetFloat("MoveX", visual.Facing.x);
                if (visual.HasMoveY) visual.Animator.SetFloat("MoveY", visual.Facing.y);
                if (visual.HasMoveSpeed) visual.Animator.SetFloat("MoveSpeed", visual.IsWandering ? 1f : 0f);
                return;
            }
            if (frames == null || frames.Length == 0)
                return;
            Sprite frame = frames[animationFrame % frames.Length];
            if (visual.Renderer.sprite == frame)
                return;
            visual.Renderer.sprite = frame;
            ScaleSprite(visual.AuthoredView != null ? visual.Renderer.transform : visual.Root.transform,
                frame, visual.DesiredSize);
        }

        private static void UpdateSelectionTint(Guid id, KeyboardWandererEntityVisualState visual,
            KeyboardWandererSelectionController selection)
        {
            bool selected = selection.SelectedTarget == id || selection.SelectedSecondaryTarget == id;
            float pulse = 0.78f + Mathf.Sin(Time.unscaledTime * 7f) * 0.18f;
            Color selectionColor = selection.Ability == AbilityKind.Delete
                ? new Color(1f, 0.16f, 0.12f, 1f)
                : selection.Ability == AbilityKind.Restore
                    ? new Color(0.2f, 0.95f, 1f, 1f)
                    : new Color(1f, 0.82f, 0.25f, 1f);
            visual.Renderer.color = selected
                ? Color.Lerp(visual.BaseColor, selectionColor, pulse)
                : visual.BaseColor;
        }

        private static void UpdateAttachedVisuals(KeyboardWandererEntityVisualState visual)
        {
            if (visual.HealthBack != null && visual.HealthFill != null)
            {
                Vector3 position = visual.Root.transform.position + new Vector3(0f, 0.66f, -0.1f);
                visual.HealthBack.transform.position = position;
                float ratio = visual.HealthFill.transform.localScale.x / 0.74f;
                visual.HealthFill.transform.position =
                    position + new Vector3(-0.39f * (1f - ratio), 0f, -0.01f);
            }
            if (visual.RootComponentLabel != null)
                visual.RootComponentLabel.transform.position =
                    visual.Root.transform.position + new Vector3(0f, -0.72f, -0.2f);
        }

        private static Sprite[] DirectionalFrames(Vector2 facing, bool attacking, Sprite[] fallback,
            Sprite[] walkRight, Sprite[] walkLeft, Sprite[] walkUp, Sprite[] walkDown,
            Sprite[] attackRight, Sprite[] attackLeft, Sprite[] attackUp, Sprite[] attackDown)
        {
            Sprite[] selected;
            if (facing.y > 0.5f)
                selected = attacking ? attackUp : walkUp;
            else if (facing.y < -0.5f)
                selected = attacking ? attackDown : walkDown;
            else if (facing.x < -0.5f)
                selected = attacking ? attackLeft : walkLeft;
            else
                selected = attacking ? attackRight : walkRight;
            return selected != null && selected.Length > 0 ? selected : fallback ?? Array.Empty<Sprite>();
        }

        private static void UpdateNonPlayerMovement(
            KeyboardWandererEntityVisualState visual, float movementSpeed, bool usesAnimator)
        {
            Vector3 direction = visual.TargetPosition - visual.Root.transform.position;
            visual.IsWandering = direction.sqrMagnitude >= 0.0004f;
            if (!visual.IsWandering)
                return;
            visual.Facing = Mathf.Abs(direction.x) > Mathf.Abs(direction.y)
                ? new Vector2(Mathf.Sign(direction.x), 0f)
                : new Vector2(0f, Mathf.Sign(direction.y));
            visual.Root.transform.position = Vector3.MoveTowards(
                visual.Root.transform.position, visual.TargetPosition, movementSpeed * Time.unscaledDeltaTime);
            if (Vector3.SqrMagnitude(visual.Root.transform.position - visual.TargetPosition) >= 0.0004f)
                return;
            visual.Root.transform.position = visual.TargetPosition;
            visual.IsWandering = false;
        }

        private static void CacheAnimatorParameters(KeyboardWandererEntityVisualState visual)
        {
            AnimatorControllerParameter[] parameters = visual.Animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].type != AnimatorControllerParameterType.Float) continue;
                switch (parameters[i].name)
                {
                    case "MoveX": visual.HasMoveX = true; break;
                    case "MoveY": visual.HasMoveY = true; break;
                    case "MoveSpeed": visual.HasMoveSpeed = true; break;
                }
            }
        }

        private static void ScaleSprite(Transform target, Sprite sprite, float desiredSize)
        {
            if (target == null || sprite == null)
                return;
            float largest = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            float scale = largest > 0.001f ? desiredSize / largest : desiredSize;
            target.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
