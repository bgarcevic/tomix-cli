using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace Mdl.App.Bpa;

/// <summary>
/// Evaluates a Best-Practice-Analyzer rule <c>Expression</c> (a Dynamic-LINQ predicate)
/// against a collection of adapter objects, returning the elements for which the
/// predicate is <c>true</c> (i.e. the objects in violation).
///
/// The dialect is essentially what <c>System.Linq.Dynamic.Core</c> parses natively (<c>it</c>/
/// <c>outerIt</c> iterators, implicit member access inside <c>.Any()/.Where()</c>, <c>=</c> as
/// equality, <c>and</c>/<c>or</c>/<c>not</c>). A thin normalization pass maps the few
/// dialect-specific spellings that differ. A rule whose expression cannot be parsed, or which throws
/// at runtime for an element, never produces a violation — it is skipped, never a false positive.
/// </summary>
public sealed partial class BpaExpressionEvaluator
{
    private readonly ParsingConfig _config = new()
    {
        CustomTypeProvider = new BpaTypeProvider(),
        ResolveTypesBySimpleName = true,
    };

    /// <summary>
    /// Returns the elements of <paramref name="items"/> that violate <paramref name="expression"/>.
    /// Returns an empty list (never throws) if the expression cannot be parsed.
    /// </summary>
    public IReadOnlyList<T> Evaluate<T>(string expression, IReadOnlyList<T> items)
        where T : class
    {
        if (items.Count == 0)
            return [];

        var normalized = Normalize(expression);
        var usesCurrent = CurrentKeyword().IsMatch(normalized);

        Delegate predicate;
        try
        {
            predicate = Compile<T>(normalized, usesCurrent);
        }
        catch
        {
            // Unsupported syntax or an unknown member for this scope — skip the rule.
            return [];
        }

        var violations = new List<T>();
        var args = new object[usesCurrent ? 2 : 1];
        foreach (var item in items)
        {
            try
            {
                args[0] = item;
                if (usesCurrent)
                    args[1] = item;

                if (predicate.DynamicInvoke(args) is true)
                    violations.Add(item);
            }
            catch
            {
                // Runtime error for this element (e.g. converting a missing annotation) — not a violation.
            }
        }

        return violations;
    }

    private Delegate Compile<T>(string expression, bool usesCurrent)
    {
        LambdaExpression lambda;
        if (usesCurrent)
        {
            var it = Expression.Parameter(typeof(T), "it");
            var current = Expression.Parameter(typeof(T), "current");
            lambda = DynamicExpressionParser.ParseLambda(_config, [it, current], typeof(bool), expression);
        }
        else
        {
            lambda = DynamicExpressionParser.ParseLambda(_config, typeof(T), typeof(bool), expression);
        }

        return lambda.Compile();
    }

    /// <summary>Maps rule-dialect expression spellings onto what the parser understands.</summary>
    private static string Normalize(string expression)
    {
        // Enum-style literals -> string literals (the adapter stores these properties as strings).
        var result = EnumLiteral().Replace(expression, "\"$2\"");

        // The rule dialect spells it "IsNullOrWhitespace"; the BCL method is "IsNullOrWhiteSpace".
        result = result.Replace("IsNullOrWhitespace", "IsNullOrWhiteSpace");

        return result;
    }

    [GeneratedRegex(@"\b(DataType|AggregateFunction|CrossFilteringBehavior)\.([A-Za-z0-9_]+)")]
    private static partial Regex EnumLiteral();

    [GeneratedRegex(@"\bcurrent\b")]
    private static partial Regex CurrentKeyword();
}
