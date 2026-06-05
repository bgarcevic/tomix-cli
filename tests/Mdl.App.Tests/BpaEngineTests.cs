using Mdl.App.Bpa;
using Mdl.Core.Bpa;
using Mdl.Core.Models;

namespace Mdl.App.Tests;

public sealed class BpaEngineTests
{
    [Fact]
    public void Evaluate_NoViolationsOnEmptyModel()
    {
        var snapshot = new ModelSnapshot("TestModel", 1601, []);
        var engine = new BpaEngine();
        var rules = new List<BpaRule>
        {
            new("HIDE_FK", "Hide FK", "Formatting", BpaSeverity.Warning, ["Column"])
        };

        var result = engine.Evaluate(snapshot, new BpaEngineOptions(rules));

        Assert.Empty(result.Violations);
        Assert.Equal("TestModel", result.ModelName);
        Assert.Equal(1, result.RulesEvaluated);
    }

    [Fact]
    public void Evaluate_DetectsFloatingPoint()
    {
        var snapshot = CreateSnapshotWithColumn(
            new ModelObject("Amount", ModelObjectKind.Column, "Table/Amount",
                Detail: null, Expression: null, Description: null, Hidden: false,
                SourceColumn: "Amount",
                Children: [],
                Properties: new Dictionary<string, string> { ["DataType"] = "Double", ["ObjectType"] = "DataColumn" }));

        var rules = new List<BpaRule>
        {
            new("AVOID_FLOATING_POINT_DATA_TYPES", "No float", "Performance", BpaSeverity.Warning,
                ["DataColumn", "CalculatedColumn", "CalculatedTableColumn"],
                Expression: "DataType = \"Double\"",
                FixExpression: "DataType = DataType.Decimal")
        };

        var engine = new BpaEngine();
        var result = engine.Evaluate(snapshot, new BpaEngineOptions(rules));

        var violation = Assert.Single(result.Violations);
        Assert.Equal("AVOID_FLOATING_POINT_DATA_TYPES", violation.RuleId);
        Assert.True(violation.CanFix);
        Assert.Equal(ModelObjectKind.Column, violation.ObjectKind);
    }

    [Fact]
    public void Evaluate_ViolationCanFix_TrueWhenFixExpression()
    {
        var snapshot = CreateSnapshotWithColumn(
            new ModelObject("Col", ModelObjectKind.Column, "T/Col",
                Detail: null, Expression: null, Description: null, Hidden: false,
                SourceColumn: "Col",
                Children: [],
                Properties: new Dictionary<string, string> { ["ObjectType"] = "DataColumn" }));

        var rules = new List<BpaRule>
        {
            new("HAS_FIX", "With fix", "Test", BpaSeverity.Info, ["DataColumn"],
                Expression: "not IsHidden",
                FixExpression: "IsHidden = true"),
            new("NO_FIX", "No fix", "Test", BpaSeverity.Info, ["DataColumn"],
                Expression: "not IsHidden")
        };

        var engine = new BpaEngine();
        var result = engine.Evaluate(snapshot, new BpaEngineOptions(rules));

        Assert.All(result.Violations, v =>
        {
            if (v.RuleId == "HAS_FIX") Assert.True(v.CanFix);
            else Assert.False(v.CanFix);
        });
    }

