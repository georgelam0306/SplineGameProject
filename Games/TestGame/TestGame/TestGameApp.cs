using DerpLib;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;
using DerpDocDatabase;
using Silk.NET.Input;
using System.Globalization;

namespace TestGame;

public sealed class TestGameApp : IDisposable
{
    private const float TargetDeltaTime = 1f / 60f;
    private const string InvalidWeaponButtonId = "##weapon_select_invalid";
    private const string RuntimeDemoIncreaseButtonId = "Increase Min Level##runtime_demo_inc";
    private const string RuntimeDemoDecreaseButtonId = "Decrease Min Level##runtime_demo_dec";
    private const string RuntimeDemoToggleSortButtonId = "Toggle Sort Direction##runtime_demo_toggle_sort";
    private const string RuntimeDemoResetButtonId = "Reset Runtime Defaults##runtime_demo_reset";
    private const string WeaponsBaseVariantButtonId = "Weapons Base##variant_weapons_base";
    private const string WeaponsHeroicVariantButtonId = "Weapons Heroic##variant_weapons_heroic";
    private const string FirstLinkedBaseVariantButtonId = "FirstLinked Base##variant_firstlinked_base";
    private const string FirstLinkedHeroicVariantButtonId = "FirstLinked Heroic##variant_firstlinked_heroic";

    private readonly GameDatabase _db;
    private string[] _weaponButtonIds = Array.Empty<string>();
    private string[] _weaponInventoryLines = Array.Empty<string>();
    private int _selectedWeaponKey = -1;
    private int _weaponsVariantId;
    private int _firstLinkedVariantId;
    private string _weaponsVariantStatusLine = string.Empty;
    private string _firstLinkedVariantStatusLine = string.Empty;
    private string _firstLinkedHeaderLine = string.Empty;
    private string[] _firstLinkedRowLines = Array.Empty<string>();

    private int _detailsWeaponKey = int.MinValue;
    private int _detailsTabIndex;
    private string _detailsHeaderLine = string.Empty;
    private string _detailsRequirementLine = string.Empty;
    private string _detailsDamageLine = string.Empty;
    private string _detailsSpeedLine = string.Empty;
    private string _detailsCritLine = string.Empty;
    private string _detailsDpsLine = string.Empty;
    private string _detailsElementLine = string.Empty;
    private string _detailsHandednessLine = string.Empty;
    private string _detailsValueLine = string.Empty;
    private string _detailsWeightLine = string.Empty;
    private string _detailsDescriptionLine = string.Empty;

    private int _runtimeDemoInstanceId = -1;
    private int _runtimeDemoVersion = int.MinValue;
    private string _runtimeDemoHeaderLine = string.Empty;
    private string _runtimeDemoThresholdLine = string.Empty;
    private string _runtimeDemoEffectiveThresholdLine = string.Empty;
    private string _runtimeDemoThresholdTextLine = string.Empty;
    private string _runtimeDemoSortDirectionLine = string.Empty;
    private string _runtimeDemoResolvedFilterLine = string.Empty;
    private string _runtimeDemoResolvedSortLine = string.Empty;
    private string _runtimeDemoResolvedColumnLine = string.Empty;
    private string _runtimeDemoEligibleRowsLine = string.Empty;
    private string _runtimeDemoVisibleRowsHeaderLine = string.Empty;
    private string[] _runtimeDemoVisibleRowLines = Array.Empty<string>();

    private bool _isDisposed;

    public TestGameApp(GameDatabase db)
    {
        _db = db;
        ResetRuntimeDemoState();
        UpdateVariantStatusLines();
    }

