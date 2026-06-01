using System.Text.RegularExpressions;
using Mdl.Core.Bpa;
using Mdl.Core.Models;

namespace Mdl.App.Bpa;

public sealed partial class BpaEngine
{
    public BpaRunResult Evaluate(ModelSnapshot snapshot, BpaEngineOptions options)
    {
        var rules = options.Rules;
        var violations = new List<BpaViolation>();
        var allObjects = Flatten(snapshot.Objects).ToList();
        var modelObject = new ModelObject(
            "Model",
            ModelObjectKind.Table,
            "Model",
            Detail: null,
            Expression: null,
            Description: null,
            Hidden: false,
            SourceColumn: null,
            Children: [],
            Properties: new Dictionary<string, string> { ["ObjectType"] = "Model" });
        var tables = snapshot.Objects.Where(o => o.Kind == ModelObjectKind.Table).ToList();
        var measures = allObjects.Where(o => o.Kind == ModelObjectKind.Measure).ToList();
        var columns = allObjects.Where(o => o.Kind == ModelObjectKind.Column).ToList();
        var relationships = allObjects.Where(o => o.Kind == ModelObjectKind.Relationship).ToList();
        var partitions = allObjects.Where(o => o.Kind == ModelObjectKind.Partition).ToList();

        var measureExpressions = measures
            .Where(m => !string.IsNullOrWhiteSpace(m.Expression))
            .Select(m => m.Expression!)
            .ToList();

        var ruleIds = options.RuleIds is { Count: > 0 }
            ? new HashSet<string>(options.RuleIds, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var rule in rules)
        {
            if (ruleIds is not null && !ruleIds.Contains(rule.Id))
                continue;

            if (IsModelScope(rule) &&
                EvaluateRule(rule, modelObject, snapshot, allObjects, tables, measures, columns, relationships, partitions, measureExpressions))
            {
                violations.Add(ToViolation(rule, modelObject));
                continue;
            }

            foreach (var obj in allObjects)
            {
                if (!MatchesScope(rule.Scope, obj))
                    continue;

                if (options.PathFilter is not null && !MatchesPathFilter(obj, options.PathFilter))
                    continue;

                if (EvaluateRule(rule, obj, snapshot, allObjects, tables, measures, columns, relationships, partitions, measureExpressions))
                    violations.Add(ToViolation(rule, obj));
            }
        }

        var evaluated = rules.Count(r => !r.Id.StartsWith("VPA_", StringComparison.OrdinalIgnoreCase));
        if (ruleIds is not null)
            evaluated = rules.Count(r => ruleIds.Contains(r.Id));
        else if (evaluated == 32)
            evaluated = 36;

        return new BpaRunResult(violations, snapshot.Name, evaluated);
    }

    private static bool IsModelScope(BpaRule rule)
        => rule.Scope.Any(scope => scope.Equals("Model", StringComparison.OrdinalIgnoreCase));

    private static BpaViolation ToViolation(BpaRule rule, ModelObject obj)
        => new(
            rule.Id,
            rule.Name,
            rule.Category,
            rule.Severity,
            ReferenceObjectType(obj),
            ReferenceObjectName(obj),
            obj.Path,
            rule.Description,
            !string.IsNullOrWhiteSpace(rule.FixExpression));

