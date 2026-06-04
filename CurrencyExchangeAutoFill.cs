using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.Shared;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.PoEMemory.Elements.Village;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared.Enums;
using ImGuiNET;
using CurrencyExchangeAutoFill.Utils;

namespace CurrencyExchangeAutoFill
{
    public class CurrencyExchangeAutoFill : BaseSettingsPlugin<Settings>
    {
        private readonly ConcurrentDictionary<RectangleF, bool?> _mouseStateForRect = new();
        
        public override bool Initialise()
        {
            try
            {
                var imagePath = Path.Combine(DirectoryFullName, "images\\pick.png").Replace('\\', '/');
                Graphics.InitImage(imagePath, false);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize CurrencyExchangeAutoFill: {ex.Message}");
                return false;
            }
        }

        public override void Render()
        {
            var offeredItemCountInput = GameController.IngameState.IngameUi.CurrencyExchangePanel?.OfferedItemCountInput;
            if (offeredItemCountInput == null || GameController?.IngameState.IngameUi.CurrencyExchangePanel == null || !GameController.IngameState.IngameUi.CurrencyExchangePanel.IsVisible || IsSelectorVisible()) {
                LogMessage("CurrencyExchangePanel not found or not visible");
                return;
            }
            
            // get the input field rectangle
            var inputRect = offeredItemCountInput.GetClientRectCache;
            
            // calculate button position: horizontally centered, positioned above the input
            const int buttonSize = 37;
            var buttonX = inputRect.X + (inputRect.Width - buttonSize) / 2;
            var buttonY = inputRect.Y - buttonSize;
            var buttonRect = new RectangleF(buttonX, buttonY, buttonSize, buttonSize);
            
            // render the button image
            var imagePath = Path.Combine("pick.png").Replace('\\', '/');
            Graphics.DrawImage(imagePath, buttonRect);

            // log where button is drawn
            LogMessage($"Button drawn at {buttonRect.X}, {buttonRect.Y}");

            if (IsButtonPressed(buttonRect))
            {
                _ = Task.Run(async () =>
                {
                    // wait for mouse release before proceeding
                    while (Control.MouseButtons == MouseButtons.Left)
                    {
                        await Task.Delay(100);
                    }
                });
                _ = Task.Run(FillInputField);
            }
        }

        private async Task FillInputField() {
            LogError("FillInputField");

            var currencyExchangePanel = GameController.IngameState.IngameUi.CurrencyExchangePanel;
            if (currencyExchangePanel == null || !currencyExchangePanel.IsVisible || IsSelectorVisible()) return;

            var offeredItemType = currencyExchangePanel.OfferedItemType;
            if (offeredItemType == null) {
                LogError("Offered item type not found");
                return;
            }

            var offeredItemCountInput = currencyExchangePanel.OfferedItemCountInput;
            var wantedItemCountInput = currencyExchangePanel.WantedItemCountInput;
            if (offeredItemCountInput == null || wantedItemCountInput == null) {
                LogError("Offered item count input or wanted item count input not found");
                return;
            }

            if (!TryGetStockAndRatio(currencyExchangePanel, offeredItemType, out var offeredAmount, out var wantedAmount)) {
                LogError("Failed to get stock and ratio");
                return;
            }

            // click on the input field, clear it and type the offered amount
            await Mouse.MoveMouse(offeredItemCountInput.GetClientRectCache.TopRight + GameController.Window.GetWindowRectangleTimeCache.TopLeft);
            await Mouse.LeftDown();
            await Mouse.LeftUp();
            for (int i = 0; i < 6; i++)
            {
                await Keyboard.KeyPress(Keys.Back);
            }
            await Keyboard.Type(offeredAmount.ToString());

            // click on the wanted field, clear it and type the wanted amount
            await Mouse.MoveMouse(wantedItemCountInput.GetClientRectCache.TopRight + GameController.Window.GetWindowRectangleTimeCache.TopLeft);
            await Mouse.LeftDown();
            await Mouse.LeftUp();
            await Mouse.LeftDown();
            await Mouse.LeftUp();
            for (int i = 0; i < 6; i++)
            {
                await Keyboard.KeyPress(Keys.Back);
            }
            await Keyboard.Type(wantedAmount.ToString());

            var placeOrderButton = GetPlaceOrderButton();
            if (placeOrderButton == null) return;
            await Mouse.MoveMouse(placeOrderButton.GetClientRectCache.Center + GameController.Window.GetWindowRectangleTimeCache.TopLeft);
        }

