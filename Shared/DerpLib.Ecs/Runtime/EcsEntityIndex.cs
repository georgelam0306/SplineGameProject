using System;
using System.Runtime.CompilerServices;

namespace DerpLib.Ecs;

/// <summary>
/// Stable entity identity allocator + indirection:
/// maps <see cref="EntityHandle.RawId"/> to the entity's current storage location.
/// </summary>
public sealed class EcsEntityIndex
{
    private const int DefaultCapacity = 256;

    private EntityLocation[] _locationByRawId;
    private ushort[] _generationByRawId;
    private int[] _nextFreeByRawId;

    private int _freeListHeadRawId;
    private int _nextRawId;

    public EcsEntityIndex(int initialCapacity = DefaultCapacity)
    {
        if (initialCapacity < 1)
        {
            initialCapacity = 1;
        }

        _locationByRawId = new EntityLocation[initialCapacity];
        _generationByRawId = new ushort[initialCapacity];
        _nextFreeByRawId = new int[initialCapacity];

        for (int i = 0; i < initialCapacity; i++)
        {
            _locationByRawId[i] = new EntityLocation(archetypeId: 0, row: -1);
            _generationByRawId[i] = 1;
            _nextFreeByRawId[i] = -1;
        }

        _freeListHeadRawId = -1;
        _nextRawId = 0;
    }

    public int Capacity => _locationByRawId.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityHandle Allocate(ushort kindId, in EntityLocation initialLocation)
    {
        if (kindId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(kindId), "kindId must be non-zero.");
        }

        int rawId;
        if (_freeListHeadRawId >= 0)
        {
            rawId = _freeListHeadRawId;
            _freeListHeadRawId = _nextFreeByRawId[rawId];
            _nextFreeByRawId[rawId] = -1;
        }
        else
        {
            rawId = _nextRawId;
            _nextRawId = rawId + 1;
            EnsureCapacity(_nextRawId);
        }

        ushort generation = _generationByRawId[rawId];
        if (generation == 0)
        {
            generation = 1;
            _generationByRawId[rawId] = 1;
        }
        _locationByRawId[rawId] = initialLocation;

        return new EntityHandle(kindId, (uint)rawId, generation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(EntityHandle entity)
    {
        uint rawId = entity.RawId;
        if (rawId >= (uint)_locationByRawId.Length)
        {
            return false;
        }

        if (_generationByRawId[(int)rawId] != entity.Generation)
        {
            return false;
        }

        return _locationByRawId[(int)rawId].Row >= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetLocation(EntityHandle entity, out EntityLocation location)
    {
        uint rawId = entity.RawId;
        if (rawId >= (uint)_locationByRawId.Length)
        {
            location = default;
            return false;
        }

        int idx = (int)rawId;
        if (_generationByRawId[idx] != entity.Generation)
        {
            location = default;
            return false;
        }

        location = _locationByRawId[idx];
        return location.Row >= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityLocation GetLocationUnchecked(EntityHandle entity)
    {
        return _locationByRawId[(int)entity.RawId];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetLocationUnchecked(uint rawId, in EntityLocation location)
    {
        _locationByRawId[(int)rawId] = location;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetLocation(EntityHandle entity, in EntityLocation location)
    {
        uint rawId = entity.RawId;
        if (rawId >= (uint)_locationByRawId.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(entity), "rawId out of range.");
        }

        int idx = (int)rawId;
        if (_generationByRawId[idx] != entity.Generation)
        {
            throw new InvalidOperationException("Stale entity handle.");
        }

        _locationByRawId[idx] = location;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Free(EntityHandle entity)
    {
        uint rawId = entity.RawId;
        if (rawId >= (uint)_locationByRawId.Length)
        {
            return;
        }

        int idx = (int)rawId;
        if (_generationByRawId[idx] != entity.Generation)
        {
            return;
        }

        if (_locationByRawId[idx].Row < 0)
        {
            return;
        }

        _locationByRawId[idx] = new EntityLocation(archetypeId: 0, row: -1);
        unchecked
        {
            _generationByRawId[idx]++;
        }
        if (_generationByRawId[idx] == 0)
        {
            _generationByRawId[idx] = 1;
        }

        _nextFreeByRawId[idx] = _freeListHeadRawId;
        _freeListHeadRawId = idx;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EnsureCapacity(int requiredLength)
    {
        if (requiredLength <= _locationByRawId.Length)
        {
            return;
        }

        int oldLength = _locationByRawId.Length;
        int newLength = Math.Max(requiredLength, oldLength * 2);
        Array.Resize(ref _locationByRawId, newLength);
        Array.Resize(ref _generationByRawId, newLength);
        Array.Resize(ref _nextFreeByRawId, newLength);

        for (int i = oldLength; i < newLength; i++)
        {
            _locationByRawId[i] = new EntityLocation(archetypeId: 0, row: -1);
            _generationByRawId[i] = 1;
            _nextFreeByRawId[i] = -1;
        }
    }
}