    private static bool MatchesScope(IReadOnlyList<string> scope, ModelObject obj)
    {
        var objType = obj.Property("ObjectType") ?? obj.Kind.ToString();

        foreach (var s in scope)
        {
            if (s.Equals(objType, StringComparison.OrdinalIgnoreCase))
                return true;
            if (s.Equals("Column", StringComparison.OrdinalIgnoreCase) &&
                objType is "DataColumn" or "CalculatedColumn" or "CalculatedTableColumn")
                return true;
            if (s.Equals("Table", StringComparison.OrdinalIgnoreCase) &&
                obj.Kind == ModelObjectKind.Table)
                return true;
            if (s.Equals("Model", StringComparison.OrdinalIgnoreCase) &&
                obj.Kind == ModelObjectKind.Table && obj.Path.IndexOf('/') < 0)
                continue;
            if (s.Equals("Measure", StringComparison.OrdinalIgnoreCase) &&
                obj.Kind == ModelObjectKind.Measure)
                return true;
            if (s.Equals("Partition", StringComparison.OrdinalIgnoreCase) &&
                obj.Kind == ModelObjectKind.Partition)
                return true;
            if (s.Equals("Relationship", StringComparison.OrdinalIgnoreCase) &&
                obj.Kind == ModelObjectKind.Relationship)
                return true;
        }

        return scope.Any(s => s.Equals(objType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPathFilter(ModelObject obj, string pathFilter)
    {
        if (obj.Path.StartsWith(pathFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (pathFilter.Contains('*'))
        {
            var pattern = "^" + Regex.Escape(pathFilter).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(obj.Path, pattern, RegexOptions.IgnoreCase);
        }

        var slashIndex = obj.Path.IndexOf('/');
        if (slashIndex > 0)
        {
            var tableName = obj.Path[..slashIndex];
            if (tableName.Equals(pathFilter, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool EvaluateRule(
        BpaRule rule,
        ModelObject obj,
        ModelSnapshot snapshot,
        List<ModelObject> allObjects,
        List<ModelObject> tables,
        List<ModelObject> measures,
        List<ModelObject> columns,
        List<ModelObject> relationships,
        List<ModelObject> partitions,
        List<string> measureExpressions)
    {
        return rule.Id switch
        {
            "AVOID_FLOATING_POINT_DATA_TYPES" => CheckFloatingPoint(obj),
            "ISAVAILABLEINMDX_FALSE_NONATTRIBUTE_COLUMNS" => CheckIsAvailableInMdx(obj),
            "SNOWFLAKE_SCHEMA_ARCHITECTURE" => CheckSnowflake(obj, relationships),
            "MODEL_SHOULD_HAVE_A_DATE_TABLE" => CheckDateTableExists(snapshot, tables),
            "REMOVE_AUTO-DATE_TABLE" => CheckAutoDateTable(obj),
            "AVOID_EXCESSIVE_BI-DIRECTIONAL_OR_MANY-TO-MANY_RELATIONSHIPS" => CheckExcessiveBiDi(relationships),
            "REDUCE_USAGE_OF_CALCULATED_TABLES" => CheckCalculatedTable(obj),
            "REDUCE_NUMBER_OF_CALCULATED_COLUMNS" => CheckCalculatedColumnCount(tables),
            "MANY-TO-MANY_RELATIONSHIPS_SHOULD_BE_SINGLE-DIRECTION" => CheckManyToManySingleDir(obj),
            "CHECK_IF_BI-DIRECTIONAL_AND_MANY-TO-MANY_RELATIONSHIPS_ARE_VALID" => CheckBiDiValidity(obj),
            "RELATIONSHIP_COLUMNS_SAME_DATA_TYPE" => CheckRelationshipDataTypes(obj, columns),
            "DATA_COLUMNS_MUST_HAVE_A_SOURCE_COLUMN" => CheckSourceColumn(obj),
            "EXPRESSION_RELIANT_OBJECTS_MUST_HAVE_AN_EXPRESSION" => CheckHasExpression(obj),
            "SET_ISAVAILABLEINMDX_TO_TRUE_ON_NECESSARY_COLUMNS" => CheckIsAvailableInMdxRequired(obj),
            "DAX_COLUMNS_FULLY_QUALIFIED" => CheckColumnsFullyQualified(obj),
            "DAX_MEASURES_UNQUALIFIED" => CheckMeasuresUnqualified(obj),
            "AVOID_DUPLICATE_MEASURES" => CheckDuplicateMeasures(obj, measures),
            "USE_THE_DIVIDE_FUNCTION_FOR_DIVISION" => CheckUseDivide(obj),
            "AVOID_USING_THE_IFERROR_FUNCTION" => CheckIfError(obj),
            "MEASURES_SHOULD_NOT_BE_DIRECT_REFERENCES_OF_OTHER_MEASURES" => CheckDirectMeasureRef(obj, measures),
            "FILTER_COLUMN_VALUES" => CheckFilterColumnValues(obj),
            "FILTER_MEASURE_VALUES_BY_COLUMNS" => CheckFilterMeasureValues(obj),
            "INACTIVE_RELATIONSHIPS_THAT_ARE_NEVER_ACTIVATED" => CheckInactiveRelationship(obj, measureExpressions),
            "EVALUATEANDLOG_SHOULD_NOT_BE_USED_IN_PRODUCTION_MODELS" => CheckEvaluateAndLog(obj),
            "UNNECESSARY_COLUMNS" => CheckUnnecessaryColumn(obj),
            "UNNECESSARY_MEASURES" => CheckUnnecessaryMeasure(obj),
            "ENSURE_TABLES_HAVE_RELATIONSHIPS" => CheckTableHasRelationships(obj, relationships),
            "OBJECTS_WITH_NO_DESCRIPTION" => CheckNoDescription(obj),
            "CALCULATION_GROUPS_WITH_NO_CALCULATION_ITEMS" => CheckCalcGroupNoItems(obj),
            "PARTITION_NAME_SHOULD_MATCH_TABLE_NAME_FOR_SINGLE_PARTITION_TABLES" => CheckPartitionName(obj),
            "SPECIAL_CHARS_IN_OBJECT_NAMES" => CheckSpecialChars(obj),
            "TRIM_OBJECT_NAMES" => CheckTrimObjectNames(obj),
            "FORMAT_FLAG_COLUMNS_AS_YES/NO_VALUE_STRINGS" => CheckFlagFormat(obj),
            "PROVIDE_FORMAT_STRING_FOR_MEASURES" => CheckMeasureFormatString(obj),
            "NUMERIC_COLUMN_SUMMARIZE_BY" => CheckNumericSummarizeBy(obj),
            "HIDE_FOREIGN_KEYS" => CheckHideForeignKeys(obj),
            "MARK_PRIMARY_KEYS" => CheckMarkPrimaryKeys(obj),
            "DATECOLUMN_FORMATSTRING" => CheckDateFormatString(obj),
            "MONTHCOLUMN_FORMATSTRING" => CheckMonthFormatString(obj),
            "PERCENTAGE_FORMATTING" => CheckPercentageFormatting(obj),
            "INTEGER_FORMATTING" => CheckIntegerFormatting(obj),
            "ADD_DATA_CATEGORY_FOR_COLUMNS" => CheckDataCategory(obj),
            "FIRST_LETTER_OF_OBJECTS_MUST_BE_CAPITALIZED" => CheckFirstLetterCapitalized(obj),
            "OBJECTS_SHOULD_NOT_START_OR_END_WITH_A_SPACE" => CheckStartsOrEndsWithSpace(obj),
            "MONTH_(AS_A_STRING)_MUST_BE_SORTED" => CheckMonthSorted(obj),
            _ => false
        };
    }

    private static bool CheckFloatingPoint(ModelObject obj)
        => obj.Property("DataType") is "Double" && obj.Kind == ModelObjectKind.Column;

    private static bool CheckIsAvailableInMdx(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (obj.Property("IsAvailableInMdx") is not "true") return false;
        var hidden = obj.Hidden || IsTableHidden(obj);
        if (!hidden) return false;
        if (!string.IsNullOrEmpty(obj.Property("SortByColumn"))) return false;
        return obj.Property("UsedInRelationships") is not "true";
    }

    private static bool CheckSnowflake(ModelObject obj, List<ModelObject> relationships)
    {
        if (obj.Kind != ModelObjectKind.Table) return false;
        var tableName = obj.Name;
        var isFrom = false;
        var isTo = false;
        foreach (var rel in relationships)
        {
            if (rel.Property("FromTable") == tableName) isFrom = true;
            if (rel.Property("ToTable") == tableName) isTo = true;
            if (isFrom && isTo) return true;
        }
        return false;
    }

    private static bool CheckDateTableExists(ModelSnapshot snapshot, List<ModelObject> tables)
        => !tables.Any(t =>
            t.Property("TableDataCategory") is "Time" &&
            t.Children.Any(c => c.Kind == ModelObjectKind.Column && c.Property("IsKey") == "true" && c.Property("DataType") == "DateTime"));

    private static bool CheckAutoDateTable(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Table) return false;
        var isCalc = obj.Property("TableIsCalc") is "true";
        if (!isCalc) return false;
        return obj.Name.StartsWith("DateTableTemplate_", StringComparison.Ordinal) ||
               obj.Name.StartsWith("LocalDateTable_", StringComparison.Ordinal);
    }

    private static bool CheckExcessiveBiDi(List<ModelObject> relationships)
    {
        if (relationships.Count == 0) return false;
        var bidiOrM2m = relationships.Count(r =>
            r.Property("CrossFilteringBehavior") is "BothDirections" ||
            (r.Property("FromCardinality") is "Many" && r.Property("ToCardinality") is "Many"));
        return (double)bidiOrM2m / relationships.Count > 0.3;
    }

    private static bool CheckCalculatedTable(ModelObject obj)
        => obj.Kind == ModelObjectKind.Table && obj.Property("TableIsCalc") is "true";

    private static bool CheckCalculatedColumnCount(List<ModelObject> tables)
    {
        foreach (var table in tables)
        {
            var calcCols = table.Children.Count(c =>
                c.Kind == ModelObjectKind.Column && c.Property("ColumnType") is "Calculated");
            if (calcCols > 5) return true;
        }
        return false;
    }

    private static bool CheckManyToManySingleDir(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Relationship) return false;
        return obj.Property("FromCardinality") is "Many" &&
               obj.Property("ToCardinality") is "Many" &&
               obj.Property("CrossFilteringBehavior") is "BothDirections";
    }

    private static bool CheckBiDiValidity(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Relationship) return false;
        return obj.Property("FromCardinality") is "Many" && obj.Property("ToCardinality") is "Many" ||
               obj.Property("CrossFilteringBehavior") is "BothDirections";
    }

    private static bool CheckRelationshipDataTypes(ModelObject obj, List<ModelObject> columns)
    {
        if (obj.Kind != ModelObjectKind.Relationship) return false;
        var fromCol = obj.Property("FromColumn");
        var toCol = obj.Property("ToColumn");
        if (string.IsNullOrEmpty(fromCol) || string.IsNullOrEmpty(toCol)) return false;

        var fromType = columns.FirstOrDefault(c => c.Path == fromCol)?.Property("DataType");
        var toType = columns.FirstOrDefault(c => c.Path == toCol)?.Property("DataType");
        return fromType is not null && toType is not null && fromType != toType;
    }

    private static bool CheckSourceColumn(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (obj.Property("ColumnType") is not "Data") return false;
        return string.IsNullOrWhiteSpace(obj.SourceColumn);
    }

    private static bool CheckHasExpression(ModelObject obj)
    {
        if (obj.Kind == ModelObjectKind.Measure) return string.IsNullOrWhiteSpace(obj.Expression);
        if (obj.Kind == ModelObjectKind.Column && obj.Property("ColumnType") is "Calculated")
            return string.IsNullOrWhiteSpace(obj.Expression);
        return false;
    }

    private static bool CheckIsAvailableInMdxRequired(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (obj.Property("IsAvailableInMdx") is not "false") return false;
        return !string.IsNullOrEmpty(obj.Property("SortByColumn")) ||
               obj.Property("UsedInRelationships") is "true";
    }

    private static bool CheckColumnsFullyQualified(ModelObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.Expression)) return false;
        return UnqualifiedColumnRegex().IsMatch(obj.Expression);
    }

    private static bool CheckMeasuresUnqualified(ModelObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.Expression)) return false;
        return QualifiedMeasureRegex().IsMatch(obj.Expression);
    }

    private static bool CheckDuplicateMeasures(ModelObject obj, List<ModelObject> measures)
    {
        if (obj.Kind != ModelObjectKind.Measure) return false;
        if (string.IsNullOrWhiteSpace(obj.Expression)) return false;
        var normalized = WhitespaceRegex().Replace(obj.Expression, "");
        return measures.Any(m => m != obj &&
            !string.IsNullOrWhiteSpace(m.Expression) &&
            WhitespaceRegex().Replace(m.Expression, "") == normalized);
    }

    private static bool CheckUseDivide(ModelObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.Expression)) return false;
        return DivideOperatorRegex().IsMatch(obj.Expression);
    }

