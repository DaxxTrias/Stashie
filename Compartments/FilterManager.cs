using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ExileCore2;
using ExileCore2.Shared;
using ExileCore2.Shared.Nodes;
using ItemFilterLibrary;
using Stashie.Classes;
using static Stashie.StashieCore;
using Vector2N = System.Numerics.Vector2;

namespace Stashie.Compartments;

internal class FilterManager
{
    public static void LoadCustomFilters()
    {
        if (Main == null)
            return;

        LoadCustomFilters(Main);
    }

    public static void LoadCustomFilters(StashieCore stashie)
    {
        EnsureFilterState(stashie);
        stashie.Settings.FilterFile.Values = [];

        var pickitConfigFileDirectory = stashie.ConfigDirectory;
        if (string.IsNullOrWhiteSpace(pickitConfigFileDirectory))
            return;

        if (!Directory.Exists(pickitConfigFileDirectory))
        {
            Directory.CreateDirectory(pickitConfigFileDirectory);
            return;
        }

        var dirInfo = new DirectoryInfo(pickitConfigFileDirectory);
        stashie.Settings.FilterFile.Values =
            dirInfo.GetFiles("*.json").Select(x => Path.GetFileNameWithoutExtension(x.Name)).ToList();
        if (stashie.Settings.FilterFile.Values.Count == 0)
        {
            stashie.Settings.FilterFile.Value = "";
            return;
        }

        if (!stashie.Settings.FilterFile.Values.Contains(stashie.Settings.FilterFile.Value))
            stashie.Settings.FilterFile.Value = stashie.Settings.FilterFile.Values.First();

        if (!string.IsNullOrWhiteSpace(stashie.Settings.FilterFile.Value))
        {
            var filterFilePath = Path.Combine(pickitConfigFileDirectory, $"{stashie.Settings.FilterFile.Value}.json");
            if (File.Exists(filterFilePath))
            {
                stashie.currentFilter =
                    FilterFileHandler.Load($"{stashie.Settings.FilterFile.Value}.json", filterFilePath);

                foreach (var customFilter in stashie.currentFilter)
                foreach (var filter in customFilter.Filters)
                {
                    if (!stashie.Settings.CustomFilterOptions.TryGetValue(customFilter.ParentMenuName + filter.FilterName,
                            out var indexNodeS))
                    {
                        indexNodeS = new ListIndexNode { Value = "Ignore", Index = -1 };
                        stashie.Settings.CustomFilterOptions.Add(customFilter.ParentMenuName + filter.FilterName,
                            indexNodeS);
                    }

                    filter.StashIndexNode = indexNodeS;
                    stashie.SettingsListNodes.Add(indexNodeS);
                }
            }
            else
            {
                stashie.currentFilter = [];
                stashie.LogError("Item filter file not found, plugin will not work");
            }
        }
    }

    private static void EnsureFilterState(StashieCore stashie)
    {
        stashie.currentFilter = [];
        stashie.SettingsListNodes = new List<ListIndexNode>(100);
        stashie.Settings.FilterFile ??= new ListNode();
        stashie.Settings.FilterFile.Values ??= [];
        stashie.Settings.CustomFilterOptions ??= [];
    }

    public static FilterResult CheckFilters(ItemData itemData, Vector2N clickPos)
    {
        if (Main.currentFilter == null || Main.currentFilter.Count == 0)
            return null;

        foreach (var filter in Main.currentFilter)
        foreach (var subFilter in filter.Filters)
            try
            {
                if (!subFilter.AllowProcess)
                    continue;

                if (filter.CompareItem(itemData, subFilter.CompiledQuery))
                    return new FilterResult(subFilter, itemData, clickPos);
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"Check filters error: {ex}");
            }

        return null;
    }

    public static async SyncTask<bool> ParseItems()
    {
        var _serverData = Main.GameController.Game.IngameState.Data.ServerData;
        var invItems = _serverData.PlayerInventories[0].Inventory.InventorySlotItems;

        await TaskUtils.CheckEveryFrameWithThrow(() => invItems != null, new CancellationTokenSource(500).Token);
        Main.DropItems = [];
        Main.ClickWindowOffset = Main.GameController.Window.GetWindowRectangle().TopLeft;

        foreach (var invItem in invItems)
        {
            if (invItem.Item == null || invItem.Address == 0)
                continue;

            if (Utility.CheckIgnoreCells(invItem, (12, 5), Main.Settings.IgnoredCells))
                continue;

            var testItem = new ItemData(invItem.Item, Main.GameController);
            var result = CheckFilters(testItem, invItem.GetClientRect().Center);
            if (result != null)
                Main.DropItems.Add(result);
        }

        #region Ignore 1 max stack of wisdoms/portals

        if (Main.Settings.KeepHighestIDStack) KeepHighestStackItem("Scroll of Wisdom");

        if (Main.Settings.KeepHighestTPStack) KeepHighestStackItem("Portal Scroll");

        void KeepHighestStackItem(string itemName)
        {
            var items = Main.DropItems.Where(item => item.ItemData.BaseName == itemName).ToList();
            if (items.Count == 0)
                return;

            var maxStackItem = items.MaxBy(item => item.ItemData.StackInfo.Count);
            if (maxStackItem == null)
                return;

            Main.DropItems.Remove(maxStackItem);
        }

        #endregion

        return true;
    }

    public static List<ItemData> GetInventoryItems()
    {
        var serverData = Main.GameController.Game.IngameState.Data.ServerData;
        var invItems = serverData.PlayerInventories[0].Inventory.InventorySlotItems;

        Main.DropItems = [];
        Main.ClickWindowOffset = Main.GameController.Window.GetWindowRectangle().TopLeft;

        return (from invItem in invItems
            where invItem.Item != null && invItem.Address != 0
            select new ItemData(invItem.Item, Main.GameController)).ToList();
    }
}