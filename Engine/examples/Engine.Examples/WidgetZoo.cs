using System.Numerics;
using Serilog;
using DerpLib.Diagnostics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using DerpLib.ImGui.Windows;
using DerpLib.Text;

namespace DerpLib.Examples;

/// <summary>
/// Demo showcasing all ImGUI widgets: buttons, checkboxes, sliders,
/// radio buttons, text inputs, dropdowns, and combo boxes.
/// </summary>
public static class WidgetZoo
{
    private static readonly string[] SizeGroupOptions = ["Small", "Medium", "Large"];

    // Text input buffers (fixed size, zero-allocation)
    private static readonly char[] _nameBuffer = new char[64];
    private static int _nameLength = 0;

    private static readonly char[] _searchBuffer = new char[64];
    private static int _searchLength = 0;

    private static readonly char[] _comboBuffer = new char[64];
    private static int _comboLength = 0;

    private static readonly ImTextBuffer _notesBuffer = new(initialCapacity: 512);

    // Dropdown/ComboBox options
    private static readonly string[] _colorOptions = { "Red", "Green", "Blue", "Yellow", "Purple", "Orange", "Cyan", "Magenta" };
    private static readonly string[] _fruitSuggestions = { "Apple", "Apricot", "Banana", "Blueberry", "Cherry", "Grape", "Lemon", "Mango", "Orange", "Peach", "Pear", "Strawberry" };

    // New widget state
    private static int _tabIndex = 0;
    private static readonly string[] _tabNames = { "General", "Settings", "Advanced" };
    private static float _numberValue = 42.5f;
    private static int _intValue = 10;
    private static float _rangeMin = 20f;
    private static float _rangeMax = 80f;
    private static uint _pickerColor = 0xFF4488FF;
    private static Vector2 _vec2 = new(1.0f, 2.0f);
    private static Vector3 _vec3 = new(1.0f, 2.0f, 3.0f);
    private static readonly string[] _breadcrumbPath = { "Home", "Documents", "Projects", "MyProject" };
    private static int _currentPage = 0;
    private static Curve _curve = Curve.EaseInOut();

    // Table data
    private struct TableItem
    {
        public int Id;
        public string Name;
        public float Value;
        public bool Active;
    }
    private static readonly TableItem[] _tableItems =
    {
        new() { Id = 1, Name = "Alpha", Value = 10.5f, Active = true },
        new() { Id = 2, Name = "Beta", Value = 25.3f, Active = false },
        new() { Id = 3, Name = "Gamma", Value = 42.1f, Active = true },
        new() { Id = 4, Name = "Delta", Value = 18.7f, Active = true },
        new() { Id = 5, Name = "Epsilon", Value = 55.2f, Active = false },
        new() { Id = 6, Name = "Zeta", Value = 33.9f, Active = true },
        new() { Id = 7, Name = "Eta", Value = 67.4f, Active = false },
        new() { Id = 8, Name = "Theta", Value = 12.8f, Active = true },
    };

    public static void Run()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== Widget Zoo - ImGUI Widget Showcase ===");

        // Initialize window and SDF
        Derp.InitWindow(1600, 1000, "Widget Zoo - ImGUI Showcase");
        Derp.InitSdf();

        Log.Information("Window: {W}x{H}, Scale: {Scale:F1}x",
            Derp.GetScreenWidth(), Derp.GetScreenHeight(),
            Derp.GetContentScale());

        // Load font
        Font font = Derp.LoadFont("arial");
        Derp.SetSdfFontAtlas(font.Atlas);
        Log.Information("Font loaded: Atlas={W}x{H}", font.Atlas.Width, font.Atlas.Height);

        // Initialize ImGUI
        Im.Initialize();
        Im.SetFont(font);

        // Create main dock area with an empty viewport window
        var mainViewportId = ImWindow.GetIdForTitle("Main Viewport");
        Im.Context.DockController.CreateMainDockArea(mainViewportId);

        // Widget state
        int buttonClicks = 0;
        bool checkboxValue = false;
        bool toggleA = true;
        bool toggleB = false;
        float sliderValue = 0.5f;
        int radioValue = 0;
        int radioGroupValue = 1;
        int dropdownIndex = 0;