        private Element GetPlaceOrderButton() {
            var currencyExchangePanel = GameController.IngameState.IngameUi.CurrencyExchangePanel;
            if (currencyExchangePanel == null || !currencyExchangePanel.IsVisible) return null;
            foreach (var child in currencyExchangePanel.Children) {
                foreach (var grandchild in child.Children) {
                    if (grandchild.Text == "place order") {
                        return grandchild;
                    }
                }
            }
            return null;
        }

        private bool IsSelectorVisible() {
            var currencyExchangePanel = GameController.IngameState.IngameUi.CurrencyExchangePanel;
            if (currencyExchangePanel == null || !currencyExchangePanel.IsVisible) return false;
            foreach (var child in currencyExchangePanel.Children) {
                foreach (var grandchild in child.Children) {
                    if (grandchild.Text == "I Have" || grandchild.Text == "I Want") {
                        return grandchild.IsVisible;
                    }
                }
            }
            return false;
        }

        private bool TryGetStockAndRatio(CurrencyExchangePanel currencyExchangePanel,
            ExileCore2.PoEMemory.Models.BaseItemType offeredItemType,
            out int offeredAmount,
            out int wantedAmount)
        {
            offeredAmount = 0;
            wantedAmount = 0;

            // compare both OfferedItemStock and WantedItemStock to find best ratio
            int offerPart = 0;
            int wantPart = 0;
            double bestValue = 0.0;

            // check OfferedItemStock
            var offeredItemStock = currencyExchangePanel.OfferedItemStock;
            if (offeredItemStock != null && offeredItemStock.Count > 0)
            {
                dynamic firstOfferedStock = offeredItemStock[0];
                if (firstOfferedStock != null)
                {
                    int offerPart1 = firstOfferedStock.Get;
                    int wantPart1 = firstOfferedStock.Give;
                    if (offerPart1 > 0 && wantPart1 > 0)
                    {
                        double value1 = (double)wantPart1 / offerPart1;
                        if (value1 > bestValue)
                        {
                            bestValue = value1;
                            offerPart = offerPart1;
                            wantPart = wantPart1;
                        }
                    }
                }
            }

            // check WantedItemStock
            var wantedItemStock = currencyExchangePanel.WantedItemStock;
            if (wantedItemStock != null && wantedItemStock.Count > 0)
            {
                dynamic firstWantedStock = wantedItemStock[0];
                if (firstWantedStock != null)
                {
                    int offerPart2 = firstWantedStock.Give;
                    int wantPart2 = firstWantedStock.Get;
                    if (offerPart2 > 0 && wantPart2 > 0)
                    {
                        double value2 = (double)wantPart2 / offerPart2;
                        if (value2 > bestValue)
                        {
                            bestValue = value2;
                            offerPart = offerPart2;
                            wantPart = wantPart2;
                        }
                    }
                }
            }

            if (offerPart <= 0 || wantPart <= 0) {
                LogError("No valid ratio found in either OfferedItemStock or WantedItemStock");
                return false;
            }

            var stock = GetAvailableOfferedStock(offeredItemType);

            if (stock <= 0)
            {
                LogError($"No stock found for offered item '{offeredItemType.BaseName}' in the visible stash");
                return false;
            }

            // use all available stock and round the wanted side down to preserve ratio as closely as possible
            offeredAmount = stock;
            if (offerPart <= 0 || wantPart <= 0)
            {
                LogError("Invalid offer/want parts when computing rounded ratio");
                return false;
            }

            var wantedExact = (double)stock * wantPart / offerPart;
            wantedAmount = (int)Math.Ceiling(wantedExact);

            if (offeredAmount <= 0 || wantedAmount <= 0) {
                LogError("Offered amount or wanted amount not found");
                return false;
            }

            LogError($"Offered amount: {offeredAmount}, Wanted amount: {wantedAmount}");

            return true;
        }

