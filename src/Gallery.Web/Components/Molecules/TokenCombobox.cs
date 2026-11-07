namespace Gallery.Web.Components.Molecules;

/// <summary>A current value of a <see cref="TokenCombobox"/>. For <see cref="ComboPending.Create"/>, <see cref="Id"/> is the typed label.</summary>
public sealed record ComboToken(string Id, string Label, string? Href = null, ComboPending Pending = ComboPending.No);

/// <summary>Whether a token is stored (<see cref="No"/>), picked from the options, or created from typed text, and not yet saved.</summary>
public enum ComboPending { No, Add, Create }

/// <summary>One searchable choice. <see cref="Detail"/> follows the label on the same line; <see cref="Warning"/> is shown while the option is a pending add.</summary>
public sealed record ComboOption(string Id, string Label, string? Detail = null, string? Warning = null);

/// <summary>The result of <see cref="TokenCombobox.Match"/>: the rows to show in order, the create row's text, the too-long message and the status announcement.</summary>
public sealed record ComboMatch(IReadOnlyList<ComboOption> Shown, string? CreateLabel, string? Message, string Status);

public partial class TokenCombobox
{
    /// <summary>
    /// THE filter rules (contracts K1). Pure, no I/O. <c>admin-assets/library.js</c> mirrors them,
    /// and a browser parity test pins the two together.
    /// </summary>
    public static ComboMatch Match(string query, IReadOnlyList<ComboOption> options, IReadOnlyList<ComboToken> tokens,
        int maxShown, int? createMax, bool allowCreate)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) return new ComboMatch([], null, null, "");

        var taken = new HashSet<string>(tokens.Where(t => t.Pending != ComboPending.Create).Select(t => t.Id), StringComparer.Ordinal);

        var shown = options
            .Where(o => o.Label.Contains(q, StringComparison.OrdinalIgnoreCase) && !taken.Contains(o.Id))
            .OrderBy(o => Rank(o.Label, q))
            .ThenBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .Take(Math.Max(0, maxShown))
            .ToList();

        var exact = options.Any(o => string.Equals(o.Label, q, StringComparison.OrdinalIgnoreCase))
            || tokens.Any(t => t.Pending == ComboPending.Create && string.Equals(t.Label, q, StringComparison.OrdinalIgnoreCase));

        string? createLabel;
        string? message;
        if (allowCreate && createMax is int m && q.Length > m)
        {
            createLabel = null;
            message = $"Too long: at most {m} characters.";
        }
        else
        {
            createLabel = allowCreate && !exact ? q : null;
            message = null;
        }

        var n = shown.Count + (createLabel is null ? 0 : 1);
        var status = n switch
        {
            0 when exact => "Already added",
            0 => "No matches",
            1 => "1 option",
            _ => $"{n} options",
        };

        return new ComboMatch(shown, createLabel, message, status);
    }

    private static int Rank(string label, string q)
        => string.Equals(label, q, StringComparison.OrdinalIgnoreCase) ? 0
         : label.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1
         : 2;
}
