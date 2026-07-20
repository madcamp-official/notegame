using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;
using KeyboardWanderer.Presentation;
using UnityEngine;

namespace KeyboardWanderer.World
{
    /// <summary>
    /// 미니맵 Texture와 Sprite의 수명, 타일 샘플링, 표식 그리기를 소유한다.
    /// 컨트롤러는 정규화된 엔티티와 월드 색상 함수만 전달하고 픽셀을 직접 다루지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererMinimapRenderer : MonoBehaviour
    {
        [SerializeField, Min(32)] private int textureSize = 80;
        [SerializeField] private Color landmarkColor = new Color(1f, 0.55f, 0.18f, 1f);
        [SerializeField] private Color enemyColor = new Color(0.92f, 0.18f, 0.2f, 1f);
        [SerializeField] private Color selectionColor = new Color(1f, 0.86f, 0.2f, 1f);
        [SerializeField] private Color objectiveColor = new Color(1f, 0.2f, 0.62f, 1f);
        [SerializeField] private Color playerColor = new Color(0.2f, 0.95f, 1f, 1f);

        private readonly MinimapPresenter _invalidation = new MinimapPresenter();
        private Texture2D _texture;
        private Sprite _sprite;

        public Sprite Sprite => _sprite;
        public string Signature => _invalidation.Signature;

        /// <summary>
        /// 레이아웃·플레이어·선택·목표가 바뀐 경우에만 픽셀을 다시 그리고 현재 상태 문구를 반환한다.
        /// </summary>
        public Sprite Render(int worldWidth, int worldHeight, string layoutHash, long version, int turn,
            GridCoord player, GridCoord? selectedCoord, Guid? selectedTarget,
            GridCoord? objectiveCoord, Guid? objectiveId, string objectiveName,
            IReadOnlyList<GridCoord> landmarks, IReadOnlyList<RunPresentationEntity> entities,
            Func<GridCoord, Color> tileColorAt, out string status)
        {
            worldWidth = Mathf.Max(1, worldWidth);
            worldHeight = Mathf.Max(1, worldHeight);
            string signature = (layoutHash ?? string.Empty) + ":" + version + ":" + player + ":" +
                               (selectedCoord.HasValue ? selectedCoord.Value.ToString() : "none") + ":" +
                               (selectedTarget.HasValue ? selectedTarget.Value.ToString("N") : "none") + ":" +
                               (objectiveCoord.HasValue ? objectiveId + "@" + objectiveCoord.Value : "no-objective");
            if (_invalidation.ShouldRedraw(signature))
            {
                EnsureTexture();
                DrawTiles(worldWidth, worldHeight, tileColorAt);
                DrawLandmarks(landmarks, worldWidth, worldHeight);
                DrawEnemies(entities, selectedTarget, worldWidth, worldHeight);
                if (selectedCoord.HasValue)
                    PaintMarker(selectedCoord.Value, worldWidth, worldHeight, selectionColor, 2);
                if (objectiveCoord.HasValue)
                    PaintMarker(objectiveCoord.Value, worldWidth, worldHeight, objectiveColor, 2);
                PaintMarker(player, worldWidth, worldHeight, playerColor, 2);
                _texture.Apply(false, false);
            }

            status = "턴 " + turn + " · 나 " + player;
            if (objectiveCoord.HasValue)
            {
                GridCoord target = objectiveCoord.Value;
                status = "턴 " + turn + " · 나 " + player + " · " + (objectiveName ?? "현재 목표") + " " +
                         KeyboardWandererHudTextComposer.DirectionLabel(player, target) + " " +
                         player.ManhattanDistance(target) + "칸";
            }
            else if (selectedCoord.HasValue)
            {
                status = "턴 " + turn + " · 나 " + player + " · 선택 " + selectedCoord.Value;
            }
            return _sprite;
        }

        private void EnsureTexture()
        {
            int size = Mathf.Max(32, textureSize);
            if (_texture != null && _texture.width == size && _texture.height == size)
                return;
            ReleaseRuntimeAssets();
            _texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime World Minimap",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            _sprite = Sprite.Create(_texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
            _sprite.name = "Runtime World Minimap";
            _sprite.hideFlags = HideFlags.DontSave;
        }

        private void DrawTiles(int worldWidth, int worldHeight, Func<GridCoord, Color> tileColorAt)
        {
            for (int py = 0; py < _texture.height; py++)
            {
                int worldY = Mathf.Clamp(py * worldHeight / _texture.height, 0, worldHeight - 1);
                for (int px = 0; px < _texture.width; px++)
                {
                    int worldX = Mathf.Clamp(px * worldWidth / _texture.width, 0, worldWidth - 1);
                    var coord = new GridCoord(worldX, worldY);
                    _texture.SetPixel(px, py, tileColorAt == null ? Color.clear : tileColorAt(coord));
                }
            }
        }

        private void DrawLandmarks(IReadOnlyList<GridCoord> landmarks, int width, int height)
        {
            if (landmarks == null)
                return;
            for (int i = 0; i < landmarks.Count; i++)
                PaintMarker(landmarks[i], width, height, landmarkColor, 1);
        }

        private void DrawEnemies(IReadOnlyList<RunPresentationEntity> entities, Guid? selected,
            int width, int height)
        {
            if (entities == null)
                return;
            for (int i = 0; i < entities.Count; i++)
            {
                RunPresentationEntity entity = entities[i];
                if (entity == null || entity.Kind != RunPresentationEntityKind.Enemy ||
                    !entity.IsHostile || !entity.IsActive)
                    continue;
                bool isSelected = selected.HasValue && selected.Value == entity.Id;
                PaintMarker(entity.Position, width, height,
                    isSelected ? selectionColor : enemyColor, isSelected ? 2 : 1);
            }
        }

        private void PaintMarker(GridCoord coord, int width, int height, Color color, int radius)
        {
            if (_texture == null)
                return;
            int px = Mathf.Clamp(coord.X * _texture.width / Mathf.Max(1, width), 0, _texture.width - 1);
            int py = Mathf.Clamp(coord.Y * _texture.height / Mathf.Max(1, height), 0, _texture.height - 1);
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                int targetX = px + x;
                int targetY = py + y;
                if (targetX >= 0 && targetY >= 0 && targetX < _texture.width && targetY < _texture.height)
                    _texture.SetPixel(targetX, targetY, color);
            }
        }

        private void OnDestroy()
        {
            ReleaseRuntimeAssets();
        }

        private void ReleaseRuntimeAssets()
        {
            if (_sprite != null)
            {
                if (Application.isPlaying) Destroy(_sprite); else DestroyImmediate(_sprite);
                _sprite = null;
            }
            if (_texture != null)
            {
                if (Application.isPlaying) Destroy(_texture); else DestroyImmediate(_texture);
                _texture = null;
            }
        }
    }
}