        private int GetAvailableOfferedStock(ExileCore2.PoEMemory.Models.BaseItemType offeredItemType)
        {
            if (offeredItemType == null)
            {
                return 0;
            }

            var stock = GetVisibleStashStock(offeredItemType);
            if (stock > 0)
            {
                LogError($"Found {stock}x {offeredItemType.BaseName} in visible stash");
                return stock;
            }

            stock = GetPlayerInventoryStock(offeredItemType);
            if (stock > 0)
            {
                LogError($"Found {stock}x {offeredItemType.BaseName} in player inventories");
            }

            return stock;
        }

        private int GetVisibleStashStock(ExileCore2.PoEMemory.Models.BaseItemType offeredItemType)
        {
            var visibleStashItems = GameController?.IngameState?.IngameUi?.StashElement?.VisibleStash?.VisibleInventoryItems;
            if (visibleStashItems == null)
            {
                return 0;
            }

            var stock = 0;
            foreach (var item in visibleStashItems)
            {
                if (item?.Item == null || !IsMatchingItemType(item.Item.Metadata, offeredItemType))
                {
                    continue;
                }

                stock += item.Item.GetComponent<Stack>()?.Size ?? 1;
            }

            return stock;
        }

        private int GetPlayerInventoryStock(ExileCore2.PoEMemory.Models.BaseItemType offeredItemType)
        {
            var playerInventories = GameController?.IngameState?.ServerData?.PlayerInventories;
            if (playerInventories == null)
            {
                return 0;
            }

            var stock = 0;
            var processedInventories = new HashSet<string>();

            foreach (var playerInventory in playerInventories)
            {
                var inventory = playerInventory?.Inventory;
                if (inventory?.Items == null || playerInventory?.Id == 46) // ritual reward window
                {
                    continue;
                }

                var inventoryKey = $"{inventory.InventType}_{inventory.Address}";
                if (!processedInventories.Add(inventoryKey))
                {
                    continue;
                }

                foreach (var item in inventory.Items)
                {
                    if (item == null || !IsMatchingItemType(item.Metadata, offeredItemType))
                    {
                        continue;
                    }

                    stock += item.GetComponent<Stack>()?.Size ?? 1;
                }
            }

            return stock;
        }

        private bool IsMatchingItemType(string metadata, ExileCore2.PoEMemory.Models.BaseItemType offeredItemType)
        {
            if (string.IsNullOrEmpty(metadata) || offeredItemType == null)
            {
                return false;
            }

            var baseItemType = GameController.Files.BaseItemTypes.Translate(metadata);
            return string.Equals(baseItemType?.BaseName, offeredItemType.BaseName, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsButtonPressed(RectangleF buttonRect)
        {
            var prevState = _mouseStateForRect.GetValueOrDefault(buttonRect);
            var cursorPos = Mouse.GetCursorPosition();
            var windowOffset = GameController.Window.GetWindowRectangleTimeCache.TopLeft;
            var relativeCursorPos = new Vector2(cursorPos.X - windowOffset.X, cursorPos.Y - windowOffset.Y);
            
            var isHovered = buttonRect.Contains(relativeCursorPos);
            if (!isHovered)
            {
                _mouseStateForRect[buttonRect] = null;
                return false;
            }

            var isPressed = Control.MouseButtons == MouseButtons.Left;
            _mouseStateForRect[buttonRect] = isPressed;
            
            // button press detected on transition from not pressed to pressed
            return isPressed && prevState == false;
        }
    }
}
