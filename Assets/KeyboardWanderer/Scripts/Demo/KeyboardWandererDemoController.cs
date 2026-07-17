using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KeyboardWanderer.Demo
{
    public sealed class KeyboardWandererDemoController : MonoBehaviour
    {
        private const float TileSize = 1f;
        private const float PanelWidth = 360f;
        private readonly List<GameObject> _visuals = new List<GameObject>();
        private readonly List<Sprite> _runtimeSprites = new List<Sprite>();

        private LocalTurnService _service;
        private NinjaAdventureAssetManifest _assets;
        private Sprite _floorSprite;
        private Sprite _wallSprite;
        private Sprite _hazardSprite;
        private Sprite _playerSprite;
        private Sprite _wardenSprite;
        private Sprite _bookSprite;
        private Sprite _crateSprite;
        private AbilityKind _ability = AbilityKind.Move;
        private GridCoord? _selectedCoord;
        private Guid? _selectedTarget;
        private string _intent = "Move toward the archive core";
        private string _lastResult = "Choose a tile, select an ability, and commit a turn.";

        private void Awake()
        {
            _service = LocalTurnService.CreateDemo();
            LoadNinjaAdventureAssets();
            ConfigureCamera();
            RebuildVisuals();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _runtimeSprites.Count; i++)
                Destroy(_runtimeSprites[i]);
            _runtimeSprites.Clear();
        }

        private void Update()
        {
            Mouse mouseDevice = Mouse.current;
            if (mouseDevice == null || !mouseDevice.leftButton.wasPressedThisFrame || Camera.main == null)
                return;

            Vector2 mouse = mouseDevice.position.ReadValue();
            float guiY = Screen.height - mouse.y;
            if (mouse.x <= PanelWidth + 24f && guiY <= 610f)
                return;

            Vector3 world = Camera.main.ScreenToWorldPoint(mouse);
            RunView view = _service.CurrentView;
            Vector2 origin = MapOrigin(view.Region);
            var coord = new GridCoord(
                Mathf.FloorToInt((world.x - origin.x) / TileSize),
                Mathf.FloorToInt((world.y - origin.y) / TileSize));
            if (!view.Region.Contains(coord))
                return;

            _selectedCoord = coord;
            _selectedTarget = FindTarget(view, coord);
            _lastResult = _selectedTarget.HasValue
                ? "Selected " + EntityName(view, _selectedTarget.Value) + " at " + coord
                : "Selected tile " + coord;
            RebuildVisuals();
        }

        private void OnGUI()
        {
            if (_assets != null && _assets.PixelFont != null)
                GUI.skin.font = _assets.PixelFont;
            RunView view = _service.CurrentView;
            GUILayout.BeginArea(new Rect(16, 16, PanelWidth, 594), GUI.skin.box);
            GUILayout.Label("KEYBOARD WANDERER · Vertical Slice");
            GUILayout.Space(6);
            GUILayout.Label("Turn " + view.CurrentTurn + "/" + view.TurnLimit + "   Version " + view.Version);
            GUILayout.Label("Act: " + view.Act + "   Focus: " + view.Focus + "   Remaining: " + view.RemainingTurns);
            GUILayout.Label("Layout hash: " + view.Region.LayoutHash.Substring(0, 12));
            GUILayout.Space(12);
            GUILayout.Label("Ability");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_ability == AbilityKind.Move, "Move", GUI.skin.button)) SetAbility(AbilityKind.Move);
            if (GUILayout.Toggle(_ability == AbilityKind.Copy, "Copy", GUI.skin.button)) SetAbility(AbilityKind.Copy);
            if (GUILayout.Toggle(_ability == AbilityKind.Delete, "Delete", GUI.skin.button)) SetAbility(AbilityKind.Delete);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Intent (untrusted player text)");
            _intent = GUILayout.TextArea(_intent, GUILayout.Height(58));
            GUILayout.Label("Tile: " + (_selectedCoord.HasValue ? _selectedCoord.Value.ToString() : "none"));
            GUILayout.Label("Target: " + (_selectedTarget.HasValue ? EntityName(view, _selectedTarget.Value) : "none"));
            GUI.enabled = view.Status == RunStatus.Playing;
            if (GUILayout.Button("ROLL D20 + COMMIT TURN", GUILayout.Height(42)))
                Submit();
            GUI.enabled = true;
            GUILayout.Space(10);
            GUILayout.Label("Authoritative result");
            GUILayout.TextArea(_lastResult, GUILayout.ExpandHeight(true));
            GUILayout.Space(6);
            GUILayout.Label("Ninja Adventure CC0 assets · white tint marks selection");
            GUILayout.EndArea();
        }

        private void Submit()
        {
            RunView view = _service.CurrentView;
            GridCoord? destination = _ability == AbilityKind.Delete ? null : _selectedCoord;
            Guid? target = _ability == AbilityKind.Move ? null : _selectedTarget;
            var request = new TurnRequest(Guid.NewGuid().ToString("N"), view.Version, _ability, target, destination, _intent);
            TurnResponse response = _service.Submit(request);
            if (!response.IsSuccess)
            {
                _lastResult = response.ErrorCode + "\n" + response.ErrorMessage + "\nTurn was not consumed.";
            }
            else
            {
                _lastResult = "D20 " + response.D20 + " · score " + response.MechanicalScore + " · " + response.Outcome +
                    "\n" + response.NormalizedAttempt + "\n\n" + response.Narrative +
                    "\n\nEvents\n- " + string.Join("\n- ", response.Events);
            }
            _selectedTarget = null;
            RebuildVisuals();
        }

        private void SetAbility(AbilityKind ability)
        {
            if (_ability == ability)
                return;
            _ability = ability;
            _selectedTarget = null;
            _intent = ability == AbilityKind.Move
                ? "Move toward the archive core"
                : ability == AbilityKind.Copy
                    ? "Copy the selected object onto the chosen tile"
                    : "Delete the selected temporary object";
        }

        private void ConfigureCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
            }
            camera.orthographic = true;
            camera.orthographicSize = 6.2f;
            camera.transform.position = new Vector3(2.2f, 0f, -10f);
            camera.backgroundColor = new Color(0.035f, 0.047f, 0.065f);
        }

        private void LoadNinjaAdventureAssets()
        {
            _assets = Resources.Load<NinjaAdventureAssetManifest>("NinjaAdventureAssetManifest");
            if (_assets == null || _assets.InteriorFloorAtlas == null)
                throw new InvalidOperationException("Ninja Adventure asset manifest is missing. Run Keyboard Wanderer/Rebuild Ninja Adventure Manifest.");

            _floorSprite = CreateAtlasSprite(_assets.InteriorFloorAtlas, _assets.FloorRect, "NinjaFloor");
            _wallSprite = CreateAtlasSprite(_assets.InteriorFloorAtlas, _assets.WallRect, "NinjaWall");
            _hazardSprite = CreateAtlasSprite(_assets.InteriorFloorAtlas, _assets.HazardRect, "NinjaHazard");
            _playerSprite = CreateCenteredSprite(_assets.PlayerIdle, "NinjaPlayer");
            _wardenSprite = CreateCenteredSprite(_assets.WardenIdle, "NinjaWarden");
            _bookSprite = CreateCenteredSprite(_assets.RuneBook, "NinjaRuneBook");
            _crateSprite = CreateCenteredSprite(_assets.Crate, "NinjaCrate");
        }

        private void RebuildVisuals()
        {
            for (int i = 0; i < _visuals.Count; i++)
                Destroy(_visuals[i]);
            _visuals.Clear();

            RunView view = _service.CurrentView;
            RegionMap map = view.Region;
            Vector2 origin = MapOrigin(map);
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var coord = new GridCoord(x, y);
                    BaseTile tile = map.GetTile(coord);
                    Sprite sprite = tile.Kind == TileKind.Wall ? _wallSprite : tile.Kind == TileKind.Hazard ? _hazardSprite : _floorSprite;
                    Color color = Color.white;
                    if (_selectedCoord.HasValue && _selectedCoord.Value == coord)
                        color = new Color(1f, 1f, 1f, 0.58f);
                    CreateVisual("Tile " + coord, origin, coord, sprite, color, 1f, 0);
                }
            }

            for (int i = 0; i < view.Entities.Count; i++)
            {
                EntityView entity = view.Entities[i];
                Sprite sprite = SpriteForEntity(entity);
                Color color = _selectedTarget.HasValue && _selectedTarget.Value == entity.EntityId
                    ? new Color(1f, 1f, 1f, 0.72f)
                    : Color.white;
                CreateVisual(entity.DisplayName, origin, entity.Position, sprite, color, 0.82f, 5);
            }
        }

        private void CreateVisual(string objectName, Vector2 origin, GridCoord coord, Sprite sprite, Color color, float tileFraction, int order)
        {
            var visual = new GameObject(objectName);
            visual.transform.SetParent(transform, false);
            visual.transform.position = new Vector3(origin.x + (coord.X + 0.5f) * TileSize, origin.y + (coord.Y + 0.5f) * TileSize, 0f);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            float largestSide = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            float uniformScale = largestSide > 0f ? TileSize * tileFraction / largestSide : 1f;
            visual.transform.localScale = new Vector3(uniformScale, uniformScale, 1f);
            _visuals.Add(visual);
        }

        private Sprite SpriteForEntity(EntityView entity)
        {
            switch (entity.AssetId)
            {
                case "player.green.v1": return _playerSprite;
                case "npc.warden.v1": return _wardenSprite;
                case "item.rune-book.v1": return _bookSprite;
                case "item.crate.v1": return _crateSprite;
                default: return _bookSprite;
            }
        }

        private Sprite CreateAtlasSprite(Texture2D texture, Rect rect, string spriteName)
        {
            Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 16f, 0, SpriteMeshType.FullRect);
            sprite.name = spriteName;
            _runtimeSprites.Add(sprite);
            return sprite;
        }

        private Sprite CreateCenteredSprite(Sprite source, string spriteName)
        {
            if (source == null)
                throw new InvalidOperationException("Ninja Adventure manifest sprite is missing: " + spriteName);
            Rect sourceRect = source.rect;
            var firstFrameRect = new Rect(sourceRect.x, sourceRect.y, Mathf.Min(16f, sourceRect.width), Mathf.Min(16f, sourceRect.height));
            Sprite sprite = Sprite.Create(source.texture, firstFrameRect, new Vector2(0.5f, 0.5f), 16f, 0, SpriteMeshType.FullRect);
            sprite.name = spriteName;
            _runtimeSprites.Add(sprite);
            return sprite;
        }

        private static Vector2 MapOrigin(RegionMap map)
        {
            return new Vector2(-map.Width * TileSize * 0.5f + 2.2f, -map.Height * TileSize * 0.5f);
        }

        private static Guid? FindTarget(RunView view, GridCoord coord)
        {
            for (int i = 0; i < view.Entities.Count; i++)
            {
                if (view.Entities[i].Position == coord && view.Entities[i].EntityId != view.PlayerEntityId)
                    return view.Entities[i].EntityId;
            }
            return null;
        }

        private static string EntityName(RunView view, Guid entityId)
        {
            for (int i = 0; i < view.Entities.Count; i++)
            {
                if (view.Entities[i].EntityId == entityId)
                    return view.Entities[i].DisplayName;
            }
            return "unknown";
        }
    }
}
