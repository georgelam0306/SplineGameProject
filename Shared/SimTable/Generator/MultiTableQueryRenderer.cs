#nullable enable
using System.Text;

namespace SimTable.Generator
{
    internal static class MultiTableQueryRenderer
    {
        public static string Render(MultiTableQueryModel model)
        {
            var sb = new StringBuilder();
            var name = model.QueryName;
            var ns = model.Type.ContainingNamespace?.ToDisplayString();

            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using SimTable;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
            }

            // Generate the ref struct for accessing entity fields
            GenerateRefStruct(sb, model);
            sb.AppendLine();

            // Generate the enumerable wrapper
            GenerateEnumerable(sb, model);
            sb.AppendLine();

            // Generate the enumerator
            GenerateEnumerator(sb, model);
            sb.AppendLine();

            // Generate the table chunk for SIMD
            GenerateTableChunk(sb, model);
            sb.AppendLine();

            // Generate the by-table enumerable/enumerator
            GenerateByTableEnumerable(sb, model);
            sb.AppendLine();

            // Generate the Query<T>() extension method
            GenerateQueryExtension(sb, model);

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static void GenerateQueryExtension(StringBuilder sb, MultiTableQueryModel model)
        {
            var name = model.QueryName;
            var interfaceName = model.Type.Name;

            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Extension methods for querying {interfaceName} from SimWorld.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public static class {name}QueryExtensions");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// Query all entities matching {interfaceName} across participating tables.");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {name}Enumerable Query<T>(this SimWorld world) where T : {interfaceName}");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            return new {name}Enumerable(world);");
            sb.AppendLine($"        }}");
            sb.AppendLine();

