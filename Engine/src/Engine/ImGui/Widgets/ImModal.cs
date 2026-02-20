using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Rendering;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Modal dialog widget with backdrop dimming and focus trapping.
/// </summary>
public static class ImModal
{
    // Active modal state
    private static string? _activeModalId;
    private static bool _closeRequested;
    private static Vector2 _contentOffset;
    private static int _previousSortKey;
    private static Vector4 _previousClipRect;
    private static ImDrawLayer _previousLayer;
    private static bool _pushedCancelTransform;
    private static bool _pushedOverlayScope;
    private static bool _overlayActive;
    private static int _openFrame;
    private static int _openViewportResourcesId;

    // Very high sort key to render above all windows
    private const int ModalSortKey = int.MaxValue;

    /// <summary>Backdrop dim color (semi-transparent black).</summary>
    public static uint BackdropColor = 0xA0000000;

    /// <summary>If true, clicking backdrop closes the modal.</summary>
    public static bool CloseOnBackdrop = true;

    /// <summary>
    /// Check if any modal is currently open.
    /// </summary>
    public static bool IsAnyOpen => _activeModalId != null;

    /// <summary>
    /// Check if a specific modal is currently open.
    /// </summary>
    public static bool IsOpen(string id) => _activeModalId == id;

    /// <summary>
    /// Open a modal dialog.
    /// </summary>
    public static void Open(string id)
    {
        _activeModalId = id;
        _closeRequested = false;
        _openFrame = Im.Context.FrameCount;
        _openViewportResourcesId = Im.CurrentViewport?.ResourcesId ?? 0;
    }

    /// <summary>
    /// Close the currently open modal.
    /// </summary>
    public static void Close()
    {
        _closeRequested = true;
    }

    /// <summary>
    /// Prime overlay capture before normal UI runs, so background widgets cannot consume input while a modal is open.
    /// </summary>
    public static void PrimeOverlayCapture(ImContext ctx)
    {
        if (_activeModalId == null)
        {
            return;
        }

        var currentViewport = ctx.CurrentViewport;
        if (currentViewport == null)
        {
            return;
        }

        if (_openViewportResourcesId != 0 && currentViewport.ResourcesId != _openViewportResourcesId)
        {
            return;
        }

        ctx.AddOverlayCaptureRect(new ImRect(0f, 0f, currentViewport.Size.X, currentViewport.Size.Y));
    }

    /// <summary>
    /// Begin a modal dialog. Returns true if the modal should render content.
    /// </summary>
    /// <param name="id">Unique identifier for this modal.</param>
    /// <param name="width">Modal width.</param>
    /// <param name="height">Modal height.</param>
    /// <param name="title">Optional title for the modal header.</param>
    public static bool Begin(string id, float width, float height, string? title = null)
    {
        if (_activeModalId != id)
            return false;

        _overlayActive = false;
        _pushedCancelTransform = false;
        _pushedOverlayScope = false;

        // Handle close request from previous frame
        if (_closeRequested)
        {
            _activeModalId = null;
            _closeRequested = false;
            _openViewportResourcesId = 0;
            return false;
        }

        var viewport = Im.CurrentViewport;
        if (viewport == null) return false;
        var viewportSize = viewport.Size;

        // Always render modals in overlay layer above all windows/layouts.
        _previousLayer = viewport.CurrentLayer;
        Im.SetDrawLayer(ImDrawLayer.Overlay);

        // Set high sort key to render above all windows
        var drawList = viewport.GetDrawList(ImDrawLayer.Overlay);
        _previousSortKey = drawList.GetSortKey();
        _previousClipRect = drawList.GetClipRect();
        drawList.SetSortKey(ModalSortKey);
        drawList.ClearClipRect();
        _overlayActive = true;

        if (Im.CurrentTransformMatrix != Matrix3x2.Identity)
        {
            Im.PushInverseTransform();
            _pushedCancelTransform = true;
        }

        // Draw backdrop
        Im.DrawRect(0, 0, viewportSize.X, viewportSize.Y, BackdropColor);

        // Center modal
        float modalX = (viewportSize.X - width) * 0.5f;
        float modalY = (viewportSize.Y - height) * 0.5f;
        var modalRect = new ImRect(modalX, modalY, width, height);
        ImPopover.EnterOverlayScope(new ImRect(0f, 0f, viewportSize.X, viewportSize.Y));
        _pushedOverlayScope = true;

        bool shouldClose = ImPopover.ShouldClose(
            openedFrame: _openFrame,
            closeOnEscape: true,
            closeOnOutsideButtons: CloseOnBackdrop ? ImPopoverCloseButtons.Left : ImPopoverCloseButtons.None,
            consumeCloseClick: false,
            requireNoMouseOwner: false,
            useViewportMouseCoordinates: true,
            insideRect: modalRect);
        if (shouldClose)
        {
            Close();
            CleanupOverlay(viewport);
            return false;
        }

        // Draw modal background with shadow
        Im.DrawRoundedRect(modalX + 4, modalY + 4, width, height, Im.Style.CornerRadius, Im.Style.ShadowColor);
        Im.DrawRoundedRect(modalX, modalY, width, height, Im.Style.CornerRadius, Im.Style.Background);
        Im.DrawRoundedRectStroke(modalX, modalY, width, height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);

        // Draw title bar if present
        float contentY = modalY + Im.Style.Padding;
        if (!string.IsNullOrEmpty(title))
        {
            float titleBarHeight = Im.Style.TitleBarHeight;
            // Draw title bar with top-only rounding
            Im.DrawRoundedRectPerCorner(modalX, modalY, width, titleBarHeight,
                Im.Style.CornerRadius, Im.Style.CornerRadius, 0, 0, Im.Style.TitleBar);

            // Draw title text centered
            float titleWidth = MeasureTextWidth(title);
            float titleX = modalX + (width - titleWidth) / 2;
            float titleY = modalY + (titleBarHeight - Im.Style.FontSize) / 2;
            Im.Text(title.AsSpan(), titleX, titleY, Im.Style.FontSize, Im.Style.TextPrimary);

            // Separator line
            Im.DrawLine(modalX, modalY + titleBarHeight, modalX + width, modalY + titleBarHeight, 1, Im.Style.Border);

            contentY = modalY + titleBarHeight + Im.Style.Padding;
        }

        _contentOffset = new Vector2(modalX + Im.Style.Padding, contentY);

        // Set up layout for modal content
        float contentWidth = width - Im.Style.Padding * 2;
        float contentHeight = height - (contentY - modalY) - Im.Style.Padding;
        ImLayout.BeginVertical(new ImRect(_contentOffset.X, _contentOffset.Y, contentWidth, contentHeight),
            Im.Style.Padding, Im.Style.Spacing);

        return true;
    }

