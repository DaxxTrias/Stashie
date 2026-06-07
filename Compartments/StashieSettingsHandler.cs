using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared.Enums;
using ImGuiNET;
using Stashie.Classes;
using static Stashie.StashieCore;
using Vector2N = System.Numerics.Vector2;
using Vector4N = System.Numerics.Vector4;

namespace Stashie.Compartments;

public class StashieSettingsHandler
{
    private const int InventoryRows = 5;
    private const int MainInventoryColumns = 12;

    public static void SaveIgnoredSlotsFromInventoryTemplate(StashieCore stashie)
    {
        stashie.Settings.IgnoredCells = new int[InventoryRows, MainInventoryColumns];

        try
        {
            // Player Inventory
            var inventory_server =
                stashie.GameController.IngameState.Data.ServerData.PlayerInventories[(int)InventorySlotE.MainInventory1];
            UpdateIgnoredCells(inventory_server, stashie.Settings.IgnoredCells);
        }
        catch (Exception e)
        {
            stashie.LogError($"{e}", 5);
        }
    }

    private static void UpdateIgnoredCells(InventoryHolder server_items, int[,] ignoredCells)
    {
        foreach (var item in server_items.Inventory.InventorySlotItems)
        {
            var baseC = item.Item.GetComponent<Base>();
            var itemSizeX = baseC.ItemCellsSizeX;
            var itemSizeY = baseC.ItemCellsSizeY;
            var inventPosX = item.PosX;
            var inventPosY = item.PosY;
            for (var y = 0; y < itemSizeY; y++)
            for (var x = 0; x < itemSizeX; x++)
                ignoredCells[y + inventPosY, x + inventPosX] = 1;
        }
    }

    public static void GenerateTabMenu()
    {
        if (Main == null)
            return;

        GenerateTabMenu(Main);
    }

