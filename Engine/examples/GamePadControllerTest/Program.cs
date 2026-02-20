using System.Numerics;
using Serilog;
using DerpLib;
using DerpLib.Input;
using DerpLib.ImGui;
using DerpLib.ImGui.Layout;
using DerpLib.Text;

namespace DerpLib.Examples.GamePadControllerTest;

/// <summary>
/// GamePad/Controller input test application.
/// Demonstrates Derp gamepad API with visual feedback.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== GamePad Controller Test (Derp API) ===");

        // Initialize window (also initializes SDL gamepad subsystem via Engine)
        Derp.InitWindow(900, 700, "GamePad Controller Test");
        Derp.InitSdf();

        // Load font for text rendering
        Font font = Derp.LoadFont("arial");
        Derp.SetSdfFontAtlas(font.Atlas);

        Im.Initialize(enableMultiViewport: false);
        Im.SetFont(font);

        var state = new GamePadState();

        while (!Derp.WindowShouldClose())
        {
            Derp.PollEvents();

            // Update gamepad state using Derp API
            if (Derp.IsGamepadAvailable())
            {
                UpdateGamePadState(ref state);
            }
            else
            {
                state = default;
            }

            if (!Derp.BeginDrawing())
                continue;

            Derp.SdfBuffer.Reset();
            Im.Begin(0.016f);

            float screenW = Derp.GetScreenWidth();
            float screenH = Derp.GetScreenHeight();

            // Draw UI
            DrawControllerStatus(screenW);
            DrawGamePadVisualization(state, screenW, screenH);
            DrawButtonStates(state);
            DrawAxisValues(state);

            Im.End();
            Derp.RenderSdf();
            Derp.EndDrawing();
        }

        Derp.CloseWindow();
        Log.Information("=== GamePad Controller Test Closed ===");
    }

    private static void UpdateGamePadState(ref GamePadState state)
    {
        // Axes using Derp API
        state.LeftStickX = Derp.GetGamepadAxisMovement(GamepadAxis.LeftX);
        state.LeftStickY = Derp.GetGamepadAxisMovement(GamepadAxis.LeftY);
        state.RightStickX = Derp.GetGamepadAxisMovement(GamepadAxis.RightX);
        state.RightStickY = Derp.GetGamepadAxisMovement(GamepadAxis.RightY);
        state.LeftTrigger = Derp.GetGamepadAxisMovement(GamepadAxis.LeftTrigger);
        state.RightTrigger = Derp.GetGamepadAxisMovement(GamepadAxis.RightTrigger);

        // Buttons using Derp API
        state.A = Derp.IsGamepadButtonDown(GamepadButton.RightFaceDown);       // A
        state.B = Derp.IsGamepadButtonDown(GamepadButton.RightFaceRight);      // B
        state.X = Derp.IsGamepadButtonDown(GamepadButton.RightFaceLeft);       // X
        state.Y = Derp.IsGamepadButtonDown(GamepadButton.RightFaceUp);         // Y
        state.LeftBumper = Derp.IsGamepadButtonDown(GamepadButton.LeftTrigger1);
        state.RightBumper = Derp.IsGamepadButtonDown(GamepadButton.RightTrigger1);
        state.Back = Derp.IsGamepadButtonDown(GamepadButton.MiddleLeft);
        state.Start = Derp.IsGamepadButtonDown(GamepadButton.MiddleRight);
        state.Guide = Derp.IsGamepadButtonDown(GamepadButton.Middle);
        state.LeftStickButton = Derp.IsGamepadButtonDown(GamepadButton.LeftThumb);
        state.RightStickButton = Derp.IsGamepadButtonDown(GamepadButton.RightThumb);
        state.DPadUp = Derp.IsGamepadButtonDown(GamepadButton.LeftFaceUp);
        state.DPadDown = Derp.IsGamepadButtonDown(GamepadButton.LeftFaceDown);
        state.DPadLeft = Derp.IsGamepadButtonDown(GamepadButton.LeftFaceLeft);
        state.DPadRight = Derp.IsGamepadButtonDown(GamepadButton.LeftFaceRight);
    }

    private static void DrawControllerStatus(float screenW)
    {
        if (Im.BeginWindow("Controller Status", 20, 20, 300, 100))
        {
            if (Derp.IsGamepadAvailable())
            {
                string name = Derp.GetGamepadName() ?? "Unknown";
                Im.LabelText($"Connected: {name}");
            }
            else
            {
                Im.LabelText("No controller connected");
                Im.LabelText("Connect a gamepad to begin");
            }
        }
        Im.EndWindow();
    }

    private static void DrawGamePadVisualization(GamePadState state, float screenW, float screenH)
    {
        float centerX = screenW / 2;
        float centerY = screenH / 2 - 50;

        // Controller body outline
        Derp.DrawSdfRoundedRect(centerX, centerY, 400, 200, 30, 0.15f, 0.15f, 0.2f, 1f);

        // Left stick background
        float leftStickBaseX = centerX - 120;
        float leftStickBaseY = centerY - 20;
        Derp.DrawSdfCircle(leftStickBaseX, leftStickBaseY, 50, 0.1f, 0.1f, 0.15f, 1f);

        // Left stick position
        float lsX = leftStickBaseX + state.LeftStickX * 35;
        float lsY = leftStickBaseY + state.LeftStickY * 35;
        float lsColor = state.LeftStickButton ? 0.2f : 0.4f;
        Derp.DrawSdfCircle(lsX, lsY, 25, lsColor, 0.6f, 0.9f, 1f);

        // Right stick background
        float rightStickBaseX = centerX + 60;
        float rightStickBaseY = centerY + 30;
        Derp.DrawSdfCircle(rightStickBaseX, rightStickBaseY, 50, 0.1f, 0.1f, 0.15f, 1f);

        // Right stick position
        float rsX = rightStickBaseX + state.RightStickX * 35;
        float rsY = rightStickBaseY + state.RightStickY * 35;
        float rsColor = state.RightStickButton ? 0.2f : 0.4f;
        Derp.DrawSdfCircle(rsX, rsY, 25, rsColor, 0.6f, 0.9f, 1f);

        // D-Pad
        float dpadX = centerX - 120;
        float dpadY = centerY + 50;
        DrawDPad(dpadX, dpadY, state);

        // Face buttons (A, B, X, Y)
        float faceX = centerX + 120;
        float faceY = centerY - 20;
        DrawFaceButton(faceX, faceY + 25, "A", state.A, 0.3f, 0.8f, 0.3f);
        DrawFaceButton(faceX + 25, faceY, "B", state.B, 0.8f, 0.3f, 0.3f);
        DrawFaceButton(faceX - 25, faceY, "X", state.X, 0.3f, 0.3f, 0.8f);
        DrawFaceButton(faceX, faceY - 25, "Y", state.Y, 0.8f, 0.8f, 0.3f);

        // Triggers
        float triggerY = centerY - 120;
        DrawTrigger(centerX - 140, triggerY, "LT", state.LeftTrigger);
        DrawTrigger(centerX + 140, triggerY, "RT", state.RightTrigger);

        // Bumpers
        float bumperY = centerY - 90;
        DrawBumper(centerX - 100, bumperY, "LB", state.LeftBumper);
        DrawBumper(centerX + 100, bumperY, "RB", state.RightBumper);

        // Center buttons
        DrawSmallButton(centerX - 40, centerY, "Back", state.Back);
        DrawSmallButton(centerX, centerY - 30, "Guide", state.Guide);
        DrawSmallButton(centerX + 40, centerY, "Start", state.Start);
    }

    private static void DrawDPad(float x, float y, GamePadState state)
    {
        float size = 15;
        float gap = 18;

        // Up
        float upColor = state.DPadUp ? 0.9f : 0.3f;
        Derp.DrawSdfRoundedRect(x, y - gap, size, size, 3, upColor, upColor, upColor, 1f);

        // Down
        float downColor = state.DPadDown ? 0.9f : 0.3f;
        Derp.DrawSdfRoundedRect(x, y + gap, size, size, 3, downColor, downColor, downColor, 1f);

        // Left
        float leftColor = state.DPadLeft ? 0.9f : 0.3f;
        Derp.DrawSdfRoundedRect(x - gap, y, size, size, 3, leftColor, leftColor, leftColor, 1f);

        // Right
        float rightColor = state.DPadRight ? 0.9f : 0.3f;
        Derp.DrawSdfRoundedRect(x + gap, y, size, size, 3, rightColor, rightColor, rightColor, 1f);

        // Center
        Derp.DrawSdfRoundedRect(x, y, size, size, 3, 0.2f, 0.2f, 0.2f, 1f);
    }

    private static void DrawFaceButton(float x, float y, string label, bool pressed, float r, float g, float b)
    {
        float brightness = pressed ? 1f : 0.4f;
        Derp.DrawSdfCircle(x, y, 18, r * brightness, g * brightness, b * brightness, 1f);
    }

    private static void DrawTrigger(float x, float y, string label, float value)
    {
        float width = 50;
        float height = 30;

        // Background
        Derp.DrawSdfRoundedRect(x, y, width, height, 5, 0.15f, 0.15f, 0.2f, 1f);

        // Fill based on value
        float fillWidth = width * value;
        if (fillWidth > 2)
        {
            Derp.DrawSdfRoundedRect(x - (width - fillWidth) / 2, y, fillWidth, height - 4, 3, 0.9f, 0.5f, 0.2f, 1f);
        }
    }

    private static void DrawBumper(float x, float y, string label, bool pressed)
    {
        float brightness = pressed ? 0.9f : 0.3f;
        Derp.DrawSdfRoundedRect(x, y, 60, 20, 8, brightness, brightness, brightness, 1f);
    }

    private static void DrawSmallButton(float x, float y, string label, bool pressed)
    {
        float brightness = pressed ? 0.9f : 0.25f;
        Derp.DrawSdfCircle(x, y, 10, brightness, brightness, brightness, 1f);
    }

    private static void DrawButtonStates(GamePadState state)
    {
        if (Im.BeginWindow("Button States", 20, 140, 200, 350))
        {
            Im.LabelText("Face Buttons:");
            DrawStateRow("  A", state.A);
            DrawStateRow("  B", state.B);
            DrawStateRow("  X", state.X);
            DrawStateRow("  Y", state.Y);

            ImLayout.Space(10);
            Im.LabelText("Shoulders:");
            DrawStateRow("  LB", state.LeftBumper);
            DrawStateRow("  RB", state.RightBumper);

            ImLayout.Space(10);
            Im.LabelText("D-Pad:");
            DrawStateRow("  Up", state.DPadUp);
            DrawStateRow("  Down", state.DPadDown);
            DrawStateRow("  Left", state.DPadLeft);
            DrawStateRow("  Right", state.DPadRight);

            ImLayout.Space(10);
            Im.LabelText("Other:");
            DrawStateRow("  Start", state.Start);
            DrawStateRow("  Back", state.Back);
            DrawStateRow("  Guide", state.Guide);
            DrawStateRow("  L3", state.LeftStickButton);
            DrawStateRow("  R3", state.RightStickButton);
        }
        Im.EndWindow();
    }

    private static void DrawStateRow(string label, bool value)
    {
        string state = value ? "[X]" : "[ ]";
        Im.LabelText($"{label}: {state}");
    }

    private static void DrawAxisValues(GamePadState state)
    {
        if (Im.BeginWindow("Axis Values", 680, 20, 200, 220))
        {
            Im.LabelText("Left Stick:");
            Im.LabelText($"  X: {state.LeftStickX:F3}");
            Im.LabelText($"  Y: {state.LeftStickY:F3}");

            ImLayout.Space(10);
            Im.LabelText("Right Stick:");
            Im.LabelText($"  X: {state.RightStickX:F3}");
            Im.LabelText($"  Y: {state.RightStickY:F3}");

            ImLayout.Space(10);
            Im.LabelText("Triggers:");
            Im.LabelText($"  LT: {state.LeftTrigger:F3}");
            Im.LabelText($"  RT: {state.RightTrigger:F3}");
        }
        Im.EndWindow();
    }
}

/// <summary>
/// Current state of a gamepad.
/// </summary>
public struct GamePadState
{
    // Axes
    public float LeftStickX;
    public float LeftStickY;
    public float RightStickX;
    public float RightStickY;
    public float LeftTrigger;
    public float RightTrigger;

    // Face buttons
    public bool A;
    public bool B;
    public bool X;
    public bool Y;

    // Shoulder buttons
    public bool LeftBumper;
    public bool RightBumper;

    // Center buttons
    public bool Back;
    public bool Start;
    public bool Guide;

    // Stick buttons
    public bool LeftStickButton;
    public bool RightStickButton;

    // D-Pad
    public bool DPadUp;
    public bool DPadDown;
    public bool DPadLeft;
    public bool DPadRight;
}
