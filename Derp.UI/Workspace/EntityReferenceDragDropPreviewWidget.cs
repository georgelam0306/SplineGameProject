using System;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using Property.Runtime;

namespace Derp.UI;

internal static class EntityReferenceDragDropPreviewWidget
{
    public static void Draw(UiWorkspace workspace)
    {
        if (!EntityReferenceDragDrop.IsDragging)
        {
            return;
        }

        uint stableId = EntityReferenceDragDrop.StableId;
        if (stableId == 0)
        {
            return;
        }

        ReadOnlySpan<char> name = GetDragLabel(workspace, stableId);

        float fontSize = Im.Style.FontSize;
        float paddingX = 10f;
        float paddingY = 7f;

        float textWidth = name.Length * (fontSize * 0.55f);
        float rectW = textWidth + paddingX * 2f;
        float rectH = fontSize + paddingY * 2f;

        var mouse = Im.MousePos;
        float x = mouse.X + 14f;
        float y = mouse.Y + 18f;

        uint fill = ImStyle.WithAlphaF(Im.Style.Surface, 0.92f);
        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.85f);
        uint text = Im.Style.TextPrimary;

        Im.DrawRoundedRect(x, y, rectW, rectH, Im.Style.CornerRadius, fill);
        Im.DrawRoundedRectStroke(x, y, rectW, rectH, Im.Style.CornerRadius, stroke, Im.Style.BorderWidth);
        Im.Text(name, x + paddingX, y + paddingY, fontSize, text);
    }

    private static ReadOnlySpan<char> GetDragLabel(UiWorkspace workspace, uint stableId)
    {
        if (workspace.TryGetLayerName(stableId, out string name) && !string.IsNullOrEmpty(name))
        {
            return name.AsSpan();
        }

        EntityId entity = workspace.World.GetEntityByStableId(stableId);
        if (entity.IsNull)
        {
            return "Node".AsSpan();
        }

        UiNodeType type = workspace.World.GetNodeType(entity);
        if (type == UiNodeType.Shape &&
            workspace.World.TryGetComponent(entity, ShapeComponent.Api.PoolIdConst, out AnyComponentHandle shapeAny) &&
            shapeAny.IsValid)
        {
            var handle = new ShapeComponentHandle(shapeAny.Index, shapeAny.Generation);
            var view = ShapeComponent.Api.FromHandle(workspace.PropertyWorld, handle);
            if (view.IsAlive)
            {
                return (view.Kind switch
                {
                    ShapeKind.Rect => "Rectangle",
                    ShapeKind.Circle => "Ellipse",
                    ShapeKind.Polygon => "Polygon",
                    _ => "Shape"
                }).AsSpan();
            }
        }

        return (type switch
        {
            UiNodeType.Prefab => "Prefab",
            UiNodeType.PrefabInstance => "Prefab Instance",
            UiNodeType.BooleanGroup => "Group",
            UiNodeType.Text => "Text",
            _ => "Node"
        }).AsSpan();
    }
}