    public static void GenerateTabMenu(StashieCore stashie)
    {
        RenamedAllStashNames = GetStashNames(stashie);
        stashie.StashTabNamesByIndex = [.. RenamedAllStashNames];

        stashie.FilterTabs = null;

        if (stashie.currentFilter == null || stashie.currentFilter.Count == 0)
            return;

        foreach (var parent in stashie.currentFilter)
            stashie.FilterTabs += () =>
            {
                ImGui.TextColored(new Vector4N(0f, 1f, 0.022f, 1f), parent.ParentMenuName);
                ImGui.SameLine();

                var deleteCategoryPopupId = $"Delete category?##{parent.ParentMenuName}";
                if (ImGui.Button($"Delete Category##{parent.ParentMenuName}"))
                    ImGui.OpenPopup(deleteCategoryPopupId);

                if (StashieEditorHandler.ShowButtonPopup(deleteCategoryPopupId, [$"Delete '{parent.ParentMenuName}'?", "Cancel"], out var deleteCategoryIndex) &&
                    deleteCategoryIndex == 0)
                    DeleteFilterCategory(stashie, parent.ParentMenuName);

                var filterButtonWidth = GetFilterButtonWidth(parent);

                foreach (var filter in parent.Filters)
                    if (stashie.Settings.CustomFilterOptions.TryGetValue(parent.ParentMenuName + filter.FilterName,
                            out var indexNode))
                    {
                        var filterName = string.IsNullOrWhiteSpace(filter.FilterName) ? "Null" : filter.FilterName;
                        var strId = $"{filterName}##{parent.ParentMenuName + filter.FilterName}";

                        ImGui.Columns(2, strId, true);
                        ImGui.SetColumnWidth(0, filterButtonWidth + 20);

                        if (ImGui.Button(strId, new Vector2N(filterButtonWidth, 20)))
                            ImGui.OpenPopup(strId);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip(filterName);

                        ImGui.SameLine();
                        ImGui.NextColumn();

                        var item = Math.Clamp(indexNode.Index + 1, 0, stashie.StashTabNamesByIndex.Length - 1);

                        if (ImGui.Combo($"##{parent.ParentMenuName + filter.FilterName}", ref item,
                                stashie.StashTabNamesByIndex, stashie.StashTabNamesByIndex.Length))
                        {
                            indexNode.Value = stashie.StashTabNamesByIndex[item];
                            StashTabNameCoRoutine.OnSettingsStashNameChanged(indexNode,
                                stashie.StashTabNamesByIndex[item]);
                        }

                        var specialTag = "";

                        if (filter.Shifting != null && (bool)filter.Shifting) specialTag += "Holds Shift";

                        if (filter.Affinity != null && (bool)filter.Affinity)
                            specialTag += !string.IsNullOrEmpty(specialTag) ? ", Expects Affinity" : "Expects Affinity";

                        ImGui.SameLine();
                        ImGui.Text($"{specialTag}");

                        ImGui.NextColumn();
                        ImGui.Columns(1, "", false);
                        var pop = true;

                        if (!ImGui.BeginPopupModal(strId, ref pop,
                                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
                            continue;

                        var x = 0;

                        foreach (var name in stashie.StashTabNamesByIndex)
                        {
                            x++;

                            if (ImGui.Button($"{name}", new Vector2N(100, 20)))
                            {
                                indexNode.Value = name;
                                StashTabNameCoRoutine.OnSettingsStashNameChanged(indexNode, name);
                                ImGui.CloseCurrentPopup();
                            }

                            if (x % 10 != 0)
                                ImGui.SameLine();
                        }

                        ImGui.Spacing();
                        ImGuiNative.igIndent(350);
                        if (ImGui.Button("Close", new Vector2N(100, 20)))
                            ImGui.CloseCurrentPopup();

                        ImGui.EndPopup();
                    }
                    else
                    {
                        indexNode = new ListIndexNode { Value = "Ignore", Index = -1 };
                    }
            };
    }

    private static float GetFilterButtonWidth(Stashie.Filter.CustomFilter parent)
    {
        var width = 300f;
        foreach (var filter in parent.Filters)
        {
            var filterName = string.IsNullOrWhiteSpace(filter.FilterName) ? "Null" : filter.FilterName;
            width = Math.Max(width, ImGui.CalcTextSize(filterName).X + 48f);
        }

        return width;
    }

    private static void DeleteFilterCategory(StashieCore stashie, string categoryName)
    {
        var filterFileName = stashie.Settings.FilterFile.Value;
        if (string.IsNullOrWhiteSpace(filterFileName))
        {
            stashie.LogError("No filter file selected to delete category from.", 5);
            return;
        }

        if (!FileManager.TryLoadFile<FilterEditor.FilterParent>(
                filterFileName,
                ".json",
                loadedFilter =>
                {
                    loadedFilter.ParentMenu ??= [];
                    var removedCount = loadedFilter.ParentMenu.RemoveAll(
                        parentMenu => string.Equals(parentMenu.MenuName, categoryName, StringComparison.Ordinal));

                    if (removedCount == 0)
                    {
                        stashie.LogError($"Category '{categoryName}' was not found in {filterFileName}.json.", 5);
                        return;
                    }

                    stashie.Settings.CurrentFilterOptions = loadedFilter;
                    FileManager.SaveToFile(loadedFilter, filterFileName);
                    FilterManager.LoadCustomFilters(stashie);
                    GenerateTabMenu(stashie);
                    DebugWindow.LogMsg($"Deleted Stashie category '{categoryName}' and reloaded config.", 5, Color.LimeGreen);
                }))
            stashie.LogError($"Failed to load {filterFileName}.json while deleting category '{categoryName}'.", 5);
    }

    private static List<string> GetStashNames(StashieCore stashie)
    {
        if (RenamedAllStashNames is { Count: > 0 })
            return RenamedAllStashNames;

        if (stashie.Settings.AllStashNames is { Count: > 0 })
        {
            var stashNames = new List<string>(stashie.Settings.AllStashNames.Count + 1);
            if (!string.Equals(stashie.Settings.AllStashNames[0], "Ignore", StringComparison.Ordinal))
                stashNames.Add("Ignore");

            stashNames.AddRange(stashie.Settings.AllStashNames);
            return stashNames;
        }

        return ["Ignore"];
    }

    public static void DrawReloadConfigButton(StashieCore stashie)
    {
        if (!ImGui.Button("Reload config"))
            return;

        FilterManager.LoadCustomFilters(stashie);
        GenerateTabMenu(stashie);
        DebugWindow.LogMsg("Reloaded Stashie config", 2, Color.LimeGreen);
    }

    public static void DrawIgnoredCellsSettings(StashieCore stashie)
    {
        EnsureIgnoredCellSettings(stashie.Settings);

        try
        {
            if (ImGui.Button("Copy Inventory"))
                SaveIgnoredSlotsFromInventoryTemplate(stashie);

            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    $"Checked = Item will be ignored{Environment.NewLine}UnChecked = Item will be processed");
        }
        catch (Exception e)
        {
            DebugWindow.LogError(e.ToString(), 10);
        }

        var numb = 1;
        for (var i = 0; i < InventoryRows; i++)
        for (var j = 0; j < MainInventoryColumns; j++)
        {
            var toggled = Convert.ToBoolean(stashie.Settings.IgnoredCells[i, j]);
            if (ImGui.Checkbox($"##{numb}IgnoredMainInventoryCells", ref toggled))
                stashie.Settings.IgnoredCells[i, j] ^= 1;

            if ((numb - 1) % 12 < 11)
                ImGui.SameLine();

            numb += 1;
        }
    }

    private static void EnsureIgnoredCellSettings(StashieSettings settings)
    {
        settings.IgnoredCells = EnsureIgnoredCellShape(
            settings.IgnoredCells,
            InventoryRows,
            MainInventoryColumns);
    }

    private static int[,] EnsureIgnoredCellShape(int[,] ignoredCells, int rows, int columns)
    {
        if (ignoredCells != null &&
            ignoredCells.GetLength(0) == rows &&
            ignoredCells.GetLength(1) == columns)
            return ignoredCells;

        var resizedIgnoredCells = new int[rows, columns];
        if (ignoredCells == null)
            return resizedIgnoredCells;

        var rowsToCopy = Math.Min(rows, ignoredCells.GetLength(0));
        var columnsToCopy = Math.Min(columns, ignoredCells.GetLength(1));
        for (var row = 0; row < rowsToCopy; row++)
        for (var column = 0; column < columnsToCopy; column++)
            resizedIgnoredCells[row, column] = ignoredCells[row, column];

        return resizedIgnoredCells;
    }

    public static void FilePicker(StashieCore stashie)
    {
        DrawReloadConfigButton(stashie);
        DrawIgnoredCellsSettings(stashie);
        if (ImGui.Button("Open Filter Folder"))
        {
            var configDir = stashie.ConfigDirectory;
            var directoryToOpen = Directory.Exists(configDir);

            if (!directoryToOpen)
            {
                // Log error when the config directory doesn't exist
            }

            if (configDir != null) Process.Start("explorer.exe", configDir);
        }
    }
}