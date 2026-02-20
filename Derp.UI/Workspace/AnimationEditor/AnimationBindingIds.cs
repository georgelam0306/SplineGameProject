namespace Derp.UI;

internal static class AnimationBindingIds
{
    // AnimationDocument.AnimationBinding.PropertyId is used for serialized property IDs.
    // Prefab variables are dynamic (user-defined), so we encode them into PropertyId using a reserved tag bit.
    private const ulong PrefabVariableTag = 1UL << 63;

    public static ulong MakePrefabVariablePropertyId(ushort variableId)
    {
        return PrefabVariableTag | variableId;
    }

    public static bool TryGetPrefabVariableId(ulong propertyId, out ushort variableId)
    {
        if ((propertyId & PrefabVariableTag) == 0)
        {
            variableId = 0;
            return false;
        }

        variableId = (ushort)(propertyId & 0xFFFF);
        return variableId != 0;
    }
}