    [Fact]
    public void Evaluate_PathFilter_FiltersByPath()
    {
        var col1 = new ModelObject("Col1", ModelObjectKind.Column, "Orders/Col1",
            Detail: null, Expression: null, Description: null, Hidden: false,
            SourceColumn: "Col1",
            Children: [],
            Properties: new Dictionary<string, string> { ["DataType"] = "Double", ["ObjectType"] = "DataColumn" });

        var col2 = new ModelObject("Col2", ModelObjectKind.Column, "Products/Col2",
            Detail: null, Expression: null, Description: null, Hidden: false,
            SourceColumn: "Col2",
            Children: [],
            Properties: new Dictionary<string, string> { ["DataType"] = "Double", ["ObjectType"] = "DataColumn" });

        var table1 = new ModelObject("Orders", ModelObjectKind.Table, "Orders",
            Detail: null, Expression: null, Description: null, Hidden: false,
            SourceColumn: null, Children: [col1],
            Properties: new Dictionary<string, string> { ["ObjectType"] = "Table" });

        var table2 = new ModelObject("Products", ModelObjectKind.Table, "Products",
            Detail: null, Expression: null, Description: null, Hidden: false,
            SourceColumn: null, Children: [col2],
            Properties: new Dictionary<string, string> { ["ObjectType"] = "Table" });

        var snapshot = new ModelSnapshot("Test", 1601, [table1, table2]);

        var rules = new List<BpaRule>
        {
            new("AVOID_FLOATING_POINT_DATA_TYPES", "No float", "Performance", BpaSeverity.Warning,
                ["DataColumn", "CalculatedColumn", "CalculatedTableColumn"],
                Expression: "DataType = \"Double\"")
        };

        var engine = new BpaEngine();
        var result = engine.Evaluate(snapshot, new BpaEngineOptions(rules, PathFilter: "Orders"));

        var violation = Assert.Single(result.Violations);
        Assert.Equal("Orders/Col1", violation.ObjectPath);
    }

    [Fact]
    public void Evaluate_RuleIds_FiltersById()
    {
        var snapshot = CreateSnapshotWithColumn(
            new ModelObject("Col", ModelObjectKind.Column, "T/Col",
                Detail: null, Expression: null, Description: null, Hidden: false,
                SourceColumn: "Col",
                Children: [],
                Properties: new Dictionary<string, string> { ["DataType"] = "Double", ["ObjectType"] = "DataColumn" }));

        var rules = new List<BpaRule>
        {
            new("RULE_A", "Rule A", "Test", BpaSeverity.Warning, ["DataColumn"], Expression: "not IsHidden"),
            new("RULE_B", "Rule B", "Test", BpaSeverity.Warning, ["DataColumn"], Expression: "not IsHidden")
        };

        var engine = new BpaEngine();
        var result = engine.Evaluate(snapshot, new BpaEngineOptions(rules, RuleIds: ["RULE_A"]));

        Assert.NotEmpty(result.Violations);
        Assert.All(result.Violations, v => Assert.Equal("RULE_A", v.RuleId));
    }

    [Fact]
    public void Evaluate_ScopeIsMatchedByObjectType_CalculatedColumnRuleSkipsDataColumns()
    {
        var dataCol = new ModelObject("DataCol", ModelObjectKind.Column, "T/DataCol",
            Detail: null, Expression: null, Description: null, Hidden: false, SourceColumn: "DataCol",
            Children: [],
            Properties: new Dictionary<string, string> { ["ObjectType"] = "DataColumn" });
        var calcCol = new ModelObject("CalcCol", ModelObjectKind.Column, "T/CalcCol",
            Detail: null, Expression: "1", Description: null, Hidden: false, SourceColumn: null,
            Children: [],
            Properties: new Dictionary<string, string> { ["ObjectType"] = "CalculatedColumn" });
        var table = new ModelObject("T", ModelObjectKind.Table, "T",
            Detail: null, Expression: null, Description: null, Hidden: false, SourceColumn: null,
            Children: [dataCol, calcCol],
            Properties: new Dictionary<string, string> { ["ObjectType"] = "Table" });
        var snapshot = new ModelSnapshot("M", 1601, [table]);

        // Scoped to CalculatedColumn only — the always-true predicate must skip the DataColumn.
        var rules = new List<BpaRule>
        {
            new("CALC_ONLY", "Calc only", "Test", BpaSeverity.Info, ["CalculatedColumn"], Expression: "not IsHidden")
        };

        var result = new BpaEngine().Evaluate(snapshot, new BpaEngineOptions(rules));

        var violation = Assert.Single(result.Violations);
        Assert.Equal("T/CalcCol", violation.ObjectPath);
    }

    private static ModelSnapshot CreateSnapshotWithColumn(ModelObject column)
    {
        var table = new ModelObject("Table", ModelObjectKind.Table, "Table",
            Detail: null, Expression: null, Description: null, Hidden: false,
            SourceColumn: null, Children: [column],
            Properties: new Dictionary<string, string> { ["ObjectType"] = "Table" });

        return new ModelSnapshot("TestModel", 1601, [table]);
    }
}
