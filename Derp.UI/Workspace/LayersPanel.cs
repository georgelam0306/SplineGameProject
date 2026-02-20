using System;
using System.Collections.Generic;
using System.Numerics;
using System.Buffers;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using FontAwesome.Sharp;
using Pooled.Runtime;
using Property.Runtime;

namespace Derp.UI;

internal static class LayersPanel
{
    private const float DragThreshold = 5f;
    private const int RenameMaxChars = 64;
    private const float LayerIconSize = 18f;
    private const float LayerIconPad = 6f;
    private const int MaxDragSelection = 64;

    private enum DropZone { None, Above, Below, Into }

    private struct LayerRowInfo
    {
        public ImRect Rect;
        public EntityId Entity;
        public EntityId ParentEntity;
        public int PrefabId;
        public int IndexInParent;
    }

    private struct LayersDragState
    {
        public EntityId DraggingEntity;     // Node being dragged
        public int SourceFrameId;           // Original prefab
        public EntityId SourceParentEntity; // Original parent (prefab entity for direct children)
        public int SourceIndex;             // Index in parent's children

        public Vector2 DragStartPos;
        public bool ThresholdMet;           // Mouse moved > threshold

        public int TargetFrameId;           // Drop target prefab
        public EntityId TargetParentEntity; // Drop target parent (prefab entity for direct children)
        public EntityId TargetEntity;       // The node we're dropping on/near
        public int TargetInsertIndex;       // Insert position (-1 = append)
        public DropZone Zone;               // Above, Below, Into
    }

    private static readonly List<LayerRowInfo> _rowInfos = new(capacity: 256);
    private static LayersDragState _dragState;

    private static readonly EntityId[] _dragEntities = new EntityId[MaxDragSelection];
    private static readonly int[] _dragEntitySourceIndices = new int[MaxDragSelection];
    private static int _dragEntityCount;

    private static EntityId _contextMenuEntity;
    private const string LayerContextMenuId = "layers_context_menu";

    private static uint _renameStableId;
    private static readonly char[] _renameBuffer = new char[RenameMaxChars];
    private static int _renameLength;
    private static bool _renameNeedsFocus;
    private static int _renameStartFrame;

    private static readonly string TreeExpandedIcon = ((char)IconChar.ChevronDown).ToString();
    private static readonly string TreeCollapsedIcon = ((char)IconChar.ChevronRight).ToString();

    private static readonly string TypePrefabIcon = ((char)IconChar.LayerGroup).ToString();
    private static readonly string TypeRectIcon = ((char)IconChar.Square).ToString();
    private static readonly string TypeCircleIcon = ((char)IconChar.Circle).ToString();
    private static readonly string TypePolygonIcon = ((char)IconChar.VectorSquare).ToString();
    private static readonly string TypeGroupIcon = ((char)IconChar.ObjectGroup).ToString();
    private static readonly string TypeBooleanGroupIcon = ((char)IconChar.CircleNodes).ToString();
    private static readonly string TypeMaskGroupIcon = ((char)IconChar.Mask).ToString();
    private static readonly string TypeTextIcon = ((char)IconChar.Font).ToString();
    private static readonly string TypePrefabInstanceIcon = ((char)IconChar.Clone).ToString();

    private static readonly string EyeOpenIcon = ((char)IconChar.Eye).ToString();
    private static readonly string EyeClosedIcon = ((char)IconChar.EyeSlash).ToString();
    private static readonly string LockClosedIcon = ((char)IconChar.Lock).ToString();
    private static readonly string LockOpenIcon = ((char)IconChar.LockOpen).ToString();
    private static readonly string ReferenceDragIcon = ((char)IconChar.Link).ToString();

    private static EntityId _pendingClickEntity;
    private static bool _pendingClickIsPrefabRow;
    private static bool _pendingClickIsListGenerated;
    private static bool _pendingClickShift;
    private static bool _pendingClickDoubleClick;
    private static Vector2 _pendingClickMousePos;

