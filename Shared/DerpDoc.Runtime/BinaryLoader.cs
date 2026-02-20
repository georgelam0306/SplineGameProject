using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Core;

namespace DerpDoc.Runtime;

/// <summary>
/// Memory-mapped binary file loader for GameDocDb.
/// Provides zero-copy access to table data.
/// </summary>
public sealed class BinaryLoader : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly unsafe byte* _basePtr;
    private readonly long _fileSize;
    private readonly BinaryHeader _header;
    private readonly Dictionary<string, TableInfo> _tables;
    private bool _pointerAcquired;
    private bool _disposed;

    /// <summary>
    /// Loads a binary GameDocDb file.
    /// </summary>
    public static BinaryLoader Load(string filePath)
    {
        return new BinaryLoader(filePath);
    }

    /// <summary>
    /// Loads a binary GameDocDb view from a larger file at the given byte offset.
    /// Intended for .derpdoc-live.bin double-buffer slots.
    /// </summary>
    public static BinaryLoader Load(string filePath, long fileOffset, long length)
    {
        return new BinaryLoader(filePath, fileOffset, length);
    }

    private unsafe BinaryLoader(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        _fileSize = fileInfo.Length;

        MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        MemoryMappedViewAccessor accessor;
        try
        {
            accessor = mmf.CreateViewAccessor(0, _fileSize, MemoryMappedFileAccess.Read);
        }
        catch
        {
            mmf.Dispose();
            throw;
        }

        _mmf = mmf;
        _accessor = accessor;

        byte* ptr = null;
        try
        {
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            _pointerAcquired = true;
            _basePtr = ptr + _accessor.PointerOffset;

            // Read and validate header
            _header = ReadHeader();
            if (_header.Magic != BinaryFormat.Magic)
            {
                throw new InvalidDataException($"Invalid magic: expected {BinaryFormat.Magic:X8}, got {_header.Magic:X8}");
            }

            if (_header.Version != BinaryFormat.Version)
            {
                throw new InvalidDataException($"Unsupported version: {_header.Version}");
            }

            ValidateChecksum();

            // Read table directory
            _tables = new Dictionary<string, TableInfo>((int)_header.TableCount);
            ReadTableDirectory();

            // Load string registry
            LoadStringRegistry();
        }
        catch
        {
            CleanupFailedInitialization();
            throw;
        }
    }

    private unsafe BinaryLoader(string filePath, long fileOffset, long length)
    {
        _fileSize = length;

        MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        MemoryMappedViewAccessor accessor;
        try
        {
            accessor = mmf.CreateViewAccessor(fileOffset, length, MemoryMappedFileAccess.Read);
        }
        catch
        {
            mmf.Dispose();
            throw;
        }

        _mmf = mmf;
        _accessor = accessor;

        byte* ptr = null;
        try
        {
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            _pointerAcquired = true;
            _basePtr = ptr + _accessor.PointerOffset;

            // Read and validate header
            _header = ReadHeader();
            if (_header.Magic != BinaryFormat.Magic)
            {
                throw new InvalidDataException($"Invalid magic: expected {BinaryFormat.Magic:X8}, got {_header.Magic:X8}");
            }

            if (_header.Version != BinaryFormat.Version)
            {
                throw new InvalidDataException($"Unsupported version: {_header.Version}");
            }

            ValidateChecksum();

            // Read table directory
            _tables = new Dictionary<string, TableInfo>((int)_header.TableCount);
            ReadTableDirectory();

            // Load string registry
            LoadStringRegistry();
        }
        catch
        {
            CleanupFailedInitialization();
            throw;
        }
    }

    private unsafe void ValidateChecksum()
    {
        const int headerSize = 24;
        if (_fileSize < headerSize)
        {
            throw new InvalidDataException("File too small to contain a valid header.");
        }

        long dataLen = _fileSize - headerSize;
        uint crc = ComputeCrc32(_basePtr + headerSize, dataLen);
        if (crc != _header.Checksum)
        {
            throw new InvalidDataException($"Checksum mismatch: expected {_header.Checksum:X8}, got {crc:X8}");
        }
    }

    private static unsafe uint ComputeCrc32(byte* ptr, long length)
    {
        const uint polynomial = 0xEDB88320;
        uint crc = 0xFFFFFFFF;

        for (long i = 0; i < length; i++)
        {
            crc ^= ptr[i];
            for (int bit = 0; bit < 8; bit++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (crc >> 1) ^ polynomial;
                }
                else
                {
                    crc >>= 1;
                }
            }
        }

        return crc ^ 0xFFFFFFFF;
    }

    private unsafe BinaryHeader ReadHeader()
    {
        return *(BinaryHeader*)_basePtr;
    }

    private unsafe void ReadTableDirectory()
    {
        var directoryPtr = (TableDirectoryEntry*)(_basePtr + sizeof(BinaryHeader));

        // Find string table start (after directory)
        long stringTableStart = sizeof(BinaryHeader) + sizeof(TableDirectoryEntry) * _header.TableCount;

        for (int i = 0; i < _header.TableCount; i++)
        {
            var entry = directoryPtr[i];

            // Read table name from string table
            var namePtr = _basePtr + stringTableStart + entry.NameOffset;
            var name = ReadNullTerminatedString(namePtr);

            _tables[name] = new TableInfo
            {
                Name = name,
                RecordOffset = entry.RecordOffset,
                RecordCount = (int)entry.RecordCount,
                RecordSize = (int)entry.RecordSize,
                SlotArrayOffset = entry.SlotArrayOffset,
                SlotArrayLength = (int)entry.SlotArrayLength
            };
        }
    }

    private static unsafe string ReadNullTerminatedString(byte* ptr)
    {
        int length = 0;
        while (ptr[length] != 0) length++;
        return Encoding.UTF8.GetString(ptr, length);
    }

    private unsafe void LoadStringRegistry()
    {
        if (_header.StringRegistryOffset == 0 || _header.StringRegistryCount == 0)
            return;

        var ptr = _basePtr + _header.StringRegistryOffset;
        for (int i = 0; i < _header.StringRegistryCount; i++)
        {
            uint id = *(uint*)ptr;
            ptr += 4;
            ushort strLen = *(ushort*)ptr;
            ptr += 2;
            var str = Encoding.UTF8.GetString(ptr, strLen);
            ptr += strLen;

            StringRegistry.Instance.RegisterWithId(id, str);
        }
    }

    /// <summary>
    /// Gets a read-only span of records for a table.
    /// </summary>
    public unsafe ReadOnlySpan<T> GetRecords<T>(string tableName) where T : unmanaged
    {
        if (!_tables.TryGetValue(tableName, out var info))
            throw new KeyNotFoundException($"Table '{tableName}' not found");

        if (sizeof(T) != info.RecordSize)
            throw new InvalidOperationException($"Type size mismatch: {typeof(T).Name} is {sizeof(T)} bytes, table expects {info.RecordSize}");

        var ptr = (T*)(_basePtr + info.RecordOffset);
        return new ReadOnlySpan<T>(ptr, info.RecordCount);
    }

    /// <summary>
    /// Gets the slot array for O(1) lookups on a table.
    /// </summary>
    public unsafe ReadOnlySpan<int> GetSlotArray(string tableName)
    {
        if (!_tables.TryGetValue(tableName, out var info))
            throw new KeyNotFoundException($"Table '{tableName}' not found");

        if (info.SlotArrayLength == 0)
            return ReadOnlySpan<int>.Empty;

        var ptr = (int*)(_basePtr + info.SlotArrayOffset);
        return new ReadOnlySpan<int>(ptr, info.SlotArrayLength);
    }

    /// <summary>
    /// Finds a record by primary key using O(1) slot array lookup.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref readonly T FindById<T>(string tableName, int id) where T : unmanaged
    {
        if (!_tables.TryGetValue(tableName, out var info))
            throw new KeyNotFoundException($"Table '{tableName}' not found");

        var slotArray = (int*)(_basePtr + info.SlotArrayOffset);
        if ((uint)id >= (uint)info.SlotArrayLength)
            throw new KeyNotFoundException($"Id {id} out of range for table '{tableName}'");

        int slot = slotArray[id];
        if (slot < 0)
            throw new KeyNotFoundException($"Id {id} not found in table '{tableName}'");

        var records = (T*)(_basePtr + info.RecordOffset);
        return ref records[slot];
    }

    /// <summary>
    /// Tries to find a record by primary key.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool TryFindById<T>(string tableName, int id, out T result) where T : unmanaged
    {
        result = default;

        if (!_tables.TryGetValue(tableName, out var info))
            return false;

        var slotArray = (int*)(_basePtr + info.SlotArrayOffset);
        if ((uint)id >= (uint)info.SlotArrayLength)
            return false;

        int slot = slotArray[id];
        if (slot < 0)
            return false;

        var records = (T*)(_basePtr + info.RecordOffset);
        result = records[slot];
        return true;
    }

    /// <summary>
    /// Gets a direct pointer to records for zero-dictionary-lookup access.
    /// The returned struct can be cached for repeated fast access.
    /// </summary>
    public unsafe TableAccessor<T> GetTableAccessor<T>(string tableName) where T : unmanaged
    {
        if (!_tables.TryGetValue(tableName, out var info))
            throw new KeyNotFoundException($"Table '{tableName}' not found");

        if (sizeof(T) != info.RecordSize)
            throw new InvalidOperationException($"Type size mismatch: {typeof(T).Name} is {sizeof(T)} bytes, table expects {info.RecordSize}");

        return new TableAccessor<T>(
            (T*)(_basePtr + info.RecordOffset),
            info.RecordCount,
            (int*)(_basePtr + info.SlotArrayOffset),
            info.SlotArrayLength);
    }

    /// <summary>
    /// Gets table names.
    /// </summary>
    public IEnumerable<string> TableNames => _tables.Keys;

    /// <summary>
    /// Gets record count for a table.
    /// </summary>
    public int GetRecordCount(string tableName)
    {
        return _tables.TryGetValue(tableName, out var info) ? info.RecordCount : 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_pointerAcquired)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointerAcquired = false;
        }

        _accessor.Dispose();
        _mmf.Dispose();
    }

    private void CleanupFailedInitialization()
    {
        if (_pointerAcquired)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointerAcquired = false;
        }

        _accessor.Dispose();
        _mmf.Dispose();
    }

    private class TableInfo
    {
        public string Name { get; init; } = "";
        public uint RecordOffset { get; init; }
        public int RecordCount { get; init; }
        public int RecordSize { get; init; }
        public uint SlotArrayOffset { get; init; }
        public int SlotArrayLength { get; init; }
    }
}

