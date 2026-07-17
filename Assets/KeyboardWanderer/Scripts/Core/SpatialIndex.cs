using System;
using System.Collections.Generic;

namespace KeyboardWanderer.Core
{
    public enum EntityKind
    {
        Player,
        Npc,
        Prop,
        Enemy,
        Effect
    }

    public sealed class EntityState
    {
        public Guid EntityId { get; }
        public EntityKind Kind { get; }
        public string AssetId { get; }
        public string DisplayName { get; }
        public bool IsBlocking { get; }
        public bool IsProtected { get; }
        public bool IsCloneable { get; }
        public bool IsHostile { get; }
        public bool IsActive { get; private set; }
        public bool IsOpened { get; private set; }
        public int Health { get; private set; }
        public int MaxHealth { get; }
        public Guid RegionId { get; private set; }
        public GridCoord Position { get; private set; }
        public int Layer { get; private set; }

        public EntityState(
            Guid entityId,
            EntityKind kind,
            string assetId,
            string displayName,
            bool isBlocking,
            bool isProtected,
            bool isCloneable,
            Guid regionId,
            GridCoord position,
            int layer = 0)
            : this(entityId, kind, assetId, displayName, isBlocking, isProtected, isCloneable,
                false, kind == EntityKind.Player ? 10 : kind == EntityKind.Enemy ? 4 : 1,
                regionId, position, layer)
        {
        }

        public EntityState(
            Guid entityId,
            EntityKind kind,
            string assetId,
            string displayName,
            bool isBlocking,
            bool isProtected,
            bool isCloneable,
            bool isHostile,
            int maxHealth,
            Guid regionId,
            GridCoord position,
            int layer = 0)
        {
            EntityId = entityId;
            Kind = kind;
            AssetId = assetId;
            DisplayName = displayName;
            IsBlocking = isBlocking;
            IsProtected = isProtected;
            IsCloneable = isCloneable;
            IsHostile = isHostile;
            IsActive = true;
            IsOpened = false;
            MaxHealth = Math.Max(1, maxHealth);
            Health = MaxHealth;
            RegionId = regionId;
            Position = position;
            Layer = layer;
        }

        internal void SetPosition(Guid regionId, GridCoord position, int layer)
        {
            RegionId = regionId;
            Position = position;
            Layer = layer;
        }

        internal void Deactivate()
        {
            IsActive = false;
        }

        internal int ApplyDamage(int amount)
        {
            Health = Math.Max(0, Health - Math.Max(0, amount));
            return Health;
        }

        internal int ApplyHealing(int amount)
        {
            Health = Math.Min(MaxHealth, Health + Math.Max(0, amount));
            return Health;
        }

        internal void SetOpened()
        {
            IsOpened = true;
        }

        internal void RestoreRuntimeState(int health, bool isOpened, bool isActive)
        {
            Health = Math.Max(0, Math.Min(MaxHealth, health));
            IsOpened = isOpened;
            IsActive = isActive;
        }

        internal EntityState Clone()
        {
            var clone = new EntityState(EntityId, Kind, AssetId, DisplayName, IsBlocking, IsProtected, IsCloneable,
                IsHostile, MaxHealth, RegionId, Position, Layer);
            clone.RestoreRuntimeState(Health, IsOpened, IsActive);
            return clone;
        }
    }

    /// <summary>
    /// Immutable runtime-only entity state used by Restore and compensating Undo events.
    /// Structural entity data remains owned by the deterministic world/entity catalog.
    /// </summary>
    public sealed class EntityRuntimeSnapshot
    {
        public Guid EntityId { get; }
        public Guid RegionId { get; }
        public GridCoord Position { get; }
        public int Layer { get; }
        public int Health { get; }
        public bool IsOpened { get; }
        public bool IsActive { get; }

        public EntityRuntimeSnapshot(Guid entityId, Guid regionId, GridCoord position, int layer, int health,
            bool isOpened, bool isActive)
        {
            EntityId = entityId;
            RegionId = regionId;
            Position = position;
            Layer = layer;
            Health = health;
            IsOpened = isOpened;
            IsActive = isActive;
        }

