namespace Prdb.Fab.Core.Automation;

/// <summary>The rules that can be checked without reading application state.</summary>
public static class AutomationRules
{
    public static bool SizeFits(long? size, long? minimumSize, long? maximumSize)
    {
        if (minimumSize is null && maximumSize is null) return true;
        if (size is null) return false;
        return (minimumSize is null || size >= minimumSize)
            && (maximumSize is null || size <= maximumSize);
    }

    public static AutomationRuleValidation Validate(
        string? name,
        bool enabled,
        long? minimumSize,
        long? maximumSize,
        int allowedIndexerCount)
    {
        if (string.IsNullOrWhiteSpace(name))
            return AutomationRuleValidation.Invalid("Give the Automation Rule a name.");
        if (minimumSize is < 0 || maximumSize is < 0)
            return AutomationRuleValidation.Invalid("Size limits cannot be negative.");
        if (minimumSize is { } minimum && maximumSize is { } maximum && minimum > maximum)
            return AutomationRuleValidation.Invalid("The minimum size cannot exceed the maximum size.");
        if (enabled && allowedIndexerCount == 0)
            return AutomationRuleValidation.Invalid("An enabled Automation Rule needs at least one allowed Indexer.");
        return AutomationRuleValidation.Valid;
    }
}

public sealed record AutomationRuleValidation(bool Accepted, string? Detail)
{
    public static AutomationRuleValidation Valid { get; } = new(true, null);
    public static AutomationRuleValidation Invalid(string detail) => new(false, detail);
}