    public static void DrawLayersPanel(UiWorkspace workspace)
    {
        Im.LabelText("Hierarchy");
        ImLayout.Space(6f);

        ReadOnlySpan<EntityId> rootChildren = workspace.World.GetChildren(EntityId.Null);
        if (rootChildren.Length == 0)
        {
            Im.LabelText("(none)");
            return;
        }

        var input = Im.Context.Input;
        EntityReferenceDragDrop.Update(input, Im.Context.FrameCount);
        bool consumedClick = false;
        float rowHeight = ImTree.RowHeight;

        _rowInfos.Clear();
        if (input.MousePressed)
        {
            ClearPendingSelection();
        }

        for (int i = 0; i < rootChildren.Length; i++)
        {
            EntityId prefabEntity = rootChildren[i];
            if (workspace.World.GetNodeType(prefabEntity) != UiNodeType.Prefab)
            {
                continue;
            }

            int frameId = 0;
            workspace.TryGetLegacyPrefabId(prefabEntity, out frameId);
            if (frameId <= 0)
            {
                frameId = (int)workspace.World.GetStableId(prefabEntity);
            }

            bool prefabSelected = workspace._selectedPrefabEntity.Value == prefabEntity.Value && workspace._selectedEntities.Count == 0;

            ReadOnlySpan<EntityId> prefabChildren = workspace.World.GetChildren(prefabEntity);
            bool hasChildren = prefabChildren.Length > 0;
            uint prefabStableId = workspace.World.GetStableId(prefabEntity);
            bool expanded = hasChildren && workspace.GetLayerExpanded(prefabStableId);

            bool prefabHidden = workspace.GetLayerHidden(prefabStableId);
            bool prefabLocked = workspace.GetLayerLocked(prefabStableId);

            var prefabRow = AllocateLayerRow(rowHeight);
            bool togglesClicked = HandleLayerToggleInput(workspace, prefabEntity, prefabRow, ref prefabHidden, ref prefabLocked, ref consumedClick);
            bool prefabRowClicked = DrawLayerTreeRowBase(
                prefabRow,
                depth: 0,
                hasChildren,
                ref expanded,
                prefabSelected,
                ref consumedClick,
                TypePrefabIcon,
                out float textX,
                out float textY,
                out uint textColor);

            _rowInfos.Add(new LayerRowInfo
            {
                Rect = prefabRow,
                Entity = prefabEntity,
                ParentEntity = EntityId.Null,
                PrefabId = frameId,
                IndexInParent = -1,
            });

            DrawLayerToggleIcons(prefabRow, prefabHidden, prefabLocked);
            TryOpenLayerContextMenu(workspace, prefabEntity, prefabRow, isPrefabRow: true, togglesClicked, input, ref consumedClick);

            string prefabLabel = $"Prefab {frameId}";
            if (workspace.TryGetLayerName(prefabStableId, out string prefabName))
            {
                prefabLabel = prefabName;
            }

            uint effectiveTextColor = textColor;
            if (prefabHidden)
            {
                effectiveTextColor = ImStyle.WithAlphaF(effectiveTextColor, 0.45f);
            }
            else if (prefabLocked)
            {
                effectiveTextColor = ImStyle.WithAlphaF(effectiveTextColor, 0.65f);
            }

            if (_renameStableId == prefabStableId && prefabStableId != 0)
            {
                DrawInlineRenameInput(prefabStableId, prefabRow, textX, input, effectiveTextColor);
            }
            else
            {
                Im.Text(prefabLabel, textX, textY, Im.Style.FontSize, effectiveTextColor);
            }

            if (prefabRowClicked && _dragState.DraggingEntity.IsNull && !togglesClicked)
            {
                BeginPendingSelection(prefabEntity, isPrefabRow: true, isListGenerated: false, input);
            }

            if (hasChildren)
            {
                workspace.SetLayerExpanded(prefabStableId, expanded);
            }

            if (!expanded)
            {
                continue;
            }

            int childDepth = 1;
            for (int childIndex = 0; childIndex < prefabChildren.Length; childIndex++)
            {
                DrawEntityLayer(workspace, frameId, prefabEntity, prefabChildren[childIndex], childDepth, input, ref consumedClick, indexInParent: childIndex);
            }
        }

        DrawLayerContextMenus(workspace);

        HandleDragDrop(workspace);
        CommitPendingSelection(workspace, input);

        if (!_dragState.DraggingEntity.IsNull && _dragState.ThresholdMet && _dragState.Zone != DropZone.None)
        {
            DrawDropPreview();
        }

        bool deletePressed = input.KeyDelete || input.KeyBackspace;
        if (_renameStableId == 0 && _dragState.DraggingEntity.IsNull && deletePressed && Im.Context.FocusId == 0 && !Im.Context.AnyActive && !Im.Context.WantCaptureKeyboard && !IsBlockingGlobalShortcuts())
        {
            workspace.Commands.DeleteSelectedLayers();
            consumedClick = true;
        }

        HandleRenameCommitOrCancel(workspace, input);
    }

    private static bool IsBlockingGlobalShortcuts()
    {
        if (DocumentIoDialog.IsBlockingGlobalShortcuts())
        {
            return true;
        }

        if (ConstraintTargetPickerDialog.IsBlockingGlobalShortcuts())
        {
            return true;
        }

        var focused = Im.WindowManager.FocusedWindow;
        if (focused != null && focused.Title == "Animations")
        {
            return true;
        }

        var animationsWindow = Im.WindowManager.FindWindow("Animations");
        if (animationsWindow == null || !animationsWindow.IsOpen)
        {
            return false;
        }

        var viewport = Im.Context.CurrentViewport;
        Vector2 screenMouse = viewport == null ? Im.Context.Input.MousePos : viewport.ScreenPosition + Im.Context.Input.MousePos;
        return animationsWindow.Rect.Contains(screenMouse);
    }