/// <summary>
/// Cached accessor for a table's records with zero dictionary lookup overhead.
/// Store this struct to avoid repeated dictionary lookups on each access.
/// </summary>
public readonly unsafe struct TableAccessor<T> where T : unmanaged
{
    private readonly T* _records;
    private readonly int _recordCount;
    private readonly int* _slotArray;
    private readonly int _slotArrayLength;

    internal TableAccessor(T* records, int recordCount, int* slotArray, int slotArrayLength)
    {
        _records = records;
        _recordCount = recordCount;
        _slotArray = slotArray;
        _slotArrayLength = slotArrayLength;
    }

    /// <summary>Number of records in this table.</summary>
    public int Count => _recordCount;

    /// <summary>All records as a read-only span (zero-copy).</summary>
    public ReadOnlySpan<T> All => new ReadOnlySpan<T>(_records, _recordCount);

    /// <summary>Slot array for O(1) primary key lookups.</summary>
    public ReadOnlySpan<int> SlotArray => new ReadOnlySpan<int>(_slotArray, _slotArrayLength);

    /// <summary>Finds a record by primary key (O(1) lookup, no dictionary overhead).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly T FindById(int id)
    {
        int slot = _slotArray[id];
        return ref _records[slot];
    }

    /// <summary>Tries to find a record by primary key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryFindById(int id, out T result)
    {
        if ((uint)id >= (uint)_slotArrayLength)
        {
            result = default;
            return false;
        }
        int slot = _slotArray[id];
        if (slot < 0)
        {
            result = default;
            return false;
        }
        result = _records[slot];
        return true;
    }

    /// <summary>Gets a record by index (not by ID).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly T GetAtIndex(int index) => ref _records[index];
}