    /// <summary>
    /// Get the content offset for modal-relative drawing.
    /// </summary>
    public static Vector2 ContentOffset => _contentOffset;

    /// <summary>
    /// End the modal dialog.
    /// </summary>
    public static void End()
    {
        // End the content layout
        ImLayout.End();

        var viewport = Im.CurrentViewport;
        CleanupOverlay(viewport);

        // Process close request at end of frame
        if (_closeRequested)
        {
            _activeModalId = null;
            _closeRequested = false;
            _openViewportResourcesId = 0;
        }
    }

    /// <summary>
    /// Begin a simple alert modal with OK button.
    /// Returns true while the modal is open.
    /// </summary>
    public static bool Alert(string id, string message, string buttonText = "OK")
    {
        if (_activeModalId != id)
            return false;

        float textWidth = MeasureTextWidth(message);
        float width = Math.Max(textWidth + Im.Style.Padding * 4, 200);
        float height = Im.Style.FontSize + Im.Style.MinButtonHeight + Im.Style.Padding * 4 + Im.Style.Spacing;

        if (!Begin(id, width, height, "Alert"))
            return false;

        // Draw message
        Im.LabelText(message, _contentOffset.X, _contentOffset.Y);

        // Draw OK button (centered)
        float buttonWidth = Math.Max(Im.Style.MinButtonWidth, MeasureTextWidth(buttonText) + Im.Style.Padding * 2);
        float buttonX = _contentOffset.X + (width - Im.Style.Padding * 2 - buttonWidth) * 0.5f;
        float buttonY = _contentOffset.Y + Im.Style.FontSize + Im.Style.Spacing;

        if (Im.Button(buttonText, buttonX, buttonY, buttonWidth, Im.Style.MinButtonHeight))
        {
            Close();
        }

        End();
        return true;
    }

    /// <summary>
    /// Begin a confirmation modal with Yes/No buttons.
    /// Returns: 0 = still open, 1 = confirmed (Yes), -1 = cancelled (No)
    /// </summary>
    public static int Confirm(string id, string message, string yesText = "Yes", string noText = "No")
    {
        if (_activeModalId != id)
            return 0;

        float textWidth = MeasureTextWidth(message);
        float buttonWidth = Im.Style.MinButtonWidth;
        float totalButtonWidth = buttonWidth * 2 + Im.Style.Spacing;
        float width = Math.Max(textWidth + Im.Style.Padding * 4, totalButtonWidth + Im.Style.Padding * 4);
        float height = Im.Style.FontSize + Im.Style.MinButtonHeight + Im.Style.Padding * 4 + Im.Style.Spacing;

        if (!Begin(id, width, height, "Confirm"))
            return 0;

        // Draw message
        Im.LabelText(message, _contentOffset.X, _contentOffset.Y);

        // Draw buttons (centered)
        float buttonsX = _contentOffset.X + (width - Im.Style.Padding * 2 - totalButtonWidth) * 0.5f;
        float buttonY = _contentOffset.Y + Im.Style.FontSize + Im.Style.Spacing;

        int result = 0;

        if (Im.Button(noText, buttonsX, buttonY, buttonWidth, Im.Style.MinButtonHeight))
        {
            Close();
            result = -1;
        }

        if (Im.Button(yesText, buttonsX + buttonWidth + Im.Style.Spacing, buttonY, buttonWidth, Im.Style.MinButtonHeight))
        {
            Close();
            result = 1;
        }

        End();
        return result;
    }

    private static float MeasureTextWidth(string text)
    {
        return ImTextMetrics.MeasureWidth(Im.Context.Font, text.AsSpan(), Im.Style.FontSize);
    }

    private static void CleanupOverlay(DerpLib.ImGui.Viewport.ImViewport? viewport)
    {
        if (!_overlayActive)
        {
            return;
        }

        if (_pushedCancelTransform)
        {
            Im.PopTransform();
            _pushedCancelTransform = false;
        }

        if (_pushedOverlayScope)
        {
            ImPopover.ExitOverlayScope();
            _pushedOverlayScope = false;
        }

        if (viewport != null)
        {
            var drawList = viewport.GetDrawList(ImDrawLayer.Overlay);
            drawList.SetClipRect(_previousClipRect);
            drawList.SetSortKey(_previousSortKey);
            Im.SetDrawLayer(_previousLayer);
        }

        _overlayActive = false;
    }
}
