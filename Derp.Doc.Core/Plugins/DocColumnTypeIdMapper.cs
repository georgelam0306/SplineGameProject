using Derp.Doc.Model;

namespace Derp.Doc.Plugins;

public static class DocColumnTypeIdMapper
{
    public static string FromKind(DocColumnKind kind)
    {
        return kind switch
        {
            DocColumnKind.Id => DocColumnTypeIds.Id,
            DocColumnKind.Text => DocColumnTypeIds.Text,
            DocColumnKind.Number => DocColumnTypeIds.Number,
            DocColumnKind.Checkbox => DocColumnTypeIds.Checkbox,
            DocColumnKind.Select => DocColumnTypeIds.Select,
            DocColumnKind.Formula => DocColumnTypeIds.Formula,
            DocColumnKind.Relation => DocColumnTypeIds.Relation,
            DocColumnKind.TableRef => DocColumnTypeIds.TableRef,
            DocColumnKind.Subtable => DocColumnTypeIds.Subtable,
            DocColumnKind.Spline => DocColumnTypeIds.Spline,
            DocColumnKind.TextureAsset => DocColumnTypeIds.TextureAsset,
            DocColumnKind.MeshAsset => DocColumnTypeIds.MeshAsset,
            DocColumnKind.AudioAsset => DocColumnTypeIds.AudioAsset,
            DocColumnKind.UiAsset => DocColumnTypeIds.UiAsset,
            DocColumnKind.Vec2 => DocColumnTypeIds.Vec2Fixed64,
            DocColumnKind.Vec3 => DocColumnTypeIds.Vec3Fixed64,
            DocColumnKind.Vec4 => DocColumnTypeIds.Vec4Fixed64,
            DocColumnKind.Color => DocColumnTypeIds.ColorLdr,
            _ => DocColumnTypeIds.Text,
        };
    }

    public static bool IsBuiltIn(string? columnTypeId)
    {
        return TryToBuiltInKind(columnTypeId, out _);
    }

    public static bool TryToBuiltInKind(string? columnTypeId, out DocColumnKind kind)
    {
        kind = DocColumnKind.Text;
        string normalized = Normalize(columnTypeId);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (string.Equals(normalized, DocColumnTypeIds.Text, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Text;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.Id, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Id;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.Number, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Number;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.Checkbox, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Checkbox;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.Select, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Select;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.Formula, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Formula;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.Relation, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Relation;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.TableRef, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.TableRef;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.Subtable, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Subtable;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.Spline, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Spline;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.TextureAsset, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.TextureAsset;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.MeshAsset, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.MeshAsset;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.AudioAsset, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.AudioAsset;
            return true;
        }

        if (string.Equals(normalized, DocColumnTypeIds.UiAsset, StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.UiAsset;
            return true;
        }

        if (IsVec2TypeId(normalized))
        {
            kind = DocColumnKind.Vec2;
            return true;
        }

        if (IsVec3TypeId(normalized))
        {
            kind = DocColumnKind.Vec3;
            return true;
        }

        if (IsVec4TypeId(normalized))
        {
            kind = DocColumnKind.Vec4;
            return true;
        }

        if (IsColorTypeId(normalized))
        {
            kind = DocColumnKind.Color;
            return true;
        }

        return false;
    }

    public static string Resolve(string? columnTypeId, DocColumnKind fallbackKind)
    {
        string normalized = Normalize(columnTypeId);
        if (normalized.Length == 0)
        {
            return FromKind(fallbackKind);
        }

        if (TryToBuiltInKind(normalized, out var builtInKind))
        {
            // Keep legacy enum and built-in kind in sync while preserving built-in variants.
            return builtInKind == fallbackKind
                ? normalized
                : FromKind(fallbackKind);
        }

        return normalized;
    }

    public static bool ShouldSyncWithKind(string? columnTypeId)
    {
        string normalized = Normalize(columnTypeId);
        return normalized.Length == 0 || IsBuiltIn(normalized);
    }

    private static string Normalize(string? columnTypeId)
    {
        return string.IsNullOrWhiteSpace(columnTypeId)
            ? string.Empty
            : columnTypeId.Trim();
    }

    public static bool IsVectorKind(DocColumnKind kind)
    {
        return kind == DocColumnKind.Vec2 ||
               kind == DocColumnKind.Vec3 ||
               kind == DocColumnKind.Vec4;
    }

    public static bool IsVectorTypeId(string? columnTypeId)
    {
        string normalized = Normalize(columnTypeId);
        return IsVec2TypeId(normalized) ||
               IsVec3TypeId(normalized) ||
               IsVec4TypeId(normalized);
    }

    public static bool IsFixedVectorTypeId(string? columnTypeId)
    {
        string normalized = Normalize(columnTypeId);
        return string.Equals(normalized, DocColumnTypeIds.Vec2Fixed32, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DocColumnTypeIds.Vec2Fixed64, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DocColumnTypeIds.Vec3Fixed32, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DocColumnTypeIds.Vec3Fixed64, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DocColumnTypeIds.Vec4Fixed32, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DocColumnTypeIds.Vec4Fixed64, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFloat32VectorTypeId(string? columnTypeId)
    {
        string normalized = Normalize(columnTypeId);
        return string.Equals(normalized, DocColumnTypeIds.Vec2Float32, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DocColumnTypeIds.Vec3Float32, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DocColumnTypeIds.Vec4Float32, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFloat64VectorTypeId(string? columnTypeId)
    {
        string normalized = Normalize(columnTypeId);
        return string.Equals(normalized, DocColumnTypeIds.Vec2Float64, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DocColumnTypeIds.Vec3Float64, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DocColumnTypeIds.Vec4Float64, StringComparison.OrdinalIgnoreCase);
    }

    public static int GetVectorDimension(string? columnTypeId, DocColumnKind fallbackKind)
    {
        string normalized = Resolve(columnTypeId, fallbackKind);
        if (IsVec2TypeId(normalized))
        {
            return 2;
        }

        if (IsVec3TypeId(normalized))
        {
            return 3;
        }

        if (IsVec4TypeId(normalized))
        {
            return 4;
        }

        return fallbackKind switch
        {
            DocColumnKind.Vec2 => 2,
            DocColumnKind.Vec3 => 3,
            DocColumnKind.Vec4 => 4,
            _ => 2,
        };
    }

    public static bool IsColorTypeId(string? columnTypeId)
    {
        string normalized = Normalize(columnTypeId);
        return string.Equals(normalized, DocColumnTypeIds.ColorLdr, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DocColumnTypeIds.ColorHdr, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsHdrColorTypeId(string? columnTypeId)
    {
        string normalized = Normalize(columnTypeId);
        return string.Equals(normalized, DocColumnTypeIds.ColorHdr, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVec2TypeId(string normalizedColumnTypeId)
    {
        return string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec2Fixed32, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec2Fixed64, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec2Float32, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec2Float64, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVec3TypeId(string normalizedColumnTypeId)
    {
        return string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec3Fixed32, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec3Fixed64, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec3Float32, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec3Float64, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVec4TypeId(string normalizedColumnTypeId)
    {
        return string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec4Fixed32, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec4Fixed64, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec4Float32, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedColumnTypeId, DocColumnTypeIds.Vec4Float64, StringComparison.OrdinalIgnoreCase);
    }
}
