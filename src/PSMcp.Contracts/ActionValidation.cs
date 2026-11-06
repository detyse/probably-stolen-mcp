using System.Globalization;

namespace PSMcp.Contracts;

public sealed class ActionRejectedException : Exception
{
    public ActionRejectedException(string message) : base(message) { }
}

public static class ActionValidation
{
    public static string Fingerprint(ActionArguments args) => string.Join("|", args.Values().Select(pair =>
    {
        var value = Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? "";
        return pair.Key + ":" + value.Length + ":" + value;
    }));

    public static string? Validate(AvailableAction action, ActionArguments args)
    {
        var values = args.Values().ToDictionary(x => x.Key, x => x.Value);
        if (values.Keys.Any(key => !action.Parameters.Any(p => p.Name == key)))
            return "Only parameters listed for this action are accepted.";
        foreach (var p in action.Parameters)
        {
            if (!values.TryGetValue(p.Name, out var value))
            {
                if (p.Required) return "Missing parameter: " + p.Name;
                continue;
            }
            if (p.Type == "boolean")
            {
                if (!(value is bool)) return "Expected a boolean: " + p.Name;
            }
            else if (p.Type == "integer" || p.Type == "number")
            {
                if (!(value is int) && !(value is double)) return "Expected a number: " + p.Name;
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(number) || double.IsInfinity(number)
                    || (p.Type == "integer" && number != Math.Truncate(number))
                    || (p.Min.HasValue && number < p.Min) || (p.Max.HasValue && number > p.Max))
                    return "Parameter outside the allowed range: " + p.Name;
                if (p.Step > 0)
                {
                    var steps = (number - (p.Min ?? 0)) / p.Step.Value;
                    if (Math.Abs(steps - Math.Round(steps)) > 0.0001) return "Parameter does not match the step: " + p.Name;
                }
            }
            else if (value is string text)
            {
                if (text.Length > (p.Max ?? 4096) || text.Length < (p.Min ?? 0)) return "Invalid text length: " + p.Name;
                if (p.Choices.Length > 0 && !p.Choices.Contains(text)) return "Choose a currently listed reference: " + p.Name;
            }
            else return "Expected a string: " + p.Name;
        }
        return null;
    }
}
