using System;
using System.Collections.Generic;
using System.Linq;
using PSMcp.Contracts;
using UnityEngine;

namespace PSMcp.Mod
{
    internal static class InventoryAccess
    {
        public static void Capture(GameSnapshot state, ActionRegistry registry, bool allowActions)
        {
            var nodes = UnityEngine.Object.FindObjectsOfType<TreeNodeRender>()
                .Where(n => VisibleUi.Visible(n) && n.element is GameInventory)
                .OrderBy(n => n.GetInstanceID()).ToArray();
            var inventories = new Dictionary<string, GameInventory>();
            var accessible = new HashSet<GameInventory>();
            var containers = new List<InventoryContainer>();
            foreach (var node in nodes)
            {
                var inventory = (GameInventory)node.element;
                if (inventory is GameSlotInventory slot && slot.isHidden) continue;
                var id = registry.Id(inventory);
                if (inventories.ContainsKey(id)) continue;
                inventories.Add(id, inventory);
                // Semantic inventory commands use the game's ownership, slot and lock
                // rules. Rendered scope prevents reading unopened contents; a screen
                // raycast is not a precondition for moving an already exposed item.
                accessible.Add(inventory);
                var parent = inventory.GetParentItem();
                containers.Add(new InventoryContainer
                {
                    EntityId = id, Role = Role(inventory), ParentItemId = parent == null ? null : registry.Id(parent), WindowTitle = VisibleUi.WindowTitle(node),
                    Kind = inventory is GameGridInventory ? "grid" : inventory is GameGridScrollableInventory ? "scrollable_grid" : "slot", InsertLocked = inventory.IsInsertLocked(), RemoveLocked = inventory.IsRemoveLocked(),
                    Shape = inventory is GameGridInventory grid ? DescribeShape(grid.inventoryShape) : null,
                    Items = inventory.childItems.Where(i => i != null).OrderBy(i => i.uniqueId).Select(i => Describe(i, registry, id)).ToArray()
                });
            }
            state.Containers = containers.OrderBy(c => c.EntityId, StringComparer.Ordinal).ToArray();
            state.InventoryScope = "rendered_containers_top_level; opened_nested_containers_only";
            state.Inventory = state.Containers.SelectMany(c => c.Items).OrderBy(i => i.EntityId, StringComparer.Ordinal).ToArray();
            if (!allowActions) return;

            var items = inventories.Values.Where(accessible.Contains).SelectMany(i => i.childItems)
                .Where(i => i is PixelElement element && VisibleUi.Visible(element.handler) && !GeneralHelper.IsDisplayOnly(i)).Distinct().ToArray();
            foreach (var item in items)
            {
                var name = item.GetDisplayName(true);
                var owned = GeneralHelper.IsItemOwned(item);
                if (owned && item.contentWindow != null && !item.contentWindow.isLocked && ItemMouseDoubleClickHandler.current != null)
                {
                    if (item.contentWindow.IsVisible())
                        registry.Add("item_close", item, "Close contents of " + name, _ => () => item.CloseContentWindow());
                    else registry.Add("item_open", item, "Open contents of " + name, _ => () => ItemMouseDoubleClickHandler.current.OpenContentAction(item, ScreenCenter()));
                }
                if (item.examineWindow != null && !item.examineWindow.isLocked && ItemMouseDoubleClickHandler.current != null)
                    registry.Add("item_examine", item, "Open visible examination of " + name, _ => () => ItemMouseDoubleClickHandler.current.OpenExamineAction(item, ScreenCenter()));
                if (item.MayActivateSlotItem() && item.CanActivateSlotItem())
                    registry.Add("item_activate", item, "Activate/read/consume " + name + " using its native action.", _ => () => item.ActivateSlotItem());
                if (item.MayToggleSlotItem() && item.CanToggleSlotItem())
                    registry.Add("item_toggle", item, "Toggle/unpack " + name + " using its native action.", _ => () => item.ToggleSlotItem());
                if (owned && item.MaySelectSlotItem() && item.CanSelectSlotItem() && ItemSelectHandler.current != null)
                    registry.Add("item_select", item, "Select/equip " + name, _ => () => ItemSelectHandler.current.TrySelectItem(item));
                if (ItemPinHelper.MayPin(item))
                    registry.Add("item_pin", item, "Pin or unpin " + name, a => () => ItemPinHelper.SetPinned(item, a.Toggle!.Value),
                        new[] { new ActionParameter { Name = "toggle", Type = "boolean", Required = true } });
                if (owned && item.contentWindow != null && !item.contentWindow.isLocked && !item.IsTag("IS_UNLOAD_DISABLED_TAG"))
                    registry.Add("item_unload", item, "Unload " + name + " using the native operation; pinned contents are skipped. Read state for partial results.", _ => () => item.TryUnloadAll(true));
                if (owned && item.MaySelectSlotItem() && item.CanSelectSlotItem())
                {
                    var targets = items.Where(t => t != item && item.MayTarget(t) && item.CanTarget(t)).ToDictionary(registry.Id);
                    if (targets.Count > 0) registry.Add("item_use_on", item, "Use " + name + " on a currently legal visible target.", a =>
                    {
                        if (!targets.TryGetValue(a.TargetId!, out var target) || !item.MayTarget(target) || !item.CanTarget(target))
                            throw new ActionRejectedException("The tool cannot be used on this target.");
                        return () => item.Target(target);
                    }, new[] { new ActionParameter { Name = "targetId", Required = true, Choices = targets.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray() } });
                }
                if (!item.MayRemove() || item.parentInventory == null || item.parentInventory.IsRemoveLocked() || item.MaxNumRemove() < 1) continue;
                var destinations = inventories.Where(p => accessible.Contains(p.Value) && !p.Value.IsInsertLocked()
                    && GeneralHelper.MayOwnershipAcceptItem(item, p.Value) && p.Value.MayHaveValidInventorySlot(item)).ToDictionary(p => p.Key, p => p.Value);
                if (destinations.Count == 0) continue;
                registry.Add("item_move", item, "Move an exact quantity of " + name + ". Omit coordinates for automatic placement; x/y and rotation (0..3 quarter turns) specify a grid placement.", a =>
                {
                    if (!destinations.TryGetValue(a.DestinationId!, out var destination)) throw new ActionRejectedException("Destination is not accessible.");
                    if (a.X.HasValue != a.Y.HasValue) throw new ActionRejectedException("Specify x and y together.");
                    if (!a.X.HasValue && (a.Rotation.HasValue || a.Flipped.HasValue)) throw new ActionRejectedException("Rotation and flip require x and y.");
                    if (!(destination is GameGridInventory) && !(destination is GameGridScrollableInventory) && a.X.HasValue)
                        throw new ActionRejectedException("Only grid containers accept coordinates.");
                    var quantity = a.Quantity ?? item.unitCount;
                    SlotMarker slot;
                    if (a.X.HasValue)
                    {
                        var shape = new GridShapeBuilder(item.modifiedShape).SetPosition(a.X.Value, a.Y!.Value)
                            .SetOrientation(a.Rotation ?? item.modifiedShape.orientation).SetFlipped(a.Flipped ?? item.modifiedShape.flipped).Build();
                        slot = destination.TryInventorySlot(item, quantity, shape, item.modifiedState);
                    }
                    else
                    {
                        slot = destination.TryFindOneValidInventorySlot(item);
                        // The automatic search uses the full stack. Revalidate a partial move so
                        // it cannot split into the still-occupied cells of its original stack.
                        if (slot != null && slot.targetItem == null && slot.itemGridShape != null && quantity < item.unitCount)
                            slot = destination.TryInventorySlot(item, quantity, slot.itemGridShape, item.modifiedState);
                    }
                    if (slot == null || !slot.IsValid() || slot.numTransfer < quantity || item.MaxNumRemove() < quantity)
                        throw new ActionRejectedException("The requested quantity does not fit, or the game's ownership, shape or slot rules reject it. No item was moved.");
                    return () =>
                    {
                        var moved = slot.TryAcceptOnce(quantity);
                        if (moved != quantity) throw new InvalidOperationException("Native movement accepted a different quantity; inspect inventory before continuing.");
                        OverlayHandler.current?.UpdateInventoryStateUI();
                    };
                }, new[]
                {
                    new ActionParameter { Name = "destinationId", Required = true, Choices = destinations.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray() },
                    ActionRegistry.Integer("quantity", 1, Math.Min(item.unitCount, item.MaxNumRemove()), false),
                    ActionRegistry.Integer("x", 0, 4096, false), ActionRegistry.Integer("y", 0, 4096, false),
                    ActionRegistry.Integer("rotation", 0, 3, false), new ActionParameter { Name = "flipped", Type = "boolean" }
                });
            }
            foreach (var pair in inventories)
            {
                if (pair.Value is GameGridScrollableInventory scrollable && accessible.Contains(scrollable))
                    registry.Add("inventory_scroll", scrollable, "Scroll this inventory (0..1).", a => () => scrollable.SetScroll((float)a.Value!.Value),
                        new[] { ActionRegistry.Number("value", 0, 1) });
                if (pair.Value is GameGridInventory grid && accessible.Contains(grid))
                {
                    if (InventorySortHelper.MaySort(grid) && InventorySortHelper.CanSort(grid))
                        registry.Add("inventory_sort", grid, "Sort this inventory using the game's sort operation.", a => () => InventorySortHelper.Sort(grid, a.Toggle ?? true, false),
                            new[] { new ActionParameter { Name = "toggle", Type = "boolean" } });
                    if (InventorySortHelper.MayUndo(grid) && InventorySortHelper.CanUndo(grid))
                        registry.Add("inventory_undo_sort", grid, "Undo the last native inventory sort.", _ => () => InventorySortHelper.Undo(grid));
                }
            }
            if (ItemSelectHandler.current != null && ItemSelectHandler.current.IsEquipped)
                registry.AddNamed("cancel_item_selection", "item_cancel", null, "Cancel the current selected tool.", _ => () => ItemSelectHandler.current.Cancel());
        }

