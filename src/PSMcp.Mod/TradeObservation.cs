using System;
using System.Collections.Generic;
using System.Linq;
using PSMcp.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PSMcp.Mod
{
    /// <summary>Read on the Unity thread. Uses the same offer and feature rules as the negotiation UI.</summary>
    internal static class TradeObservation
    {
        public static TradeInfo? Capture(PlayerStore store, CustomerInfo? customer, UiObservation ui)
        {
            if (customer == null || store.currentClientInstance == null) return null;
            var client = store.currentClientInstance.GetClientBlueprint();
            var negotiation = NegociationUIManager.Instance;
            var entry = EmporiumEntry.Instance;
            var trade = new TradeInfo
            {
                CustomerVisitId = customer.VisitId,
                CustomerIntent = customer.Intent,
                Direction = ObservationRules.TradeDirection(customer.Intent),
                BusinessComplete = client.isBusinessComplete,
                PanelOpen = negotiation != null && negotiation.IsOpen,
                Locked = store.lockAllFlag != 0 || NegociationUIManager.IsTradeLocked(),
                Panels = ui.TradePanels.ToArray()
            };
            if (entry == null || entry.backInvinvElement == null) return trade;
            var counterItems = entry.GetBackInvItems().Where(item => item != null).OrderBy(item => item.uniqueId).ToArray();
            var focused = negotiation != null ? negotiation.currentNegociatedItem : store.GetCurrentlyNegociatedItem();
            if (focused != null && counterItems.Contains(focused)) trade.NegotiatedItemId = focused.uniqueId;

            var items = new List<TradeItem>();
            var quotes = new List<TradeQuote>();
            foreach (var item in counterItems)
            {
                var owned = GeneralHelper.IsItemOwned(item);
                var refusal = owned && !trade.BusinessComplete && negotiation != null
                    && (trade.Direction == "player_sells" || trade.Direction == "mixed")
                    ? negotiation.GetNotBuyingReason(item) : null;
                items.Add(new TradeItem
                {
                    Id = item.uniqueId, DisplayName = item.GetDisplayName(true), Count = item.unitCount,
                    Owner = owned ? "player" : "customer",
                    Description = item.shortDescription ?? "", LongDescription = item.longDescription ?? "",
                    RefusalReason = string.IsNullOrEmpty(refusal) ? null : refusal,
                    Features = (item.itemFeatures ?? new List<ItemFeature>())
                        .Where(feature => feature != null && ObservationRules.FeatureVisible(
                            feature.IsInitiallyShown(), feature.isDisabled, feature.GetPublicHidden(), feature.IsExposed()))
                        .Select(feature => new ItemFeatureInfo
                        {
                            Text = feature.IsExposed() ? feature.GetExposedString() : feature.GetPublicDisplay(),
                            Knowledge = feature.IsExposed() ? "revealed" : "public_claim"
                        }).ToArray()
                });
                if (negotiation != null && ObservationRules.QuoteApplies(trade.Direction, owned, trade.BusinessComplete, refusal))
                    quotes.Add(new TradeQuote
                    {
                        ItemId = item.uniqueId, Direction = owned ? "player_sells" : "player_buys",
                        // This is the exact value displayed by BuildBuying/BuildSelling,
                        // not GetRefreshedValue, a hidden true value, or a second estimate.
                        Amount = item.GetNegociatedValue()
                    });
            }
            if (!trade.BusinessComplete && client.isOfferWholesale && entry.GetAllNonOwendItemCount() > 0)
                quotes.Add(new TradeQuote
                {
                    Kind = "wholesale_total", Direction = "player_buys", Amount = store.GetWholesalePrice(),
                    Scope = "all_customer_items", Source = "negotiation_ui_wholesale_calculation"
                });
            trade.Items = items.ToArray();
            trade.Quotes = quotes.ToArray();
            return trade;
        }
    }

    internal sealed class UiObservation
    {
        public List<VisiblePanel> TradePanels { get; } = new List<VisiblePanel>();
        public List<VisiblePanel> BlockingPanels { get; } = new List<VisiblePanel>();
        public List<GameObject> BlockingRoots { get; } = new List<GameObject>();
        public bool ConfirmationVisible { get; private set; }
        public bool InspectionVisible { get; private set; }
        public bool OtherModalVisible { get; private set; }
        public bool BargainingOpen { get; private set; }

        public static UiObservation Capture()
        {
            var result = new UiObservation();
            if (TransitUI.IsOpen)
                result.OtherModalVisible |= result.Add(result.BlockingPanels, "transit_map", TransitUI.Root);
            if (WildNetworkUI.IsOpen)
                result.OtherModalVisible |= result.Add(result.BlockingPanels, "upgrades", WindowRoot(WildNetworkUI.WINDOW_ID));
            var negotiation = NegociationUIManager.Instance;
            if (negotiation != null && negotiation.IsOpen && Visible(negotiation.uiObject))
            {
                result.Add(result.TradePanels, "negotiation", negotiation.uiObject);
                var frame = negotiation.uiObject.GetComponentInChildren<NegotiationTabletFrame>();
                if (frame != null) result.AddConfirmation("trade_confirmation", frame.confirmRoot);
            }
            var bargain = BargainUIManager.Instance;
            result.BargainingOpen = bargain != null && bargain.IsOpen;
            if (result.BargainingOpen)
                result.Add(result.TradePanels, "bargaining", WindowRoot("bargain_panel"));

            var generic = GenericConfirmationUIManager.Instance;
            if (generic != null) result.AddConfirmation("confirmation", generic.panel);
            var storeUi = StoreUIManager.Instance;
            if (storeUi != null)
            {
                result.AddConfirmation("close_shop_confirmation", storeUi.closeShutterConfirmationPanel);
                if (storeUi.confirmPanelUIManager != null)
                    result.AddConfirmation("store_confirmation", storeUi.confirmPanelUIManager.panel);
                if (storeUi.escapeUIManager != null)
                    result.OtherModalVisible |= result.Add(result.BlockingPanels, "pause_menu", storeUi.escapeUIManager.menuPanel);
            }
            var inspection = InspectionUIManager.Instance;
            if (inspection != null)
            {
                result.InspectionVisible |= result.Add(result.BlockingPanels, "inspection", inspection.panel);
                result.InspectionVisible |= result.Add(result.BlockingPanels, "confrontation", inspection.confrontPanel);
                result.InspectionVisible |= result.Add(result.BlockingPanels, "appraisal", inspection.appraisalPanel);
            }
            var morning = StartOfDayUIManager.Instance;
            if (morning != null) result.OtherModalVisible |= result.Add(result.BlockingPanels, "start_of_day", morning.UIPanel);
            var end = EndOfDayUIManager.Instance;
            if (end != null && end.IsOpen) result.OtherModalVisible |= result.Add(result.BlockingPanels, "end_of_day", WindowRoot("end_of_day_tablet"));
            var gameOver = GameOverUIManager.Instance;
            if (gameOver != null) result.OtherModalVisible |= result.Add(result.BlockingPanels, "game_over", gameOver.uiPanel);
            return result;
        }

        private void AddConfirmation(string kind, GameObject? root)
            => ConfirmationVisible |= Add(BlockingPanels, kind, root);

        private bool Add(List<VisiblePanel> target, string kind, GameObject? root)
        {
            if (!Visible(root)) return false;
            if (ReferenceEquals(target, BlockingPanels)) BlockingRoots.Add(root!);
            target.Add(new VisiblePanel
            {
                Kind = kind,
                Text = Text(root!),
                Controls = root!.GetComponentsInChildren<Button>()
                    .Where(button => button != null && button.isActiveAndEnabled)
                    .Select(button => new VisibleControl
                    {
                        Text = string.Join(" ", Text(button.gameObject)), Enabled = button.IsInteractable()
                    }).ToArray()
            });
            return true;
        }

        private static bool Visible(GameObject? value) => value != null && value.activeInHierarchy;
        private static GameObject? WindowRoot(string id)
            => CustomUIManager.Instance != null ? CustomUIManager.Instance.GetWindow(id)?.root : null;
        private static string[] Text(GameObject root) => root.GetComponentsInChildren<TMP_Text>()
            .Where(text => text != null && text.isActiveAndEnabled && !string.IsNullOrWhiteSpace(text.text))
            .Select(text => text.text).ToArray();
    }
}
