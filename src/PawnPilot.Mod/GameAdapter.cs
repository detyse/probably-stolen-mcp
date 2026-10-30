using System;
using System.Collections.Generic;
using System.Linq;
using PawnPilot.Contracts;

namespace PawnPilot.Mod
{
    /// <summary>Only this adapter touches game objects. Called exclusively from BridgePump.Update.</summary>
    public sealed class GameAdapter : IGameAdapter
    {
        private StoreClientInstance? previousCustomer;
        private string visitId = "";

        public GameSnapshot Capture()
        {
            var state = new GameSnapshot
            {
                GameVersion = PlayerStore.VERSION,
                Limitations = new[]
                {
                    "Inventory includes only top-level items in the main inventory; nested contents and other containers are not exported.",
                    "Hidden authenticity, NPC identities, future events and true item values are not exported.",
                    "Item movement, trading, bargaining, inspection, manual dialogue advance and night activities are not implemented yet."
                }
            };
            if (!PlayerStore.HasInstance) return state;
            var store = PlayerStore.Instance;
            state.GameLoaded = true;
            state.Phase = store.storeState.ToString().ToLowerInvariant();
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

            var inventory = store.GetStoreInventory();
            state.Inventory = inventory?.childItems?.Where(item => item != null)
                .Select(item => new InventoryItem { Id = item.uniqueId, DisplayName = item.GetDisplayName(true), Count = item.unitCount })
                .OrderBy(item => item.Id).ToArray() ?? Array.Empty<InventoryItem>();

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

            var actions = new List<AvailableAction>();
            var inStore = !ModHelper.IsOutsideStore();
            if (inStore && store.lockAllFlag == 0)
            {
                foreach (var choice in state.DialogueChoices.Where(choice => choice.Enabled))
                    Add(actions, "dialogue_choice:" + choice.Index, "Choose visible dialogue option: " + choice.Text);
                var talking = dialogue != null && (dialogue.isConversationActiveFlag > 0 || dialogue.isWaitingForManualAdvance);
                if (!talking && choices.Count == 0)
                {
                    if (StoreShutterButton.Instance != null && store.lockShutterButtonFlag == 0 && store.CanStartDay()) Add(actions, "open_shop", "Open the shop using the game's shutter button.");
                    if (store.lockNextBellFlag == 0 && store.CanNextClient()) Add(actions, "next_customer", "Call the next customer.");
                    if (store.lockClientClickFlag == 0 && store.CanDismissCurrentClient()) Add(actions, "dismiss_customer", "Dismiss the current customer without automatically calling another.");
                    if (StoreShutterButton.Instance != null && store.lockShutterButtonFlag == 0 && store.CanEndDay()) Add(actions, "close_shop", "Use the game's shutter button; a confirmation may appear.");
                }
            }
            state.AvailableActions = actions.ToArray();
            return state;
        }

        private static void Add(List<AvailableAction> actions, string name, string description) =>
            actions.Add(new AvailableAction { Name = name, Description = description });

        public void Execute(string action)
        {
            // Re-check on the main thread; never bypass the game's normal UI handlers.
            if (!Capture().AvailableActions.Any(candidate => candidate.Name == action))
                throw new InvalidOperationException("Action is no longer available.");
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
