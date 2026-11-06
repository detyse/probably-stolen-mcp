namespace PSMcp.Contracts;

/// <summary>Pure decisions shared by the game adapter and regression tests.</summary>
public static class ObservationRules
{
    public static string TradeDirection(string clientIntent) => clientIntent switch
    {
        "BUY" => "player_sells",
        "SELL" => "player_buys",
        "SELLNBUY" => "mixed",
        "BARTER" => "barter",
        "WHOLESALE" => "wholesale",
        _ => "other"
    };

    // Mirrors NegociationUIManager.AddFeatureSection, including hidden public features.
    public static bool FeatureVisible(bool initiallyShown, bool disabled, bool publicHidden, bool exposed)
        => initiallyShown && !disabled && (!publicHidden || exposed);

    public static bool QuoteApplies(string direction, bool playerOwned, bool businessComplete, string? refusal)
        => !businessComplete && string.IsNullOrEmpty(refusal)
            && (direction == "mixed" || (playerOwned ? direction == "player_sells" : direction == "player_buys"));

    public static bool BlocksShopActions(InteractionSignals signals)
        => !signals.InStore || signals.ConfirmationVisible || signals.InspectionVisible
            || signals.OtherModalVisible || signals.GlobalLocked || signals.TradeLocked
            || signals.ManualDialogueAdvance || signals.ConversationActive || signals.ChoiceCount > 0;

    public static bool BlocksDialogueChoices(InteractionSignals signals)
        => !signals.InStore || signals.ConfirmationVisible || signals.InspectionVisible
            || signals.OtherModalVisible || signals.GlobalLocked || signals.TradeLocked;

    public static InteractionInfo Describe(InteractionSignals signals, bool hasAvailableActions)
    {
        if (!signals.GameLoaded) return State("game_not_loaded", "player", "Load a game to observe shop interactions.", "load_game");
        // Modal prompts take precedence over background lock flags and dialogue.
        if (signals.ConfirmationVisible) return State("confirmation", "player", "Resolve the visible confirmation using a listed UI action.", "confirm_or_cancel", "confirmation");
        if (signals.OtherModalVisible) return State("modal", "player", "A visible game panel requires input before normal shop interaction can continue.", "panel_input", "modal");
        if (signals.InspectionVisible) return State("inspection", "player", "An inspection, confrontation or appraisal panel is open.", "inspection_input", "inspection");
        if (!signals.InStore) return State("outside_store", "player", "The player is outside the shop. Inspect the current controls, containers and availableActions.", "outside_store_input", "outside_store");
        if (signals.ChoiceCount > 0)
        {
            var canChoose = signals.EnabledChoiceCount > 0 && !BlocksDialogueChoices(signals);
            var result = State("dialogue_choice", canChoose ? "player" : "game",
                canChoose ? "Select an enabled dialogue_choice action." : "Dialogue options are visible but currently locked.",
                canChoose ? "choose_dialogue" : null, canChoose ? null : "dialogue_locked");
            result.McpSupported = canChoose && hasAvailableActions;
            return result;
        }
        if (signals.ManualDialogueAdvance) return State("dialogue_advance", "player", "The game is waiting for advance_dialogue. Use it only when listed in availableActions.", "advance_dialogue", "manual_dialogue_advance");
        if (signals.ConversationActive) return State("dialogue_playing", "game", "Dialogue is running. Observe again after it progresses.", null, "dialogue_playing");
        if (signals.GlobalLocked || signals.TradeLocked) return State("game_busy", "game", "The game has locked interaction; observe again after the transition or event.", null, "game_locked");
        if (signals.TradeActive) return State(signals.BargainingOpen ? "bargaining" : "trade", "player", "A customer trade is pending. Inspect quotes and the current UI actions; opening the customer panel may be necessary.", "trade_decision");
        var ready = State("ready", hasAvailableActions ? "player" : "unknown",
            hasAvailableActions ? "Choose one of the listed availableActions." : "No supported action or recognized blocking interaction is available.",
            hasAvailableActions ? "available_action" : null);
        ready.McpSupported = hasAvailableActions;
        return ready;
    }

    private static InteractionInfo State(string kind, string waitingFor, string message, string? requiredInput, string? blocker = null)
        => new InteractionInfo
        {
            Kind = kind, WaitingFor = waitingFor, Message = message, RequiredInput = requiredInput,
            Blockers = blocker == null ? Array.Empty<string>() : new[] { blocker }
        };
}

public sealed class InteractionSignals
{
    public bool GameLoaded { get; set; }
    public bool InStore { get; set; }
    public bool ConfirmationVisible { get; set; }
    public bool InspectionVisible { get; set; }
    public bool OtherModalVisible { get; set; }
    public bool GlobalLocked { get; set; }
    public bool TradeLocked { get; set; }
    public bool ManualDialogueAdvance { get; set; }
    public bool ConversationActive { get; set; }
    public int ChoiceCount { get; set; }
    public int EnabledChoiceCount { get; set; }
    public bool TradeActive { get; set; }
    public bool BargainingOpen { get; set; }
}