        public static EntityRuntimeSnapshot Capture(EntityState entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            return new EntityRuntimeSnapshot(entity.EntityId, entity.RegionId, entity.Position, entity.Layer,
                entity.Health, entity.IsOpened, entity.IsActive);
        }

        public EntityRuntimeSnapshot Clone()
        {
            return new EntityRuntimeSnapshot(EntityId, RegionId, Position, Layer, Health, IsOpened, IsActive);
        }
    }

    internal readonly struct SpatialCellKey : IEquatable<SpatialCellKey>
    {
        public Guid RegionId { get; }
        public GridCoord Coord { get; }
        public int Layer { get; }

        public SpatialCellKey(Guid regionId, GridCoord coord, int layer)
        {
            RegionId = regionId;
            Coord = coord;
            Layer = layer;
        }

        public bool Equals(SpatialCellKey other)
        {
            return RegionId.Equals(other.RegionId) && Coord.Equals(other.Coord) && Layer == other.Layer;
        }

        public override bool Equals(object obj)
        {
            return obj is SpatialCellKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((RegionId.GetHashCode() * 397) ^ Coord.GetHashCode()) * 397 ^ Layer;
            }
        }
    }

    internal sealed class OccupancyCell
    {
        public Guid? BlockingEntityId;
        public readonly List<Guid> NonBlockingEntityIds = new List<Guid>();
    }

    public sealed class MoveResult
    {
        public bool IsSuccess { get; }
        public string ErrorCode { get; }
        public GridCoord From { get; }
        public GridCoord To { get; }

        private MoveResult(bool isSuccess, string errorCode, GridCoord from, GridCoord to)
        {
            IsSuccess = isSuccess;
            ErrorCode = errorCode;
            From = from;
            To = to;
        }

        public static MoveResult Fail(string code) => new MoveResult(false, code, default, default);
        public static MoveResult Success(GridCoord from, GridCoord to) => new MoveResult(true, null, from, to);
    }

    public sealed class SpatialIndex
    {
        private readonly Dictionary<Guid, EntityState> _entities = new Dictionary<Guid, EntityState>();
        private readonly Dictionary<SpatialCellKey, OccupancyCell> _occupancy = new Dictionary<SpatialCellKey, OccupancyCell>();

        public IEnumerable<EntityState> Entities => _entities.Values;

        public bool TryGetEntity(Guid entityId, out EntityState entity)
        {
            return _entities.TryGetValue(entityId, out entity);
        }

        public bool Register(EntityState entity, out string errorCode)
        {
            errorCode = null;
            if (entity == null || _entities.ContainsKey(entity.EntityId))
            {
                errorCode = "ENTITY_ALREADY_EXISTS";
                return false;
            }

            var key = Key(entity);
            if (entity.IsBlocking && _occupancy.TryGetValue(key, out OccupancyCell cell) && cell.BlockingEntityId.HasValue)
            {
                errorCode = "TILE_OCCUPIED";
                return false;
            }

            _entities.Add(entity.EntityId, entity);
            AddToCell(entity);
            return true;
        }

        public bool IsBlockingOccupied(Guid regionId, GridCoord coord, int layer, Guid? exceptEntityId = null)
        {
            var key = new SpatialCellKey(regionId, coord, layer);
            if (!_occupancy.TryGetValue(key, out OccupancyCell cell) || !cell.BlockingEntityId.HasValue)
                return false;
            return !exceptEntityId.HasValue || cell.BlockingEntityId.Value != exceptEntityId.Value;
        }

        public IReadOnlyList<EntityState> FindAt(Guid regionId, GridCoord coord, int layer = 0)
        {
            var result = new List<EntityState>();
            var key = new SpatialCellKey(regionId, coord, layer);
            if (!_occupancy.TryGetValue(key, out OccupancyCell cell))
                return result;

            if (cell.BlockingEntityId.HasValue && _entities.TryGetValue(cell.BlockingEntityId.Value, out EntityState blocking))
                result.Add(blocking);
            for (int i = 0; i < cell.NonBlockingEntityIds.Count; i++)
            {
                if (_entities.TryGetValue(cell.NonBlockingEntityIds[i], out EntityState item))
                    result.Add(item);
            }
            return result;
        }

        public MoveResult TryMove(
            Guid entityId,
            Guid targetRegionId,
            GridCoord destination,
            int targetLayer,
            Func<Guid, GridCoord, bool> isWalkable)
        {
            if (!_entities.TryGetValue(entityId, out EntityState entity) || !entity.IsActive)
                return MoveResult.Fail("ENTITY_NOT_FOUND");
            if (!isWalkable(targetRegionId, destination))
                return MoveResult.Fail("TILE_NOT_WALKABLE");
            if (IsBlockingOccupied(targetRegionId, destination, targetLayer, entityId))
                return MoveResult.Fail("TILE_OCCUPIED");

            GridCoord oldPosition = entity.Position;
            RemoveFromCell(entity);
            entity.SetPosition(targetRegionId, destination, targetLayer);
            AddToCell(entity);
            return MoveResult.Success(oldPosition, destination);
        }

        public bool TryRemove(Guid entityId, out string errorCode)
        {
            errorCode = null;
            if (!_entities.TryGetValue(entityId, out EntityState entity) || !entity.IsActive)
            {
                errorCode = "ENTITY_NOT_FOUND";
                return false;
            }

            RemoveFromCell(entity);
            entity.Deactivate();
            return true;
        }

        public bool TryDamage(Guid entityId, int amount, out int remainingHealth, out bool defeated, out string errorCode)
        {
            remainingHealth = 0;
            defeated = false;
            errorCode = null;
            if (amount <= 0)
            {
                errorCode = "INVALID_DAMAGE";
                return false;
            }
            if (!_entities.TryGetValue(entityId, out EntityState entity) || !entity.IsActive)
            {
                errorCode = "ENTITY_NOT_FOUND";
                return false;
            }

            remainingHealth = entity.ApplyDamage(amount);
            defeated = remainingHealth <= 0;
            if (defeated && entity.Kind != EntityKind.Player)
            {
                RemoveFromCell(entity);
                entity.Deactivate();
            }
            return true;
        }

        public bool TryHeal(Guid entityId, int amount, out int health, out string errorCode)
        {
            health = 0;
            errorCode = null;
            if (amount <= 0)
            {
                errorCode = "INVALID_HEALING";
                return false;
            }
            if (!_entities.TryGetValue(entityId, out EntityState entity) || !entity.IsActive)
            {
                errorCode = "ENTITY_NOT_FOUND";
                return false;
            }
            health = entity.ApplyHealing(amount);
            return true;
        }

        public bool TryOpen(Guid entityId, out string errorCode)
        {
            errorCode = null;
            if (!_entities.TryGetValue(entityId, out EntityState entity) || !entity.IsActive)
            {
                errorCode = "ENTITY_NOT_FOUND";
                return false;
            }
            if (entity.IsOpened)
            {
                errorCode = "ALREADY_OPENED";
                return false;
            }
            entity.SetOpened();
            return true;
        }

        public bool CanRestore(EntityRuntimeSnapshot snapshot, Func<Guid, GridCoord, bool> isValidPosition,
            out string errorCode)
        {
            errorCode = null;
            if (snapshot == null || !_entities.TryGetValue(snapshot.EntityId, out EntityState entity))
            {
                errorCode = "ENTITY_NOT_FOUND";
                return false;
            }
            if (snapshot.IsActive && (isValidPosition == null || !isValidPosition(snapshot.RegionId, snapshot.Position)))
            {
                errorCode = "ENTITY_POSITION_INVALID";
                return false;
            }
            if (snapshot.IsActive && entity.IsBlocking &&
                IsBlockingOccupied(snapshot.RegionId, snapshot.Position, snapshot.Layer, entity.EntityId))
            {
                errorCode = "TILE_OCCUPIED";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Applies a previously captured runtime snapshot without replacing structural entity data.
        /// This never changes map geometry and is therefore safe for Restore/Undo compensation.
        /// </summary>
        public bool TryRestore(EntityRuntimeSnapshot snapshot, Func<Guid, GridCoord, bool> isValidPosition,
            out string errorCode)
        {
            if (!CanRestore(snapshot, isValidPosition, out errorCode))
                return false;

            EntityState entity = _entities[snapshot.EntityId];
            if (entity.IsActive)
                RemoveFromCell(entity);

            entity.SetPosition(snapshot.RegionId, snapshot.Position, snapshot.Layer);
            entity.RestoreRuntimeState(snapshot.Health, snapshot.IsOpened, snapshot.IsActive);
            if (snapshot.IsActive)
                AddToCell(entity);
            return true;
        }

        public List<EntityRuntimeSnapshot> CaptureRuntimeState()
        {
            var snapshots = new List<EntityRuntimeSnapshot>();
            foreach (EntityState entity in _entities.Values)
                snapshots.Add(EntityRuntimeSnapshot.Capture(entity));
            snapshots.Sort((left, right) => string.CompareOrdinal(left.EntityId.ToString("N"), right.EntityId.ToString("N")));
            return snapshots;
        }

        public List<string> Validate(Func<Guid, GridCoord, bool> isValidPosition)
        {
            var errors = new List<string>();
            var seen = new HashSet<Guid>();
            foreach (KeyValuePair<SpatialCellKey, OccupancyCell> pair in _occupancy)
            {
                if (pair.Value.BlockingEntityId.HasValue)
                    ValidateReference(pair.Key, pair.Value.BlockingEntityId.Value, seen, errors);
                for (int i = 0; i < pair.Value.NonBlockingEntityIds.Count; i++)
                    ValidateReference(pair.Key, pair.Value.NonBlockingEntityIds[i], seen, errors);
            }

            foreach (EntityState entity in _entities.Values)
            {
                if (!entity.IsActive)
                    continue;
                if (!seen.Contains(entity.EntityId))
                    errors.Add("ENTITY_MISSING_FROM_OCCUPANCY:" + entity.EntityId);
                if (!isValidPosition(entity.RegionId, entity.Position))
                    errors.Add("ENTITY_POSITION_INVALID:" + entity.EntityId);
            }
            return errors;
        }

        public SpatialIndex Clone()
        {
            var clone = new SpatialIndex();
            foreach (EntityState entity in _entities.Values)
            {
                EntityState entityClone = entity.Clone();
                clone._entities.Add(entityClone.EntityId, entityClone);
                if (entityClone.IsActive)
                    clone.AddToCell(entityClone);
            }
            return clone;
        }

        private void ValidateReference(SpatialCellKey key, Guid entityId, HashSet<Guid> seen, List<string> errors)
        {
            if (!_entities.TryGetValue(entityId, out EntityState entity) || !entity.IsActive)
            {
                errors.Add("OCCUPANCY_REFERENCES_MISSING_ENTITY:" + entityId);
                return;
            }
            if (!seen.Add(entityId))
                errors.Add("ENTITY_OCCUPIED_MORE_THAN_ONCE:" + entityId);
            if (entity.RegionId != key.RegionId || entity.Position != key.Coord || entity.Layer != key.Layer)
                errors.Add("OCCUPANCY_POSITION_MISMATCH:" + entityId);
        }

        private static SpatialCellKey Key(EntityState entity)
        {
            return new SpatialCellKey(entity.RegionId, entity.Position, entity.Layer);
        }

        private void AddToCell(EntityState entity)
        {
            SpatialCellKey key = Key(entity);
            if (!_occupancy.TryGetValue(key, out OccupancyCell cell))
            {
                cell = new OccupancyCell();
                _occupancy.Add(key, cell);
            }

            if (entity.IsBlocking)
            {
                if (cell.BlockingEntityId.HasValue)
                    throw new InvalidOperationException("Blocking occupancy collision.");
                cell.BlockingEntityId = entity.EntityId;
            }
            else
            {
                cell.NonBlockingEntityIds.Add(entity.EntityId);
            }
        }

        private void RemoveFromCell(EntityState entity)
        {
            SpatialCellKey key = Key(entity);
            if (!_occupancy.TryGetValue(key, out OccupancyCell cell))
                throw new InvalidOperationException("Spatial index is inconsistent.");

            if (entity.IsBlocking)
                cell.BlockingEntityId = null;
            else
                cell.NonBlockingEntityIds.Remove(entity.EntityId);

            if (!cell.BlockingEntityId.HasValue && cell.NonBlockingEntityIds.Count == 0)
                _occupancy.Remove(key);
        }
    }
}