    public void Run()
    {
        Derp.InitWindow(1280, 720, "TestGame");
        Derp.InitSdf();

        var primaryFont = Derp.LoadFont("arial");
        Derp.SetSdfFontAtlas(primaryFont.Atlas);

        Im.Initialize(enableMultiViewport: false);
        Im.SetFont(primaryFont);

        _db.Reloaded += OnDatabaseReloaded;
        RebuildWeaponUiCaches();
        RebuildFirstLinkedUiCache();
        TrySelectFirstWeaponIfNeeded();

        try
        {
            while (!Derp.WindowShouldClose())
            {
                Derp.PollEvents();
                _db.Update();
                HandleGlobalShortcuts();
                TrySelectFirstWeaponIfNeeded();

                if (!Derp.BeginDrawing())
                {
                    continue;
                }

                Derp.ClearBackground(0.08f, 0.10f, 0.14f);
                Derp.SdfBuffer.Reset();

                Im.Begin(TargetDeltaTime);
                DrawUi();
                Im.End();

                Derp.RenderSdf();
                Derp.EndDrawing();
            }
        }
        finally
        {
            Derp.CloseWindow();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _db.Reloaded -= OnDatabaseReloaded;
        _db.Dispose();
    }

    private void HandleGlobalShortcuts()
    {
        if (Derp.IsKeyPressed(Key.F5))
        {
            _db.ReloadNow();
        }
    }

    private void TrySelectFirstWeaponIfNeeded()
    {
        ReadOnlySpan<Weapons> weaponRows = GetCurrentWeapons();
        if (weaponRows.Length == 0)
        {
            SetSelectedWeaponKey(-1);
            return;
        }

        if (_selectedWeaponKey < 0)
        {
            SetSelectedWeaponKey(GetWeaponKey(weaponRows[0]));
            return;
        }

        for (int weaponIndex = 0; weaponIndex < weaponRows.Length; weaponIndex++)
        {
            if (GetWeaponKey(weaponRows[weaponIndex]) == _selectedWeaponKey)
            {
                return;
            }
        }

        SetSelectedWeaponKey(GetWeaponKey(weaponRows[0]));
    }

    private void DrawUi()
    {
        float screenWidth = Derp.GetScreenWidth();
        float screenHeight = Derp.GetScreenHeight();
        float margin = 16f;
        float statusHeight = 108f;
        float panelTop = margin + statusHeight + 10f;
        float panelHeight = screenHeight - panelTop - margin;
        float inventoryWidth = MathF.Max(340f, (screenWidth - margin * 3f) * 0.42f);
        float detailsWidth = screenWidth - margin * 3f - inventoryWidth;

        DrawStatusWindow(margin, margin, screenWidth - margin * 2f, statusHeight);
        DrawInventoryWindow(margin, panelTop, inventoryWidth, panelHeight);
        DrawDetailsWindow(margin * 2f + inventoryWidth, panelTop, detailsWidth, panelHeight);
    }

    private void DrawStatusWindow(float x, float y, float width, float height)
    {
        if (!Im.BeginWindow("Database Status", x, y, width, height))
        {
            Im.EndWindow();
            return;
        }

        var contentRect = Im.WindowContentRect;
        float textX = contentRect.X + 8f;
        float lineY = contentRect.Y + 6f;

        Im.Text("TestGame live data source".AsSpan(), textX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += 22f;

        Im.Text(_db.SourceText.AsSpan(), textX, lineY, Im.Style.FontSize - 1f, Im.Style.TextPrimary);
        lineY += 18f;

        Im.Text(_db.StatusText.AsSpan(), textX, lineY, Im.Style.FontSize - 1f, Im.Style.TextPrimary);
        lineY += 18f;

        if (!string.IsNullOrWhiteSpace(_db.LastErrorText))
        {
            Im.Text(_db.LastErrorText.AsSpan(), textX, lineY, Im.Style.FontSize - 1f, Im.Style.Secondary);
            lineY += 18f;
        }

        Im.Text(_weaponsVariantStatusLine.AsSpan(), textX, lineY, Im.Style.FontSize - 1f, Im.Style.TextPrimary);
        lineY += 18f;

        Im.Text(_firstLinkedVariantStatusLine.AsSpan(), textX, lineY, Im.Style.FontSize - 1f, Im.Style.TextPrimary);
        lineY += 20f;

        float buttonWidth = 92f;
        float buttonHeight = 24f;
        if (Im.Button(WeaponsBaseVariantButtonId, textX, lineY, buttonWidth, buttonHeight))
        {
            TrySetWeaponsVariant(0);
        }

        if (Im.Button(WeaponsHeroicVariantButtonId, textX + buttonWidth + 8f, lineY, buttonWidth, buttonHeight))
        {
            TrySetWeaponsVariant(1);
        }

        float secondRowY = lineY + buttonHeight + 6f;
        if (Im.Button(FirstLinkedBaseVariantButtonId, textX, secondRowY, buttonWidth, buttonHeight))
        {
            TrySetFirstLinkedVariant(0);
        }

        if (Im.Button(FirstLinkedHeroicVariantButtonId, textX + buttonWidth + 8f, secondRowY, buttonWidth, buttonHeight))
        {
            TrySetFirstLinkedVariant(1);
        }

        Im.EndWindow();
    }

    private void DrawInventoryWindow(float x, float y, float width, float height)
    {
        if (!Im.BeginWindow("Inventory", x, y, width, height))
        {
            Im.EndWindow();
            return;
        }

        var contentRect = Im.WindowContentRect;
        ReadOnlySpan<Weapons> weaponRows = GetCurrentWeapons();
        if (weaponRows.Length == 0)
        {
            float emptyTextY = contentRect.Y + 12f;
            Im.Text("No weapons loaded.".AsSpan(), contentRect.X + 8f, emptyTextY, Im.Style.FontSize, Im.Style.TextSecondary);
            Im.Text("Open Derp.Doc and export the Weapons table.".AsSpan(), contentRect.X + 8f, emptyTextY + 20f, Im.Style.FontSize - 1f, Im.Style.TextSecondary);
            Im.EndWindow();
            return;
        }

        float rowX = contentRect.X + 6f;
        float rowY = contentRect.Y + 8f;
        float rowWidth = contentRect.Width - 12f;
        float rowHeight = 26f;
        float rowGap = 4f;

        for (int weaponIndex = 0; weaponIndex < weaponRows.Length; weaponIndex++)
        {
            ref readonly Weapons weapon = ref weaponRows[weaponIndex];
            int weaponKey = GetWeaponKey(weapon);
            bool isSelected = weaponKey == _selectedWeaponKey;
            uint rowColor = isSelected
                ? ImStyle.WithAlpha(Im.Style.Primary, 105)
                : ImStyle.WithAlpha(Im.Style.Surface, 110);

            Im.DrawRect(rowX, rowY, rowWidth, rowHeight, rowColor);
            if (Im.Button(GetWeaponButtonId(weaponIndex), rowX, rowY, rowWidth, rowHeight))
            {
                SetSelectedWeaponKey(weaponKey);
            }

            float textY = rowY + (rowHeight - Im.Style.FontSize) * 0.5f;
            uint textColor = isSelected ? Im.Style.TextPrimary : Im.Style.TextSecondary;
            string inventoryLine = GetWeaponInventoryLine(weaponIndex);
            Im.Text(inventoryLine.AsSpan(), rowX + 8f, textY, Im.Style.FontSize, textColor);

            rowY += rowHeight + rowGap;
            if (rowY > contentRect.Bottom - rowHeight)
            {
                break;
            }
        }

        Im.EndWindow();
    }

    private void DrawDetailsWindow(float x, float y, float width, float height)
    {
        if (!Im.BeginWindow("Details", x, y, width, height))
        {
            Im.EndWindow();
            return;
        }

        var contentRect = Im.WindowContentRect;
        if (!ImTabs.Begin("testgame_details_tabs", contentRect.X + 4f, contentRect.Y + 4f, contentRect.Width - 8f, ref _detailsTabIndex))
        {
            Im.EndWindow();
            return;
        }

        float tabContentX = contentRect.X + 8f;
        float tabContentY = ImTabs.GetContentY() + 4f;

        if (ImTabs.BeginTab("Item Stats"))
        {
            DrawItemStatsTab(tabContentX, tabContentY);
            ImTabs.EndTab();
        }

        if (ImTabs.BeginTab("Runtime Vars"))
        {
            DrawRuntimeVariablesTab(tabContentX, tabContentY, contentRect.Width - 16f);
            ImTabs.EndTab();
        }

        if (ImTabs.BeginTab("Schema Linked"))
        {
            DrawSchemaLinkedTab(tabContentX, tabContentY);
            ImTabs.EndTab();
        }

        ImTabs.End(ref _detailsTabIndex);
        Im.EndWindow();
    }

    private void DrawSchemaLinkedTab(float contentX, float contentY)
    {
        float lineY = contentY;
        float lineStep = 20f;

        Im.Text(_firstLinkedHeaderLine.AsSpan(), contentX, lineY, Im.Style.FontSize + 1f, Im.Style.TextPrimary);
        lineY += lineStep;

        for (int rowLineIndex = 0; rowLineIndex < _firstLinkedRowLines.Length; rowLineIndex++)
        {
            Im.Text(_firstLinkedRowLines[rowLineIndex].AsSpan(), contentX, lineY, Im.Style.FontSize, Im.Style.TextSecondary);
            lineY += lineStep;
        }
    }

    private void DrawItemStatsTab(float textX, float contentY)
    {
        EnsureSelectedWeaponDetailsCache();
        if (string.IsNullOrWhiteSpace(_detailsHeaderLine))
        {
            Im.Text("Select an inventory item to inspect stats.".AsSpan(), textX, contentY + 2f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        float lineY = contentY;
        float lineStep = 20f;

        Im.Text(_detailsHeaderLine.AsSpan(), textX, lineY, Im.Style.FontSize + 1f, Im.Style.TextPrimary);
        lineY += lineStep + 4f;

        Im.Text(_detailsRequirementLine.AsSpan(), textX, lineY, Im.Style.FontSize, Im.Style.TextSecondary);
        lineY += lineStep;
        Im.Text(_detailsDamageLine.AsSpan(), textX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep;
        Im.Text(_detailsSpeedLine.AsSpan(), textX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep;
        Im.Text(_detailsCritLine.AsSpan(), textX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep;
        Im.Text(_detailsDpsLine.AsSpan(), textX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep + 6f;
        Im.Text(_detailsElementLine.AsSpan(), textX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep;
        Im.Text(_detailsHandednessLine.AsSpan(), textX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep;
        Im.Text(_detailsValueLine.AsSpan(), textX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep;
        Im.Text(_detailsWeightLine.AsSpan(), textX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep + 6f;
        Im.Text(_detailsDescriptionLine.AsSpan(), textX, lineY, Im.Style.FontSize, Im.Style.TextSecondary);
    }

    private void DrawRuntimeVariablesTab(float contentX, float contentY, float contentWidth)
    {
        if (!TryGetRuntimeDemoInstance(out RuntimeVariableDemoInstance runtimeDemoInstance))
        {
            Im.Text("Runtime database is not loaded yet.".AsSpan(), contentX, contentY + 2f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        EnsureRuntimeDemoCache(runtimeDemoInstance);

        float lineY = contentY;
        float lineStep = 18f;
        Im.Text(_runtimeDemoHeaderLine.AsSpan(), contentX, lineY, Im.Style.FontSize + 1f, Im.Style.TextPrimary);
        lineY += lineStep + 2f;
        Im.Text(_runtimeDemoThresholdLine.AsSpan(), contentX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep;
        Im.Text(_runtimeDemoEffectiveThresholdLine.AsSpan(), contentX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep;
        Im.Text(_runtimeDemoThresholdTextLine.AsSpan(), contentX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep;
        Im.Text(_runtimeDemoSortDirectionLine.AsSpan(), contentX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep + 4f;
        Im.Text(_runtimeDemoResolvedFilterLine.AsSpan(), contentX, lineY, Im.Style.FontSize, Im.Style.TextSecondary);
        lineY += lineStep;
        Im.Text(_runtimeDemoResolvedSortLine.AsSpan(), contentX, lineY, Im.Style.FontSize, Im.Style.TextSecondary);
        lineY += lineStep;
        Im.Text(_runtimeDemoResolvedColumnLine.AsSpan(), contentX, lineY, Im.Style.FontSize, Im.Style.TextSecondary);
        lineY += lineStep;
        Im.Text(_runtimeDemoEligibleRowsLine.AsSpan(), contentX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep;
        Im.Text(_runtimeDemoVisibleRowsHeaderLine.AsSpan(), contentX, lineY, Im.Style.FontSize, Im.Style.TextPrimary);
        lineY += lineStep;

        for (int rowLineIndex = 0; rowLineIndex < _runtimeDemoVisibleRowLines.Length; rowLineIndex++)
        {
            Im.Text(_runtimeDemoVisibleRowLines[rowLineIndex].AsSpan(), contentX, lineY, Im.Style.FontSize, Im.Style.TextSecondary);
            lineY += lineStep;
        }

        lineY += 8f;

        float buttonWidth = (contentWidth - 12f) * 0.5f;
        float buttonHeight = 26f;
        if (Im.Button(RuntimeDemoIncreaseButtonId, contentX, lineY, buttonWidth, buttonHeight))
        {
            SetRuntimeDemoThreshold(runtimeDemoInstance, runtimeDemoInstance.Vars.MinLevel + 1d);
        }

        if (Im.Button(RuntimeDemoDecreaseButtonId, contentX + buttonWidth + 12f, lineY, buttonWidth, buttonHeight))
        {
            SetRuntimeDemoThreshold(runtimeDemoInstance, runtimeDemoInstance.Vars.MinLevel - 1d);
        }

        lineY += buttonHeight + 8f;
        if (Im.Button(RuntimeDemoToggleSortButtonId, contentX, lineY, buttonWidth, buttonHeight))
        {
            runtimeDemoInstance.Vars.SortDesc = !runtimeDemoInstance.Vars.SortDesc;
            _runtimeDemoVersion = int.MinValue;
        }

        if (Im.Button(RuntimeDemoResetButtonId, contentX + buttonWidth + 12f, lineY, buttonWidth, buttonHeight))
        {
            runtimeDemoInstance.ResetVariablesToDefaults();
            _runtimeDemoVersion = int.MinValue;
        }
    }

    private static int GetWeaponKey(in Weapons weapon)
    {
        return weapon.WeaponIndex.ToInt();
    }

    private ReadOnlySpan<Weapons> GetCurrentWeapons()
    {
        if (!_db.HasData)
        {
            return ReadOnlySpan<Weapons>.Empty;
        }

        if (_db.TryGetWeaponsVariant(_weaponsVariantId, out var table))
        {
            return table.All;
        }

        _weaponsVariantId = 0;
        return _db.Weapons.All;
    }

    private bool TryGetRuntimeDemoInstance(out RuntimeVariableDemoInstance runtimeDemoInstance)
    {
        runtimeDemoInstance = default;
        if (!_db.HasData)
        {
            return false;
        }

        RuntimeVariableDemoRuntime runtime = _db.RuntimeVariableDemoRuntime;
        if (_runtimeDemoInstanceId < 0 || _runtimeDemoInstanceId >= runtime.InstanceCount)
        {
            RuntimeVariableDemoInstance createdInstance = runtime.CreateInstance();
            _runtimeDemoInstanceId = createdInstance.Id;
            _runtimeDemoVersion = int.MinValue;
        }

        runtimeDemoInstance = runtime.GetInstance(_runtimeDemoInstanceId);
        return true;
    }

    private void EnsureRuntimeDemoCache(in RuntimeVariableDemoInstance runtimeDemoInstance)
    {
        int version = _db.RuntimeVariableDemoRuntime.Version;
        if (_runtimeDemoVersion == version)
        {
            return;
        }

        _runtimeDemoVersion = version;
        double minimumLevel = runtimeDemoInstance.Vars.MinLevel;
        double effectiveMinimumLevel = runtimeDemoInstance.Vars.EffectiveMinLevel;
        string minimumLevelText = runtimeDemoInstance.Vars.MinLevelText;
        bool isSortDescending = runtimeDemoInstance.Vars.SortDesc;
        string levelColumnId = runtimeDemoInstance.Vars.LevelColumnId;
        string resolvedSortColumnValue = levelColumnId;
        string resolvedFilterValue = minimumLevelText;
        bool resolvedSortDescending = isSortDescending;
        string resolvedSortValue = resolvedSortDescending ? "true" : "false";
        string resolvedColumnValue = levelColumnId;

        ReadOnlySpan<RuntimeVariableDemo> rows = _db.RuntimeVariableDemo.All;
        bool isFilterColumnBoundToLevel = string.Equals(resolvedColumnValue, levelColumnId, StringComparison.Ordinal);
        bool isSortColumnBoundToLevel = string.Equals(resolvedSortColumnValue, levelColumnId, StringComparison.Ordinal);
        int comparisonThreshold = ConvertToIntFloor(effectiveMinimumLevel);
        if (int.TryParse(resolvedFilterValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedThreshold))
        {
            comparisonThreshold = parsedThreshold;
        }

        var matchedRowIndices = new int[rows.Length];
        int matchedRowCount = 0;
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            bool passesFilter = true;
            if (isFilterColumnBoundToLevel)
            {
                passesFilter = rows[rowIndex].Level > comparisonThreshold;
            }

            if (!passesFilter)
            {
                continue;
            }

            matchedRowIndices[matchedRowCount] = rowIndex;
            matchedRowCount++;
        }

        if (isSortColumnBoundToLevel)
        {
            for (int outerIndex = 1; outerIndex < matchedRowCount; outerIndex++)
            {
                int candidateRowIndex = matchedRowIndices[outerIndex];
                int scanIndex = outerIndex - 1;
                while (scanIndex >= 0 && CompareRuntimeDemoRows(rows[matchedRowIndices[scanIndex]], rows[candidateRowIndex], resolvedSortDescending) > 0)
                {
                    matchedRowIndices[scanIndex + 1] = matchedRowIndices[scanIndex];
                    scanIndex--;
                }

                matchedRowIndices[scanIndex + 1] = candidateRowIndex;
            }
        }

        _runtimeDemoHeaderLine = "RuntimeVariableDemo instance #" + runtimeDemoInstance.Id.ToString(CultureInfo.InvariantCulture);
        _runtimeDemoThresholdLine = "min_level = " + FormatNumber(minimumLevel);
        _runtimeDemoEffectiveThresholdLine = "effective_min_level = " + FormatNumber(effectiveMinimumLevel);
        _runtimeDemoThresholdTextLine = "min_level_text = \"" + minimumLevelText + "\"";
        _runtimeDemoSortDirectionLine = "sort_desc = " + (isSortDescending ? "true" : "false");
        _runtimeDemoResolvedFilterLine = "bound filter value = \"" + resolvedFilterValue + "\"";
        _runtimeDemoResolvedSortLine = "bound sort descending = " + resolvedSortValue + ", bound sort column id = \"" + resolvedSortColumnValue + "\"";
        _runtimeDemoResolvedColumnLine = "bound filter column id = \"" + resolvedColumnValue + "\"";
        _runtimeDemoEligibleRowsLine = "matching rows: " + matchedRowCount.ToString(CultureInfo.InvariantCulture);

        int visibleRowCount = Math.Min(matchedRowCount, 6);
        if (visibleRowCount <= 0)
        {
            _runtimeDemoVisibleRowsHeaderLine = "Visible rows (sorted + filtered): none";
            _runtimeDemoVisibleRowLines = Array.Empty<string>();
            return;
        }

        _runtimeDemoVisibleRowsHeaderLine = "Visible rows (sorted + filtered):";
        var visibleRowLines = new string[visibleRowCount];
        for (int visibleRowIndex = 0; visibleRowIndex < visibleRowCount; visibleRowIndex++)
        {
            RuntimeVariableDemo row = rows[matchedRowIndices[visibleRowIndex]];
            visibleRowLines[visibleRowIndex] = "#" +
                row.Id.ToString(CultureInfo.InvariantCulture) +
                " " + row.Label.ToString() +
                " (Level " + row.Level.ToString(CultureInfo.InvariantCulture) + ")";
        }

        _runtimeDemoVisibleRowLines = visibleRowLines;
    }

    private void SetRuntimeDemoThreshold(in RuntimeVariableDemoInstance runtimeDemoInstance, double nextThreshold)
    {
        if (nextThreshold < 0d)
        {
            nextThreshold = 0d;
        }

        runtimeDemoInstance.Vars.MinLevel = nextThreshold;
        runtimeDemoInstance.Vars.MinLevelText = ConvertToIntFloor(nextThreshold).ToString(CultureInfo.InvariantCulture);
        _runtimeDemoVersion = int.MinValue;
    }

    private void OnDatabaseReloaded()
    {
        RebuildWeaponUiCaches();
        RebuildFirstLinkedUiCache();
        ResetRuntimeDemoState();
        UpdateVariantStatusLines();

        if (_selectedWeaponKey >= 0 &&
            (!_db.TryGetWeaponsVariant(_weaponsVariantId, out var weaponsTable) || !weaponsTable.TryFindById(_selectedWeaponKey, out _)))
        {
            SetSelectedWeaponKey(-1);
        }

        InvalidateSelectedWeaponDetailsCache();
    }

    private void RebuildWeaponUiCaches()
    {
        ReadOnlySpan<Weapons> weaponRows = GetCurrentWeapons();
        EnsureWeaponButtonIds(weaponRows.Length);
        RebuildWeaponInventoryLines(weaponRows);
        InvalidateSelectedWeaponDetailsCache();
    }

    private void RebuildFirstLinkedUiCache()
    {
        ReadOnlySpan<FirstLinked> rows = GetCurrentFirstLinkedRows();
        if (rows.Length <= 0)
        {
            _firstLinkedHeaderLine = "FirstLinked rows: none";
            _firstLinkedRowLines = Array.Empty<string>();
            return;
        }

        _firstLinkedHeaderLine = "FirstLinked rows (" + rows.Length.ToString(CultureInfo.InvariantCulture) + "):";
        var rowLines = new string[rows.Length];
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            rowLines[rowIndex] = "#" +
                (rowIndex + 1).ToString(CultureInfo.InvariantCulture) +
                " [RowId=" +
                rows[rowIndex].RowId.ToInt().ToString(CultureInfo.InvariantCulture) +
                "] " +
                rows[rowIndex].Name.ToString();
        }

        _firstLinkedRowLines = rowLines;
    }

    private void RebuildWeaponInventoryLines(ReadOnlySpan<Weapons> weaponRows)
    {
        if (weaponRows.Length == 0)
        {
            _weaponInventoryLines = Array.Empty<string>();
            return;
        }

        var inventoryLines = new string[weaponRows.Length];
        for (int weaponIndex = 0; weaponIndex < weaponRows.Length; weaponIndex++)
        {
            ref readonly Weapons weapon = ref weaponRows[weaponIndex];
            string weaponName = weapon.Name.ToString();
            string weaponRarity = weapon.Rarity.ToString();
            inventoryLines[weaponIndex] = weaponName + " [" + weaponRarity + "]";
        }

        _weaponInventoryLines = inventoryLines;
    }

    private void EnsureWeaponButtonIds(int weaponCount)
    {
        if (_weaponButtonIds.Length == weaponCount)
        {
            return;
        }

        var buttonIds = new string[weaponCount];
        for (int weaponIndex = 0; weaponIndex < weaponCount; weaponIndex++)
        {
            buttonIds[weaponIndex] = "##weapon_select_" + (weaponIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        _weaponButtonIds = buttonIds;
    }

    private string GetWeaponButtonId(int weaponIndex)
    {
        if ((uint)weaponIndex >= (uint)_weaponButtonIds.Length)
        {
            return InvalidWeaponButtonId;
        }

        return _weaponButtonIds[weaponIndex];
    }

    private string GetWeaponInventoryLine(int weaponIndex)
    {
        if ((uint)weaponIndex >= (uint)_weaponInventoryLines.Length)
        {
            return string.Empty;
        }

        return _weaponInventoryLines[weaponIndex];
    }

    private void SetSelectedWeaponKey(int weaponKey)
    {
        if (_selectedWeaponKey == weaponKey)
        {
            return;
        }

        _selectedWeaponKey = weaponKey;
        InvalidateSelectedWeaponDetailsCache();
    }

    private void InvalidateSelectedWeaponDetailsCache()
    {
        _detailsWeaponKey = int.MinValue;
    }

    private void EnsureSelectedWeaponDetailsCache()
    {
        if (_detailsWeaponKey == _selectedWeaponKey)
        {
            return;
        }

        RebuildSelectedWeaponDetailsCache();
    }

    private void RebuildSelectedWeaponDetailsCache()
    {
        _detailsWeaponKey = _selectedWeaponKey;

        if (_selectedWeaponKey < 0 ||
            !_db.HasData ||
            !_db.TryGetWeaponsVariant(_weaponsVariantId, out var weaponsTable) ||
            !weaponsTable.TryFindById(_selectedWeaponKey, out var selectedWeapon))
        {
            ClearSelectedWeaponDetails();
            return;
        }

        int levelRequired = selectedWeapon.LevelRequired.ToInt();
        double minDamage = selectedWeapon.MinDamage.ToDouble();
        double maxDamage = selectedWeapon.MaxDamage.ToDouble();
        double attackSpeed = selectedWeapon.AttackSpeed.ToDouble();
        double critChancePercent = selectedWeapon.CritChance.ToDouble() * 100d;
        double dps = selectedWeapon.Dps.ToDouble();
        double valueGold = selectedWeapon.ValueGold.ToDouble();
        double weight = selectedWeapon.Weight.ToDouble();
        bool isTwoHanded = selectedWeapon.TwoHanded != 0;

        if (Math.Abs(dps) < 0.000001d && attackSpeed > 0d)
        {
            dps = ((minDamage + maxDamage) * 0.5d) * attackSpeed;
        }

        string weaponId = selectedWeapon.Id.ToString();
        string weaponName = selectedWeapon.Name.ToString();
        string weaponCategory = selectedWeapon.Category.ToString();
        string weaponElement = selectedWeapon.Element.ToString();

        _detailsHeaderLine = weaponId + " - " + weaponName;
        _detailsRequirementLine = "Requires level " +
            levelRequired.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " (" + weaponCategory + ")";
        _detailsDamageLine = "Damage: " + FormatNumber(minDamage) + " - " + FormatNumber(maxDamage);
        _detailsSpeedLine = "Attack speed: " + FormatNumber(attackSpeed);
        _detailsCritLine = "Crit chance: " + FormatNumber(critChancePercent) + "%";
        _detailsDpsLine = "DPS: " + FormatNumber(dps);
        _detailsElementLine = "Element: " + weaponElement;
        _detailsHandednessLine = isTwoHanded ? "Two-handed weapon" : "One-handed weapon";
        _detailsValueLine = "Value: " + FormatNumber(valueGold) + " gold";
        _detailsWeightLine = "Weight: " + FormatNumber(weight);
        _detailsDescriptionLine = "Description: " + selectedWeapon.Description.ToString();
    }

    private void ClearSelectedWeaponDetails()
    {
        _detailsHeaderLine = string.Empty;
        _detailsRequirementLine = string.Empty;
        _detailsDamageLine = string.Empty;
        _detailsSpeedLine = string.Empty;
        _detailsCritLine = string.Empty;
        _detailsDpsLine = string.Empty;
        _detailsElementLine = string.Empty;
        _detailsHandednessLine = string.Empty;
        _detailsValueLine = string.Empty;
        _detailsWeightLine = string.Empty;
        _detailsDescriptionLine = string.Empty;
    }

    private void ResetRuntimeDemoState()
    {
        _runtimeDemoInstanceId = -1;
        _runtimeDemoVersion = int.MinValue;
        _runtimeDemoHeaderLine = string.Empty;
        _runtimeDemoThresholdLine = string.Empty;
        _runtimeDemoEffectiveThresholdLine = string.Empty;
        _runtimeDemoThresholdTextLine = string.Empty;
        _runtimeDemoSortDirectionLine = string.Empty;
        _runtimeDemoResolvedFilterLine = string.Empty;
        _runtimeDemoResolvedSortLine = string.Empty;
        _runtimeDemoResolvedColumnLine = string.Empty;
        _runtimeDemoEligibleRowsLine = string.Empty;
        _runtimeDemoVisibleRowsHeaderLine = string.Empty;
        _runtimeDemoVisibleRowLines = Array.Empty<string>();
    }

    private static int ConvertToIntFloor(double value)
    {
        return (int)Math.Floor(value);
    }

    private static int CompareRuntimeDemoRows(in RuntimeVariableDemo left, in RuntimeVariableDemo right, bool sortDescending)
    {
        int levelComparison = left.Level.CompareTo(right.Level);
        if (levelComparison == 0)
        {
            levelComparison = left.Id.CompareTo(right.Id);
        }

        if (sortDescending)
        {
            return -levelComparison;
        }

        return levelComparison;
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private ReadOnlySpan<FirstLinked> GetCurrentFirstLinkedRows()
    {
        if (!_db.HasData)
        {
            return ReadOnlySpan<FirstLinked>.Empty;
        }

        if (_db.TryGetFirstLinkedVariant(_firstLinkedVariantId, out var table))
        {
            return table.All;
        }

        _firstLinkedVariantId = 0;
        return _db.FirstLinked.All;
    }

    private void TrySetWeaponsVariant(int variantId)
    {
        if (_weaponsVariantId == variantId)
        {
            return;
        }

        _weaponsVariantId = variantId;
        RebuildWeaponUiCaches();
        UpdateVariantStatusLines();
        TrySelectFirstWeaponIfNeeded();
    }

    private void TrySetFirstLinkedVariant(int variantId)
    {
        if (_firstLinkedVariantId == variantId)
        {
            return;
        }

        _firstLinkedVariantId = variantId;
        RebuildFirstLinkedUiCache();
        UpdateVariantStatusLines();
    }

    private void UpdateVariantStatusLines()
    {
        _weaponsVariantStatusLine = "Weapons variant: " +
            ResolveVariantName(GameDatabase.WeaponsVariants, _weaponsVariantId) +
            " (v" +
            _weaponsVariantId.ToString(CultureInfo.InvariantCulture) +
            ")";

        _firstLinkedVariantStatusLine = "FirstLinked variant: " +
            ResolveVariantName(GameDatabase.FirstLinkedVariants, _firstLinkedVariantId) +
            " (v" +
            _firstLinkedVariantId.ToString(CultureInfo.InvariantCulture) +
            ")";
    }

    private static string ResolveVariantName(ReadOnlySpan<DerpDocTableVariantInfo> variants, int variantId)
    {
        for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
        {
            if (variants[variantIndex].Id == variantId)
            {
                return variants[variantIndex].Name;
            }
        }

        return variantId == 0 ? "Base" : "Unknown";
    }
}
