using static Tomix.App.Dax.DaxCalculatedTableColumns;

namespace Tomix.App.Dax;

/// <summary>
/// Names an expression may define for itself as query-scoped columns, which an unqualified
/// <c>[Name]</c> can resolve to without the model having such a column: every string literal
/// (the <c>"@Krav"</c> of <c>ADDCOLUMNS</c>, <c>SELECTCOLUMNS</c>, <c>SUMMARIZE</c>, ...), plus
/// <c>Value</c>/<c>ValueN</c> when the expression builds a table with <c>GENERATESERIES</c> or a
/// <c>{ ... }</c> constructor. A heuristic: a literal that only happens to match a name hides a
/// warning, never raises an error.
/// </summary>
internal static class DaxQueryColumns
{
    public static bool Defines(string expression, string name)
    {
        var tokens = Tokenize(expression);

        if (tokens.Any(t => t.Kind == Kind.String && string.Equals(t.Text, name, StringComparison.OrdinalIgnoreCase)))
            return true;

        return IsValueColumn(name)
            && tokens.Any(t =>
                (t.Kind == Kind.Symbol && t.Text == "{")
                || (t.Kind == Kind.Word && t.Text.Equals("GENERATESERIES", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsValueColumn(string name)
        => name.StartsWith("Value", StringComparison.OrdinalIgnoreCase)
           && name[5..].All(char.IsAsciiDigit);
}
