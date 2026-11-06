using System;
using System.Collections.Generic;
using System.Linq;
using PSMcp.Contracts;

namespace PSMcp.Mod
{
    /// <summary>Only this adapter touches game objects. Called exclusively from BridgePump.Update.</summary>
    public sealed class GameAdapter : IGameAdapter, IResettableGameAdapter
    {
        private StoreClientInstance? previousCustomer;
        private string visitId = "";
        private readonly ActionRegistry registry = new ActionRegistry();

        public GameSnapshot Capture()
        {
            registry.Clear();
            var state = new GameSnapshot
            {
                GameVersion = PlayerStore.VERSION,
                Interaction = ObservationRules.Describe(new InteractionSignals(), false),
                Limitations = new[]
                {
                    "Inventory covers rendered containers and opened nested contents, not all assets or unopened containers. UI references are valid only in the returned snapshot. Wholesale quotes may include items outside the counter.",
                    "Unrevealed authenticity, NPC identities, future events and true item values are not exported. public_claim features are unverified claims; quotes are current UI prices, not appraisals.",
                    "Only availableActions are callable. UI controls use native callbacks; disabled or occluded controls have no action. Hold gestures take real time and cancel if their target disappears. Screenshot evidence is available via get_view. Native behavior requires scenario-by-scenario in-game verification."
                }
            };
            if (MainMenuUIController.Instance != null || !PlayerStore.HasInstance)
            {
                VisibleUi.Capture(state, registry, false);
                state.AvailableActions = registry.Actions.OrderBy(a => a.Name, StringComparer.Ordinal).ToArray();
                state.Interaction.McpSupported = state.AvailableActions.Length > 0;
                ApplyInputLock(state);
                return state;
            }
            var store = PlayerStore.Instance;
            state.GameLoaded = true;
            state.Phase = store.storeState.ToString().ToLowerInvariant();
            state.Day = StoreStation.Instance != null ? StoreStation.Instance.dayCounter : (int?)null;
            state.Cash = store.GetCash();
            state.Rent = store.GetRentValue();
            state.DaysUntilRent = store.GetRentDayCounter();
            state.RecentDialogue = store.dialogHistory?.Reverse().Take(8).Reverse().ToArray() ?? Array.Empty<string>();

            if (!ReferenceEquals(previousCustomer, store.currentClientInstance))
            {
                previousCustomer = store.currentClientInstance;
                visitId = Guid.NewGuid().ToString("N");
            }
            if (store.isClientArrived && store.currentClientInstance != null)
            {
                var client = store.currentClientInstance.GetClientBlueprint();
                state.Customer = new CustomerInfo
                {
                    VisitId = visitId,
                    DisplayName = client.displayName,
                    Intent = store.GetCurrentClientIntent().ToString(),
                    Budget = client.useClientBudget ? client.GetBudget() : (int?)null
                };
            }

            var dialogue = DialogUIManager.Instance;
            var choices = new List<DialogueChoice>();
            if (dialogue != null && dialogue.dialogChoiceBubbles != null)
            {
                foreach (var choice in dialogue.dialogChoiceBubbles)
                {
                    if (choice == null || !choice.gameObject.activeInHierarchy) continue;
                    choices.Add(new DialogueChoice { Index = choice.index, Text = choice.TMP?.text ?? "", Enabled = choice.IsInteractable() });
                }
            }
            state.DialogueChoices = choices.OrderBy(choice => choice.Index).ToArray();

            var ui = UiObservation.Capture();
            var storeUi = StoreUIManager.Instance;
            var specialMode = storeUi != null && (storeUi.isShootingMode || storeUi.isMagnifyingMode);
            state.Trade = TradeObservation.Capture(store, state.Customer, ui);
            var signals = new InteractionSignals
            {
                GameLoaded = true, InStore = !ModHelper.IsOutsideStore(),
                ConfirmationVisible = ui.ConfirmationVisible, InspectionVisible = ui.InspectionVisible,
                OtherModalVisible = ui.OtherModalVisible, GlobalLocked = store.lockAllFlag != 0,
                TradeLocked = state.Trade?.Locked == true,
                ManualDialogueAdvance = dialogue != null && dialogue.isWaitingForManualAdvance,
                ConversationActive = dialogue != null && dialogue.isConversationActiveFlag > 0,
                ChoiceCount = choices.Count, EnabledChoiceCount = choices.Count(choice => choice.Enabled),
                TradeActive = state.Trade != null && !state.Trade.BusinessComplete
                    && (state.Trade.Direction != "other" || state.Trade.PanelOpen),
                BargainingOpen = ui.BargainingOpen
            };

            var actions = new List<AvailableAction>();
            if (signals.ManualDialogueAdvance && !signals.ConfirmationVisible && !signals.OtherModalVisible && !signals.InspectionVisible)
                registry.AddNamed("advance_dialogue", "dialogue_advance", null, "Advance the dialogue that is explicitly waiting for player input.", _ => () => DialogUIManager.Instance.OnAdvancePressed());
            if (!specialMode && !ObservationRules.BlocksDialogueChoices(signals))
            {
                foreach (var choice in state.DialogueChoices.Where(choice => choice.Enabled))
                    Add(actions, "dialogue_choice:" + choice.Index, "Choose visible dialogue option: " + choice.Text);
                if (!ObservationRules.BlocksShopActions(signals))
                {
                    if (StoreShutterButton.Instance != null && store.storeState == PlayerStore.StoreState.MORNING && store.lockShutterButtonFlag == 0 && !store.isClientBeingArrested && !store.isHandlingShootout && store.CanStartDay()) Add(actions, "open_shop", "Open the shop using the game's shutter button.");
                    if (store.storeState == PlayerStore.StoreState.OPEN && store.lockNextBellFlag == 0 && store.CanNextClient()) Add(actions, "next_customer", "Call the next customer.");
                    if (store.isClientArrived && store.currentClientInstance != null && store.lockClientClickFlag == 0 && store.CanDismissCurrentClient()) Add(actions, "dismiss_customer", "Dismiss the current customer without automatically calling another.");
                    if (StoreShutterButton.Instance != null && store.storeState == PlayerStore.StoreState.OPEN && store.lockShutterButtonFlag == 0 && !store.isClientBeingArrested && !store.isHandlingShootout && store.CanEndDay()) Add(actions, "close_shop", "Use the game's shutter button; a confirmation may appear.");
                }
            }
            foreach (var action in actions)
            {
                var name = action.Name;
                registry.AddNamed(name, name.StartsWith("dialogue_choice:", StringComparison.Ordinal) ? "dialogue_choice" : "shop", null, action.Description, _ => () => ExecuteLegacy(name));
            }
            var free = !signals.GlobalLocked && !signals.TradeLocked && !signals.ConfirmationVisible
                && !signals.InspectionVisible && !signals.OtherModalVisible && !signals.ManualDialogueAdvance
                && !signals.ConversationActive && signals.ChoiceCount == 0 && !specialMode;
            // Use the same escape resolver as the native back key. It closes the
            // topmost item window or selection before opening the pause menu.
            if (storeUi != null && storeUi.escapeUIManager != null
                && (free || (!signals.GlobalLocked && ui.BlockingPanels.Any(p => p.Kind == "pause_menu"))))
                registry.AddNamed("navigate_back", "ui_back", null,
                    "Use the native back action: close the current window/selection, or open/close the pause menu. Read state after each call.",
                    _ => () => storeUi.ResolveEscape());
            if (free && signals.InStore && store.storeState == PlayerStore.StoreState.CLOSED)
            {
                var bed = UnityEngine.Object.FindObjectOfType<StoreBedroll>();
                if (bed != null && bed.isActiveAndEnabled)
                    registry.AddNamed("sleep", "shop", null, "Open the native sleep/day-end summary. Review its food, costs and confirmation before advancing the day.", _ => () => bed.OnClick());
            }
            VisibleUi.Capture(state, registry, free, !signals.GlobalLocked || ui.BlockingRoots.Count > 0, ui.BlockingRoots);
            InventoryAccess.Capture(state, registry, free && !ItemPinHelper.BlocksClicks
                && (ItemMouseDragHandler.current == null || !ItemMouseDragHandler.current.IsDraggingItem));
            if (storeUi != null && !signals.GlobalLocked && !signals.ConfirmationVisible && !signals.OtherModalVisible)
            {
                if (storeUi.isMagnifyingMode && storeUi.magnifyingScreen != null && storeUi.magnifyingScreen.activeInHierarchy)
                {
                    registry.AddNamed("toggle_customer_zoom", "inspection_zoom", null, "Toggle the magnified view of the current customer.", _ => () => storeUi.ToggleMagnifyZoom());
                    registry.AddNamed("exit_customer_inspection", "inspection_exit", null, "Return from the magnified customer view.", _ => () => storeUi.ExitMagnifyingMode());
                }
                if (storeUi.isShootingMode && storeUi.arm != null && storeUi.arm.activeInHierarchy)
                {
                    registry.AddNamed("put_away_shotgun", "defence_exit", null, "Put away the shotgun using the native exit handler; this may dismiss the customer.", _ => () => storeUi.ExitShootingMode());
                    if (storeUi.CanShoot()) registry.AddNamed("fire_shotgun", "defence_fire", null, "Fire at the current customer in the game's active aiming mode. The normal permanent gameplay consequences apply.", _ => () => storeUi.Shoot());
                }
            }
            state.AvailableActions = registry.Actions.OrderBy(a => a.Name, StringComparer.Ordinal).ToArray();
            state.Context = new GameContext
            {
                Location = TransitUI.IsOpen ? "transit_map" : signals.InStore ? "store" : MapUIManager.Instance?.Current?.Id ?? "outside_store",
                Mode = storeUi != null && storeUi.isShootingMode ? "aiming" : storeUi != null && storeUi.isMagnifyingMode ? "customer_inspection" : "normal",
                DialogueState = signals.ChoiceCount > 0 ? "choice" : signals.ManualDialogueAdvance ? "manual_advance" : signals.ConversationActive ? "playing" : "idle",
                InputLocks = new[] { signals.GlobalLocked ? "global" : null, signals.TradeLocked ? "trade" : null }.Where(x => x != null).Select(x => x!).ToArray(),
                ModalPanels = ui.BlockingPanels.Select(p => p.Kind).ToArray()
            };
            state.Interaction = ObservationRules.Describe(signals, state.AvailableActions.Length > 0);
            if (state.Interaction.WaitingFor == "player")
                state.Interaction.McpSupported = state.AvailableActions.Any(a => a.Kind.StartsWith("ui_", StringComparison.Ordinal)
                    || a.Kind.StartsWith("dialogue_", StringComparison.Ordinal) || a.Kind == "trade_open" || (!signals.TradeActive && free));
            if (state.Context.Mode != "normal" && state.AvailableActions.Any(a => a.Kind.StartsWith("inspection_", StringComparison.Ordinal) || a.Kind.StartsWith("defence_", StringComparison.Ordinal)))
                state.Interaction = new InteractionInfo { Kind = state.Context.Mode, WaitingFor = "player", McpSupported = true,
                    Message = "Use the listed actions for the current special input mode. get_view provides the visible evidence.", RequiredInput = state.Context.Mode };
            state.Interaction.Panels = ui.BlockingPanels.ToArray();
            ApplyInputLock(state);
            return state;
        }