        private static string Role(GameInventory inventory)
        {
            // Loaded contents may replace the construction-time inventory pointer.
            // Identify this visible fixture by its native parent item as well.
            if (inventory.GetParentItem()?.identifier == "trashcan") return "trashcan";
            var entry = EmporiumEntry.Instance;
            if (entry != null)
            {
                if (ReferenceEquals(inventory, entry.invElement)) return "player_inventory";
                if (ReferenceEquals(inventory, entry.showcaseElement)) return "showcase";
                if (ReferenceEquals(inventory, entry.frontInvinvElement)) return "customer_counter";
                if (ReferenceEquals(inventory, entry.backInvinvElement)) return "negotiation_counter";
                if (ReferenceEquals(inventory, entry.trashcanInvElement)) return "trashcan";
                if (ReferenceEquals(inventory, entry.afterhourInventory)) return "afterhours_ground";
                if (ReferenceEquals(inventory, entry.afterhourPocketSlotInvBackpack)) return "carried_backpack";
                if (ReferenceEquals(inventory, entry.afterhourPocketSlotInvLeft)) return "left_pocket";
                if (ReferenceEquals(inventory, entry.afterhourPocketSlotInvRight)) return "right_pocket";
                if (ReferenceEquals(inventory, entry.responseInventory) || ReferenceEquals(inventory, entry.responseInventoryClosable)) return "response_choices";
            }
            return inventory.GetParentItem() != null ? "item_contents" : "container";
        }

