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
            if (offeredItemCountInput == null || !GameController.IngameState.IngameUi.CurrencyExchangePanel.IsVisible || IsSelectorVisible()) {
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

            if (IsButtonPressed(buttonRect))
            {
                _ = Task.Run(async () =>
                {
                    // wait for mouse release before proceeding
                    while (Control.MouseButtons == MouseButtons.Left)
                    {
                        await Task.Delay(10);
                    }
                });
                FillInputField();
            }
        }

        private async Task FillInputField() {
            var currencyExchangePanel = GameController.IngameState.IngameUi.CurrencyExchangePanel;
            if (currencyExchangePanel == null || !currencyExchangePanel.IsVisible || IsSelectorVisible()) return;

            var offeredItemType = currencyExchangePanel.OfferedItemType;
            if (offeredItemType == null) return;

            var offeredItemCountInput = currencyExchangePanel.OfferedItemCountInput;
            if (offeredItemCountInput == null) return;

            var amountToInput = GetAmountToInput(offeredItemType);
            if (amountToInput == 0) return;

            // click on the input field, type backspace 5 times and then type the amount
            await Mouse.MoveMouse(offeredItemCountInput.GetClientRectCache.TopRight + GameController.Window.GetWindowRectangleTimeCache.TopLeft);
            await Mouse.LeftDown();
            await Mouse.LeftUp();
            for (int i = 0; i < 6; i++)
            {
                await Keyboard.KeyPress(Keys.Back);
            }
            await Keyboard.Type(amountToInput.ToString());
            var wantedItemCountInput = currencyExchangePanel.WantedItemCountInput;
            if (wantedItemCountInput == null) return;
            await Mouse.MoveMouse(wantedItemCountInput.GetClientRectCache.TopRight + GameController.Window.GetWindowRectangleTimeCache.TopLeft);
            await Mouse.LeftDown();
            await Mouse.LeftUp();
            for (int i = 0; i < 6; i++)
            {
                await Keyboard.KeyPress(Keys.Back);
            }
            await Mouse.MoveMouse(wantedItemCountInput.GetClientRectCache.TopRight + new Vector2(5, 0) + GameController.Window.GetWindowRectangleTimeCache.TopLeft);
            await Mouse.LeftDown();
            await Mouse.LeftUp();
            await Mouse.MoveMouse(wantedItemCountInput.GetClientRectCache.TopRight + GameController.Window.GetWindowRectangleTimeCache.TopLeft);
            await Mouse.LeftDown();
            await Mouse.LeftUp();
            await Mouse.MoveMouse(wantedItemCountInput.GetClientRectCache.TopRight + new Vector2(5, 0) + GameController.Window.GetWindowRectangleTimeCache.TopLeft);
            await Mouse.LeftDown();
            await Mouse.LeftUp();
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

        private static readonly HashSet<string> AllowedInventoryTypes = new HashSet<string>
        {
            "Essence", "Delirium", "Gem", "Currency", "62"
        };

        private int GetAmountToInput(ExileCore2.PoEMemory.Models.BaseItemType offeredItemType)
        {
            int amount = 0;
            var processedInventories = new HashSet<string>();
            var targetBaseName = offeredItemType.BaseName;
            
            foreach (var playerInventory in GameController.IngameState.Data.ServerData.PlayerInventories) 
            {
                var inventory = playerInventory?.Inventory;
                if (inventory?.Items == null) continue;
                
                // avoid double counting with unique inventory key
                var inventoryKey = $"{inventory.InventType}_{inventory.Address}";
                if (!processedInventories.Add(inventoryKey)) continue;
                
                // skip numeric inventory types (temporary/cached) and non-stash inventories
                var inventoryTypeString = inventory.InventType.ToString();
                if (!AllowedInventoryTypes.Contains(inventoryTypeString))
                    continue;
                
                foreach (var item in inventory.Items) 
                {
                    if (item == null) continue;
                    
                    var baseItemType = GameController.Files.BaseItemTypes.Translate(item.Metadata);
                    if (baseItemType?.BaseName == targetBaseName) 
                    {
                        amount += item.GetComponent<Stack>()?.Size ?? 1;
                    }
                }
            }
            
            return amount;
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