            // Generate TryGet method for handle-based lookup
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// Try to get an entity matching {interfaceName} by its handle.");
            sb.AppendLine($"        /// Returns true if the handle refers to a valid entity in a participating table.");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static bool TryGet{name}(this SimWorld world, SimHandle handle, out {name}Ref result)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            if (!handle.IsValid)");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                result = default;");
            sb.AppendLine($"                return false;");
            sb.AppendLine($"            }}");
            sb.AppendLine();

            for (int i = 0; i < model.ParticipatingTables.Count; i++)
            {
                var table = model.ParticipatingTables[i];
                var tableName = table.Schema.Type.Name;
                var ifKeyword = i == 0 ? "if" : "else if";

                sb.AppendLine($"            {ifKeyword} (handle.TableId == {tableName}Table.TableIdConst)");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                int slot = world.{tableName}s.GetSlot(handle);");
                sb.AppendLine($"                if (slot >= 0)");
                sb.AppendLine($"                {{");
                sb.AppendLine($"                    result = new {name}Ref({table.SwitchIndex}, slot, world);");
                sb.AppendLine($"                    return true;");
                sb.AppendLine($"                }}");
                sb.AppendLine($"            }}");
            }

            sb.AppendLine();
            sb.AppendLine($"            result = default;");
            sb.AppendLine($"            return false;");
            sb.AppendLine($"        }}");

            sb.AppendLine($"    }}");
        }

        private static void GenerateRefStruct(StringBuilder sb, MultiTableQueryModel model)
        {
            var name = model.QueryName;

            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Ref struct for accessing fields of entities matching {model.Type.Name}.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public readonly ref struct {name}Ref");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        private readonly byte _tableIndex;");
            sb.AppendLine($"        private readonly int _slot;");
            sb.AppendLine($"        private readonly SimWorld _world;");
            sb.AppendLine();
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        internal {name}Ref(byte tableIndex, int slot, SimWorld world)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            _tableIndex = tableIndex;");
            sb.AppendLine($"            _slot = slot;");
            sb.AppendLine($"            _world = world;");
            sb.AppendLine($"        }}");
            sb.AppendLine();

            // Generate Handle property
            sb.AppendLine($"        /// <summary>Gets the underlying SimHandle for this entity.</summary>");
            sb.AppendLine($"        public SimHandle Handle");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"            get => _tableIndex switch");
            sb.AppendLine($"            {{");
            foreach (var table in model.ParticipatingTables)
            {
                var tableName = table.Schema.Type.Name;
                sb.AppendLine($"                {table.SwitchIndex} => _world.{tableName}s.GetHandle(_slot),");
            }
            sb.AppendLine($"                _ => SimHandle.Invalid");
            sb.AppendLine($"            }};");
            sb.AppendLine($"        }}");
            sb.AppendLine();

            // Generate property for each query property (using if-else since ref doesn't work in switch expressions)
            foreach (var prop in model.Properties)
            {
                var typeName = prop.Type.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);

                sb.AppendLine($"        public ref {typeName} {prop.Name}");
                sb.AppendLine($"        {{");
                sb.AppendLine($"            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine($"            get");
                sb.AppendLine($"            {{");

                for (int i = 0; i < model.ParticipatingTables.Count; i++)
                {
                    var table = model.ParticipatingTables[i];
                    var tableName = table.Schema.Type.Name;
                    var ifKeyword = i == 0 ? "if" : "else if";
                    sb.AppendLine($"                {ifKeyword} (_tableIndex == {table.SwitchIndex}) return ref _world.{tableName}s.{prop.Name}(_slot);");
                }

                sb.AppendLine($"                return ref Unsafe.NullRef<{typeName}>();");
                sb.AppendLine($"            }}");
                sb.AppendLine($"        }}");
                sb.AppendLine();
            }

            // Generate Is<T>() method for type discrimination
            sb.AppendLine($"        /// <summary>Checks if this entity is of the specified table type.</summary>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public bool Is<T>() where T : struct");
            sb.AppendLine($"        {{");

            for (int i = 0; i < model.ParticipatingTables.Count; i++)
            {
                var table = model.ParticipatingTables[i];
                var tableName = table.Schema.Type.Name;
                var condition = i == 0 ? "if" : "else if";
                sb.AppendLine($"            {condition} (typeof(T) == typeof({tableName})) return _tableIndex == {table.SwitchIndex};");
            }

            sb.AppendLine($"            return false;");
            sb.AppendLine($"        }}");

            // Generate Free() method - uses Handle.TableId for dispatch
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>Frees this entity from its table.</summary>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public void Free()");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            var handle = Handle;");
            sb.AppendLine($"            _world.GetTableById(handle.TableId).Free(handle);");
            sb.AppendLine($"        }}");

            sb.AppendLine($"    }}");
        }

        private static void GenerateEnumerable(StringBuilder sb, MultiTableQueryModel model)
        {
            var name = model.QueryName;

            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Enumerable for iterating over all entities matching {model.Type.Name}.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public readonly ref struct {name}Enumerable");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        private readonly SimWorld _world;");
            sb.AppendLine();
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        internal {name}Enumerable(SimWorld world) => _world = world;");
            sb.AppendLine();
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public {name}Enumerator GetEnumerator() => new {name}Enumerator(_world);");
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>Iterate by table for SIMD-friendly span access.</summary>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public {name}ByTableEnumerable ByTable() => new {name}ByTableEnumerable(_world);");
            sb.AppendLine($"    }}");
        }

        private static void GenerateEnumerator(StringBuilder sb, MultiTableQueryModel model)
        {
            var name = model.QueryName;
            var tableCount = model.ParticipatingTables.Count;

            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Enumerator for iterating over all entities matching {model.Type.Name}.");
            sb.AppendLine($"    /// Slots are contiguous [0, Count-1] so no mask checking is needed.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public ref struct {name}Enumerator");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        private readonly SimWorld _world;");
            sb.AppendLine($"        private byte _tableIndex;");
            sb.AppendLine($"        private int _slot;");
            sb.AppendLine($"        private int _count;");
            sb.AppendLine();
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        internal {name}Enumerator(SimWorld world)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            _world = world;");
            sb.AppendLine($"            _tableIndex = 0;");
            sb.AppendLine($"            _slot = -1;");

            if (model.ParticipatingTables.Count > 0)
            {
                var firstTable = model.ParticipatingTables[0];
                sb.AppendLine($"            _count = world.{firstTable.Schema.Type.Name}s.Count;");
            }
            else
            {
                sb.AppendLine($"            _count = 0;");
            }

            sb.AppendLine($"        }}");
            sb.AppendLine();
            sb.AppendLine($"        public {name}Ref Current");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"            get => new {name}Ref(_tableIndex, _slot, _world);");
            sb.AppendLine($"        }}");
            sb.AppendLine();
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public bool MoveNext()");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            _slot++;");
            sb.AppendLine($"            // Slots are contiguous [0, Count-1], no mask check needed");
            sb.AppendLine($"            if (_slot < _count)");
            sb.AppendLine($"                return true;");
            sb.AppendLine();
            sb.AppendLine($"            // Move to next table");
            sb.AppendLine($"            _tableIndex++;");
            sb.AppendLine($"            if (_tableIndex >= {tableCount})");
            sb.AppendLine($"                return false;");
            sb.AppendLine();
            sb.AppendLine($"            _slot = 0;");
            sb.AppendLine($"            _count = GetTableCount(_tableIndex);");
            sb.AppendLine($"            return _count > 0;");
            sb.AppendLine($"        }}");
            sb.AppendLine();
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        private int GetTableCount(byte tableIndex)");
            sb.AppendLine($"        {{");

            for (int i = 0; i < model.ParticipatingTables.Count; i++)
            {
                var table = model.ParticipatingTables[i];
                var tableName = table.Schema.Type.Name;
                var ifKeyword = i == 0 ? "if" : "else if";
                sb.AppendLine($"            {ifKeyword} (tableIndex == {table.SwitchIndex}) return _world.{tableName}s.Count;");
            }

            sb.AppendLine($"            return 0;");
            sb.AppendLine($"        }}");
            sb.AppendLine($"    }}");
        }

        private static void GenerateTableChunk(StringBuilder sb, MultiTableQueryModel model)
        {
            var name = model.QueryName;

            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// A chunk representing all entities from a single table, with span access for SIMD.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public readonly ref struct {name}TableChunk");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        private readonly byte _tableIndex;");
            sb.AppendLine($"        private readonly SimWorld _world;");
            sb.AppendLine();
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        internal {name}TableChunk(byte tableIndex, SimWorld world)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            _tableIndex = tableIndex;");
            sb.AppendLine($"            _world = world;");
            sb.AppendLine($"        }}");
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>The table index (0, 1, ...).</summary>");
            sb.AppendLine($"        public byte TableIndex => _tableIndex;");
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>Number of active entities in this table.</summary>");
            sb.AppendLine($"        public int Count");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"            get => _tableIndex switch");
            sb.AppendLine($"            {{");

            foreach (var table in model.ParticipatingTables)
            {
                var tableName = table.Schema.Type.Name;
                sb.AppendLine($"                {table.SwitchIndex} => _world.{tableName}s.Count,");
            }

            sb.AppendLine($"                _ => 0");
            sb.AppendLine($"            }};");
            sb.AppendLine($"        }}");
            sb.AppendLine();
            // Note: Capacity property removed - use Count for iteration

            // Generate span properties for each query property
            foreach (var prop in model.Properties)
            {
                var typeName = prop.Type.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);

                sb.AppendLine($"        /// <summary>Contiguous span of {prop.Name} values for SIMD processing.</summary>");
                sb.AppendLine($"        public Span<{typeName}> {prop.Name}s");
                sb.AppendLine($"        {{");
                sb.AppendLine($"            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine($"            get => _tableIndex switch");
                sb.AppendLine($"            {{");

                foreach (var table in model.ParticipatingTables)
                {
                    var tableName = table.Schema.Type.Name;
                    sb.AppendLine($"                {table.SwitchIndex} => _world.{tableName}s.{prop.Name}Span,");
                }

                sb.AppendLine($"                _ => default");
                sb.AppendLine($"            }};");
                sb.AppendLine($"        }}");
                sb.AppendLine();
            }

            // Generate GetHandle(slot) method
            sb.AppendLine($"        /// <summary>Gets the SimHandle for the entity at the given slot.</summary>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public SimHandle GetHandle(int slot)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            return _tableIndex switch");
            sb.AppendLine($"            {{");

            foreach (var table in model.ParticipatingTables)
            {
                var tableName = table.Schema.Type.Name;
                sb.AppendLine($"                {table.SwitchIndex} => _world.{tableName}s.GetHandle(slot),");
            }

            sb.AppendLine($"                _ => SimHandle.Invalid");
            sb.AppendLine($"            }};");
            sb.AppendLine($"        }}");

            // Generate FreeBySlot(slot) method
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>Frees the entity at the given slot. Use with backwards iteration.</summary>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public void FreeBySlot(int slot)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            var handle = GetHandle(slot);");
            sb.AppendLine($"            _world.GetTableById(handle.TableId).Free(handle);");
            sb.AppendLine($"        }}");

            sb.AppendLine($"    }}");
        }

        private static void GenerateByTableEnumerable(StringBuilder sb, MultiTableQueryModel model)
        {
            var name = model.QueryName;
            var tableCount = model.ParticipatingTables.Count;

            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Enumerable for iterating by table (for SIMD-friendly access).");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public readonly ref struct {name}ByTableEnumerable");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        private readonly SimWorld _world;");
            sb.AppendLine();
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        internal {name}ByTableEnumerable(SimWorld world) => _world = world;");
            sb.AppendLine();
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public {name}ByTableEnumerator GetEnumerator() => new {name}ByTableEnumerator(_world);");
            sb.AppendLine($"    }}");
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Enumerator for iterating by table.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public ref struct {name}ByTableEnumerator");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        private readonly SimWorld _world;");
            sb.AppendLine($"        private byte _tableIndex;");
            sb.AppendLine();
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        internal {name}ByTableEnumerator(SimWorld world)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            _world = world;");
            sb.AppendLine($"            _tableIndex = 255; // Will increment to 0 on first MoveNext");
            sb.AppendLine($"        }}");
            sb.AppendLine();
            sb.AppendLine($"        public {name}TableChunk Current");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"            get => new {name}TableChunk(_tableIndex, _world);");
            sb.AppendLine($"        }}");
            sb.AppendLine();
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public bool MoveNext()");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            _tableIndex++;");
            sb.AppendLine($"            return _tableIndex < {tableCount};");
            sb.AppendLine($"        }}");
            sb.AppendLine($"    }}");
        }
    }
}
