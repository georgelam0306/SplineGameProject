# Im Popover Input Standard and Migration Plan

## Why This Exists
We hit two classes of bugs in the spline editor popover:

1. Open-click immediately closed the popover in the same frame.
2. Popover rendered visually but could not receive interaction because overlay input scope was not active.

Those failures are not spline-specific. They come from repeated ad-hoc popover plumbing across projects.

## Standard Behavior (Required)
Every interactive popover/dropdown/menu-like surface should follow the same contract:

1. Capture overlay hit region.
2. Enter overlay input scope for interactive controls.
3. Use one-frame open suppression for outside-close logic.
4. Use explicit outside-close policy (`close on left/right/middle` as needed).
5. Support `Esc` close policy where appropriate.
6. Optionally consume close click to prevent click-through side effects.
7. Keep all hit-testing in a single coordinate space (viewport vs local).
8. Keep rendering and close-policy centralized at popover boundary, not inside individual inner widgets.

## Proposed Engine Abstraction
Add a reusable popover helper in Engine (single implementation used by all apps):

- New file: `Engine/src/Engine/ImGui/Widgets/ImPopover.cs`
- It should provide:
  - standardized open-frame suppression
  - `AddOverlayCaptureRect(...)`
  - `PushOverlayScope()/PopOverlayScope()` lifetime
  - outside-click and `Esc` close checks
  - optional click-consume-on-close

This avoids every app re-implementing fragile input logic.

## Migration Inventory

### A) Engine-level foundational migration (must do first)
1. `Engine/src/Engine/ImGui/Widgets/ImContextMenu.cs:147`
2. `Engine/src/Engine/ImGui/Widgets/ImContextMenu.cs:183`
3. `Engine/src/Engine/ImGui/Widgets/ImContextMenu.cs:553`
4. `Engine/src/Engine/ImGui/Im.cs:2797`
5. `Engine/src/Engine/ImGui/Im.cs:3007`
6. `Engine/src/Engine/ImGui/Widgets/ImModal.cs:115`
7. `Engine/src/Engine/ImGui/Widgets/ImMainMenuBar.cs:235`

Goal: remove duplicated popover scope/capture/close logic from engine widgets and route through `ImPopover`.

### B) Derp.Doc call sites (direct migration)
1. `Derp.Doc/Panels/SpreadsheetRenderer.cs:4265` (select typeahead popup)
2. `Derp.Doc/Panels/SpreadsheetRenderer.cs:4544` (relation typeahead popup)
3. `Derp.Doc/Panels/SpreadsheetRenderer.cs:7843` (formula completion popup)
4. `Derp.Doc/Panels/SpreadsheetRenderer.cs:10049` (spline editor popover)
5. `Derp.Doc/Panels/SpreadsheetRenderer.cs:2717` (`IsSpreadsheetPopoverOpen` gating should remain consistent with active popovers)

### C) Derp.UI custom popover implementations (direct migration)
1. `Derp.UI/Workspace/ColorPicker.cs:188`
2. `Derp.UI/Workspace/ColorPicker.cs:281`
3. `Derp.UI/Workspace/Widgets/PaintLayerOptionsPopover.cs:146`
4. `Derp.UI/Workspace/Widgets/PaintLayerOptionsPopover.cs:209`
5. `Derp.UI/Workspace/Widgets/DataBindPopover.cs:122`
6. `Derp.UI/Workspace/Widgets/DataBindPopover.cs:148`
7. `Derp.UI/Workspace/Widgets/PrefabVariableColorPopover.cs:90`
8. `Derp.UI/Workspace/Widgets/PrefabVariableColorPopover.cs:110`
9. `Derp.UI/Workspace/AnimationEditor/AnimationTimelineMenuWidget.cs:42`
10. `Derp.UI/Workspace/AnimationEditor/AnimationTimelineMenuWidget.cs:93`

These classes currently hand-roll overlay rendering and close policy and should be moved to shared popover lifecycle.

### D) Derp.UI integration points (review with migration)
1. `Derp.UI/Workspace/PropertyInspector.cs:610`
2. `Derp.UI/UiWorkspace.UiPanels.cs:47`

These aggregate popover mouse-capture state today. After migration, keep only what is still necessary for panel-level gesture suppression.

### E) Context-menu call sites to regression-test after Engine migration
These mostly call `ImContextMenu.*`; behavior change will come from engine-level context menu migration:

1. `Derp.Doc/Panels/SidebarPanel.cs`
2. `Derp.Doc/Panels/ViewSwitcherBar.cs`
3. `Derp.Doc/Panels/InspectorPanel.cs`
4. `Derp.Doc/Panels/SpreadsheetRenderer.cs`
5. `Derp.UI/Workspace/PropertyInspector.cs`
6. `Derp.UI/Workspace/Toolbar.cs`
7. `Derp.UI/Workspace/Widgets/PrefabVariablesPanel.cs`
8. `Derp.UI/Workspace/Widgets/PaintStackPanel.cs`
9. `Derp.UI/Workspace/LayersPanel.cs`
10. `Derp.UI/Workspace/AnimationEditor/AnimationsLibraryWidget.cs`
11. `Derp.UI/Workspace/StateMachineEditor/StateMachineGraphCanvasWidget.cs`
12. `Derp.UI/Workspace/AnimationEditor/AnimationTrackListWidget.cs`

## Out of Scope (Do Not Conflate)
These are click-outside behaviors for inline inputs, not popovers:

1. `Engine/src/Engine/ImGui/Widgets/ImNumberInput.cs:173`
2. `Engine/src/Engine/ImGui/Widgets/ImScalarInput.cs:433`
3. `Engine/src/Engine/ImGui/Widgets/ImVectorInput.cs:618`
4. `Derp.UI/Workspace/AnimationEditor/AnimationTrackListWidget.cs:582`

They should be audited separately, not forced into popover abstraction.

## Rollout Plan
1. Add `ImPopover` helper in Engine.
2. Migrate Engine widgets (`ImContextMenu`, dropdown/combo in `Im.cs`, modal/menu bar where applicable).
3. Migrate Derp.Doc popovers.
4. Migrate Derp.UI popovers (`ColorPicker`, paint/data-bind/prefab-variable/timeline menu).
5. Revisit `CapturesMouse` plumbing in `PropertyInspector`/`UiWorkspace` and delete redundant logic.

## Validation Checklist
For each migrated popover:

1. Open click does not immediately close.
2. Controls inside popover are interactive.
3. Outside click closes according to policy.
4. `Esc` closes when enabled.
5. No click-through to underlying widgets.
6. Works in embedded and viewport-space rendering.
7. Works across multiple open windows/viewports.

## Immediate Priority
High-risk user-visible classes to migrate first after helper lands:

1. `Derp.UI/Workspace/ColorPicker.cs`
2. `Derp.UI/Workspace/Widgets/PaintLayerOptionsPopover.cs`
3. `Derp.UI/Workspace/Widgets/DataBindPopover.cs`
4. `Derp.UI/Workspace/Widgets/PrefabVariableColorPopover.cs`
5. `Derp.UI/Workspace/AnimationEditor/AnimationTimelineMenuWidget.cs`
6. `Derp.Doc/Panels/SpreadsheetRenderer.cs`
