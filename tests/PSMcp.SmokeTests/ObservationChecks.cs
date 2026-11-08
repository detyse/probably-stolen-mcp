using Newtonsoft.Json;
using PSMcp.Contracts;
using PSMcp.Mod;

internal static class ObservationChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(ObservationRules.TradeDirection("BUY") == "player_sells"
            && ObservationRules.TradeDirection("SELL") == "player_buys", "trade directions use the player's perspective");
        check(ObservationRules.TradeDirection("SELLNBUY") == "mixed"
            && ObservationRules.TradeDirection("BARTER") == "barter"
            && ObservationRules.TradeDirection("APPRAISAL_SERVICE") == "other", "mixed, barter and special customers retain distinct trade meanings");
        check(ObservationRules.FeatureVisible(true, false, false, false), "public feature claims are observable before verification");
        check(!ObservationRules.FeatureVisible(true, false, true, false), "unrevealed hidden features are excluded");
        check(ObservationRules.FeatureVisible(true, false, true, true), "revealed features follow the negotiation UI visibility rule");
        check(!ObservationRules.FeatureVisible(false, false, false, true)
            && !ObservationRules.FeatureVisible(true, true, false, true), "disabled and non-displayed features stay excluded");
        check(ObservationRules.QuoteApplies("player_buys", false, false, null)
            && ObservationRules.QuoteApplies("player_sells", true, false, ""), "quotes match ownership and trade direction");
        check(!ObservationRules.QuoteApplies("player_sells", false, false, null)
            && !ObservationRules.QuoteApplies("player_buys", true, false, null), "items on the wrong side do not receive fictitious offers");
        check(!ObservationRules.QuoteApplies("mixed", true, true, null)
            && !ObservationRules.QuoteApplies("mixed", true, false, "Not interested")
            && !ObservationRules.QuoteApplies("barter", false, false, null), "completed, refused and barter trades do not generate cash offers");

        var signals = Ready();
        signals.ConfirmationVisible = signals.GlobalLocked = signals.ConversationActive = true;
        var observation = ObservationRules.Describe(signals, true);
        check(observation.Kind == "confirmation" && observation.WaitingFor == "player" && !observation.McpSupported
            && observation.RequiredInput == "confirm_or_cancel", "confirmation takes priority over background locks and dialogue");
        check(ObservationRules.BlocksShopActions(signals) && ObservationRules.BlocksDialogueChoices(signals), "confirmation blocks unrelated shop and dialogue actions");

        signals = Ready();
        signals.ManualDialogueAdvance = signals.ConversationActive = true;
        observation = ObservationRules.Describe(signals, false);
        check(observation.Kind == "dialogue_advance" && observation.WaitingFor == "player" && !observation.McpSupported,
            "manual dialogue advance is distinguished from waiting for animation");
        signals.ManualDialogueAdvance = false;
        check(ObservationRules.Describe(signals, false).WaitingFor == "game" && ObservationRules.BlocksShopActions(signals), "running dialogue waits on the game and blocks shop actions");

        signals = Ready();
        signals.ChoiceCount = signals.EnabledChoiceCount = 2;
        signals.ConversationActive = true;
        observation = ObservationRules.Describe(signals, true);
        check(observation.Kind == "dialogue_choice" && observation.McpSupported
            && !ObservationRules.BlocksDialogueChoices(signals) && ObservationRules.BlocksShopActions(signals), "enabled dialogue choices remain actionable during conversation");
        signals.EnabledChoiceCount = 0;
        check(ObservationRules.Describe(signals, false).WaitingFor == "game", "disabled dialogue choices are not reported as actionable input");
        signals.EnabledChoiceCount = 2;
        signals.GlobalLocked = true;
        check(!ObservationRules.Describe(signals, true).McpSupported && ObservationRules.BlocksDialogueChoices(signals), "locked dialogue cannot be advertised as supported input");

        signals = Ready();
        signals.InspectionVisible = true;
        check(ObservationRules.Describe(signals, false).Kind == "inspection" && ObservationRules.BlocksShopActions(signals), "inspection input is observed and blocks unrelated shop actions");
        signals.InspectionVisible = false;
        signals.OtherModalVisible = true;
        check(ObservationRules.Describe(signals, false).Kind == "modal" && ObservationRules.BlocksDialogueChoices(signals), "pause and day-summary panels block background actions");
        signals = Ready();
        signals.TradeLocked = true;
        check(ObservationRules.Describe(signals, false).Kind == "game_busy", "a disrupted trade waits for the game");
        signals.TradeLocked = false;
        signals.TradeActive = true;
        check(ObservationRules.Describe(signals, true).Kind == "trade" && !ObservationRules.Describe(signals, true).McpSupported,
            "a pending trade is not presented as an implemented trade action");
        signals.BargainingOpen = true;
        check(ObservationRules.Describe(signals, false).Kind == "bargaining", "bargaining input has its own wait state");
        check(ObservationRules.Describe(Ready(), true).McpSupported && ObservationRules.Describe(Ready(), false).WaitingFor == "unknown",
            "an empty action list without a known blocker does not falsely promise progress");
        check(ObservationRules.Describe(new InteractionSignals(), false).Kind == "game_not_loaded", "main-menu observation explicitly reports no loaded game");

        var fake = new FakeGame { Trade = SampleTrade(), Interaction = ObservationRules.Describe(signals, false) };
        var session = new GameSession(fake, state => JsonConvert.SerializeObject(state, HttpBridge.JsonSettings));
        var first = session.Observe();
        check(first.SnapshotId == session.Observe().SnapshotId, "unchanged trade observations retain the snapshot ID");
        fake.Trade.Quotes[0].Amount++;
        var rejected = session.Perform(new ActionRequest
        {
            Action = "next_customer", ExpectedSnapshotId = first.SnapshotId, RequestId = Guid.NewGuid().ToString()
        });
        check(rejected.ErrorCode == "stale_snapshot" && fake.Actions == 0, "a changed quote invalidates decisions before any action runs");
        var beforeFeature = session.Observe();
        fake.Trade.Items[0].Features[0] = new ItemFeatureInfo { Text = "Revealed condition", Knowledge = "revealed" };
        check(beforeFeature.SnapshotId != session.Observe().SnapshotId, "revealed item information invalidates the snapshot");
        var beforeModal = session.Observe();
        fake.Interaction = ObservationRules.Describe(new InteractionSignals { GameLoaded = true, InStore = true, ConfirmationVisible = true }, false);
        check(beforeModal.SnapshotId != session.Observe().SnapshotId, "opening a confirmation invalidates the snapshot");

        var json = JsonConvert.SerializeObject(BridgeResponse.Ok(session.Observe()), HttpBridge.JsonSettings);
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<BridgeResponse>(json, TestBridgeClient.JsonOptions)!;
        var mcpJson = System.Text.Json.JsonSerializer.SerializeToElement(roundTrip, TestBridgeClient.JsonOptions);
        var stateJson = mcpJson.GetProperty("state");
        check(stateJson.GetProperty("trade").GetProperty("quotes")[0].GetProperty("amount").GetInt64() == 5_000_000_001L
            && stateJson.GetProperty("trade").GetProperty("items")[0].GetProperty("features")[0].GetProperty("knowledge").GetString() == "revealed"
            && stateJson.GetProperty("interaction").GetProperty("kind").GetString() == "confirmation",
            "trade, long quote amounts and wait states survive the Mod-to-MCP JSON round trip");
        var oldState = System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>("{\"gameLoaded\":true}", TestBridgeClient.JsonOptions)!;
        check(oldState.Trade == null && oldState.Interaction.Kind == "unknown", "older Mod payloads report unavailable observations rather than fabricated readiness");
        fake.GameLoaded = false;
        check(session.Observe().Trade == null, "returning to the main menu clears customer trade information");
    }

    public static TradeInfo SampleTrade() => new()
    {
        CustomerVisitId = "test-visit", CustomerIntent = "SELL", Direction = "player_buys", PanelOpen = true,
        NegotiatedItemId = 43,
        Items = [new TradeItem
        {
            Id = 43, DisplayName = "Test offered item", Count = 2, Owner = "customer", Description = "Visible description",
            Features = [new ItemFeatureInfo { Text = "Claimed condition", Knowledge = "public_claim" }]
        }],
        Quotes = [new TradeQuote { ItemId = 43, Direction = "player_buys", Amount = 5_000_000_000L }],
        Panels = [new VisiblePanel { Kind = "negotiation", Text = ["Visible trade text"], Controls = [new VisibleControl { Text = "Buy", Enabled = true }] }]
    };

    private static InteractionSignals Ready() => new() { GameLoaded = true, InStore = true };
}