        private void ApplyInputLock(GameSnapshot state)
        {
            if (NativeInput.Busy)
            {
                registry.Clear(); state.AvailableActions = Array.Empty<AvailableAction>();
                foreach (var control in state.Controls) { control.Enabled = false; control.Actions = Array.Empty<string>(); control.DisabledReason = "native_hold"; }
                state.Context.InputLocks = state.Context.InputLocks.Concat(new[] { "native_hold" }).ToArray();
                state.Interaction = new InteractionInfo { Kind = "input_in_progress", WaitingFor = "game", Message = "A hold gesture is in progress. Observe again after it releases or cancels.", Blockers = new[] { "native_hold" } };
            }
        }

        private static void Add(List<AvailableAction> actions, string name, string description) =>
            actions.Add(new AvailableAction { Name = name, Description = description });

        public Action Prepare(string action, ActionArguments arguments) => registry.Prepare(action, arguments);

        public void ResetReferences() { registry.Reset(); previousCustomer = null; visitId = ""; }

        private void ExecuteLegacy(string action)
        {
            // GameSession has just rebuilt and validated the registry on this same main-thread turn.
            var store = PlayerStore.Instance;
            switch (action)
            {
                case "open_shop":
                case "close_shop":
                    StoreShutterButton.Instance.OnClick();
                    return;
                case "next_customer": store.TryCallNextClient(); return;
                case "dismiss_customer": store.TryPlayerDismissCurrentClient(false); return;
            }
            if (action.StartsWith("dialogue_choice:", StringComparison.Ordinal)
                && int.TryParse(action.Substring("dialogue_choice:".Length), out var index))
            {
                var choice = DialogUIManager.Instance.dialogChoiceBubbles.FirstOrDefault(bubble =>
                    bubble != null && bubble.index == index && bubble.gameObject.activeInHierarchy && bubble.IsInteractable());
                if (choice == null) throw new InvalidOperationException("Dialogue choice expired.");
                choice.OnDialogChoiceClick();
                return;
            }
            throw new InvalidOperationException("Unknown action.");
        }
    }
}