    private static void DrawEntityLayer(
        UiWorkspace workspace,
        int frameId,
        EntityId parentEntity,
        EntityId entity,
        int depth,
        DerpLib.ImGui.Input.ImInput input,
        ref bool consumedClick,
        int indexInParent)
    {
        UiNodeType nodeType = workspace.World.GetNodeType(entity);
        bool isSelected = workspace.IsEntitySelected(entity);

        bool hasChildren = nodeType != UiNodeType.PrefabInstance && workspace.World.GetChildCount(entity) > 0;
        bool expanded = hasChildren && workspace.GetLayerExpanded(workspace.World.GetStableId(entity));

        var row = AllocateLayerRow(ImTree.RowHeight);
        uint stableId = workspace.World.GetStableId(entity);
        bool isHidden = workspace.GetLayerHidden(stableId);
        bool isLocked = workspace.GetLayerLocked(stableId);
        bool isListGenerated = workspace.World.HasComponent(entity, ListGeneratedComponent.Api.PoolIdConst);
        if (isListGenerated)
        {
            isLocked = true;
        }

        bool togglesClicked = HandleLayerToggleInput(workspace, entity, row, ref isHidden, ref isLocked, ref consumedClick);

        string typeIcon = GetHierarchyTypeIcon(workspace, entity, nodeType);
        bool rowClicked = DrawLayerTreeRowBase(
            row,
            depth,
            hasChildren,
            ref expanded,
            isSelected,
            ref consumedClick,
            typeIcon,
            out float textX,
            out float textY,
            out uint textColor);

        _rowInfos.Add(new LayerRowInfo
        {
            Rect = row,
            Entity = entity,
            ParentEntity = parentEntity,
            PrefabId = frameId,
            IndexInParent = indexInParent,
        });

        DrawLayerToggleIcons(row, isHidden, isLocked);
        if (!isListGenerated)
        {
            TryOpenLayerContextMenu(workspace, entity, row, isPrefabRow: false, togglesClicked, input, ref consumedClick);
        }

        string label = GetEntityLabel(workspace, entity, nodeType);
        if (workspace.TryGetLayerName(stableId, out string name))
        {
            label = name;
        }

        uint effectiveTextColor = textColor;
        if (isHidden)
        {
            effectiveTextColor = ImStyle.WithAlphaF(effectiveTextColor, 0.45f);
        }
        else if (isLocked)
        {
            effectiveTextColor = ImStyle.WithAlphaF(effectiveTextColor, 0.65f);
        }

        if (!isListGenerated && _renameStableId == stableId && stableId != 0)
        {
            DrawInlineRenameInput(stableId, row, textX, input, effectiveTextColor);
        }
        else
        {
            if (isListGenerated &&
                workspace.World.TryGetComponent(entity, ListGeneratedComponent.Api.PoolIdConst, out AnyComponentHandle generatedAny) &&
                generatedAny.IsValid)
            {
                var generatedHandle = new ListGeneratedComponentHandle(generatedAny.Index, generatedAny.Generation);
                var generated = ListGeneratedComponent.Api.FromHandle(workspace.PropertyWorld, generatedHandle);
                if (generated.IsAlive)
                {
                    DrawListGeneratedItemLabel(generated.ItemIndex, textX, textY, effectiveTextColor);
                }
                else
                {
                    Im.Text(label, textX, textY, Im.Style.FontSize, effectiveTextColor);
                }
            }
            else
            {
                Im.Text(label, textX, textY, Im.Style.FontSize, effectiveTextColor);
            }
        }

        if (rowClicked && !togglesClicked && _dragState.DraggingEntity.IsNull)
        {
            BeginPendingSelection(entity, isPrefabRow: false, isListGenerated, input);
        }

        if (hasChildren)
        {
            workspace.SetLayerExpanded(workspace.World.GetStableId(entity), expanded);
        }

        if (!expanded)
        {
            return;
        }

        ReadOnlySpan<EntityId> children = workspace.World.GetChildren(entity);
        if (children.Length == 0)
        {
            return;
        }

        int childDepth = depth + 1;
        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            DrawEntityLayer(workspace, frameId, entity, children[childIndex], childDepth, input, ref consumedClick, indexInParent: childIndex);
        }
    }

    private static string GetEntityLabel(UiWorkspace workspace, EntityId entity, UiNodeType nodeType)
    {
        switch (nodeType)
        {
            case UiNodeType.Shape:
            {
                ShapeKind kind = GetShapeKind(workspace, entity);
                switch (kind)
                {
                    case ShapeKind.Rect:
                        return "Rectangle";
                    case ShapeKind.Circle:
                        return "Ellipse";
                    default:
                        return "Polygon";
                }
            }
            case UiNodeType.BooleanGroup:
            {
                if (workspace.World.TryGetComponent(entity, MaskGroupComponent.Api.PoolIdConst, out _))
                {
                    return "Mask Group";
                }
                if (workspace.World.TryGetComponent(entity, BooleanGroupComponent.Api.PoolIdConst, out _))
                {
                    return "Boolean Group";
                }
                return "Group";
            }
            case UiNodeType.Text:
            {
                return "Text";
            }
            case UiNodeType.PrefabInstance:
            {
                return "Prefab Instance";
            }
            default:
                return "Node";
        }
    }

    private static string GetHierarchyTypeIcon(UiWorkspace workspace, EntityId entity, UiNodeType nodeType)
    {
        switch (nodeType)
        {
            case UiNodeType.Prefab:
                return TypePrefabIcon;
            case UiNodeType.Shape:
            {
                ShapeKind kind = GetShapeKind(workspace, entity);
                return kind switch
                {
                    ShapeKind.Circle => TypeCircleIcon,
                    ShapeKind.Rect => TypeRectIcon,
                    _ => TypePolygonIcon,
                };
            }
            case UiNodeType.BooleanGroup:
            {
                if (workspace.World.TryGetComponent(entity, MaskGroupComponent.Api.PoolIdConst, out _))
                {
                    return TypeMaskGroupIcon;
                }
                if (workspace.World.TryGetComponent(entity, BooleanGroupComponent.Api.PoolIdConst, out _))
                {
                    return TypeBooleanGroupIcon;
                }
                return TypeGroupIcon;
            }
            case UiNodeType.Text:
                return TypeTextIcon;
            case UiNodeType.PrefabInstance:
                return TypePrefabInstanceIcon;
            default:
                return TypeGroupIcon;
        }
    }

    private static bool HandleLayerToggleInput(
        UiWorkspace workspace,
        EntityId entity,
        ImRect rowRect,
        ref bool isHidden,
        ref bool isLocked,
        ref bool consumedClick)
    {
        if (_dragState.DraggingEntity.IsNull == false)
        {
            return false;
        }

        if (workspace.World.HasComponent(entity, ListGeneratedComponent.Api.PoolIdConst))
        {
            return false;
        }

        float cy = rowRect.Y + (rowRect.Height - LayerIconSize) * 0.5f;
        float lockX = rowRect.Right - LayerIconPad - LayerIconSize;
        float eyeX = lockX - LayerIconPad - LayerIconSize;
        float refX = eyeX - LayerIconPad - LayerIconSize;

        var eyeRect = new ImRect(eyeX, cy, LayerIconSize, LayerIconSize);
        var lockRect = new ImRect(lockX, cy, LayerIconSize, LayerIconSize);
        var refRect = new ImRect(refX, cy, LayerIconSize, LayerIconSize);

        bool hoveredEye = eyeRect.Contains(Im.MousePos);
        bool hoveredLock = lockRect.Contains(Im.MousePos);
        bool hoveredRef = refRect.Contains(Im.MousePos);

        if (!Im.MousePressed || consumedClick)
        {
            return false;
        }

        if (hoveredRef)
        {
            consumedClick = true;
            uint stableId = workspace.World.GetStableId(entity);
            if (stableId != 0)
            {
                EntityReferenceDragDrop.BeginEntityDrag(stableId, Im.MousePos);
            }
            return true;
        }

        if (hoveredEye)
        {
            consumedClick = true;
            uint stableId = workspace.World.GetStableId(entity);
            bool nextHidden = !isHidden;
            workspace.SetLayerHidden(stableId, hidden: nextHidden);
            isHidden = nextHidden;
            if (nextHidden)
            {
                ClearSelectionIfDescendantSelected(workspace, entity);
            }
            return true;
        }

        if (hoveredLock)
        {
            consumedClick = true;
            uint stableId = workspace.World.GetStableId(entity);
            bool nextLocked = !isLocked;
            workspace.SetLayerLocked(stableId, locked: nextLocked);
            isLocked = nextLocked;
            return true;
        }

        return false;
    }

    private static void DrawLayerToggleIcons(ImRect rowRect, bool isHidden, bool isLocked)
    {
        float cy = rowRect.Y + (rowRect.Height - LayerIconSize) * 0.5f;
        float lockX = rowRect.Right - LayerIconPad - LayerIconSize;
        float eyeX = lockX - LayerIconPad - LayerIconSize;
        float refX = eyeX - LayerIconPad - LayerIconSize;

        var eyeRect = new ImRect(eyeX, cy, LayerIconSize, LayerIconSize);
        var lockRect = new ImRect(lockX, cy, LayerIconSize, LayerIconSize);
        var refRect = new ImRect(refX, cy, LayerIconSize, LayerIconSize);

        bool hoveredEye = eyeRect.Contains(Im.MousePos);
        bool hoveredLock = lockRect.Contains(Im.MousePos);
        bool hoveredRef = refRect.Contains(Im.MousePos);

        uint iconColor = Im.Style.TextSecondary;
        if (hoveredEye || hoveredLock || hoveredRef)
        {
            iconColor = Im.Style.TextPrimary;
        }

        DrawCenteredIcon(refRect, ReferenceDragIcon, iconColor);
        DrawEyeIcon(eyeRect, open: !isHidden, iconColor);
        DrawLockIcon(lockRect, closed: isLocked, iconColor);
    }

    private static void ClearSelectionIfDescendantSelected(UiWorkspace workspace, EntityId ancestor)
    {
        for (int i = 0; i < workspace._selectedEntities.Count; i++)
        {
            EntityId selected = workspace._selectedEntities[i];
            EntityId current = selected;
            int guard = 512;
            while (!current.IsNull && guard-- > 0)
            {
                if (current.Value == ancestor.Value)
                {
                    workspace.ClearSelection();
                    return;
                }
                current = workspace.World.GetParent(current);
            }
        }
    }

    private static void BeginInlineRename(uint stableId, string currentLabel)
    {
        if (stableId == 0)
        {
            return;
        }

        _renameStableId = stableId;
        _renameStartFrame = Im.Context.FrameCount;
        _renameLength = 0;
        if (!string.IsNullOrEmpty(currentLabel))
        {
            int copyLen = Math.Min(currentLabel.Length, RenameMaxChars - 1);
            currentLabel.AsSpan(0, copyLen).CopyTo(_renameBuffer);
            _renameLength = copyLen;
        }
        _renameNeedsFocus = true;
    }

    private static void DrawInlineRenameInput(uint stableId, ImRect rowRect, float textX, DerpLib.ImGui.Input.ImInput input, uint textColor)
    {
        float rightPad = LayerIconPad + (LayerIconSize + LayerIconPad) * 3f;
        float maxWidth = MathF.Max(20f, rowRect.Right - rightPad - textX);

        var ctx = Im.Context;
        ctx.PushId(unchecked((int)stableId));
        int widgetId = ctx.GetId("layer_rename");

        if (_renameNeedsFocus)
        {
            ImTextArea.ClearState(widgetId);
            ctx.RequestFocus(widgetId);
            ctx.SetActive(widgetId);
            ctx.ResetCaretBlink();
            _renameNeedsFocus = false;
        }

        bool focused = ctx.IsFocused(widgetId);
        uint bg = ImStyle.WithAlpha(Im.Style.Surface, 220);
        uint border = focused ? ImStyle.WithAlpha(Im.Style.Primary, 200) : ImStyle.WithAlpha(Im.Style.Border, 160);
        Im.DrawRoundedRect(textX - 2f, rowRect.Y + 2f, maxWidth + 4f, rowRect.Height - 4f, 4f, bg);
        Im.DrawRoundedRectStroke(textX - 2f, rowRect.Y + 2f, maxWidth + 4f, rowRect.Height - 4f, 4f, border, 1.25f);

        ref var style = ref Im.Style;
        float savedPadding = style.Padding;
        style.Padding = 0f;

        bool changed = ImTextArea.DrawAt(
            "layer_rename",
            _renameBuffer,
            ref _renameLength,
            RenameMaxChars,
            textX,
            rowRect.Y,
            maxWidth,
            rowRect.Height,
            wordWrap: false,
            fontSizePx: style.FontSize,
            flags: ImTextArea.ImTextAreaFlags.NoBackground | ImTextArea.ImTextAreaFlags.NoBorder | ImTextArea.ImTextAreaFlags.NoRounding | ImTextArea.ImTextAreaFlags.SingleLine,
            lineHeightPx: style.FontSize,
            letterSpacingPx: 0f,
            alignX: 0,
            alignY: 1,
            textColor: textColor);

        style.Padding = savedPadding;

        ctx.PopId();

        _ = changed;
        _ = input;
    }

    private static void HandleRenameCommitOrCancel(UiWorkspace workspace, DerpLib.ImGui.Input.ImInput input)
    {
        if (_renameStableId == 0)
        {
            return;
        }

        var ctx = Im.Context;
        ctx.PushId(unchecked((int)_renameStableId));
        int widgetId = ctx.GetId("layer_rename");
        bool focused = ctx.IsFocused(widgetId);
        ctx.PopId();

        if (input.KeyEscape)
        {
            _renameStableId = 0;
            _renameLength = 0;
            _renameNeedsFocus = false;
            _renameStartFrame = 0;
            return;
        }

        bool canCommitOutsideClick = Im.Context.FrameCount != _renameStartFrame;
        bool commit = input.KeyEnter || (canCommitOutsideClick && !focused && Im.MousePressed);
        if (!commit)
        {
            return;
        }

        string name = _renameLength <= 0 ? string.Empty : new string(_renameBuffer, 0, _renameLength);
        workspace.SetLayerName(_renameStableId, name);
        _renameStableId = 0;
        _renameLength = 0;
        _renameNeedsFocus = false;
        _renameStartFrame = 0;
    }

    private static void BeginPendingSelection(EntityId entity, bool isPrefabRow, bool isListGenerated, DerpLib.ImGui.Input.ImInput input)
    {
        if (entity.IsNull)
        {
            return;
        }

        _pendingClickEntity = entity;
        _pendingClickIsPrefabRow = isPrefabRow;
        _pendingClickIsListGenerated = isListGenerated;
        _pendingClickShift = input.KeyShift;
        _pendingClickDoubleClick = input.IsDoubleClick;
        _pendingClickMousePos = Im.MousePos;
    }

    private static void ClearPendingSelection()
    {
        _pendingClickEntity = EntityId.Null;
        _pendingClickIsPrefabRow = false;
        _pendingClickIsListGenerated = false;
        _pendingClickShift = false;
        _pendingClickDoubleClick = false;
        _pendingClickMousePos = default;
    }

    private static void CommitPendingSelection(UiWorkspace workspace, DerpLib.ImGui.Input.ImInput input)
    {
        if (_pendingClickEntity.IsNull || !input.MouseReleased)
        {
            return;
        }

        Vector2 delta = Im.MousePos - _pendingClickMousePos;
        if (delta.LengthSquared() > 8f * 8f)
        {
            ClearPendingSelection();
            return;
        }

        bool releasedOverRow = false;
        for (int i = 0; i < _rowInfos.Count; i++)
        {
            ref var row = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_rowInfos)[i];
            if (row.Entity.Value != _pendingClickEntity.Value)
            {
                continue;
            }

            if (row.Rect.Contains(Im.MousePos))
            {
                releasedOverRow = true;
            }
            break;
        }

        if (!releasedOverRow)
        {
            ClearPendingSelection();
            return;
        }

        uint stableId = workspace.World.GetStableId(_pendingClickEntity);
        if (_pendingClickIsPrefabRow)
        {
            string prefabLabel = stableId == 0 ? "Prefab" : $"Prefab {stableId}";
            if (stableId != 0 && workspace.TryGetLayerName(stableId, out string prefabName) && !string.IsNullOrEmpty(prefabName))
            {
                prefabLabel = prefabName;
            }

            if (_pendingClickDoubleClick)
            {
                BeginInlineRename(stableId, prefabLabel);
                ClearPendingSelection();
                return;
            }

            workspace.SelectPrefabEntity(_pendingClickEntity);
            if (workspace.TryGetLegacyPrefabIndex(_pendingClickEntity, out int prefabIndex))
            {
                workspace._selectedPrefabIndex = prefabIndex;
            }

            ClearPendingSelection();
            return;
        }

        if (_pendingClickDoubleClick && !_pendingClickIsListGenerated && stableId != 0)
        {
            string label = GetEntityLabel(workspace, _pendingClickEntity, workspace.World.GetNodeType(_pendingClickEntity));
            if (workspace.TryGetLayerName(stableId, out string name) && !string.IsNullOrEmpty(name))
            {
                label = name;
            }

            BeginInlineRename(stableId, label);
            ClearPendingSelection();
            return;
        }

        if (_pendingClickShift)
        {
            workspace.ToggleSelectedEntity(_pendingClickEntity);
            ClearPendingSelection();
            return;
        }

        bool entityAlreadySelected = workspace.IsEntitySelected(_pendingClickEntity);
        bool hasMultiSelection = workspace._selectedEntities.Count > 1;
        if (!entityAlreadySelected || !hasMultiSelection)
        {
            workspace.SelectSingleEntity(_pendingClickEntity);
        }

        ClearPendingSelection();
    }

    private static void DrawEyeIcon(ImRect rect, bool open, uint color)
    {
        DrawCenteredIcon(rect, open ? EyeOpenIcon : EyeClosedIcon, color);
    }

    private static void DrawLockIcon(ImRect rect, bool closed, uint color)
    {
        DrawCenteredIcon(rect, closed ? LockClosedIcon : LockOpenIcon, color);
    }

    private static void DrawCenteredIcon(ImRect rect, string icon, uint color)
    {
        float fontSize = MathF.Min(Im.Style.FontSize, rect.Height);
        float iconWidth = Im.MeasureTextWidth(icon.AsSpan(), fontSize);
        float x = rect.X + (rect.Width - iconWidth) * 0.5f;
        float y = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(icon.AsSpan(), x, y, fontSize, color);
    }

    private static ShapeKind GetShapeKind(UiWorkspace workspace, EntityId entity)
    {
        if (!workspace.World.TryGetComponent(entity, ShapeComponent.Api.PoolIdConst, out var component))
        {
            return ShapeKind.Rect;
        }

        var handle = new ShapeComponentHandle(component.Index, component.Generation);
        var view = ShapeComponent.Api.FromHandle(workspace.PropertyWorld, handle);
        return view.Kind;
    }

    private static void HandleDragDrop(UiWorkspace workspace)
    {
        if (Im.MousePressed && _dragState.DraggingEntity.IsNull)
        {
            for (int i = 0; i < _rowInfos.Count; i++)
            {
                ref var row = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_rowInfos)[i];
                if (!row.Rect.Contains(Im.MousePos))
                {
                    continue;
                }

                if (IsMouseOverLayerToggleIcons(row.Rect, Im.MousePos))
                {
                    continue;
                }

                UiNodeType type = workspace.World.GetNodeType(row.Entity);
                if (type == UiNodeType.Prefab || row.Entity.IsNull)
                {
                    continue;
                }
                if (workspace.World.HasComponent(row.Entity, ListGeneratedComponent.Api.PoolIdConst))
                {
                    continue;
                }

                _dragState = new LayersDragState
                {
                    DraggingEntity = row.Entity,
                    SourceFrameId = row.PrefabId,
                    SourceParentEntity = row.ParentEntity,
                    SourceIndex = row.IndexInParent,
                    DragStartPos = Im.MousePos,
                    ThresholdMet = false,
                    Zone = DropZone.None,
                };
                _dragEntityCount = 0;
                if (TryInitializeMultiDragSelection(workspace, row.Entity, row.ParentEntity))
                {
                    // Multi-selection drag initialized.
                }

                // Allow dragging the row into inspector target fields (entity reference drag/drop),
                // while still supporting hierarchy reparenting when dropped on another row.
                if (_dragEntityCount <= 1)
                {
                    uint stableId = workspace.World.GetStableId(row.Entity);
                    if (stableId != 0)
                    {
                        EntityReferenceDragDrop.BeginEntityDrag(stableId, Im.MousePos);
                        EntityReferenceDragDrop.Update(Im.Context.Input, Im.Context.FrameCount);
                    }
                }
                break;
            }
        }

        if (Im.MouseDown && !_dragState.DraggingEntity.IsNull)
        {
            if (!_dragState.ThresholdMet)
            {
                float dist = Vector2.Distance(Im.MousePos, _dragState.DragStartPos);
                if (dist > DragThreshold)
                {
                    _dragState.ThresholdMet = true;
                }
            }

            if (_dragState.ThresholdMet)
            {
                UpdateDropTarget(workspace);
            }
        }

        if (Im.Context.Input.MouseReleased && !_dragState.DraggingEntity.IsNull)
        {
            if (_dragState.ThresholdMet && _dragState.Zone != DropZone.None)
            {
                CommitDrop(workspace);
            }
            _dragState = default;
            _dragEntityCount = 0;
        }
    }

    private static bool IsMouseOverLayerToggleIcons(ImRect rowRect, Vector2 mousePos)
    {
        float cy = rowRect.Y + (rowRect.Height - LayerIconSize) * 0.5f;
        float lockX = rowRect.Right - LayerIconPad - LayerIconSize;
        float eyeX = lockX - LayerIconPad - LayerIconSize;
        float refX = eyeX - LayerIconPad - LayerIconSize;

        var eyeRect = new ImRect(eyeX, cy, LayerIconSize, LayerIconSize);
        var lockRect = new ImRect(lockX, cy, LayerIconSize, LayerIconSize);
        var refRect = new ImRect(refX, cy, LayerIconSize, LayerIconSize);
        return eyeRect.Contains(mousePos) || lockRect.Contains(mousePos) || refRect.Contains(mousePos);
    }

    private static void UpdateDropTarget(UiWorkspace workspace)
    {
        _dragState.Zone = DropZone.None;
        _dragState.TargetFrameId = 0;
        _dragState.TargetParentEntity = EntityId.Null;
        _dragState.TargetEntity = EntityId.Null;
        _dragState.TargetInsertIndex = -1;

        for (int i = 0; i < _rowInfos.Count; i++)
        {
            ref var row = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_rowInfos)[i];
            if (!row.Rect.Contains(Im.MousePos))
            {
                continue;
            }

            if (!row.Entity.IsNull && IsEntityInDragSelection(row.Entity))
            {
                continue;
            }

            if (!row.Entity.IsNull && IsDescendantOfDragSelection(workspace.World, row.Entity))
            {
                continue;
            }

            _dragState.TargetFrameId = row.PrefabId;
            _dragState.TargetEntity = row.Entity;

            float relativeY = (Im.MousePos.Y - row.Rect.Y) / row.Rect.Height;

            UiNodeType targetType = workspace.World.GetNodeType(row.Entity);
            if (relativeY < 0.2f)
            {
                _dragState.Zone = DropZone.Above;
                _dragState.TargetParentEntity = row.ParentEntity;
                _dragState.TargetInsertIndex = row.IndexInParent;
            }
            else if (relativeY > 0.8f)
            {
                _dragState.Zone = DropZone.Below;
                _dragState.TargetParentEntity = row.ParentEntity;
                _dragState.TargetInsertIndex = row.IndexInParent + 1;
            }
            else
            {
                bool canHaveChildren = CanEntityHaveChildren(targetType);
                if (canHaveChildren)
                {
                    _dragState.Zone = DropZone.Into;
                    _dragState.TargetParentEntity = row.Entity;
                    _dragState.TargetInsertIndex = -1;
                }
                else
                {
                    _dragState.Zone = DropZone.Below;
                    _dragState.TargetParentEntity = row.ParentEntity;
                    _dragState.TargetInsertIndex = row.IndexInParent + 1;
                }
            }

            break;
        }
    }

    private static bool CanEntityHaveChildren(UiNodeType nodeType)
    {
        return nodeType != UiNodeType.None;
    }

    private static bool IsDescendantOf(UiWorld world, EntityId entity, EntityId possibleAncestor)
    {
        EntityId current = world.GetParent(entity);
        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            if (current.Value == possibleAncestor.Value)
            {
                return true;
            }

            current = world.GetParent(current);
        }

        return false;
    }

    private static void CommitDrop(UiWorkspace workspace)
    {
        if (_dragEntityCount > 1)
        {
            CommitMultiDrop(workspace);
            return;
        }

        workspace.Commands.TryMoveEntityPreserveWorldTransform(
            _dragState.DraggingEntity,
            _dragState.SourceParentEntity,
            _dragState.SourceIndex,
            _dragState.TargetParentEntity,
            _dragState.TargetInsertIndex);
    }

    private static bool TryInitializeMultiDragSelection(UiWorkspace workspace, EntityId draggingEntity, EntityId sourceParent)
    {
        _dragEntityCount = 0;

        if (draggingEntity.IsNull || sourceParent.IsNull)
        {
            return false;
        }

        List<EntityId> selectedEntities = workspace._selectedEntities;
        int selectionCount = selectedEntities.Count;
        if (selectionCount <= 1 || selectionCount > MaxDragSelection)
        {
            return false;
        }

        bool draggingIsSelected = false;
        for (int i = 0; i < selectionCount; i++)
        {
            if (selectedEntities[i].Value == draggingEntity.Value)
            {
                draggingIsSelected = true;
                break;
            }
        }

        if (!draggingIsSelected)
        {
            return false;
        }

        for (int i = 0; i < selectionCount; i++)
        {
            EntityId entity = selectedEntities[i];
            if (entity.IsNull)
            {
                return false;
            }

            if (workspace.World.GetNodeType(entity) == UiNodeType.Prefab)
            {
                return false;
            }

            EntityId parent = workspace.World.GetParent(entity);
            if (parent.IsNull || parent.Value != sourceParent.Value)
            {
                return false;
            }

            if (!workspace.World.TryGetChildIndex(sourceParent, entity, out int indexInParent))
            {
                return false;
            }

            _dragEntities[_dragEntityCount] = entity;
            _dragEntitySourceIndices[_dragEntityCount] = indexInParent;
            _dragEntityCount++;
        }

        SortDraggedEntitiesByIndex();
        return _dragEntityCount > 1;
    }

    private static void SortDraggedEntitiesByIndex()
    {
        for (int i = 1; i < _dragEntityCount; i++)
        {
            EntityId entity = _dragEntities[i];
            int sourceIndex = _dragEntitySourceIndices[i];

            int j = i - 1;
            while (j >= 0 && _dragEntitySourceIndices[j] > sourceIndex)
            {
                _dragEntities[j + 1] = _dragEntities[j];
                _dragEntitySourceIndices[j + 1] = _dragEntitySourceIndices[j];
                j--;
            }

            _dragEntities[j + 1] = entity;
            _dragEntitySourceIndices[j + 1] = sourceIndex;
        }
    }

    private static bool IsEntityInDragSelection(EntityId entity)
    {
        if (_dragEntityCount <= 1)
        {
            return entity.Value == _dragState.DraggingEntity.Value;
        }

        for (int i = 0; i < _dragEntityCount; i++)
        {
            if (_dragEntities[i].Value == entity.Value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDescendantOfDragSelection(UiWorld world, EntityId entity)
    {
        if (_dragEntityCount <= 1)
        {
            return IsDescendantOf(world, entity, _dragState.DraggingEntity);
        }

        for (int i = 0; i < _dragEntityCount; i++)
        {
            if (IsDescendantOf(world, entity, _dragEntities[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static void CommitMultiDrop(UiWorkspace workspace)
    {
        EntityId sourceParent = _dragState.SourceParentEntity;
        EntityId targetParent = _dragState.TargetParentEntity;
        if (sourceParent.IsNull || targetParent.IsNull)
        {
            return;
        }

        RefreshDraggedEntityIndices(workspace.World, sourceParent);
        SortDraggedEntitiesByIndex();

        if (sourceParent.Value == targetParent.Value)
        {
            CommitMultiDropSameParent(workspace, sourceParent);
        }
        else
        {
            CommitMultiDropCrossParent(workspace, sourceParent, targetParent);
        }
    }

    private static void RefreshDraggedEntityIndices(UiWorld world, EntityId sourceParent)
    {
        for (int i = 0; i < _dragEntityCount; i++)
        {
            if (!world.TryGetChildIndex(sourceParent, _dragEntities[i], out int index))
            {
                _dragEntitySourceIndices[i] = int.MaxValue;
                continue;
            }

            _dragEntitySourceIndices[i] = index;
        }
    }

    private static void CommitMultiDropCrossParent(UiWorkspace workspace, EntityId sourceParent, EntityId targetParent)
    {
        int baseInsertIndex = _dragState.TargetInsertIndex;
        if (baseInsertIndex < 0)
        {
            for (int i = 0; i < _dragEntityCount; i++)
            {
                workspace.Commands.TryMoveEntityPreserveWorldTransform(
                    _dragEntities[i],
                    sourceParent,
                    sourceIndex: -1,
                    targetParent,
                    targetInsertIndex: -1);
            }

            return;
        }

        int insertIndex = baseInsertIndex;
        for (int i = 0; i < _dragEntityCount; i++)
        {
            workspace.Commands.TryMoveEntityPreserveWorldTransform(
                _dragEntities[i],
                sourceParent,
                sourceIndex: -1,
                targetParent,
                targetInsertIndex: insertIndex);
            insertIndex++;
        }
    }

    private static void CommitMultiDropSameParent(UiWorkspace workspace, EntityId parent)
    {
        ReadOnlySpan<EntityId> children = workspace.World.GetChildren(parent);
        int childCount = children.Length;
        if (_dragEntityCount <= 1 || childCount <= 1)
        {
            return;
        }

        int baseInsertIndex = _dragState.TargetInsertIndex;
        if (baseInsertIndex < 0)
        {
            baseInsertIndex = childCount;
        }
        else
        {
            baseInsertIndex = Math.Clamp(baseInsertIndex, 0, childCount);
        }

        int adjustedInsertIndex = baseInsertIndex;
        for (int i = 0; i < _dragEntityCount; i++)
        {
            if (_dragEntitySourceIndices[i] < baseInsertIndex)
            {
                adjustedInsertIndex--;
            }
        }

        int remainingCount = Math.Max(0, childCount - _dragEntityCount);
        adjustedInsertIndex = Math.Clamp(adjustedInsertIndex, 0, remainingCount);

        EntityId[] desired = ArrayPool<EntityId>.Shared.Rent(childCount);
        try
        {
            int desiredCount = 0;
            int nonDraggedIndex = 0;
            bool inserted = false;

            for (int i = 0; i < childCount; i++)
            {
                EntityId child = children[i];
                if (IsEntityInDragSelection(child))
                {
                    continue;
                }

                if (!inserted && nonDraggedIndex == adjustedInsertIndex)
                {
                    for (int d = 0; d < _dragEntityCount; d++)
                    {
                        desired[desiredCount++] = _dragEntities[d];
                    }
                    inserted = true;
                }

                desired[desiredCount++] = child;
                nonDraggedIndex++;
            }

            if (!inserted)
            {
                for (int d = 0; d < _dragEntityCount; d++)
                {
                    desired[desiredCount++] = _dragEntities[d];
                }
            }

            bool anyDiff = false;
            if (desiredCount == childCount)
            {
                for (int i = 0; i < childCount; i++)
                {
                    if (children[i].Value != desired[i].Value)
                    {
                        anyDiff = true;
                        break;
                    }
                }
            }

            if (!anyDiff)
            {
                return;
            }

            for (int i = 0; i < childCount; i++)
            {
                children = workspace.World.GetChildren(parent);
                if ((uint)i >= (uint)children.Length)
                {
                    break;
                }

                if (children[i].Value == desired[i].Value)
                {
                    continue;
                }

                workspace.Commands.TryMoveEntityPreserveWorldTransform(
                    desired[i],
                    parent,
                    sourceIndex: -1,
                    parent,
                    targetInsertIndex: i);
            }
        }
        finally
        {
            ArrayPool<EntityId>.Shared.Return(desired);
        }
    }

    private static void DrawDropPreview()
    {
        for (int i = 0; i < _rowInfos.Count; i++)
        {
            ref var row = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_rowInfos)[i];
            if (row.Entity.Value != _dragState.TargetEntity.Value || row.PrefabId != _dragState.TargetFrameId)
            {
                continue;
            }

            uint color = 0xFF4488FF;
            switch (_dragState.Zone)
            {
                case DropZone.Above:
                    Im.DrawRect(row.Rect.X, row.Rect.Y - 1, row.Rect.Width, 2, color);
                    break;
                case DropZone.Below:
                    Im.DrawRect(row.Rect.X, row.Rect.Y + row.Rect.Height - 1, row.Rect.Width, 2, color);
                    break;
                case DropZone.Into:
                {
                    float x = row.Rect.X + 1;
                    float y = row.Rect.Y + 1;
                    float w = row.Rect.Width - 2;
                    float h = row.Rect.Height - 2;
                    float t = 2f;
                    Im.DrawRect(x, y, w, t, color);
                    Im.DrawRect(x, y + h - t, w, t, color);
                    Im.DrawRect(x, y, t, h, color);
                    Im.DrawRect(x + w - t, y, t, h, color);
                    break;
                }
            }

            break;
        }
    }

    private static ImRect AllocateLayerRow(float height)
    {
        float width = Math.Max(0f, ImLayout.RemainingWidth());
        return ImLayout.AllocateRect(width, height);
    }

    private static bool DrawLayerTreeRowBase(
        ImRect rowRect,
        int depth,
        bool hasChildren,
        ref bool expanded,
        bool selected,
        ref bool consumedClick,
        string typeIcon,
        out float textX,
        out float textY,
        out uint textColor)
    {
        var style = Im.Style;
        bool hovered = rowRect.Contains(Im.MousePos);
        bool isDragging = !_dragState.DraggingEntity.IsNull && _dragState.ThresholdMet;

        float indent = depth * ImTree.IndentWidth;
        float contentX = rowRect.X + indent;

        var iconRect = ImRect.Zero;
        float iconY = rowRect.Y + (rowRect.Height - ImTree.IconSize) * 0.5f;
        float expandIconX = contentX + ImTree.IconPadding;
        float typeIconX = hasChildren ? (expandIconX + ImTree.IconSize + ImTree.IconPadding) : expandIconX;

        if (hasChildren)
        {
            iconRect = new ImRect(expandIconX, iconY, ImTree.IconSize, ImTree.IconSize);
        }

        if (selected)
        {
            Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, style.Active);
        }
        else if (hovered && !isDragging)
        {
            Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, style.Hover);
        }

        if (ImTree.ShowIndentLines && depth > 0)
        {
            for (int i = 0; i < depth; i++)
            {
                float lineX = rowRect.X + i * ImTree.IndentWidth + ImTree.IndentWidth * 0.5f;
                Im.DrawLine(lineX, rowRect.Y, lineX, rowRect.Y + rowRect.Height, 1f, 0x40FFFFFF);
            }
        }

        if (hasChildren)
        {
            DrawCenteredIcon(new ImRect(expandIconX, iconY, ImTree.IconSize, ImTree.IconSize), expanded ? TreeExpandedIcon : TreeCollapsedIcon, Im.Style.TextSecondary);
        }

        DrawCenteredIcon(new ImRect(typeIconX, iconY, ImTree.IconSize, ImTree.IconSize), typeIcon, Im.Style.TextSecondary);

        textColor = selected ? 0xFFFFFFFF : style.TextPrimary;
        textX = typeIconX + ImTree.IconSize + ImTree.IconPadding;
        textY = rowRect.Y + (rowRect.Height - style.FontSize) * 0.5f;

        bool rowClicked = !consumedClick && !isDragging && hovered && Im.MousePressed && (!hasChildren || !iconRect.Contains(Im.MousePos));
        if (rowClicked)
        {
            consumedClick = true;
        }

        if (hasChildren && iconRect.Contains(Im.MousePos) && Im.MousePressed && !isDragging)
        {
            expanded = !expanded;
        }

        return rowClicked;
    }

        // Expanded state is stored on the workspace editor document (stable-id keyed).

    private static void TryOpenLayerContextMenu(
        UiWorkspace workspace,
        EntityId entity,
        ImRect rowRect,
        bool isPrefabRow,
        bool togglesClicked,
        DerpLib.ImGui.Input.ImInput input,
        ref bool consumedClick)
    {
        if (togglesClicked || consumedClick || _dragState.DraggingEntity.IsNull == false)
        {
            return;
        }

        if (!input.MouseRightPressed || !rowRect.Contains(Im.MousePos))
        {
            return;
        }

        if (IsMouseOverLayerToggleIcons(rowRect, Im.MousePos))
        {
            return;
        }

        consumedClick = true;

        if (isPrefabRow)
        {
            workspace.SelectPrefabEntity(entity);
        }
        else if (!workspace.IsEntitySelected(entity))
        {
            workspace.SelectSingleEntity(entity);
        }

        _contextMenuEntity = entity;
        ImContextMenu.Open(LayerContextMenuId);
    }

    private static void DrawListGeneratedItemLabel(ushort itemIndex, float x, float y, uint color)
    {
        Span<char> buffer = stackalloc char[16];
        "Item ".AsSpan().CopyTo(buffer);
        int len = 5;
        len += WriteNonNegativeInt(buffer.Slice(len), itemIndex);
        Im.Text(buffer.Slice(0, len), x, y, Im.Style.FontSize, color);
    }

    private static int WriteNonNegativeInt(Span<char> destination, int value)
    {
        if (value < 0)
        {
            value = 0;
        }

        if (value == 0)
        {
            if (!destination.IsEmpty)
            {
                destination[0] = '0';
                return 1;
            }
            return 0;
        }

        int length = 0;
        int remaining = value;
        while (remaining > 0 && length < destination.Length)
        {
            int digit = remaining % 10;
            destination[length++] = (char)('0' + digit);
            remaining /= 10;
        }

        for (int i = 0; i < length / 2; i++)
        {
            char temp = destination[i];
            destination[i] = destination[length - 1 - i];
            destination[length - 1 - i] = temp;
        }

        return length;
    }

    private static void DrawLayerContextMenus(UiWorkspace workspace)
    {
        if (ImContextMenu.Begin(LayerContextMenuId))
        {
            bool canWrapGroup = workspace.TryGetGroupCreateCandidate(out _);
            bool canWrapBoolean = workspace.TryGetBooleanCreateCandidate(out _, out _, out _);
            bool canWrapMask = workspace.TryGetMaskCreateCandidate(out _, out _, out _);
            bool canWrapAny = canWrapGroup || canWrapBoolean || canWrapMask;

            if (!canWrapAny)
            {
                ImContextMenu.ItemDisabled("Wrap In");
            }
            else if (ImContextMenu.BeginMenu("Wrap In"))
            {
                if (canWrapBoolean)
                {
                    if (ImContextMenu.Item("Boolean"))
                    {
                        workspace.Commands.CreateBooleanGroupFromSelection(workspace._pendingBooleanOpIndex);
                    }
                }
                else
                {
                    ImContextMenu.ItemDisabled("Boolean");
                }

                if (canWrapMask)
                {
                    if (ImContextMenu.Item("Mask"))
                    {
                        workspace.Commands.CreateMaskGroupFromSelection();
                    }
                }
                else
                {
                    ImContextMenu.ItemDisabled("Mask");
                }

                if (canWrapGroup)
                {
                    if (ImContextMenu.Item("Group"))
                    {
                        workspace.Commands.CreateGroupFromSelection();
                    }
                }
                else
                {
                    ImContextMenu.ItemDisabled("Group");
                }

                ImContextMenu.EndMenu();
            }

            ImContextMenu.End();
        }

        _ = _contextMenuEntity;
    }
}
