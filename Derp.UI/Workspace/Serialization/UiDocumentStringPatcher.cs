using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Core;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class UiDocumentStringPatcher
{
    public static void CollectStringsForSave(UiWorkspace workspace, StringTableBuilder strings)
    {
        CollectEditorLayerStrings(workspace, strings);

        ReadOnlySpan<EntityId> rootChildren = workspace.World.GetChildren(EntityId.Null);
        for (int i = 0; i < rootChildren.Length; i++)
        {
            CollectStringsForEntitySubtree(workspace, rootChildren[i], strings);
        }
    }

    private static void CollectEditorLayerStrings(UiWorkspace workspace, StringTableBuilder strings)
    {
        List<string> names = workspace._editorLayersDocument.NameByStableId;
        for (int i = 1; i < names.Count; i++)
        {
            strings.GetOrAdd(names[i]);
        }
    }

    private static void CollectStringsForEntitySubtree(UiWorkspace workspace, EntityId root, StringTableBuilder strings)
    {
        if (root.IsNull)
        {
            return;
        }

        var stack = new List<EntityId>(capacity: 64);
        stack.Add(root);

        for (int stackIndex = 0; stackIndex < stack.Count; stackIndex++)
        {
            EntityId entity = stack[stackIndex];

            // Skip expanded prefab-instance clones/proxies; save only authored hierarchy.
            if (workspace.World.TryGetComponent(entity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) &&
                expandedAny.IsValid)
            {
                continue;
            }

            CollectStringsFromComponents(workspace, entity, strings);

            ReadOnlySpan<EntityId> children = workspace.World.GetChildren(entity);
            for (int i = 0; i < children.Length; i++)
            {
                EntityId child = children[i];
                if (workspace.World.TryGetComponent(child, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle childExpandedAny) &&
                    childExpandedAny.IsValid)
                {
                    continue;
                }
                stack.Add(child);
            }
        }
    }

    private static void CollectStringsFromComponents(UiWorkspace workspace, EntityId entity, StringTableBuilder strings)
    {
        if (workspace.World.TryGetComponent(entity, TextComponent.Api.PoolIdConst, out AnyComponentHandle textAny) && textAny.IsValid)
        {
            var handle = new TextComponentHandle(textAny.Index, textAny.Generation);
            var view = TextComponent.Api.FromHandle(workspace.PropertyWorld, handle);
            if (view.IsAlive)
            {
                strings.GetOrAdd((string)view.Text);
                strings.GetOrAdd((string)view.Font);
            }
        }

        if (workspace.World.TryGetComponent(entity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) && varsAny.IsValid)
        {
            var handle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
            var view = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, handle);
            if (view.IsAlive)
            {
                ushort count = view.VariableCount;
                int n = Math.Min((int)count, PrefabVariablesComponent.MaxVariables);

                ReadOnlySpan<StringHandle> names = view.NameReadOnlySpan();
                ReadOnlySpan<int> kinds = view.KindReadOnlySpan();
                ReadOnlySpan<PropertyValue> defaults = view.DefaultValueReadOnlySpan();

                for (int i = 0; i < n; i++)
                {
                    strings.GetOrAdd((string)names[i]);
                    if ((PropertyKind)kinds[i] == PropertyKind.StringHandle)
                    {
                        strings.GetOrAdd((string)defaults[i].StringHandle);
                    }
                }
            }
        }

        if (workspace.World.TryGetComponent(entity, PrefabInstancePropertyOverridesComponent.Api.PoolIdConst, out AnyComponentHandle overridesAny) && overridesAny.IsValid)
        {
            var handle = new PrefabInstancePropertyOverridesComponentHandle(overridesAny.Index, overridesAny.Generation);
            var view = PrefabInstancePropertyOverridesComponent.Api.FromHandle(workspace.PropertyWorld, handle);
            if (view.IsAlive)
            {
                ushort count = view.Count;
                int n = Math.Min((int)count, PrefabInstancePropertyOverridesComponent.MaxOverrides);

                ReadOnlySpan<ushort> kinds = view.PropertyKindReadOnlySpan();
                ReadOnlySpan<PropertyValue> values = view.ValueReadOnlySpan();

                for (int i = 0; i < n; i++)
                {
                    if ((PropertyKind)kinds[i] != PropertyKind.StringHandle)
                    {
                        continue;
                    }

                    strings.GetOrAdd((string)values[i].StringHandle);
                }
            }
        }

        if (workspace.World.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) && instanceAny.IsValid)
        {
            var handle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var view = PrefabInstanceComponent.Api.FromHandle(workspace.PropertyWorld, handle);
            if (view.IsAlive)
            {
                ushort count = view.ValueCount;
                if (count == 0)
                {
                    return;
                }

                if (!TryGetPrefabVariablesForInstance(workspace, view.SourcePrefabStableId, out PrefabVariablesComponent.ViewProxy vars))
                {
                    throw new InvalidOperationException("Prefab instance references missing source prefab variables.");
                }

                int n = Math.Min((int)count, PrefabInstanceComponent.MaxVariables);
                ReadOnlySpan<ushort> variableIds = view.VariableIdReadOnlySpan();
                ReadOnlySpan<PropertyValue> values = view.ValueReadOnlySpan();

                for (int i = 0; i < n; i++)
                {
                    ushort variableId = variableIds[i];
                    if (variableId == 0)
                    {
                        continue;
                    }

                    if (!TryGetPrefabVariableKind(vars, variableId, out PropertyKind kind))
                    {
                        throw new InvalidOperationException("Prefab instance contains unknown variable id.");
                    }

                    if (kind == PropertyKind.StringHandle)
                    {
                        strings.GetOrAdd((string)values[i].StringHandle);
                    }
                }
            }
        }

        if (workspace.World.TryGetComponent(entity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle listAny) && listAny.IsValid)
        {
            var handle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var view = PrefabListDataComponent.Api.FromHandle(workspace.PropertyWorld, handle);
            if (view.IsAlive)
            {
                CollectStringsFromListData(workspace, entity, view, strings);
            }
        }

        if (workspace.World.TryGetComponent(entity, AnimationLibraryComponent.Api.PoolIdConst, out AnyComponentHandle libraryAny) && libraryAny.IsValid)
        {
            var handle = new AnimationLibraryComponentHandle(libraryAny.Index, libraryAny.Generation);
            var view = AnimationLibraryComponent.Api.FromHandle(workspace.PropertyWorld, handle);
            if (view.IsAlive)
            {
                ushort folderCount = view.FolderCount;
                int folderN = Math.Min((int)folderCount, AnimationLibraryComponent.MaxFolders);
                ReadOnlySpan<StringHandle> folderNames = view.FolderNameReadOnlySpan();
                for (int i = 0; i < folderN; i++)
                {
                    strings.GetOrAdd((string)folderNames[i]);
                }

                ushort timelineCount = view.TimelineCount;
                int timelineN = Math.Min((int)timelineCount, AnimationLibraryComponent.MaxTimelines);
                ReadOnlySpan<StringHandle> timelineNames = view.TimelineNameReadOnlySpan();
                for (int i = 0; i < timelineN; i++)
                {
                    strings.GetOrAdd((string)timelineNames[i]);
                }

                ushort trackCount = view.TrackCount;
                int trackN = Math.Min((int)trackCount, AnimationLibraryComponent.MaxTracks);
                ushort keyCount = view.KeyCount;
                int keyN = Math.Min((int)keyCount, AnimationLibraryComponent.MaxKeys);

                ReadOnlySpan<ushort> trackPropertyKind = view.TrackPropertyKindReadOnlySpan();
                ReadOnlySpan<ushort> trackKeyStart = view.TrackKeyStartReadOnlySpan();
                ReadOnlySpan<ushort> trackKeyCount = view.TrackKeyCountReadOnlySpan();
                ReadOnlySpan<PropertyValue> keyValues = view.KeyValueReadOnlySpan();

                for (int t = 0; t < trackN; t++)
                {
                    if ((PropertyKind)trackPropertyKind[t] != PropertyKind.StringHandle)
                    {
                        continue;
                    }

                    int start = trackKeyStart[t];
                    int count = trackKeyCount[t];
                    int end = start + count;
                    if ((uint)start >= (uint)keyN || (uint)end > (uint)keyN)
                    {
                        continue;
                    }

                    for (int k = start; k < end; k++)
                    {
                        strings.GetOrAdd((string)keyValues[k].StringHandle);
                    }
                }
            }
        }

        if (workspace.World.TryGetComponent(entity, StateMachineDefinitionComponent.Api.PoolIdConst, out AnyComponentHandle stateMachineAny) && stateMachineAny.IsValid)
        {
            var handle = new StateMachineDefinitionComponentHandle(stateMachineAny.Index, stateMachineAny.Generation);
            var view = StateMachineDefinitionComponent.Api.FromHandle(workspace.PropertyWorld, handle);
            if (view.IsAlive)
            {
                ushort machineCount = view.MachineCount;
                int machineN = Math.Min((int)machineCount, StateMachineDefinitionComponent.MaxMachines);
                ReadOnlySpan<StringHandle> machineNames = view.MachineNameReadOnlySpan();
                for (int i = 0; i < machineN; i++)
                {
                    strings.GetOrAdd((string)machineNames[i]);
                }

                ushort layerCount = view.LayerCount;
                int layerN = Math.Min((int)layerCount, StateMachineDefinitionComponent.MaxLayers);
                ReadOnlySpan<StringHandle> layerNames = view.LayerNameReadOnlySpan();
                for (int i = 0; i < layerN; i++)
                {
                    strings.GetOrAdd((string)layerNames[i]);
                }

                ushort stateCount = view.StateCount;
                int stateN = Math.Min((int)stateCount, StateMachineDefinitionComponent.MaxStates);
                ReadOnlySpan<StringHandle> stateNames = view.StateNameReadOnlySpan();
                for (int i = 0; i < stateN; i++)
                {
                    strings.GetOrAdd((string)stateNames[i]);
                }
            }
        }
    }

    private static void CollectStringsFromListData(UiWorkspace workspace, EntityId ownerEntity, PrefabListDataComponent.ViewProxy list, StringTableBuilder strings)
    {
        ushort entryCount = list.EntryCount;
        int n = Math.Min((int)entryCount, PrefabListDataComponent.MaxListEntries);

        ReadOnlySpan<ushort> entryVarId = list.EntryVariableIdReadOnlySpan();
        ReadOnlySpan<ushort> entryItemCount = list.EntryItemCountReadOnlySpan();
        ReadOnlySpan<ushort> entryItemStart = list.EntryItemStartReadOnlySpan();
        ReadOnlySpan<ushort> entryFieldCount = list.EntryFieldCountReadOnlySpan();
        ReadOnlySpan<PropertyValue> items = list.ItemsReadOnlySpan();

        Span<byte> stringFieldMask = stackalloc byte[PrefabListDataComponent.MaxFieldsPerItem];

        for (int entryIndex = 0; entryIndex < n; entryIndex++)
        {
            ushort variableId = entryVarId[entryIndex];
            int itemCount = entryItemCount[entryIndex];
            int itemStart = entryItemStart[entryIndex];
            int fieldCount = entryFieldCount[entryIndex];

            if (itemCount <= 0 || fieldCount <= 0)
            {
                continue;
            }

            if (!TryResolveListTypePrefabStableId(workspace, ownerEntity, variableId, out uint typeStableId) || typeStableId == 0)
            {
                throw new InvalidOperationException("List data has items but no list type is set.");
            }

            if (!TryGetListSchemaStringFieldMask(workspace, typeStableId, fieldCount, stringFieldMask))
            {
                throw new InvalidOperationException("List data references an invalid type prefab.");
            }

            for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                int baseIndex = itemStart + itemIndex * fieldCount;
                for (int fieldIndex = 0; fieldIndex < fieldCount && fieldIndex < PrefabListDataComponent.MaxFieldsPerItem; fieldIndex++)
                {
                    if (stringFieldMask[fieldIndex] == 0)
                    {
                        continue;
                    }

                    int slotIndex = baseIndex + fieldIndex;
                    if ((uint)slotIndex >= (uint)items.Length)
                    {
                        continue;
                    }

                    strings.GetOrAdd((string)items[slotIndex].StringHandle);
                }
            }
        }
    }

    public static void PatchPackedComponentForSave(UiWorkspace workspace, EntityId ownerEntity, ushort componentKind, Span<byte> bytes, StringTableBuilder strings)
    {
        if (componentKind == TextComponent.Api.PoolIdConst)
        {
            PatchTextComponentForSave(bytes, strings);
            return;
        }

        if (componentKind == AnimationLibraryComponent.Api.PoolIdConst)
        {
            PatchAnimationLibraryForSave(bytes, strings);
            return;
        }

        if (componentKind == StateMachineDefinitionComponent.Api.PoolIdConst)
        {
            PatchStateMachineDefinitionForSave(bytes, strings);
            return;
        }

        if (componentKind == PrefabVariablesComponent.Api.PoolIdConst)
        {
            PatchPrefabVariablesForSave(bytes, strings);
            return;
        }

        if (componentKind == PrefabInstancePropertyOverridesComponent.Api.PoolIdConst)
        {
            PatchPrefabInstanceOverridesForSave(bytes, strings);
            return;
        }

        if (componentKind == PrefabInstanceComponent.Api.PoolIdConst)
        {
            PatchPrefabInstanceForSave(workspace, bytes, strings);
            return;
        }

        if (componentKind == PrefabListDataComponent.Api.PoolIdConst)
        {
            PatchPrefabListDataForSave(workspace, ownerEntity, bytes, strings);
            return;
        }
    }

    public static void UnpatchPackedComponentForLoad(UiWorkspace workspace, EntityId ownerEntity, ushort componentKind, Span<byte> bytes, IReadOnlyList<string> stringTable)
    {
        if (componentKind == TextComponent.Api.PoolIdConst)
        {
            UnpatchTextComponentForLoad(bytes, stringTable);
            return;
        }

        if (componentKind == AnimationLibraryComponent.Api.PoolIdConst)
        {
            UnpatchAnimationLibraryForLoad(bytes, stringTable);
            return;
        }

        if (componentKind == StateMachineDefinitionComponent.Api.PoolIdConst)
        {
            UnpatchStateMachineDefinitionForLoad(bytes, stringTable);
            return;
        }

        if (componentKind == PrefabVariablesComponent.Api.PoolIdConst)
        {
            UnpatchPrefabVariablesForLoad(bytes, stringTable);
            return;
        }

        if (componentKind == PrefabInstancePropertyOverridesComponent.Api.PoolIdConst)
        {
            UnpatchPrefabInstanceOverridesForLoad(bytes, stringTable);
            return;
        }

        if (componentKind == PrefabInstanceComponent.Api.PoolIdConst)
        {
            UnpatchPrefabInstanceForLoad(workspace, bytes, stringTable);
            return;
        }

        if (componentKind == PrefabListDataComponent.Api.PoolIdConst)
        {
            UnpatchPrefabListDataForLoad(workspace, ownerEntity, bytes, stringTable);
            return;
        }
    }

    private static void PatchTextComponentForSave(Span<byte> bytes, StringTableBuilder strings)
    {
        ref TextComponent snapshot = ref MemoryMarshal.AsRef<TextComponent>(bytes);

        uint textIndex = strings.GetOrAdd((string)snapshot.Text);
        Unsafe.As<StringHandle, uint>(ref snapshot.Text) = textIndex;

        uint fontIndex = strings.GetOrAdd((string)snapshot.Font);
        Unsafe.As<StringHandle, uint>(ref snapshot.Font) = fontIndex;
    }

    private static void UnpatchTextComponentForLoad(Span<byte> bytes, IReadOnlyList<string> stringTable)
    {
        ref TextComponent snapshot = ref MemoryMarshal.AsRef<TextComponent>(bytes);

        uint textIndex = Unsafe.As<StringHandle, uint>(ref snapshot.Text);
        Unsafe.As<StringHandle, uint>(ref snapshot.Text) = RegisterStringHandleFromIndex(textIndex, stringTable);

        uint fontIndex = Unsafe.As<StringHandle, uint>(ref snapshot.Font);
        Unsafe.As<StringHandle, uint>(ref snapshot.Font) = RegisterStringHandleFromIndex(fontIndex, stringTable);
    }

    private static void PatchAnimationLibraryForSave(Span<byte> bytes, StringTableBuilder strings)
    {
        ref AnimationLibraryComponent snapshot = ref MemoryMarshal.AsRef<AnimationLibraryComponent>(bytes);

        int folderCount = Math.Min((int)snapshot.FolderCount, AnimationLibraryComponent.MaxFolders);
        for (int i = 0; i < folderCount; i++)
        {
            ref StringHandle name = ref snapshot.FolderName[i];
            uint nameIndex = strings.GetOrAdd((string)name);
            Unsafe.As<StringHandle, uint>(ref name) = nameIndex;
        }

        int timelineCount = Math.Min((int)snapshot.TimelineCount, AnimationLibraryComponent.MaxTimelines);
        for (int i = 0; i < timelineCount; i++)
        {
            ref StringHandle name = ref snapshot.TimelineName[i];
            uint nameIndex = strings.GetOrAdd((string)name);
            Unsafe.As<StringHandle, uint>(ref name) = nameIndex;
        }

        int trackCount = Math.Min((int)snapshot.TrackCount, AnimationLibraryComponent.MaxTracks);
        int keyCountTotal = Math.Min((int)snapshot.KeyCount, AnimationLibraryComponent.MaxKeys);
        for (int i = 0; i < trackCount; i++)
        {
            if ((PropertyKind)snapshot.TrackPropertyKind[i] != PropertyKind.StringHandle)
            {
                continue;
            }

            int start = snapshot.TrackKeyStart[i];
            int count = snapshot.TrackKeyCount[i];
            int end = start + count;
            if ((uint)start >= (uint)keyCountTotal || (uint)end > (uint)keyCountTotal)
            {
                continue;
            }

            for (int k = start; k < end; k++)
            {
                ref PropertyValue value = ref snapshot.KeyValue[k];
                uint valueIndex = strings.GetOrAdd((string)value.StringHandle);
                Unsafe.As<PropertyValue, uint>(ref value) = valueIndex;
            }
        }
    }

    private static void UnpatchAnimationLibraryForLoad(Span<byte> bytes, IReadOnlyList<string> stringTable)
    {
        ref AnimationLibraryComponent snapshot = ref MemoryMarshal.AsRef<AnimationLibraryComponent>(bytes);

        int folderCount = Math.Min((int)snapshot.FolderCount, AnimationLibraryComponent.MaxFolders);
        for (int i = 0; i < folderCount; i++)
        {
            ref StringHandle name = ref snapshot.FolderName[i];
            uint nameIndex = Unsafe.As<StringHandle, uint>(ref name);
            Unsafe.As<StringHandle, uint>(ref name) = RegisterStringHandleFromIndex(nameIndex, stringTable);
        }

        int timelineCount = Math.Min((int)snapshot.TimelineCount, AnimationLibraryComponent.MaxTimelines);
        for (int i = 0; i < timelineCount; i++)
        {
            ref StringHandle name = ref snapshot.TimelineName[i];
            uint nameIndex = Unsafe.As<StringHandle, uint>(ref name);
            Unsafe.As<StringHandle, uint>(ref name) = RegisterStringHandleFromIndex(nameIndex, stringTable);
        }

        int trackCount = Math.Min((int)snapshot.TrackCount, AnimationLibraryComponent.MaxTracks);
        int keyCountTotal = Math.Min((int)snapshot.KeyCount, AnimationLibraryComponent.MaxKeys);
        for (int i = 0; i < trackCount; i++)
        {
            if ((PropertyKind)snapshot.TrackPropertyKind[i] != PropertyKind.StringHandle)
            {
                continue;
            }

            int start = snapshot.TrackKeyStart[i];
            int count = snapshot.TrackKeyCount[i];
            int end = start + count;
            if ((uint)start >= (uint)keyCountTotal || (uint)end > (uint)keyCountTotal)
            {
                continue;
            }

            for (int k = start; k < end; k++)
            {
                ref PropertyValue value = ref snapshot.KeyValue[k];
                uint valueIndex = Unsafe.As<PropertyValue, uint>(ref value);
                Unsafe.As<PropertyValue, uint>(ref value) = RegisterStringHandleFromIndex(valueIndex, stringTable);
            }
        }
    }

    private static void PatchStateMachineDefinitionForSave(Span<byte> bytes, StringTableBuilder strings)
    {
        ref StateMachineDefinitionComponent snapshot = ref MemoryMarshal.AsRef<StateMachineDefinitionComponent>(bytes);

        int machineCount = Math.Min((int)snapshot.MachineCount, StateMachineDefinitionComponent.MaxMachines);
        for (int i = 0; i < machineCount; i++)
        {
            ref StringHandle name = ref snapshot.MachineName[i];
            uint nameIndex = strings.GetOrAdd((string)name);
            Unsafe.As<StringHandle, uint>(ref name) = nameIndex;
        }

        int layerCount = Math.Min((int)snapshot.LayerCount, StateMachineDefinitionComponent.MaxLayers);
        for (int i = 0; i < layerCount; i++)
        {
            ref StringHandle name = ref snapshot.LayerName[i];
            uint nameIndex = strings.GetOrAdd((string)name);
            Unsafe.As<StringHandle, uint>(ref name) = nameIndex;
        }

        int stateCount = Math.Min((int)snapshot.StateCount, StateMachineDefinitionComponent.MaxStates);
        for (int i = 0; i < stateCount; i++)
        {
            ref StringHandle name = ref snapshot.StateName[i];
            uint nameIndex = strings.GetOrAdd((string)name);
            Unsafe.As<StringHandle, uint>(ref name) = nameIndex;
        }
    }

    private static void UnpatchStateMachineDefinitionForLoad(Span<byte> bytes, IReadOnlyList<string> stringTable)
    {
        ref StateMachineDefinitionComponent snapshot = ref MemoryMarshal.AsRef<StateMachineDefinitionComponent>(bytes);

        int machineCount = Math.Min((int)snapshot.MachineCount, StateMachineDefinitionComponent.MaxMachines);
        for (int i = 0; i < machineCount; i++)
        {
            ref StringHandle name = ref snapshot.MachineName[i];
            uint nameIndex = Unsafe.As<StringHandle, uint>(ref name);
            Unsafe.As<StringHandle, uint>(ref name) = RegisterStringHandleFromIndex(nameIndex, stringTable);
        }

        int layerCount = Math.Min((int)snapshot.LayerCount, StateMachineDefinitionComponent.MaxLayers);
        for (int i = 0; i < layerCount; i++)
        {
            ref StringHandle name = ref snapshot.LayerName[i];
            uint nameIndex = Unsafe.As<StringHandle, uint>(ref name);
            Unsafe.As<StringHandle, uint>(ref name) = RegisterStringHandleFromIndex(nameIndex, stringTable);
        }

        int stateCount = Math.Min((int)snapshot.StateCount, StateMachineDefinitionComponent.MaxStates);
        for (int i = 0; i < stateCount; i++)
        {
            ref StringHandle name = ref snapshot.StateName[i];
            uint nameIndex = Unsafe.As<StringHandle, uint>(ref name);
            Unsafe.As<StringHandle, uint>(ref name) = RegisterStringHandleFromIndex(nameIndex, stringTable);
        }
    }

    private static void PatchPrefabVariablesForSave(Span<byte> bytes, StringTableBuilder strings)
    {
        ref PrefabVariablesComponent snapshot = ref MemoryMarshal.AsRef<PrefabVariablesComponent>(bytes);

        int n = Math.Min((int)snapshot.VariableCount, PrefabVariablesComponent.MaxVariables);
        for (int i = 0; i < n; i++)
        {
            ref StringHandle name = ref snapshot.Name[i];
            uint nameIndex = strings.GetOrAdd((string)name);
            Unsafe.As<StringHandle, uint>(ref name) = nameIndex;

            PropertyKind kind = (PropertyKind)snapshot.Kind[i];
            if (kind != PropertyKind.StringHandle)
            {
                continue;
            }

            ref PropertyValue value = ref snapshot.DefaultValue[i];
            uint valueIndex = strings.GetOrAdd((string)value.StringHandle);
            Unsafe.As<PropertyValue, uint>(ref value) = valueIndex;
        }
    }

    private static void UnpatchPrefabVariablesForLoad(Span<byte> bytes, IReadOnlyList<string> stringTable)
    {
        ref PrefabVariablesComponent snapshot = ref MemoryMarshal.AsRef<PrefabVariablesComponent>(bytes);

        int n = Math.Min((int)snapshot.VariableCount, PrefabVariablesComponent.MaxVariables);
        for (int i = 0; i < n; i++)
        {
            ref StringHandle name = ref snapshot.Name[i];
            uint nameIndex = Unsafe.As<StringHandle, uint>(ref name);
            Unsafe.As<StringHandle, uint>(ref name) = RegisterStringHandleFromIndex(nameIndex, stringTable);

            PropertyKind kind = (PropertyKind)snapshot.Kind[i];
            if (kind != PropertyKind.StringHandle)
            {
                continue;
            }

            ref PropertyValue value = ref snapshot.DefaultValue[i];
            uint valueIndex = Unsafe.As<PropertyValue, uint>(ref value);
            Unsafe.As<PropertyValue, uint>(ref value) = RegisterStringHandleFromIndex(valueIndex, stringTable);
        }
    }

    private static void PatchPrefabInstanceOverridesForSave(Span<byte> bytes, StringTableBuilder strings)
    {
        ref PrefabInstancePropertyOverridesComponent snapshot = ref MemoryMarshal.AsRef<PrefabInstancePropertyOverridesComponent>(bytes);

        int n = Math.Min((int)snapshot.Count, PrefabInstancePropertyOverridesComponent.MaxOverrides);
        for (int i = 0; i < n; i++)
        {
            if ((PropertyKind)snapshot.PropertyKind[i] != PropertyKind.StringHandle)
            {
                continue;
            }

            ref PropertyValue value = ref snapshot.Value[i];
            uint valueIndex = strings.GetOrAdd((string)value.StringHandle);
            Unsafe.As<PropertyValue, uint>(ref value) = valueIndex;
        }
    }

    private static void UnpatchPrefabInstanceOverridesForLoad(Span<byte> bytes, IReadOnlyList<string> stringTable)
    {
        ref PrefabInstancePropertyOverridesComponent snapshot = ref MemoryMarshal.AsRef<PrefabInstancePropertyOverridesComponent>(bytes);

        int n = Math.Min((int)snapshot.Count, PrefabInstancePropertyOverridesComponent.MaxOverrides);
        for (int i = 0; i < n; i++)
        {
            if ((PropertyKind)snapshot.PropertyKind[i] != PropertyKind.StringHandle)
            {
                continue;
            }

            ref PropertyValue value = ref snapshot.Value[i];
            uint valueIndex = Unsafe.As<PropertyValue, uint>(ref value);
            Unsafe.As<PropertyValue, uint>(ref value) = RegisterStringHandleFromIndex(valueIndex, stringTable);
        }
    }

    private static void PatchPrefabInstanceForSave(UiWorkspace workspace, Span<byte> bytes, StringTableBuilder strings)
    {
        ref PrefabInstanceComponent snapshot = ref MemoryMarshal.AsRef<PrefabInstanceComponent>(bytes);

        if (snapshot.ValueCount == 0)
        {
            return;
        }

        if (!TryGetPrefabVariablesForInstance(workspace, snapshot.SourcePrefabStableId, out PrefabVariablesComponent.ViewProxy vars))
        {
            throw new InvalidOperationException("Prefab instance references missing source prefab variables.");
        }

        int n = Math.Min((int)snapshot.ValueCount, PrefabInstanceComponent.MaxVariables);
        for (int i = 0; i < n; i++)
        {
            ushort variableId = snapshot.VariableId[i];
            if (variableId == 0)
            {
                continue;
            }

            if (!TryGetPrefabVariableKind(vars, variableId, out PropertyKind kind))
            {
                throw new InvalidOperationException("Prefab instance contains unknown variable id.");
            }

            if (kind != PropertyKind.StringHandle)
            {
                continue;
            }

            ref PropertyValue value = ref snapshot.Value[i];
            uint valueIndex = strings.GetOrAdd((string)value.StringHandle);
            Unsafe.As<PropertyValue, uint>(ref value) = valueIndex;
        }
    }

    private static void UnpatchPrefabInstanceForLoad(UiWorkspace workspace, Span<byte> bytes, IReadOnlyList<string> stringTable)
    {
        ref PrefabInstanceComponent snapshot = ref MemoryMarshal.AsRef<PrefabInstanceComponent>(bytes);

        if (snapshot.ValueCount == 0)
        {
            return;
        }

        if (!TryGetPrefabVariablesForInstance(workspace, snapshot.SourcePrefabStableId, out PrefabVariablesComponent.ViewProxy vars))
        {
            throw new InvalidOperationException("Prefab instance references missing source prefab variables.");
        }

        int n = Math.Min((int)snapshot.ValueCount, PrefabInstanceComponent.MaxVariables);
        for (int i = 0; i < n; i++)
        {
            ushort variableId = snapshot.VariableId[i];
            if (variableId == 0)
            {
                continue;
            }

            if (!TryGetPrefabVariableKind(vars, variableId, out PropertyKind kind))
            {
                throw new InvalidOperationException("Prefab instance contains unknown variable id.");
            }

            if (kind != PropertyKind.StringHandle)
            {
                continue;
            }

            ref PropertyValue value = ref snapshot.Value[i];
            uint valueIndex = Unsafe.As<PropertyValue, uint>(ref value);
            Unsafe.As<PropertyValue, uint>(ref value) = RegisterStringHandleFromIndex(valueIndex, stringTable);
        }
    }

    private static void PatchPrefabListDataForSave(UiWorkspace workspace, EntityId ownerEntity, Span<byte> bytes, StringTableBuilder strings)
    {
        ref PrefabListDataComponent snapshot = ref MemoryMarshal.AsRef<PrefabListDataComponent>(bytes);

        int n = Math.Min((int)snapshot.EntryCount, PrefabListDataComponent.MaxListEntries);
        Span<byte> stringFieldMask = stackalloc byte[PrefabListDataComponent.MaxFieldsPerItem];

        for (int entryIndex = 0; entryIndex < n; entryIndex++)
        {
            ushort variableId = snapshot.EntryVariableId[entryIndex];
            int itemCount = snapshot.EntryItemCount[entryIndex];
            int itemStart = snapshot.EntryItemStart[entryIndex];
            int fieldCount = snapshot.EntryFieldCount[entryIndex];

            if (itemCount <= 0 || fieldCount <= 0)
            {
                continue;
            }

            if (!TryResolveListTypePrefabStableId(workspace, ownerEntity, variableId, out uint typeStableId) || typeStableId == 0)
            {
                throw new InvalidOperationException("List data has items but no list type is set.");
            }

            if (!TryGetListSchemaStringFieldMask(workspace, typeStableId, fieldCount, stringFieldMask))
            {
                throw new InvalidOperationException("List data references an invalid type prefab.");
            }

            for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                int baseIndex = itemStart + itemIndex * fieldCount;
                for (int fieldIndex = 0; fieldIndex < fieldCount && fieldIndex < PrefabListDataComponent.MaxFieldsPerItem; fieldIndex++)
                {
                    if (stringFieldMask[fieldIndex] == 0)
                    {
                        continue;
                    }

                    int slotIndex = baseIndex + fieldIndex;
                    if ((uint)slotIndex >= (uint)PrefabListDataComponent.MaxTotalSlots)
                    {
                        continue;
                    }

                    ref PropertyValue value = ref snapshot.Items[slotIndex];
                    uint valueIndex = strings.GetOrAdd((string)value.StringHandle);
                    Unsafe.As<PropertyValue, uint>(ref value) = valueIndex;
                }
            }
        }
    }

    private static void UnpatchPrefabListDataForLoad(UiWorkspace workspace, EntityId ownerEntity, Span<byte> bytes, IReadOnlyList<string> stringTable)
    {
        ref PrefabListDataComponent snapshot = ref MemoryMarshal.AsRef<PrefabListDataComponent>(bytes);

        int n = Math.Min((int)snapshot.EntryCount, PrefabListDataComponent.MaxListEntries);
        Span<byte> stringFieldMask = stackalloc byte[PrefabListDataComponent.MaxFieldsPerItem];

        for (int entryIndex = 0; entryIndex < n; entryIndex++)
        {
            ushort variableId = snapshot.EntryVariableId[entryIndex];
            int itemCount = snapshot.EntryItemCount[entryIndex];
            int itemStart = snapshot.EntryItemStart[entryIndex];
            int fieldCount = snapshot.EntryFieldCount[entryIndex];

            if (itemCount <= 0 || fieldCount <= 0)
            {
                continue;
            }

            if (!TryResolveListTypePrefabStableId(workspace, ownerEntity, variableId, out uint typeStableId) || typeStableId == 0)
            {
                throw new InvalidOperationException("List data has items but no list type is set.");
            }

            if (!TryGetListSchemaStringFieldMask(workspace, typeStableId, fieldCount, stringFieldMask))
            {
                throw new InvalidOperationException("List data references an invalid type prefab.");
            }

            for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                int baseIndex = itemStart + itemIndex * fieldCount;
                for (int fieldIndex = 0; fieldIndex < fieldCount && fieldIndex < PrefabListDataComponent.MaxFieldsPerItem; fieldIndex++)
                {
                    if (stringFieldMask[fieldIndex] == 0)
                    {
                        continue;
                    }

                    int slotIndex = baseIndex + fieldIndex;
                    if ((uint)slotIndex >= (uint)PrefabListDataComponent.MaxTotalSlots)
                    {
                        continue;
                    }

                    ref PropertyValue value = ref snapshot.Items[slotIndex];
                    uint valueIndex = Unsafe.As<PropertyValue, uint>(ref value);
                    Unsafe.As<PropertyValue, uint>(ref value) = RegisterStringHandleFromIndex(valueIndex, stringTable);
                }
            }
        }
    }

    private static uint RegisterStringHandleFromIndex(uint stringIndex, IReadOnlyList<string> stringTable)
    {
        if (stringIndex == 0)
        {
            return 0;
        }

        if (stringIndex >= (uint)stringTable.Count)
        {
            throw new InvalidOperationException("String table index is out of range.");
        }

        string value = stringTable[(int)stringIndex];
        StringHandle handle = value;
        return Unsafe.As<StringHandle, uint>(ref handle);
    }

    private static bool TryGetPrefabVariablesForInstance(UiWorkspace workspace, uint sourcePrefabStableId, out PrefabVariablesComponent.ViewProxy vars)
    {
        vars = default;
        if (sourcePrefabStableId == 0)
        {
            return false;
        }

        EntityId sourcePrefabEntity = workspace.World.GetEntityByStableId(sourcePrefabStableId);
        if (sourcePrefabEntity.IsNull || workspace.World.GetNodeType(sourcePrefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (!workspace.World.TryGetComponent(sourcePrefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return false;
        }

        var handle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, handle);
        return vars.IsAlive;
    }

    private static bool TryGetPrefabVariableKind(PrefabVariablesComponent.ViewProxy vars, ushort variableId, out PropertyKind kind)
    {
        kind = default;
        if (!vars.IsAlive || variableId == 0)
        {
            return false;
        }

        ushort count = vars.VariableCount;
        int n = Math.Min((int)count, PrefabVariablesComponent.MaxVariables);
        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();

        for (int i = 0; i < n; i++)
        {
            if (ids[i] != variableId)
            {
                continue;
            }

            kind = (PropertyKind)kinds[i];
            return true;
        }

        return false;
    }

    private static bool TryResolveListTypePrefabStableId(UiWorkspace workspace, EntityId ownerEntity, ushort variableId, out uint typeStableId)
    {
        typeStableId = 0;
        if (ownerEntity.IsNull || variableId == 0)
        {
            return false;
        }

        if (workspace.World.GetNodeType(ownerEntity) == UiNodeType.PrefabInstance)
        {
            if (!workspace.World.TryGetComponent(ownerEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
            {
                return false;
            }

            var handle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(workspace.PropertyWorld, handle);
            if (!instance.IsAlive)
            {
                return false;
            }

            int n = Math.Min((int)instance.ValueCount, PrefabInstanceComponent.MaxVariables);
            ReadOnlySpan<ushort> ids = instance.VariableIdReadOnlySpan();
            ReadOnlySpan<PropertyValue> values = instance.ValueReadOnlySpan();

            for (int i = 0; i < n; i++)
            {
                if (ids[i] == variableId)
                {
                    typeStableId = values[i].UInt;
                    return true;
                }
            }

            return false;
        }

        if (!workspace.World.TryGetComponent(ownerEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return false;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive)
        {
            return false;
        }

        int n2 = Math.Min((int)vars.VariableCount, PrefabVariablesComponent.MaxVariables);
        ReadOnlySpan<ushort> ids2 = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds2 = vars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> defaults2 = vars.DefaultValueReadOnlySpan();
        for (int i = 0; i < n2; i++)
        {
            if (ids2[i] != variableId)
            {
                continue;
            }

            if ((PropertyKind)kinds2[i] != PropertyKind.List)
            {
                return false;
            }

            typeStableId = defaults2[i].UInt;
            return true;
        }

        return false;
    }

    private static bool TryGetListSchemaStringFieldMask(UiWorkspace workspace, uint typePrefabStableId, int fieldCount, Span<byte> stringFieldMask)
    {
        if (typePrefabStableId == 0)
        {
            return false;
        }

        EntityId typePrefabEntity = workspace.World.GetEntityByStableId(typePrefabStableId);
        if (typePrefabEntity.IsNull || workspace.World.GetNodeType(typePrefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        for (int i = 0; i < stringFieldMask.Length; i++)
        {
            stringFieldMask[i] = 0;
        }

        if (fieldCount <= 0)
        {
            return true;
        }

        if (!workspace.World.TryGetComponent(typePrefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return true;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive || vars.VariableCount == 0)
        {
            return true;
        }

        int n = Math.Min((int)vars.VariableCount, PrefabVariablesComponent.MaxVariables);
        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();

        int outCount = 0;
        int maxOut = Math.Min(fieldCount, PrefabListDataComponent.MaxFieldsPerItem);

        for (int i = 0; i < n && outCount < maxOut; i++)
        {
            if (ids[i] == 0)
            {
                continue;
            }

            PropertyKind kind = (PropertyKind)kinds[i];
            if (kind == PropertyKind.List)
            {
                continue;
            }

            if (kind == PropertyKind.StringHandle)
            {
                stringFieldMask[outCount] = 1;
            }

            outCount++;
        }

        return true;
    }
}
