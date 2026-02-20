using DerpLib.ImGui.Core;
using DerpLib.ImGui.Docking;
using DerpLib.ImGui.Windows;
using DerpLib.Presentation;

namespace DerpLib.ImGui.Viewport;

/// <summary>
/// Computes which viewport each window should render to based on screen-space bounds.
/// Creates/destroys secondary OS windows automatically.
/// Allocation-free per frame: uses preallocated storage and simple loops.
/// </summary>
public sealed class ViewportAssigner
{
    private readonly ViewportManager _viewportManager;
    private readonly ViewportLink[] _links;
    private int _linkCount;
    private int _frameTag;

    private enum ViewportOwnerKind : byte
    {
        Window = 0,
        DockLayout = 1
    }

    private struct ViewportLink
    {
        public ViewportOwnerKind OwnerKind;
        public ImWindow? Window;
        public DockingLayout? Layout;
        public ViewportResources Viewport;
        public int LastSeenFrameTag;
    }

    public ViewportAssigner(ViewportManager viewportManager, int maxWindows)
    {
        _viewportManager = viewportManager;
        _links = new ViewportLink[maxWindows];
    }

    /// <summary>
    /// Assign windows to viewports. Call once per frame before rendering.
    /// </summary>
    public void AssignViewports(ImWindowManager windowManager, ImRect primaryViewportScreenBounds, DockController dockController)
    {
        _frameTag++;

        // 1) Dock group viewports (multi-window floating layouts).
        // These must be assigned before per-window viewports so docked tabs render with their chrome/background.
        var floatingLayouts = dockController.FloatingLayouts;
        for (int layoutIndex = 0; layoutIndex < floatingLayouts.Count; layoutIndex++)
        {
            var layout = floatingLayouts[layoutIndex];
            if (layout.Root == null || !layout.IsFloatingGroup())
            {
                continue;
            }

            // If fully inside primary, render as part of the primary viewport.
            if (primaryViewportScreenBounds.Contains(layout.Rect))
            {
                RemoveViewportForDockLayout(layout);
                continue;
            }

            ref var layoutLink = ref GetOrCreateDockLayoutLink(layout, windowManager);
            layoutLink.LastSeenFrameTag = _frameTag;
            layoutLink.Viewport.UpdatePositionAndSize(
                (int)layout.Rect.X,
                (int)layout.Rect.Y,
                (int)layout.Rect.Width,
                (int)layout.Rect.Height);
            SyncImViewportFromResources(layoutLink.Viewport);

            // Ensure member windows don't keep their own per-window viewports.
            RemoveViewportsForWindowsInLayout(layout, windowManager);
        }

        // 2) Per-window viewports (windows outside primary that are not part of a dock group viewport).
        foreach (var window in windowManager.GetWindowsBackToFront())
        {
            if (!window.IsOpen)
            {
                RemoveViewportForWindow(window);
                continue;
            }

            // Windows that belong to an out-of-primary floating dock group share the group's viewport.
            if (IsWindowInDockLayoutViewport(window, dockController))
            {
                // Ensure we don't keep stale per-window viewports around.
                RemoveViewportForWindow(window);
                continue;
            }

            bool insidePrimary = primaryViewportScreenBounds.Contains(window.Rect);
            if (insidePrimary)
            {
                RemoveViewportForWindow(window);
                continue;
            }

            ref var link = ref GetOrCreateLink(window);
            link.LastSeenFrameTag = _frameTag;

            link.Viewport.UpdatePositionAndSize(
                (int)window.Rect.X,
                (int)window.Rect.Y,
                (int)window.Rect.Width,
                (int)window.Rect.Height);
            SyncImViewportFromResources(link.Viewport);
        }

        // Destroy viewports for windows no longer present.
        for (int linkIndex = _linkCount - 1; linkIndex >= 0; linkIndex--)
        {
            if (_links[linkIndex].LastSeenFrameTag != _frameTag)
            {
                DestroyLinkAt(linkIndex);
            }
        }
    }

    /// <summary>
    /// Get the secondary viewport a window should render to, if any.
    /// Returns null for primary viewport.
    /// </summary>
    public ViewportResources? GetViewportForWindow(ImWindow window, DockController dockController)
    {
        // If this window is part of a floating dock group with a viewport, return that viewport.
        var layout = dockController.FindLayoutForWindow(window.Id);
        if (layout != null && !layout.IsMainLayout && layout.IsFloatingGroup())
        {
            int layoutIndex = FindLinkIndex(layout);
            if (layoutIndex >= 0)
            {
                return _links[layoutIndex].Viewport;
            }
        }

        int linkIndex = FindLinkIndex(window);
        if (linkIndex < 0)
        {
            return null;
        }

        return _links[linkIndex].Viewport;
    }

    public bool TryGetWindowForViewport(ViewportResources viewport, out ImWindow window)
    {
        for (int linkIndex = 0; linkIndex < _linkCount; linkIndex++)
        {
            if (_links[linkIndex].OwnerKind == ViewportOwnerKind.Window
                && ReferenceEquals(_links[linkIndex].Viewport, viewport)
                && _links[linkIndex].Window != null)
            {
                window = _links[linkIndex].Window!;
                return true;
            }
        }

        window = null!;
        return false;
    }

    public bool TryGetDockLayoutForViewport(ViewportResources viewport, out DockingLayout layout)
    {
        for (int linkIndex = 0; linkIndex < _linkCount; linkIndex++)
        {
            if (_links[linkIndex].OwnerKind == ViewportOwnerKind.DockLayout
                && ReferenceEquals(_links[linkIndex].Viewport, viewport)
                && _links[linkIndex].Layout != null)
            {
                layout = _links[linkIndex].Layout!;
                return true;
            }
        }

        layout = null!;
        return false;
    }