        // Pre-fill some text
        "Hello World".AsSpan().CopyTo(_nameBuffer);
        _nameLength = 11;

        const string initialNotes =
            "Line 1: Hello!\n" +
            "Line 2: This is a multi-line text area.\n" +
            "Line 3: Use mouse wheel to scroll when it overflows.\n" +
            "Line 4: Arrow keys move the caret.\n";
        _notesBuffer.SetText(initialNotes.AsSpan());

        // Frame counter for warmup
        int frameNumber = 0;
        const int warmupFrames = 100;

        while (!Derp.WindowShouldClose())
        {
            frameNumber++;

            if (frameNumber == warmupFrames)
            {
                AllocationTracker.Enable(Log.Logger, logEveryFrame: false);
            }

            AllocationTracker.BeginFrame();

            Derp.PollEvents();
            float dt = 0.016f;

            if (!Derp.BeginDrawing())
            {
                AllocationTracker.EndFrame();
                continue;
            }

            Derp.SdfBuffer.Reset();
            Im.Begin(dt);

            // === Main Viewport (docked to main area) ===
            if (Im.BeginWindow("Main Viewport", 0, 0, 800, 600))
            {
                // Empty for now
            }
            Im.EndWindow();

            // === Window 1: Basic Widgets ===
            if (Im.BeginWindow("Basic Widgets", 30, 30, 320, 350))
            {
                Im.Label($"Button Clicks: {buttonClicks}");
                if (Im.Button("Click Me!", 120))
                {
                    buttonClicks++;
                }
                if (Im.Button("Reset", 80))
                {
                    buttonClicks = 0;
                }

                ImLayout.Space(15);

                Im.LabelText("Checkboxes");
                Im.Checkbox("Enable Feature", ref checkboxValue);
                ImLayout.Space(4);
                Im.Checkbox("Toggle A", ref toggleA);
                ImLayout.Space(4);
                Im.Checkbox("Toggle B", ref toggleB);

                ImLayout.Space(15);

                Im.LabelText("Slider");
                Im.Label($"Value: {sliderValue:F2}");
                Im.Slider("slider1", ref sliderValue, 0f, 1f);

                ImLayout.Space(10);
                ImDivider.Draw();
                ImLayout.Space(8);

                Im.LabelText("Progress + Toggle Switch");
                ImProgressBar.DrawWithPercent(sliderValue, width: 260);
                ImLayout.Space(6);
                ImToggleSwitch.Draw("Fancy Mode", ref toggleA);

                int fancyModeId = Im.Context.GetId("Fancy Mode");
                ImTooltip.Begin(fancyModeId, Im.Context.IsHot(fancyModeId));
                if (ImTooltip.ShouldShow(fancyModeId))
                {
                    ImTooltip.Draw("Toggle switch (separate from checkbox).");
                }
            }
            Im.EndWindow();

            // === Window 2: Radio Buttons ===
            if (Im.BeginWindow("Radio Buttons", 380, 30, 320, 280))
            {
                Im.LabelText("Individual Radio Buttons");
                Im.RadioButton("Option A", ref radioValue, 0);
                ImLayout.Space(4);
                Im.RadioButton("Option B", ref radioValue, 1);
                ImLayout.Space(4);
                Im.RadioButton("Option C", ref radioValue, 2);
                ImLayout.Space(4);
                Im.RadioButton("Option D", ref radioValue, 3);

                Im.Label($"Selected: Option {(char)('A' + radioValue)}");

                ImLayout.Space(20);

                Im.LabelText("Radio Group (Horizontal)");
                Im.RadioGroup("sizeGroup", SizeGroupOptions, ref radioGroupValue);
                Im.Label($"Selected: {SizeGroupOptions[radioGroupValue]}");
            }
            Im.EndWindow();

            // === Window 3: Text Input ===
            if (Im.BeginWindow("Text + Search + TextArea", 730, 30, 420, 610))
            {
                Im.LabelText("Name Input");
                if (Im.TextInput("nameInput", _nameBuffer, ref _nameLength, 64, 280))
                {
                    Log.Debug("Name changed (len={Len})", _nameLength);
                }
                Im.Label($"Length: {_nameLength} chars");

                ImLayout.Space(15);

                Im.LabelText("Search Box");
                ImSearchBox.Draw("searchBox", _searchBuffer, ref _searchLength, 64, 280);

                if (_searchLength > 0)
                {
                    Im.Label($"Searching for: \"{_searchBuffer.AsSpan(0, _searchLength)}\"");
                }
                else
                {
                    Im.Label($"Type to search...", Im.Style.TextSecondary);
                }

                int searchBoxId = Im.Context.GetId("searchBox");
                ImTooltip.Begin(searchBoxId, Im.Context.IsHot(searchBoxId));
                if (ImTooltip.ShouldShow(searchBoxId))
                {
                    ImTooltip.Draw("Search icon + clear button; ESC clears/unfocuses.");
                }

                ImLayout.Space(15);

                Im.LabelText("Text Area (scrolls)");
                ImTextArea.Draw("notes", _notesBuffer, width: 0, height: 220);
            }
            Im.EndWindow();

            // === Window 4: Dropdown ===
            if (Im.BeginWindow("Dropdown", 30, 410, 320, 220))
            {
                Im.LabelText("Color Selection");
                if (Im.Dropdown("colorDropdown", _colorOptions, ref dropdownIndex, 250))
                {
                    Log.Debug("Color selected: {Color}", _colorOptions[dropdownIndex]);
                }

                ImLayout.Space(10);

                // Show selected color as a colored box
                uint selectedColor = dropdownIndex switch
                {
                    0 => 0xFFFF0000, // Red
                    1 => 0xFF00FF00, // Green
                    2 => 0xFF0000FF, // Blue
                    3 => 0xFFFFFF00, // Yellow
                    4 => 0xFF800080, // Purple
                    5 => 0xFFFFA500, // Orange
                    6 => 0xFF00FFFF, // Cyan
                    7 => 0xFFFF00FF, // Magenta
                    _ => 0xFFFFFFFF
                };

                Im.Label($"Selected: {_colorOptions[dropdownIndex]}");
                var rect = ImLayout.AllocateRect(100, 30);
                Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, 4, selectedColor);
            }
            Im.EndWindow();

