using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PSMcp.Contracts;

namespace PSMcp.Mod
{
    internal sealed class ActionRegistry
    {
        private sealed class Reference { public string Id = ""; }
        private ConditionalWeakTable<object, Reference> references = new ConditionalWeakTable<object, Reference>();
        private readonly Dictionary<string, Func<ActionArguments, Action>> handlers = new Dictionary<string, Func<ActionArguments, Action>>();
        private long nextId;
        private string prefix = Guid.NewGuid().ToString("N").Substring(0, 12);
        public readonly List<AvailableAction> Actions = new List<AvailableAction>();

        public string Id(object target) => references.GetValue(target, _ => new Reference { Id = "e" + prefix + "_" + ++nextId }).Id;
        public void Clear() { handlers.Clear(); Actions.Clear(); }
        public void Reset()
        {
            Clear(); references = new ConditionalWeakTable<object, Reference>();
            prefix = Guid.NewGuid().ToString("N").Substring(0, 12); nextId = 0;
        }

        public string Add(string kind, object target, string description, Func<ActionArguments, Action> prepare,
            ActionParameter[]? parameters = null, bool allowWithoutGame = false)
        {
            var id = Id(target);
            var name = kind + ":" + id;
            AddNamed(name, kind, id, description, prepare, parameters, allowWithoutGame);
            return name;
        }

        public void AddNamed(string name, string kind, string? targetId, string description, Func<ActionArguments, Action> prepare,
            ActionParameter[]? parameters = null, bool allowWithoutGame = false)
        {
            if (handlers.ContainsKey(name)) return;
            handlers.Add(name, prepare);
            Actions.Add(new AvailableAction { Name = name, Kind = kind, TargetId = targetId, Description = description,
                Parameters = parameters ?? Array.Empty<ActionParameter>(), AllowWithoutGame = allowWithoutGame });
        }

        public Action Prepare(string name, ActionArguments arguments) => handlers.TryGetValue(name, out var prepare)
            ? prepare(arguments) : throw new ActionRejectedException("Action is no longer available.");

        public static ActionParameter Number(string name, double min, double max, double? step = null, bool required = true) =>
            new ActionParameter { Name = name, Type = "number", Min = min, Max = max, Step = step, Required = required };
        public static ActionParameter Integer(string name, int min, int max, bool required = true) =>
            new ActionParameter { Name = name, Type = "integer", Min = min, Max = max, Required = required };
    }
}