    public void RemoveViewportForViewport(ViewportResources viewport)
    {
        for (int linkIndex = 0; linkIndex < _linkCount; linkIndex++)
        {
            if (ReferenceEquals(_links[linkIndex].Viewport, viewport))
            {
                DestroyLinkAt(linkIndex);
                return;
            }
        }
    }

    private int FindLinkIndex(ImWindow window)
    {
        for (int linkIndex = 0; linkIndex < _linkCount; linkIndex++)
        {
            if (ReferenceEquals(_links[linkIndex].Window, window))
            {
                return linkIndex;
            }
        }

        return -1;
    }

    private ref ViewportLink GetOrCreateLink(ImWindow window)
    {
        int existingIndex = FindLinkIndex(window);
        if (existingIndex >= 0)
        {
            return ref _links[existingIndex];
        }

        if (_linkCount >= _links.Length)
        {
            throw new InvalidOperationException("Maximum window count exceeded");
        }

        var viewport = _viewportManager.CreateViewportForWindow(window);
        _links[_linkCount] = new ViewportLink
        {
            OwnerKind = ViewportOwnerKind.Window,
            Window = window,
            Layout = null,
            Viewport = viewport,
            LastSeenFrameTag = _frameTag
        };

        _linkCount++;
        return ref _links[_linkCount - 1];
    }

    private ref ViewportLink GetOrCreateDockLayoutLink(DockingLayout layout, ImWindowManager windowManager)
    {
        int existingIndex = FindLinkIndex(layout);
        if (existingIndex >= 0)
        {
            return ref _links[existingIndex];
        }

        if (_linkCount >= _links.Length)
        {
            throw new InvalidOperationException("Maximum window count exceeded");
        }

        string title = GetDockLayoutTitle(layout, windowManager);
        var viewport = _viewportManager.CreateViewportForDockLayout(layout.Rect, title);
        _links[_linkCount] = new ViewportLink
        {
            OwnerKind = ViewportOwnerKind.DockLayout,
            Window = null,
            Layout = layout,
            Viewport = viewport,
            LastSeenFrameTag = _frameTag
        };

        _linkCount++;
        return ref _links[_linkCount - 1];
    }

    private void RemoveViewportForWindow(ImWindow window)
    {
        int existingIndex = FindLinkIndex(window);
        if (existingIndex < 0)
        {
            return;
        }

        DestroyLinkAt(existingIndex);
    }

    private void RemoveViewportForDockLayout(DockingLayout layout)
    {
        int existingIndex = FindLinkIndex(layout);
        if (existingIndex < 0)
        {
            return;
        }

        DestroyLinkAt(existingIndex);
    }

    private void DestroyLinkAt(int linkIndex)
    {
        var viewport = _links[linkIndex].Viewport;
        _viewportManager.DestroyViewport(viewport);

        int lastIndex = _linkCount - 1;
        _links[linkIndex] = _links[lastIndex];
        _links[lastIndex] = default;
        _linkCount--;
    }

    private int FindLinkIndex(DockingLayout layout)
    {
        for (int linkIndex = 0; linkIndex < _linkCount; linkIndex++)
        {
            if (_links[linkIndex].OwnerKind == ViewportOwnerKind.DockLayout
                && ReferenceEquals(_links[linkIndex].Layout, layout))
            {
                return linkIndex;
            }
        }

        return -1;
    }

    private bool IsWindowInDockLayoutViewport(ImWindow window, DockController dockController)
    {
        var layout = dockController.FindLayoutForWindow(window.Id);
        if (layout == null || layout.IsMainLayout || !layout.IsFloatingGroup())
        {
            return false;
        }

        return FindLinkIndex(layout) >= 0;
    }

    private static string GetDockLayoutTitle(DockingLayout layout, ImWindowManager windowManager)
    {
        if (layout.Root == null)
        {
            return "Dock Group";
        }

        int firstWindowId = layout.GetFirstWindowId();
        if (firstWindowId != -1)
        {
            var window = windowManager.FindWindowById(firstWindowId);
            if (window != null)
            {
                return window.Title;
            }
        }

        return "Dock Group";
    }

    private void RemoveViewportsForWindowsInLayout(DockingLayout layout, ImWindowManager windowManager)
    {
        if (layout.Root == null)
        {
            return;
        }

        RemoveViewportsForWindowsInNode(layout.Root, windowManager);
    }

    private void RemoveViewportsForWindowsInNode(ImDockNode node, ImWindowManager windowManager)
    {
        if (node is ImDockLeaf leaf)
        {
            for (int i = 0; i < leaf.WindowIds.Count; i++)
            {
                var window = windowManager.FindWindowById(leaf.WindowIds[i]);
                if (window != null)
                {
                    RemoveViewportForWindow(window);
                }
            }

            return;
        }

        if (node is ImDockSplit split)
        {
            if (split.First != null)
            {
                RemoveViewportsForWindowsInNode(split.First, windowManager);
            }

            if (split.Second != null)
            {
                RemoveViewportsForWindowsInNode(split.Second, windowManager);
            }
        }
    }

    private void SyncImViewportFromResources(ViewportResources viewportResources)
    {
        if (!_viewportManager.TryGetImViewport(viewportResources, out var imViewport))
        {
            return;
        }

        imViewport.Size = new System.Numerics.Vector2(viewportResources.Window.Width, viewportResources.Window.Height);
        imViewport.ScreenPosition = viewportResources.Window.ScreenPosition;
        imViewport.ContentScale = viewportResources.Window.ContentScaleX;
    }
}