            // === Window 5: Combo Box ===
            if (Im.BeginWindow("Combo Box", 380, 410, 340, 220))
            {
                Im.LabelText("Fruit Search (type to filter)");
                if (Im.ComboBox("fruitCombo", _comboBuffer, ref _comboLength, 64, _fruitSuggestions, 280))
                {
                    Log.Debug("Fruit input changed (len={Len})", _comboLength);
                }

                ImLayout.Space(10);

                if (_comboLength > 0)
                {
                    Im.Label($"Current text: \"{_comboBuffer.AsSpan(0, _comboLength)}\"");
                }
                else
                {
                    Im.Label($"Start typing a fruit name...", Im.Style.TextSecondary);
                }

                ImLayout.Space(10);
                Im.Label($"Try: Apple, Banana, Cherry...", Im.Style.TextSecondary);
            }
            Im.EndWindow();

            // === Window 6: State Summary ===
            if (Im.BeginWindow("State Summary", 1170, 30, 400, 260))
            {
                Im.LabelText("Current Widget States");

                ImLayout.Space(5);
                Im.Label($"Button clicks: {buttonClicks}");
                Im.Label($"Checkbox: {checkboxValue}");
                Im.Label($"Toggle A: {toggleA}, Toggle B: {toggleB}");
                Im.Label($"Slider: {sliderValue:F2}");
                Im.Label($"Radio: Option {(char)('A' + radioValue)}");
                Im.Label($"Dropdown: {_colorOptions[dropdownIndex]}");

                ImLayout.Space(10);

                if (Im.Button("Reset All", 100))
                {
                    buttonClicks = 0;
                    checkboxValue = false;
                    toggleA = true;
                    toggleB = false;
                    sliderValue = 0.5f;
                    radioValue = 0;
                    radioGroupValue = 1;
                    dropdownIndex = 0;
                    _nameLength = 0;
                    _searchLength = 0;
                    _comboLength = 0;
                }
            }
            Im.EndWindow();

