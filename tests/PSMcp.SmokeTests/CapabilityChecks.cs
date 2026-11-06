using Newtonsoft.Json;
using PSMcp.Contracts;
using PSMcp.Mod;

internal static class CapabilityChecks
{
    public static void Run(Action<bool, string> check)
    {
        var game = new ArgumentGame();
        var session = new GameSession(game, s => JsonConvert.SerializeObject(s, HttpBridge.JsonSettings));
        ActionRequest Command(ActionArguments args) => new() { Action = "set_value", RequestId = Guid.NewGuid().ToString(), ExpectedSnapshotId = session.Observe().SnapshotId, Arguments = args };
        var first = Command(new ActionArguments { Value = 4 });
        check(session.Perform(first).Success && game.Value == 4 && game.Effects == 1, "structured values reach the prepared game handler");
        check(session.Perform(first).Success && game.Effects == 1, "parameterized retries do not repeat native callbacks");
        var changed = new ActionRequest { Action = first.Action, RequestId = first.RequestId, ExpectedSnapshotId = first.ExpectedSnapshotId, Arguments = new ActionArguments { Value = 6 } };
        check(session.Perform(changed).ErrorCode == "request_id_conflict" && game.Effects == 1, "same UUID with changed arguments is rejected");
        check(session.GetActionStatus(first.RequestId.ToUpperInvariant()).ActionStatus == "dispatched" && game.Effects == 1, "status reads canonical UUIDs without executing actions");
        foreach (var arguments in new[] { new ActionArguments(), new ActionArguments { Value = 11 }, new ActionArguments { Value = 3 }, new ActionArguments { Value = double.NaN }, new ActionArguments { Value = double.PositiveInfinity }, new ActionArguments { Value = 2, Text = "unlisted" } })
            check(session.Perform(Command(arguments)).ErrorCode == "invalid_arguments" && game.Effects == 1, "invalid/missing/extra/out-of-step values never dispatch");
        game.RejectPreparation = true;
        check(session.Perform(Command(new ActionArguments { Value = 2 })).ActionStatus == "rejected" && game.Effects == 1, "game-side preparation can reject before any effects");
        game.RejectPreparation = false;
        var beforeRangeChange = Command(new ActionArguments { Value = 10 });
        game.Maximum = 8;
        check(session.Perform(beforeRangeChange).ErrorCode == "stale_snapshot", "changed machine parameter bounds invalidate decisions");
        var beforeReload = Command(new ActionArguments { Value = 2 });
        session.Reset();
        check(session.Perform(beforeReload).ErrorCode == "stale_snapshot" && session.GetActionStatus(first.RequestId).Success && game.ReferenceResets == 1,
            "save changes invalidate entity decisions while retaining old command outcomes");
        check(session.GetActionStatus(Guid.NewGuid().ToString()).ErrorCode == "action_not_found", "missing outcomes are distinguished from failed or completed actions");

        var referenceAction = new AvailableAction { Parameters = [new ActionParameter { Name = "targetId", Required = true, Choices = ["visible-item"] }] };
        check(ActionValidation.Validate(referenceAction, new ActionArguments { TargetId = "hidden-item" }) != null,
            "unexposed target references cannot satisfy action parameters");
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var canonical = ActionValidation.Fingerprint(new ActionArguments { Value = 2.5, Text = "a|b:c\n" });
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            check(canonical == ActionValidation.Fingerprint(new ActionArguments { Value = 2.5, Text = "a|b:c\n" }), "argument deduplication is culture independent and preserves literal text");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }

        game.Loaded = false;
        check(session.Perform(Command(new ActionArguments { Value = 2 })).ErrorCode == "action_unavailable", "ordinary capabilities remain blocked without a loaded game");
        game.AllowWithoutGame = true;
        check(session.Perform(Command(new ActionArguments { Value = 2 })).Success, "only explicitly marked menu capabilities work without a loaded save");
        for (var i = 0; i < 257; i++) session.Perform(Command(new ActionArguments { Value = 2 }));
        check(session.GetActionStatus(first.RequestId).ErrorCode == "action_not_found", "action status retention is bounded to 256 commands");
    }

    private sealed class ArgumentGame : IGameAdapter, IResettableGameAdapter
    {
        public int Effects;
        public double Value;
        public double Maximum = 10;
        public bool RejectPreparation;
        public bool Loaded = true;
        public bool AllowWithoutGame;
        public int ReferenceResets;
        public void ResetReferences() => ReferenceResets++;
        public GameSnapshot Capture() => new()
        {
            GameLoaded = Loaded, Controls = [new UiControl { EntityId = "control", Value = Value }],
            AvailableActions = [new AvailableAction { Name = "set_value", AllowWithoutGame = AllowWithoutGame,
                Parameters = [new ActionParameter { Name = "value", Type = "number", Min = 0, Max = Maximum, Step = 2, Required = true }] }]
        };
        public Action Prepare(string action, ActionArguments args)
        {
            if (RejectPreparation) throw new ActionRejectedException("Target is locked.");
            return () => { Value = args.Value!.Value; Effects++; };
        }
    }
}
