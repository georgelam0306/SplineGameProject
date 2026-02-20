# Input (Keyboard / Mouse / Gamepad)

This repo supports two ways to read input:

1) **Direct hardware API** via `Derp.*` (DerpLib Engine).
2) **Action-based API** via `Core.Input.IInputManager` (shared, backend-agnostic).

The action system is designed so the game can run the same high-level input logic on different backends:

- `Core.Input.InputManager` (Raylib backend)
- `DerpLib.DerpInputManager` (Derp backend: Silk.NET keyboard/mouse + SDL gamepad via `Derp.*`)

## Direct API (Derp.*)

### Update order

Call `Derp.PollEvents()` **once per frame** before reading input. Edge-detected queries (Pressed/Released) are only valid after the per-frame state has been advanced.

### Keyboard

- `Derp.IsKeyDown(Silk.NET.Input.Key key)`
- `Derp.IsKeyPressed(Silk.NET.Input.Key key)` (edge detect)
- `Derp.IsKeyReleased(Silk.NET.Input.Key key)` (edge detect)

### Mouse

- `Derp.GetMousePosition()`
- `Derp.GetMouseDelta()`
- `Derp.GetScrollDelta()` (alias: `Derp.GetMouseScrollDelta()`)
- `Derp.IsMouseButtonDown(Silk.NET.Input.MouseButton button)`
- `Derp.IsMouseButtonPressed(Silk.NET.Input.MouseButton button)` (edge detect)
- `Derp.IsMouseButtonReleased(Silk.NET.Input.MouseButton button)` (edge detect)

### Gamepad (SDL, via Derp)

Gamepads are indexed `0..3`.

- `Derp.IsGamepadAvailable(int gamepad = 0)`
- `Derp.GetGamepadName(int gamepad = 0)`
- `Derp.IsGamepadButtonDown(int gamepad, DerpLib.Input.GamepadButton button)`
- `Derp.IsGamepadButtonPressed(int gamepad, DerpLib.Input.GamepadButton button)` (edge detect)
- `Derp.IsGamepadButtonReleased(int gamepad, DerpLib.Input.GamepadButton button)` (edge detect)
- `Derp.GetGamepadAxisMovement(int gamepad, DerpLib.Input.GamepadAxis axis)`

Axis ranges:

- sticks: `[-1, 1]`
- triggers: `[0, 1]`

Minimal example:

```csharp
Derp.PollEvents();

if (Derp.IsGamepadAvailable())
{
    float moveX = Derp.GetGamepadAxisMovement(GamepadAxis.LeftX);
    if (Derp.IsGamepadButtonPressed(GamepadButton.RightFaceDown))
    {
        // "A" on Xbox / "Cross" on PlayStation
    }
}
```

For a complete example, see `Engine/examples/GamePadControllerTest/Program.cs`.

## Action API (IInputManager)

The action system provides:

- action maps (contexts like `"Gameplay"` / `"Menu"`)
- a context stack (push/pop)
- optional per-frame input history buffering (for decoupling render and simulation ticks)

### Recommended hot-path usage

Avoid looking up maps/actions by string every frame. Prefer caching references returned during setup:

```csharp
var input = new DerpInputManager(); // or: new Core.Input.InputManager() for Raylib

var gameplay = input.CreateActionMap("Gameplay");
var move = gameplay.AddAction("Move", ActionType.Vector2);
move.AddCompositeBinding(
    "move",
    "<Keyboard>/w",
    "<Keyboard>/s",
    "<Keyboard>/a",
    "<Keyboard>/d");

var jump = gameplay.AddAction("Jump", ActionType.Button);
jump.AddBinding("<Gamepad>/a");
jump.AddBinding("<Keyboard>/space");

input.PushContext("Gameplay");

// Per-frame:
input.Update(deltaTimeSeconds);
Vector2 moveAxis = move.Value.Vector2;
bool jumpPressedThisFrame = jump.Phase == ActionPhase.Started;
```

### Binding path format

Binding strings are parsed up-front and cached. Call `AddBinding(...)` / `AddCompositeBinding(...)` during initialization (not in a per-frame loop).

Supported forms:

- Keyboard: `"<Keyboard>/w"`, `"<Keyboard>/space"`, `"<Keyboard>/leftshift"`, `"<Keyboard>/escape"`, `"<Keyboard>/f1"`, …
- Mouse buttons: `"<Mouse>/leftButton"`, `"<Mouse>/rightButton"`, `"<Mouse>/middleButton"`
- Mouse axes: `"<Mouse>/position"`, `"<Mouse>/delta"`, `"<Mouse>/scroll"`
- Gamepad buttons: `"<Gamepad>/a"`, `"<Gamepad>/b"`, `"<Gamepad>/x"`, `"<Gamepad>/y"`, `"<Gamepad>/start"`, `"<Gamepad>/select"`, …
- Gamepad axes (directional): `"<Gamepad>/leftStick/left"`, `"<Gamepad>/leftStick/right"`, `"<Gamepad>/leftStick/up"`, `"<Gamepad>/leftStick/down"`
- Gamepad triggers: `"<Gamepad>/leftTrigger"`, `"<Gamepad>/rightTrigger"`

### Backend selection

- **Raylib**: use `Core.Input.InputManager` (implemented in `Shared/Core.RaylibInput/Core.RaylibInput.csproj`).
- **Derp**: use `DerpLib.DerpInputManager` (reads keyboard/mouse from `Derp.*` and gamepad from the SDL-backed `Derp` gamepad API).

Both implement `Core.Input.IInputManager`.

### JSON configs

Games can define action maps in JSON and load them at startup via `Core.Input.InputConfigLoader`.

Example (abbreviated):

```json
{
  "actionMaps": [
    {
      "name": "Tank",
      "actions": [
        { "name": "Aim", "type": "Vector2", "deadZone": 0.20, "composites": [ /* ... */ ] },
        {
          "name": "Fire",
          "type": "Button",
          "bindings": [ "<Keyboard>/space", "<Gamepad>/a" ],
          "derivedTriggers": [
            { "sourceAction": "Aim", "kind": "Vector2MagnitudeAtLeast", "threshold": 0.35, "mode": "Hold" }
          ]
        }
      ]
    }
  ]
}
```

#### `derivedTriggers`

`derivedTriggers` lets a **Button** action become active based on another action’s analog value (e.g. “fire when aim stick is pushed”).

- `sourceAction`: name of the source action in the same map
- `kind`:
  - `Vector2MagnitudeAtLeast`
  - `AxisMagnitudeAtLeast`
- `threshold`: magnitude cutoff (Vector2 uses squared magnitude internally)
- `mode`: currently only `Hold`

Derived triggers are merged with normal bindings/composites (logical OR).

### Canonical input enums

The action system’s binding codes are defined in `Core.Input` (not Raylib):

- `Core.Input.KeyboardKey`
- `Core.Input.MouseButton`
- `Core.Input.GamepadButton`
- `Core.Input.GamepadAxis`

`IInputManager`’s “direct device access” methods use `int` parameters for abstraction; those `int` values are the corresponding `Core.Input.*` enum values.