            // === Window 7: Tabs ===
            if (Im.BeginWindow("Tabs Demo", 30, 670, 320, 180))
            {
                var tabRect = ImLayout.AllocateRect(300, 140);
                ImTabs.Begin("demo_tabs", tabRect.X, tabRect.Y, tabRect.Width, ref _tabIndex);

                if (ImTabs.BeginTab(_tabNames[0]))
                {
                    Im.Label($"General settings go here.");
                    Im.Checkbox("Enable notifications", ref toggleA);
                    ImTabs.EndTab();
                }
                if (ImTabs.BeginTab(_tabNames[1]))
                {
                    Im.Label($"Settings panel content.");
                    Im.Slider("Volume", ref sliderValue, 0, 1);
                    ImTabs.EndTab();
                }
                if (ImTabs.BeginTab(_tabNames[2]))
                {
                    Im.Label($"Advanced options.");
                    Im.Label($"For power users only!", Im.Style.TextSecondary);
                    ImTabs.EndTab();
                }

                ImTabs.End(ref _tabIndex);
            }
            Im.EndWindow();

            // === Window 8: Number & Range Input ===
            if (Im.BeginWindow("Number & Range", 380, 670, 340, 180))
            {
                Im.LabelText("Number Input");
                var numRect = ImLayout.AllocateRect(200, Im.Style.MinButtonHeight);
                ImNumberInput.DrawAt("Value", "num1", numRect.X, numRect.Y, 60, 140, ref _numberValue, 0, 100, 0.5f);

                var intRect = ImLayout.AllocateRect(200, Im.Style.MinButtonHeight);
                ImNumberInput.DrawIntAt("Int Val", "num2", intRect.X, intRect.Y, 60, 140, ref _intValue, 0, 100, 1);

                ImLayout.Space(8);
                Im.LabelText("Range Slider");
                var rangeRect = ImLayout.AllocateRect(280, Im.Style.MinButtonHeight + 20);
                ImRangeSlider.DrawAt("range1", rangeRect.X, rangeRect.Y, 280, ref _rangeMin, ref _rangeMax, 0, 100);
                Im.Label($"Range: {_rangeMin:F0} - {_rangeMax:F0}");
            }
            Im.EndWindow();

            // === Window 9: Color Picker ===
            if (Im.BeginWindow("Color Picker", 750, 670, 200, 280))
            {
                var pickerRect = ImLayout.AllocateRect(180, 240);
                ImColorPicker.DrawAt("picker1", pickerRect.X, pickerRect.Y, 180, ref _pickerColor);
            }
            Im.EndWindow();

            // === Window 10: Vector Input ===
            if (Im.BeginWindow("Vector Input", 980, 670, 280, 140))
            {
                Im.LabelText("Vector2");
                var vec2Rect = ImLayout.AllocateRect(240, Im.Style.MinButtonHeight);
                ImVectorInput.DrawAt("vec2", vec2Rect.X, vec2Rect.Y, 240, ref _vec2);

                Im.LabelText("Vector3");
                var vec3Rect = ImLayout.AllocateRect(240, Im.Style.MinButtonHeight);
                ImVectorInput.DrawAt("vec3", vec3Rect.X, vec3Rect.Y, 240, ref _vec3);
            }
            Im.EndWindow();

            // === Window 11: Breadcrumbs & Pagination ===
            if (Im.BeginWindow("Navigation", 1170, 310, 400, 140))
            {
                Im.LabelText("Breadcrumbs");
                var crumbRect = ImLayout.AllocateRect(380, ImBreadcrumbs.Height);
                int clicked = ImBreadcrumbs.Draw("crumbs", _breadcrumbPath, crumbRect.X, crumbRect.Y);
                if (clicked >= 0)
                {
                    Log.Debug("Breadcrumb clicked: {Index}", clicked);
                }

                ImLayout.Space(8);
                Im.LabelText("Pagination");
                var pageRect = ImLayout.AllocateRect(380, ImPagination.Height);
                if (ImPagination.Draw("pager", ref _currentPage, 10, pageRect.X, pageRect.Y))
                {
                    Log.Debug("Page changed: {Page}", _currentPage);
                }
            }
            Im.EndWindow();

