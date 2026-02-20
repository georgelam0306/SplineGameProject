using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Viewport;
using DerpLib.Presentation;
using Silk.NET.GLFW;

namespace DerpLib.ImGui.Input;

internal static class ImMouseCursorLock
{
    private static bool _isLocked;
    private static int _ownerId;
    private static bool _ownerIsPrimary;
    private static int _ownerResourcesId;
    private static Vector2 _restoreMousePosLocal;
    private static Vector2 _lastMousePosLocal;
    private static CursorModeValue _previousCursorMode;

    public static bool Begin(int ownerId)
    {
        if (_isLocked)
        {
            return _ownerId == ownerId;
        }

        if (!TryGetWindowForCurrentViewport(out var window))
        {
            return false;
        }

        var viewport = Im.Context.CurrentViewport;
        if (viewport == null)
        {
            return false;
        }

        _isLocked = true;
        _ownerId = ownerId;
        _ownerIsPrimary = viewport.IsPrimary;
        _ownerResourcesId = viewport.ResourcesId;
        _previousCursorMode = window.GetCursorMode();

        _restoreMousePosLocal = window.GetCursorPosition();
        _lastMousePosLocal = _restoreMousePosLocal;

        window.SetCursorMode(CursorModeValue.CursorDisabled);
        return true;
    }

    public static Vector2 ConsumeDelta(int ownerId)
    {
        if (!_isLocked || _ownerId != ownerId)
        {
            return Vector2.Zero;
        }

        if (!TryGetWindowForOwner(out var window))
        {
            return Vector2.Zero;
        }

        var pos = window.GetCursorPosition();
        var delta = pos - _lastMousePosLocal;
        _lastMousePosLocal = pos;
        return delta;
    }

    public static void End(int ownerId)
    {
        if (!_isLocked || _ownerId != ownerId)
        {
            return;
        }

        if (TryGetWindowForOwner(out var window))
        {
            window.SetCursorMode(_previousCursorMode);
            window.SetCursorPosition(_restoreMousePosLocal.X, _restoreMousePosLocal.Y);
        }

        _isLocked = false;
        _ownerId = 0;
        _ownerIsPrimary = false;
        _ownerResourcesId = -1;
        _restoreMousePosLocal = default;
        _lastMousePosLocal = default;
        _previousCursorMode = CursorModeValue.CursorNormal;
    }

    private static bool TryGetWindowForCurrentViewport(out Window window)
    {
        var viewport = Im.Context.CurrentViewport;
        if (viewport == null)
        {
            window = null!;
            return false;
        }

        return TryGetWindowForViewport(viewport.IsPrimary, viewport.ResourcesId, out window);
    }

    private static bool TryGetWindowForOwner(out Window window)
    {
        return TryGetWindowForViewport(_ownerIsPrimary, _ownerResourcesId, out window);
    }

    private static bool TryGetWindowForViewport(bool isPrimary, int resourcesId, out Window window)
    {
        if (isPrimary)
        {
            window = Derp.GetPrimaryWindowForImGui();
            return true;
        }

        ViewportManager? vm = ImContext.Instance.ViewportManager;
        if (vm != null && resourcesId >= 0 && vm.TryGetViewportResourcesById(resourcesId, out var resources))
        {
            window = resources.Window;
            return true;
        }

        window = null!;
        return false;
    }
}