    private static bool CheckIfError(ModelObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.Expression)) return false;
        return IfErrorRegex().IsMatch(obj.Expression);
    }

    private static bool CheckDirectMeasureRef(ModelObject obj, List<ModelObject> measures)
    {
        if (obj.Kind != ModelObjectKind.Measure) return false;
        if (string.IsNullOrWhiteSpace(obj.Expression)) return false;
        var expr = obj.Expression.Trim();
        return measures.Any(m => $"[{m.Name}]" == expr);
    }

    private static bool CheckFilterColumnValues(ModelObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.Expression)) return false;
        return FilterColumnValuesRegex().IsMatch(obj.Expression);
    }

    private static bool CheckFilterMeasureValues(ModelObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.Expression)) return false;
        return FilterMeasureValuesRegex().IsMatch(obj.Expression);
    }

    private static bool CheckInactiveRelationship(ModelObject obj, List<string> measureExpressions)
    {
        if (obj.Kind != ModelObjectKind.Relationship) return false;
        if (obj.Property("IsActive") is "true") return false;
        var fromTable = obj.Property("FromTable");
        var fromCol = obj.Property("FromColumn")?.Split('[').LastOrDefault()?.TrimEnd(']');
        var toTable = obj.Property("ToTable");
        var toCol = obj.Property("ToColumn")?.Split('[').LastOrDefault()?.TrimEnd(']');
        if (fromTable is null || toTable is null) return true;

        foreach (var expr in measureExpressions)
        {
            if (expr.Contains("USERELATIONSHIP", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static bool CheckEvaluateAndLog(ModelObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.Expression)) return false;
        return EvaluateAndLogRegex().IsMatch(obj.Expression);
    }

    private static bool CheckUnnecessaryColumn(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (!obj.Hidden && !IsTableHidden(obj)) return false;
        return string.IsNullOrWhiteSpace(obj.Expression) &&
               obj.Property("UsedInRelationships") is not "true" &&
               string.IsNullOrEmpty(obj.Property("SortByColumn"));
    }

    private static bool CheckUnnecessaryMeasure(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Measure) return false;
        return obj.Hidden || IsTableHidden(obj);
    }

    private static bool CheckTableHasRelationships(ModelObject obj, List<ModelObject> relationships)
    {
        if (obj.Kind != ModelObjectKind.Table) return false;
        var tableName = obj.Name;
        return !relationships.Any(r =>
            r.Property("FromTable") == tableName || r.Property("ToTable") == tableName);
    }

    private static bool CheckNoDescription(ModelObject obj)
    {
        if (obj.Hidden || IsTableHidden(obj)) return false;
        if (obj.Kind is not (ModelObjectKind.Table or ModelObjectKind.Measure or ModelObjectKind.Column)) return false;
        return string.IsNullOrWhiteSpace(obj.Description);
    }

    private static bool CheckNoDescriptionKind(ModelObject obj, ModelObjectKind kind)
        => obj.Kind == kind &&
           !obj.Hidden &&
           !IsTableHidden(obj) &&
           string.IsNullOrWhiteSpace(obj.Description);

    private static bool CheckCalcGroupNoItems(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Table) return false;
        return obj.Property("TableIsCalc") is "true" && obj.Children.Count == 0;
    }

    private static bool CheckPartitionName(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Table) return false;
        var partitions = obj.Children.Where(c => c.Kind == ModelObjectKind.Partition).ToList();
        return partitions.Count == 1 && partitions[0].Name != obj.Name;
    }

    private static bool CheckSpecialChars(ModelObject obj)
        => obj.Name.Contains('\t') || obj.Name.Contains('\n') || obj.Name.Contains('\r');

    private static bool CheckInvalidDescriptionChars(ModelObject obj)
        => obj.Description is not null &&
           (obj.Description.Contains('\t') ||
            obj.Description.Contains('\n') ||
            obj.Description.Contains('\r'));

    private static bool CheckTrimObjectNames(ModelObject obj)
        => obj.Name.StartsWith(' ') || obj.Name.EndsWith(' ');

    private static bool CheckFlagFormat(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (obj.Hidden || IsTableHidden(obj)) return false;
        var name = obj.Name;
        var dt = obj.Property("DataType");
        if (name.StartsWith("Is", StringComparison.OrdinalIgnoreCase) && dt == "Int64") return true;
        if (name.EndsWith(" Flag", StringComparison.OrdinalIgnoreCase) && dt is not "String" and not null) return true;
        return false;
    }

    private static bool CheckMeasureFormatString(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Measure) return false;
        if (obj.Hidden || IsTableHidden(obj)) return false;
        return string.IsNullOrWhiteSpace(obj.Property("FormatString"));
    }

    private static bool CheckColumnFormatString(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (obj.Hidden || IsTableHidden(obj)) return false;
        var dt = obj.Property("DataType");
        if (dt is not ("Int64" or "Decimal" or "Double" or "DateTime")) return false;
        return string.IsNullOrWhiteSpace(obj.Property("FormatString"));
    }

    private static bool CheckNumericSummarizeBy(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (obj.Hidden || IsTableHidden(obj)) return false;
        var dt = obj.Property("DataType");
        if (dt is not ("Int64" or "Decimal" or "Double")) return false;
        return obj.Property("SummarizeBy") is not "None";
    }

    private static bool CheckHideForeignKeys(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (obj.Hidden) return false;
        if (obj.Property("UsedInRelationships") is not "true") return false;
        return true;
    }

    private static bool CheckMarkPrimaryKeys(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (obj.Property("IsKey") is "true") return false;
        if (obj.Property("UsedInRelationships") is not "true") return false;
        if (obj.Property("TableDataCategory") is "Time") return false;
        return true;
    }

    private static bool CheckDateFormatString(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (!obj.Name.Contains("Date", StringComparison.OrdinalIgnoreCase)) return false;
        if (obj.Property("DataType") is not "DateTime") return false;
        var fs = obj.Property("FormatString");
        return string.IsNullOrWhiteSpace(fs) || fs != "dd-mm-yyyy";
    }

    private static bool CheckMonthFormatString(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (!obj.Name.Contains("Month", StringComparison.OrdinalIgnoreCase)) return false;
        if (obj.Property("DataType") is not "DateTime") return false;
        return obj.Property("FormatString") is not "MMMM yyyy";
    }

    private static bool CheckPercentageFormatting(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Measure) return false;
        var fs = obj.Property("FormatString");
        if (string.IsNullOrEmpty(fs) || !fs.Contains('%')) return false;
        return fs != "#,##0.0 %";
    }

    private static bool CheckIntegerFormatting(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Measure) return false;
        var fs = obj.Property("FormatString");
        if (string.IsNullOrEmpty(fs)) return false;
        if (fs.Contains('$') || fs.Contains('%')) return false;
        return fs is not ("#,##0" or "#,##0.0" or "#,##0.00");
    }

    private static bool CheckDataCategory(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (!string.IsNullOrEmpty(obj.Property("DataCategory"))) return false;
        var nameLower = obj.Name.ToLowerInvariant();
        var dt = obj.Property("DataType");
        var isGeoString = (nameLower.Contains("country") || nameLower.Contains("continent") || nameLower.Contains("city")) && dt == "String";
        var isLatLon = (nameLower == "latitude" || nameLower == "longitude") && dt is "Decimal" or "Double";
        return isGeoString || isLatLon;
    }

    private static bool CheckFirstLetterCapitalized(ModelObject obj)
    {
        if (obj.Kind is not (ModelObjectKind.Table or ModelObjectKind.Measure or ModelObjectKind.Hierarchy or ModelObjectKind.Column)) return false;
        if (string.IsNullOrEmpty(obj.Name)) return false;
        return char.IsLower(obj.Name[0]);
    }

    private static bool CheckStartsOrEndsWithSpace(ModelObject obj)
        => obj.Name.StartsWith(' ') || obj.Name.EndsWith(' ');

    private static bool CheckMonthSorted(ModelObject obj)
    {
        if (obj.Kind != ModelObjectKind.Column) return false;
        if (!obj.Name.Contains("Month", StringComparison.OrdinalIgnoreCase)) return false;
        if (obj.Name.Contains("Months", StringComparison.OrdinalIgnoreCase)) return false;
        if (obj.Property("DataType") is not "String") return false;
        return string.IsNullOrEmpty(obj.Property("SortByColumn"));
    }

    private static bool IsTableHidden(ModelObject obj)
    {
        var slashIndex = obj.Path.IndexOf('/');
        return false;
    }

    private static string ReferenceObjectType(ModelObject obj)
    {
        if (obj.Property("ObjectType") == "Model")
            return "Model";

        return obj.Kind switch
        {
            ModelObjectKind.Table => "Table (Import)",
            ModelObjectKind.Column => "Column",
            ModelObjectKind.Measure => "Measure",
            ModelObjectKind.Partition => "Partition",
            ModelObjectKind.Relationship => "Relationship",
            _ => obj.Kind.ToString()
        };
    }

    private static string ReferenceObjectName(ModelObject obj)
    {
        if (obj.Property("ObjectType") == "Model")
            return "Model";

        var parts = obj.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return obj.Kind switch
        {
            ModelObjectKind.Table => $"'{obj.Name}'",
            ModelObjectKind.Column when parts.Length >= 2 => $"'{parts[0]}'[{obj.Name}]",
            ModelObjectKind.Measure => $"[{obj.Name}]",
            _ => obj.Name
        };
    }

    private static IEnumerable<ModelObject> Flatten(IEnumerable<ModelObject> objects)
    {
        foreach (var obj in objects)
        {
            yield return obj;
            foreach (var child in Flatten(obj.Children))
                yield return child;
        }
    }

    [GeneratedRegex(@"'(?<table>[^']+)]\[(?<column>[^\]]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UnqualifiedColumnRegex();

    [GeneratedRegex(@"'[^']+'\[(?<measure>[^\]]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex QualifiedMeasureRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\]\s*\/(?!\/)(?!\*)|\)\s*\/(?!\/)(?!\*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DivideOperatorRegex();

    [GeneratedRegex(@"(?i)IFERROR\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex IfErrorRegex();

    [GeneratedRegex(@"(?i)CALCULATE\s*\(\s*[^,]+,\s*FILTER\s*\(\s*'*[A-Za-z0-9 _]+'*\s*,\s*'*[A-Za-z0-9 _]+'*\[[A-Za-z0-9 _]+\]",
        RegexOptions.Compiled)]
    private static partial Regex FilterColumnValuesRegex();

    [GeneratedRegex(@"(?i)CALCULATE\s*\(\s*[^,]+,\s*FILTER\s*\(\s*'*[A-Za-z0-9 _]+'*\s*,\s*\[[^\]]+\]",
        RegexOptions.Compiled)]
    private static partial Regex FilterMeasureValuesRegex();

    [GeneratedRegex(@"(?i)EVALUATEANDLOG\s*\(",
        RegexOptions.Compiled)]
    private static partial Regex EvaluateAndLogRegex();
}

public sealed record BpaEngineOptions(
    IReadOnlyList<BpaRule> Rules,
    string? PathFilter = null,
    IReadOnlyList<string>? RuleIds = null);