            // === Window 12: Table ===
            if (Im.BeginWindow("Table Demo", 1170, 470, 400, 220))
            {
                var tableRect = ImLayout.AllocateRect(380, 180);
                ImTable.Begin("demo_table", tableRect.X, tableRect.Y, 380, 180);
                ImTable.Column("ID", 50, align: ImAlign.End);
                ImTable.Column("Name", 120);
                ImTable.Column("Value", 80, sortable: true, align: ImAlign.End);
                ImTable.Column("Active", 80, align: ImAlign.Center);
                ImTable.HeadersRow();

                foreach (var item in _tableItems)
                {
                    if (ImTable.BeginRow(item.Id))
                    {
                        ImTable.Cell(item.Id);
                        ImTable.Cell(item.Name);
                        ImTable.Cell(item.Value);
                        ImTable.Cell(item.Active ? "Yes" : "No");
                        ImTable.EndRow();
                    }
                }
                ImTable.End();
            }
            Im.EndWindow();

            // === Window 13: Tree ===
            if (Im.BeginWindow("Tree View", 30, 870, 320, 120))
            {
                var treeRect = ImLayout.AllocateRect(300, 100);
                ImTree.Begin("tree1", treeRect.X, treeRect.Y, treeRect.Width);
                if (ImTree.BeginNode("Root Folder", true))
                {
                    if (ImTree.BeginNode("Documents"))
                    {
                        ImTree.Leaf("Report.pdf");
                        ImTree.Leaf("Notes.txt");
                        ImTree.EndNode();
                    }
                    if (ImTree.BeginNode("Images"))
                    {
                        ImTree.Leaf("Photo.png");
                        ImTree.EndNode();
                    }
                    ImTree.EndNode();
                }
                ImTree.End();
            }
            Im.EndWindow();

            // === Window 14: Curve Editor ===
            if (Im.BeginWindow("Curve Editor", 380, 870, 340, 120))
            {
                var curveRect = ImLayout.AllocateRect(300, 80);
                if (ImCurveEditor.DrawAt("curve1", ref _curve, curveRect.X, curveRect.Y, 300, 80))
                {
                    Log.Debug("Curve modified");
                }
            }
            Im.EndWindow();

            // === Window 15: Modal Demo ===
            if (Im.BeginWindow("Modal Demo", 750, 870, 200, 100))
            {
                if (Im.Button("Show Modal", 150))
                {
                    ImModal.Open("confirm_modal");
                }
            }
            Im.EndWindow();

            // Draw modal if open
            if (ImModal.Begin("confirm_modal", 300, 150, "Confirm Action"))
            {
                Im.Label($"Are you sure you want to proceed?");
                ImLayout.Space(20);

                if (Im.Button("Yes", 80))
                {
                    Log.Debug("Modal: Yes clicked");
                    ImModal.Close();
                }
                ImLayout.Space(4);
                if (Im.Button("No", 80))
                {
                    Log.Debug("Modal: No clicked");
                    ImModal.Close();
                }

                ImModal.End();
            }

            // Mouse cursor indicator
            uint cursorColor = Im.MouseDown ? 0xFF0078D4u : 0xFF3391FFu;
            Im.DrawCircle(Im.MousePos.X, Im.MousePos.Y, 6, cursorColor);

            Im.End();

            Derp.RenderSdf();
            Derp.EndDrawing();

            // Update secondary viewports (for extracted windows)
            Im.UpdateSecondaryViewports(dt);

            AllocationTracker.EndFrame();
        }

        // Cleanup
        AllocationTracker.LogSummary();
        AllocationTracker.Disable();
        Derp.CloseWindow();
        Log.Information("=== Widget Zoo Complete ===");
    }
}