        private static Vector2 ScreenCenter() => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        private static InventoryItem Describe(GameItem item, ActionRegistry registry, string container) => new InventoryItem
        {
            Id = item.uniqueId, EntityId = registry.Id(item), ContainerId = container, DisplayName = item.GetDisplayName(true), Count = item.unitCount,
            Owner = GeneralHelper.IsItemOwned(item) ? "player" : "not_player", Description = item.shortDescription ?? "",
            Shape = DescribeShape(item.modifiedShape), ContentsOpen = item.contentWindow != null && item.contentWindow.IsVisible(),
            Features = (item.itemFeatures ?? new List<ItemFeature>()).Where(f => f != null && ObservationRules.FeatureVisible(f.IsInitiallyShown(), f.isDisabled, f.GetPublicHidden(), f.IsExposed()))
                .Select(f => new ItemFeatureInfo { Text = f.IsExposed() ? f.GetExposedString() : f.GetPublicDisplay(), Knowledge = f.IsExposed() ? "revealed" : "public_claim" }).ToArray()
        };

        private static GridInfo? DescribeShape(GridShape? shape)
        {
            if (shape == null) return null;
            return new GridInfo { X = shape.minX, Y = shape.minY, Width = shape.globalWidth, Height = shape.globalHeight,
                Rotation = shape.orientation, Flipped = shape.flipped,
                Cells = Enumerable.Range(shape.minY, Math.Min(shape.globalHeight, 256)).Select(y => new string(
                    Enumerable.Range(shape.minX, Math.Min(shape.globalWidth, 256)).Select(x => shape.Get(x, y) == 1 ? '#' : '.').ToArray())).ToArray() };
        }
    }
}
