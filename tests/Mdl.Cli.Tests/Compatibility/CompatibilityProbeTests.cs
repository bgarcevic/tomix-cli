using System.Text.Json.Nodes;

namespace Mdl.Cli.Tests.Compatibility;

public sealed class CompatibilityProbeTests
{
    [Fact]
    public void LoadBasicTmdl_MatchesReferenceSummary()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("load", "samples\\basic-tmdl"),
            CliProcess.RunMdl("load", "samples\\basic-tmdl"));
    }

    [Fact]
    public void LoadBasicTmdlJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("load", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("load", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void LoadBasicTmdlCsv_FallsBackToReferenceText()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("load", "samples\\basic-tmdl", "--output-format", "csv"),
            CliProcess.RunMdl("load", "samples\\basic-tmdl", "--output-format", "csv"));
    }

    [Fact]
    public void LoadPbip_MatchesReferenceSummary()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("load", "samples\\Artificial Intelligence Sample.pbip"),
            CliProcess.RunMdl("load", "samples\\Artificial Intelligence Sample.pbip"));
    }

    [Fact]
    public void LoadGlobalModel_MatchesReferenceSummary()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("load", "--model", "samples\\basic-tmdl"),
            CliProcess.RunMdl("load", "--model", "samples\\basic-tmdl"));
    }

    [Fact]
    public void LoadBimFile_MatchesReferenceSummary()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-bim-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.bim");
            File.WriteAllText(modelPath, BasicBim);

            var reference = CliProcess.RunReference("load", modelPath);
            var mdl = CliProcess.RunMdl("load", modelPath);

            Assert.Equal(0, reference.ExitCode);
            Assert.Equal(0, mdl.ExitCode);
            AssertSameTextIgnoringSpacingAndFooter(reference, mdl);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public void CompletionScript_MatchesReferenceShape(string shell)
    {
        var reference = CliProcess.RunReference("completion", shell);
        var mdl = CliProcess.RunMdl("completion", shell);

        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);
        Assert.Equal(ToMdlCompletionScript(reference.StdOut), mdl.StdOut.Trim());
    }

    [Fact]
    public void CompletionUnsupportedShell_MatchesReferenceExitAndError()
    {
        var reference = CliProcess.RunReference("completion", "cmd");
        var mdl = CliProcess.RunMdl("completion", "cmd");

        Assert.Equal(reference.ExitCode, mdl.ExitCode);
        Assert.Equal(reference.StdErr.Trim(), mdl.StdErr.Trim());
        Assert.Contains("completion", reference.StdOut);
        Assert.Contains("completion", mdl.StdOut);
    }

    [Fact]
    public void CompletionMissingShell_MatchesReferenceExitAndError()
    {
        var reference = CliProcess.RunReference("completion");
        var mdl = CliProcess.RunMdl("completion");

        Assert.Equal(reference.ExitCode, mdl.ExitCode);
        Assert.Equal(reference.StdErr.Trim(), mdl.StdErr.Trim());
        Assert.Contains("completion", reference.StdOut);
        Assert.Contains("completion", mdl.StdOut);
    }

    [Fact]
    public void LsBasicTmdl_ListsSameTablesAsReference()
    {
        var reference = CliProcess.RunReference("ls", "samples\\basic-tmdl");
        var mdl = CliProcess.RunMdl("ls", "samples\\basic-tmdl");

        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);
        foreach (var table in new[] { "Customers", "Products", "Sales" })
        {
            Assert.Contains(table, reference.StdOut);
            Assert.Contains(table, mdl.StdOut);
        }
    }

    [Fact]
    public void LsBasicTmdlText_MatchesReferenceTable()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("ls", "samples\\basic-tmdl"),
            CliProcess.RunMdl("ls", "samples\\basic-tmdl"));
    }

    [Fact]
    public void LsBasicTmdlJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("ls", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("ls", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void LsBasicTmdlCsv_MatchesReferenceCsv()
    {
        AssertCsvEqual(
            CliProcess.RunReference("ls", "samples\\basic-tmdl", "--output-format", "csv"),
            CliProcess.RunMdl("ls", "samples\\basic-tmdl", "--output-format", "csv"));
    }

    [Fact]
    public void LsGlobalModelJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("ls", "--model", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("ls", "--model", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void LsSalesColumnsJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("ls", "samples\\basic-tmdl", "Sales/Columns", "--output-format", "json"),
            CliProcess.RunMdl("ls", "samples\\basic-tmdl", "Sales/Columns", "--output-format", "json"));
    }

    [Fact]
    public void LsSalesColumnsText_MatchesReferenceTable()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("ls", "samples\\basic-tmdl", "Sales/Columns"),
            CliProcess.RunMdl("ls", "samples\\basic-tmdl", "Sales/Columns"));
    }

    [Fact]
    public void LsSalesColumnsCsv_MatchesReferenceCsv()
    {
        AssertCsvEqual(
            CliProcess.RunReference("ls", "samples\\basic-tmdl", "Sales/Columns", "--output-format", "csv"),
            CliProcess.RunMdl("ls", "samples\\basic-tmdl", "Sales/Columns", "--output-format", "csv"));
    }

    [Fact]
    public void LsSalesMeasuresJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("ls", "samples\\basic-tmdl", "Sales/Measures", "--output-format", "json"),
            CliProcess.RunMdl("ls", "samples\\basic-tmdl", "Sales/Measures", "--output-format", "json"));
    }

    [Fact]
    public void LsSalesMeasuresText_MatchesReferenceTable()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("ls", "samples\\basic-tmdl", "Sales/Measures"),
            CliProcess.RunMdl("ls", "samples\\basic-tmdl", "Sales/Measures"));
    }

    [Fact]
    public void LsSalesMeasuresCsv_MatchesReferenceCsv()
    {
        AssertCsvEqual(
            CliProcess.RunReference("ls", "samples\\basic-tmdl", "Sales/Measures", "--output-format", "csv"),
            CliProcess.RunMdl("ls", "samples\\basic-tmdl", "Sales/Measures", "--output-format", "csv"));
    }

    [Fact]
    public void GetSalesJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("get", "Sales", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("get", "Sales", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void GetSalesCsv_MatchesReferenceCsv()
    {
        AssertCsvEqual(
            CliProcess.RunReference("get", "Sales", "samples\\basic-tmdl", "--output-format", "csv"),
            CliProcess.RunMdl("get", "Sales", "samples\\basic-tmdl", "--output-format", "csv"));
    }

    [Fact]
    public void GetSalesTmdl_MatchesReferenceTmdl()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("get", "Sales", "samples\\basic-tmdl", "--output-format", "tmdl"),
            CliProcess.RunMdl("get", "Sales", "samples\\basic-tmdl", "--output-format", "tmdl"));
    }

    [Fact]
    public void GetSalesBim_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("get", "Sales", "samples\\basic-tmdl", "--output-format", "bim"),
            CliProcess.RunMdl("get", "Sales", "samples\\basic-tmdl", "--output-format", "bim"));
    }

    [Fact]
    public void GetMeasureTmdl_MatchesReferenceTmdl()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("get", "Sales/Total Sales", "samples\\basic-tmdl", "--output-format", "tmdl"),
            CliProcess.RunMdl("get", "Sales/Total Sales", "samples\\basic-tmdl", "--output-format", "tmdl"));
    }

    [Fact]
    public void GetMeasureBim_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("get", "Sales/Total Sales", "samples\\basic-tmdl", "--output-format", "bim"),
            CliProcess.RunMdl("get", "Sales/Total Sales", "samples\\basic-tmdl", "--output-format", "bim"));
    }

    [Fact]
    public void GetColumnTmdl_MatchesReferenceTmdl()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("get", "Sales/Amount", "samples\\basic-tmdl", "--output-format", "tmdl"),
            CliProcess.RunMdl("get", "Sales/Amount", "samples\\basic-tmdl", "--output-format", "tmdl"));
    }

    [Fact]
    public void GetColumnBim_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("get", "Sales/Amount", "samples\\basic-tmdl", "--output-format", "bim"),
            CliProcess.RunMdl("get", "Sales/Amount", "samples\\basic-tmdl", "--output-format", "bim"));
    }

    [Fact]
    public void GetPartitionTmdl_MatchesReferenceTmdl()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("get", "Sales/Sales", "samples\\basic-tmdl", "--type", "partition", "--output-format", "tmdl"),
            CliProcess.RunMdl("get", "Sales/Sales", "samples\\basic-tmdl", "--type", "partition", "--output-format", "tmdl"));
    }

    [Fact]
    public void GetPartitionBim_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("get", "Sales/Sales", "samples\\basic-tmdl", "--type", "partition", "--output-format", "bim"),
            CliProcess.RunMdl("get", "Sales/Sales", "samples\\basic-tmdl", "--type", "partition", "--output-format", "bim"));
    }

    [Fact]
    public void GetMeasureQueryText_MatchesReferenceScalar()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("get", "Sales/Total Sales", "samples\\basic-tmdl", "-q", "expression"),
            CliProcess.RunMdl("get", "Sales/Total Sales", "samples\\basic-tmdl", "-q", "expression"));
    }

    [Fact]
    public void GetMeasureQueryJson_MatchesReferenceScalar()
    {
        AssertJsonEqual(
            CliProcess.RunReference("get", "Sales/Total Sales", "samples\\basic-tmdl", "-q", "expression", "--output-format", "json"),
            CliProcess.RunMdl("get", "Sales/Total Sales", "samples\\basic-tmdl", "-q", "expression", "--output-format", "json"));
    }

    [Fact]
    public void GetTableQueryJson_MatchesReferenceBooleanScalar()
    {
        AssertJsonEqual(
            CliProcess.RunReference("get", "Sales", "samples\\basic-tmdl", "-q", "isHidden", "--output-format", "json"),
            CliProcess.RunMdl("get", "Sales", "samples\\basic-tmdl", "-q", "isHidden", "--output-format", "json"));
    }

    [Fact]
    public void GetMeasureQueryTmdl_MatchesReferenceScalar()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("get", "Sales/Total Sales", "samples\\basic-tmdl", "-q", "expression", "--output-format", "tmdl"),
            CliProcess.RunMdl("get", "Sales/Total Sales", "samples\\basic-tmdl", "-q", "expression", "--output-format", "tmdl"));
    }

    [Fact]
    public void GetMissingObjectJsonError_MatchesReferenceErrorFormat()
    {
        var reference = CliProcess.RunReference("get", "Nope", "samples\\basic-tmdl", "--error-format", "json");
        var mdl = CliProcess.RunMdl("get", "Nope", "samples\\basic-tmdl", "--error-format", "json");

        Assert.Equal(1, reference.ExitCode);
        Assert.Equal(1, mdl.ExitCode);
        Assert.Equal(
            CompatibilityText.WithoutPreviewFooter(reference.StdErr),
            mdl.StdErr.Trim());
    }

    [Fact]
    public void FindSalesJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("find", "Sales", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("find", "Sales", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void FindSalesText_MatchesReferenceTable()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("find", "Sales", "samples\\basic-tmdl"),
            CliProcess.RunMdl("find", "Sales", "samples\\basic-tmdl"));
    }

    [Fact]
    public void FindSalesCsv_FallsBackToReferenceTable()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("find", "Sales", "samples\\basic-tmdl", "--output-format", "csv"),
            CliProcess.RunMdl("find", "Sales", "samples\\basic-tmdl", "--output-format", "csv"));
    }

    [Fact]
    public void FindSalesPathsOnly_MatchesReferencePaths()
    {
        var reference = CliProcess.RunReference("find", "Sales", "samples\\basic-tmdl", "--paths-only");
        var mdl = CliProcess.RunMdl("find", "Sales", "samples\\basic-tmdl", "--paths-only");

        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);
        Assert.Equal(
            CompatibilityText.WithoutPreviewFooter(reference.StdOut),
            mdl.StdOut.Trim());
    }

    [Fact]
    public void FindNoMatchesText_MatchesReferenceText()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("find", "zzzzz", "samples\\basic-tmdl"),
            CliProcess.RunMdl("find", "zzzzz", "samples\\basic-tmdl"));
    }

    [Fact]
    public void DepsTotalSalesText_MatchesReferenceText()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("deps", "Sales/Total Sales", "samples\\basic-tmdl"),
            CliProcess.RunMdl("deps", "Sales/Total Sales", "samples\\basic-tmdl"));
    }

    [Fact]
    public void DepsTotalSalesJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("deps", "Sales/Total Sales", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("deps", "Sales/Total Sales", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void DepsTotalSalesUpstreamText_MatchesReferenceText()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("deps", "Sales/Total Sales", "samples\\basic-tmdl", "--upstream"),
            CliProcess.RunMdl("deps", "Sales/Total Sales", "samples\\basic-tmdl", "--upstream"));
    }

    [Fact]
    public void DepsTotalSalesUpstreamJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("deps", "Sales/Total Sales", "samples\\basic-tmdl", "--upstream", "--output-format", "json"),
            CliProcess.RunMdl("deps", "Sales/Total Sales", "samples\\basic-tmdl", "--upstream", "--output-format", "json"));
    }

    [Fact]
    public void DepsTotalSalesDownstreamText_MatchesReferenceText()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("deps", "Sales/Total Sales", "samples\\basic-tmdl", "--downstream"),
            CliProcess.RunMdl("deps", "Sales/Total Sales", "samples\\basic-tmdl", "--downstream"));
    }

    [Fact]
    public void DepsTotalSalesDownstreamJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("deps", "Sales/Total Sales", "samples\\basic-tmdl", "--downstream", "--output-format", "json"),
            CliProcess.RunMdl("deps", "Sales/Total Sales", "samples\\basic-tmdl", "--downstream", "--output-format", "json"));
    }

    [Fact]
    public void DepsMissingObjectJsonError_MatchesReferenceErrorFormat()
    {
        var reference = CliProcess.RunReference("deps", "Nope", "samples\\basic-tmdl", "--error-format", "json");
        var mdl = CliProcess.RunMdl("deps", "Nope", "samples\\basic-tmdl", "--error-format", "json");

        Assert.Equal(1, reference.ExitCode);
        Assert.Equal(1, mdl.ExitCode);
        Assert.Equal(CompatibilityText.WithoutPreviewFooter(reference.StdOut), mdl.StdOut.Trim());
        Assert.Equal(CompatibilityText.WithoutPreviewFooter(reference.StdErr), mdl.StdErr.Trim());
    }

    [Fact]
    public void DiffIdenticalBasicTmdl_MatchesReferenceJsonAndExitCode()
    {
        AssertJsonEqual(
            CliProcess.RunReference("diff", "samples\\basic-tmdl", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("diff", "samples\\basic-tmdl", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void DiffIdenticalBasicTmdlCsv_FallsBackToReferenceText()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("diff", "samples\\basic-tmdl", "samples\\basic-tmdl", "--output-format", "csv"),
            CliProcess.RunMdl("diff", "samples\\basic-tmdl", "samples\\basic-tmdl", "--output-format", "csv"));
    }

    [Fact]
    public void DiffModifiedBasicTmdl_MatchesReferenceJsonAndExitCode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-diff-test-{Guid.NewGuid():N}");
        var left = Path.Combine(tempDir, "left");
        var right = Path.Combine(tempDir, "right");
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), left);
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), right);
            var salesTable = Path.Combine(right, "tables", "Sales.tmdl");
            File.WriteAllText(
                salesTable,
                ReplaceTotalSalesExpression(
                    File.ReadAllText(salesTable),
                    "SUMX(Sales, Sales[Amount])"));

            AssertJsonEqual(
                CliProcess.RunReference("diff", left, right, "--output-format", "json"),
                CliProcess.RunMdl("diff", left, right, "--output-format", "json"),
                expectedExitCode: 1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DiffModifiedBasicTmdlText_MatchesReferenceTextAndExitCode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-diff-text-test-{Guid.NewGuid():N}");
        var left = Path.Combine(tempDir, "left");
        var right = Path.Combine(tempDir, "right");
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), left);
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), right);
            var salesTable = Path.Combine(right, "tables", "Sales.tmdl");
            File.WriteAllText(
                salesTable,
                ReplaceTotalSalesExpression(
                    File.ReadAllText(salesTable),
                    "SUMX(Sales, Sales[Amount])"));

            AssertStdOutEqualIgnoringFooter(
                CliProcess.RunReference("diff", left, right),
                CliProcess.RunMdl("diff", left, right),
                expectedExitCode: 1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DiffMissingModel_ExitsWithUsageErrorLikeReference()
    {
        var reference = CliProcess.RunReference("diff", "missing-a", "missing-b");
        var mdl = CliProcess.RunMdl("diff", "missing-a", "missing-b");

        Assert.Equal(2, reference.ExitCode);
        Assert.Equal(reference.ExitCode, mdl.ExitCode);
    }

    [Fact]
    public void SaveBasicTmdlToTmdl_WritesLoadableTmdlLikeReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-save-tmdl-test-{Guid.NewGuid():N}");
        var referenceOut = Path.Combine(tempDir, "reference");
        var mdlOut = Path.Combine(tempDir, "mdl");
        Directory.CreateDirectory(tempDir);
        try
        {
            var reference = CliProcess.RunReference(
                "save", "samples\\basic-tmdl", "--output-path", referenceOut, "--force", "--output-format", "json");
            var mdl = CliProcess.RunMdl(
                "save", "samples\\basic-tmdl", "--output-path", mdlOut, "--force", "--output-format", "json");

            AssertSaveJson(reference, mdl, "tmdl");
            Assert.True(File.Exists(Path.Combine(referenceOut, "model.tmdl")));
            Assert.True(File.Exists(Path.Combine(mdlOut, "model.tmdl")));
            AssertJsonEqual(
                CliProcess.RunReference("ls", referenceOut, "--output-format", "json"),
                CliProcess.RunMdl("ls", mdlOut, "--output-format", "json"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SaveBasicTmdlCsv_FallsBackToReferenceSavedLine()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-save-csv-test-{Guid.NewGuid():N}");
        var output = Path.Combine(tempDir, "model");
        Directory.CreateDirectory(tempDir);
        try
        {
            var reference = CliProcess.RunReference(
                "save", "samples\\basic-tmdl", "--output-path", output, "--force", "--output-format", "csv");
            var mdl = CliProcess.RunMdl(
                "save", "samples\\basic-tmdl", "--output-path", output, "--force", "--output-format", "csv");

            AssertStdOutEqualIgnoringFooter(reference, mdl);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SaveBasicTmdlToBim_WritesLoadableBimLikeReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-save-bim-test-{Guid.NewGuid():N}");
        var referenceOut = Path.Combine(tempDir, "reference.bim");
        var mdlOut = Path.Combine(tempDir, "mdl.bim");
        Directory.CreateDirectory(tempDir);
        try
        {
            var reference = CliProcess.RunReference(
                "save", "samples\\basic-tmdl", "--output-path", referenceOut, "--serialization", "bim", "--force", "--output-format", "json");
            var mdl = CliProcess.RunMdl(
                "save", "samples\\basic-tmdl", "--output-path", mdlOut, "--serialization", "bim", "--force", "--output-format", "json");

            AssertSaveJson(reference, mdl, "bim");
            Assert.True(File.Exists(referenceOut));
            Assert.True(File.Exists(mdlOut));
            AssertJsonEqual(
                CliProcess.RunReference("ls", referenceOut, "--output-format", "json"),
                CliProcess.RunMdl("ls", mdlOut, "--output-format", "json"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void InitTmdl_CreatesLoadableModelLikeReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-init-tmdl-test-{Guid.NewGuid():N}");
        var referenceOut = Path.Combine(tempDir, "reference");
        var mdlOut = Path.Combine(tempDir, "mdl");
        Directory.CreateDirectory(tempDir);
        try
        {
            var reference = CliProcess.RunReference(
                "init", referenceOut, "--name", "CustomModel", "--compatibility-level", "1601", "--output-format", "json");
            var mdl = CliProcess.RunMdl(
                "init", mdlOut, "--name", "CustomModel", "--compatibility-level", "1601", "--output-format", "json");

            AssertInitJson(reference, mdl, "tmdl", "CustomModel", 1601, "PowerBI");
            Assert.True(File.Exists(Path.Combine(referenceOut, "model.tmdl")));
            Assert.True(File.Exists(Path.Combine(mdlOut, "model.tmdl")));
            AssertSameTextIgnoringSpacingAndFooter(
                CliProcess.RunReference("load", referenceOut),
                CliProcess.RunMdl("load", mdlOut));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void InitBim_CreatesLoadableModelLikeReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-init-bim-test-{Guid.NewGuid():N}");
        var referenceOut = Path.Combine(tempDir, "reference.bim");
        var mdlOut = Path.Combine(tempDir, "mdl.bim");
        Directory.CreateDirectory(tempDir);
        try
        {
            var reference = CliProcess.RunReference(
                "init", referenceOut, "--serialization", "bim", "--name", "CustomModel", "--compatibility-level", "1601", "--output-format", "json");
            var mdl = CliProcess.RunMdl(
                "init", mdlOut, "--serialization", "bim", "--name", "CustomModel", "--compatibility-level", "1601", "--output-format", "json");

            AssertInitJson(reference, mdl, "bim", "CustomModel", 1601, "PowerBI");
            Assert.True(File.Exists(referenceOut));
            Assert.True(File.Exists(mdlOut));
            AssertSameTextIgnoringSpacingAndFooter(
                CliProcess.RunReference("load", referenceOut),
                CliProcess.RunMdl("load", mdlOut));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void InitGlobalModel_CreatesLoadableModelLikeReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-init-global-test-{Guid.NewGuid():N}");
        var referenceOut = Path.Combine(tempDir, "reference");
        var mdlOut = Path.Combine(tempDir, "mdl");
        Directory.CreateDirectory(tempDir);
        try
        {
            var reference = CliProcess.RunReference(
                "--model", referenceOut, "init", "--name", "GlobalModel", "--output-format", "json");
            var mdl = CliProcess.RunMdl(
                "--model", mdlOut, "init", "--name", "GlobalModel", "--output-format", "json");

            AssertInitJson(reference, mdl, "tmdl", "GlobalModel", 1702, "PowerBI");
            AssertSameTextIgnoringSpacingAndFooter(
                CliProcess.RunReference("load", referenceOut),
                CliProcess.RunMdl("load", mdlOut));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AddMeasureDryRunJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("add", "Sales/TestMeasure", "-t", "Measure", "-i", "1", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("add", "Sales/TestMeasure", "-t", "Measure", "-i", "1", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void SetTableHiddenDryRunJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("set", "Sales", "-q", "isHidden", "-i", "true", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("set", "Sales", "-q", "isHidden", "-i", "true", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void RemoveMeasureDryRunJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("rm", "Sales/Avg Sale", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("rm", "Sales/Avg Sale", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void RemoveMissingIfExistsJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("rm", "Sales/Nope", "samples\\basic-tmdl", "--if-exists", "--output-format", "json"),
            CliProcess.RunMdl("rm", "Sales/Nope", "samples\\basic-tmdl", "--if-exists", "--output-format", "json"));
    }

    [Fact]
    public void MoveMeasureDryRunJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("mv", "Sales/Avg Sale", "Sales/Average Sale", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("mv", "Sales/Avg Sale", "Sales/Average Sale", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void ReplaceDefaultDryRunJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("replace", "Amount", "Amt", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("replace", "Amount", "Amt", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void ReplaceExpressionsDryRunJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("replace", "Amount", "Amt", "samples\\basic-tmdl", "--in", "expressions", "--output-format", "json"),
            CliProcess.RunMdl("replace", "Amount", "Amt", "samples\\basic-tmdl", "--in", "expressions", "--output-format", "json"));
    }

    [Fact]
    public void ReplaceCaseSensitiveNoMatchJson_MatchesReferenceJson()
    {
        AssertJsonEqual(
            CliProcess.RunReference("replace", "amount", "Amt", "samples\\basic-tmdl", "--case-sensitive", "--output-format", "json"),
            CliProcess.RunMdl("replace", "amount", "Amt", "samples\\basic-tmdl", "--case-sensitive", "--output-format", "json"));
    }

    [Fact]
    public void ValidateBasicTmdlJson_MatchesReferenceResult()
    {
        AssertValidateJsonEqual(
            CliProcess.RunReference("validate", "samples\\basic-tmdl", "--output-format", "json"),
            CliProcess.RunMdl("validate", "samples\\basic-tmdl", "--output-format", "json"));
    }

    [Fact]
    public void ValidateBasicTmdlText_MatchesReferenceText()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("validate", "samples\\basic-tmdl"),
            CliProcess.RunMdl("validate", "samples\\basic-tmdl"));
    }

    [Fact]
    public void ValidateBasicTmdlCsv_FallsBackToReferenceText()
    {
        AssertStdOutEqualIgnoringFooter(
            CliProcess.RunReference("validate", "samples\\basic-tmdl", "--output-format", "csv"),
            CliProcess.RunMdl("validate", "samples\\basic-tmdl", "--output-format", "csv"));
    }

    [Fact]
    public void ValidateBrokenMeasureJson_MatchesReferenceErrorAndExitCode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-validate-test-{Guid.NewGuid():N}");
        var referenceModel = Path.Combine(tempDir, "reference");
        var mdlModel = Path.Combine(tempDir, "mdl");
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), referenceModel);
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), mdlModel);
            BreakTotalSales(referenceModel);
            BreakTotalSales(mdlModel);

            AssertValidateJsonEqual(
                CliProcess.RunReference("validate", referenceModel, "--output-format", "json"),
                CliProcess.RunMdl("validate", mdlModel, "--output-format", "json"),
                expectedExitCode: 1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ValidateBrokenMeasureText_MatchesReferenceTextAndExitCode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-validate-text-test-{Guid.NewGuid():N}");
        var referenceModel = Path.Combine(tempDir, "reference");
        var mdlModel = Path.Combine(tempDir, "mdl");
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), referenceModel);
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), mdlModel);
            BreakTotalSales(referenceModel);
            BreakTotalSales(mdlModel);

            AssertStdOutEqualIgnoringFooter(
                CliProcess.RunReference("validate", referenceModel),
                CliProcess.RunMdl("validate", mdlModel),
                expectedExitCode: 1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ValidateBrokenMeasureCsv_MatchesReferenceTextAndExitCode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-validate-csv-test-{Guid.NewGuid():N}");
        var referenceModel = Path.Combine(tempDir, "reference");
        var mdlModel = Path.Combine(tempDir, "mdl");
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), referenceModel);
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), mdlModel);
            BreakTotalSales(referenceModel);
            BreakTotalSales(mdlModel);

            AssertStdOutEqualIgnoringFooter(
                CliProcess.RunReference("validate", referenceModel, "--output-format", "csv"),
                CliProcess.RunMdl("validate", mdlModel, "--output-format", "csv"),
                expectedExitCode: 1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AddMeasureSave_CreatesFindableMeasureLikeReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-add-save-test-{Guid.NewGuid():N}");
        var referenceModel = Path.Combine(tempDir, "reference");
        var mdlModel = Path.Combine(tempDir, "mdl");
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), referenceModel);
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), mdlModel);

            AssertAddSavedJson(
                CliProcess.RunReference("add", "Sales/TestMeasure", "-t", "Measure", "-i", "1", referenceModel, "--save", "--output-format", "json"),
                CliProcess.RunMdl("add", "Sales/TestMeasure", "-t", "Measure", "-i", "1", mdlModel, "--save", "--output-format", "json"),
                "Sales/TestMeasure");

            Assert.Equal(
                CompatibilityText.WithoutPreviewFooter(CliProcess.RunReference("find", "TestMeasure", referenceModel, "--paths-only").StdOut),
                CliProcess.RunMdl("find", "TestMeasure", mdlModel, "--paths-only").StdOut.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SetTableHiddenSave_PersistsTablePropertyLikeReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-set-save-test-{Guid.NewGuid():N}");
        var referenceModel = Path.Combine(tempDir, "reference");
        var mdlModel = Path.Combine(tempDir, "mdl");
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), referenceModel);
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), mdlModel);

            AssertSetSavedJson(
                CliProcess.RunReference("set", "Sales", "-q", "isHidden", "-i", "true", referenceModel, "--save", "--output-format", "json"),
                CliProcess.RunMdl("set", "Sales", "-q", "isHidden", "-i", "true", mdlModel, "--save", "--output-format", "json"),
                "Sales",
                "isHidden",
                "true");

            AssertJsonEqual(
                CliProcess.RunReference("get", "Sales", referenceModel, "--output-format", "json"),
                CliProcess.RunMdl("get", "Sales", mdlModel, "--output-format", "json"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void RemoveMeasureSave_RemovesFromListLikeReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-rm-save-test-{Guid.NewGuid():N}");
        var referenceModel = Path.Combine(tempDir, "reference");
        var mdlModel = Path.Combine(tempDir, "mdl");
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), referenceModel);
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), mdlModel);

            AssertRemoveSavedJson(
                CliProcess.RunReference("rm", "Sales/Avg Sale", referenceModel, "--save", "--output-format", "json"),
                CliProcess.RunMdl("rm", "Sales/Avg Sale", mdlModel, "--save", "--output-format", "json"),
                "Sales/Avg Sale");

            AssertJsonEqual(
                CliProcess.RunReference("ls", referenceModel, "--output-format", "json"),
                CliProcess.RunMdl("ls", mdlModel, "--output-format", "json"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void MoveMeasureSave_RenamesFindableMeasureLikeReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-mv-save-test-{Guid.NewGuid():N}");
        var referenceModel = Path.Combine(tempDir, "reference");
        var mdlModel = Path.Combine(tempDir, "mdl");
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), referenceModel);
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), mdlModel);

            AssertMoveSavedJson(
                CliProcess.RunReference("mv", "Sales/Avg Sale", "Sales/Average Sale", referenceModel, "--save", "--output-format", "json"),
                CliProcess.RunMdl("mv", "Sales/Avg Sale", "Sales/Average Sale", mdlModel, "--save", "--output-format", "json"),
                "Sales/Avg Sale",
                "Sales/Average Sale");

            Assert.Equal(
                CompatibilityText.WithoutPreviewFooter(CliProcess.RunReference("find", "Average Sale", referenceModel, "--paths-only").StdOut),
                CliProcess.RunMdl("find", "Average Sale", mdlModel, "--paths-only").StdOut.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ReplaceDescriptionSave_PersistsPropertyLikeReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mdl-replace-save-test-{Guid.NewGuid():N}");
        var referenceModel = Path.Combine(tempDir, "reference");
        var mdlModel = Path.Combine(tempDir, "mdl");
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), referenceModel);
            CopyDirectory(Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl"), mdlModel);

            AssertSetSavedJson(
                CliProcess.RunReference("set", "Sales", "-q", "description", "-i", "OldText", referenceModel, "--save", "--output-format", "json"),
                CliProcess.RunMdl("set", "Sales", "-q", "description", "-i", "OldText", mdlModel, "--save", "--output-format", "json"),
                "Sales",
                "description",
                "OldText");

            AssertReplaceSavedJson(
                CliProcess.RunReference("replace", "OldText", "NewText", referenceModel, "--in", "descriptions", "--save", "--output-format", "json"),
                CliProcess.RunMdl("replace", "OldText", "NewText", mdlModel, "--in", "descriptions", "--save", "--output-format", "json"),
                "OldText",
                "NewText",
                expectedChangeCount: 1);

            AssertJsonEqual(
                CliProcess.RunReference("get", "Sales", referenceModel, "--output-format", "json"),
                CliProcess.RunMdl("get", "Sales", mdlModel, "--output-format", "json"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void AssertJsonEqual(CliRun reference, CliRun mdl, int expectedExitCode = 0)
    {
        Assert.Equal(expectedExitCode, reference.ExitCode);
        Assert.Equal(expectedExitCode, mdl.ExitCode);

        var referenceJson = JsonNode.Parse(CompatibilityText.JsonPrefix(reference.StdOut));
        var mdlJson = JsonNode.Parse(CompatibilityText.JsonPrefix(mdl.StdOut));
        Assert.True(JsonNode.DeepEquals(referenceJson, mdlJson), $"Reference:\n{referenceJson}\n\nmdl:\n{mdlJson}");
    }

    private static void AssertValidateJsonEqual(CliRun reference, CliRun mdl, int expectedExitCode = 0)
    {
        Assert.Equal(expectedExitCode, reference.ExitCode);
        Assert.Equal(expectedExitCode, mdl.ExitCode);

        var referenceJson = JsonNode.Parse(CompatibilityText.JsonPrefix(reference.StdOut))!.AsObject();
        var mdlJson = JsonNode.Parse(CompatibilityText.JsonPrefix(mdl.StdOut))!.AsObject();

        referenceJson.Remove("durationMs");
        referenceJson.Remove("antipatterns");
        mdlJson.Remove("durationMs");

        Assert.True(JsonNode.DeepEquals(referenceJson, mdlJson), $"Reference:\n{referenceJson}\n\nmdl:\n{mdlJson}");
    }

    private static void AssertCsvEqual(CliRun reference, CliRun mdl, int expectedExitCode = 0)
    {
        Assert.Equal(expectedExitCode, reference.ExitCode);
        Assert.Equal(expectedExitCode, mdl.ExitCode);
        Assert.Equal(
            CompatibilityText.WithoutPreviewFooter(reference.StdOut),
            mdl.StdOut.Trim());
    }

    private static void AssertStdOutEqualIgnoringFooter(CliRun reference, CliRun mdl, int expectedExitCode = 0)
    {
        Assert.Equal(expectedExitCode, reference.ExitCode);
        Assert.Equal(expectedExitCode, mdl.ExitCode);
        Assert.Equal(
            CompatibilityText.WithoutPreviewFooter(reference.StdOut),
            mdl.StdOut.Trim());
    }

    private static void AssertAddSavedJson(CliRun reference, CliRun mdl, string added)
    {
        var referenceJson = AssertJsonObject(reference);
        var mdlJson = AssertJsonObject(mdl);

        Assert.Equal(added, referenceJson["added"]!.GetValue<string>());
        Assert.Equal(added, mdlJson["added"]!.GetValue<string>());
        AssertSavedPath(referenceJson);
        AssertSavedPath(mdlJson);
    }

    private static void AssertSetSavedJson(
        CliRun reference,
        CliRun mdl,
        string set,
        string property,
        string value)
    {
        var referenceJson = AssertJsonObject(reference);
        var mdlJson = AssertJsonObject(mdl);

        foreach (var json in new[] { referenceJson, mdlJson })
        {
            Assert.Equal(set, json["set"]!.GetValue<string>());
            Assert.Equal(property, json["property"]!.GetValue<string>());
            Assert.Equal(value, json["value"]!.GetValue<string>());
            Assert.Equal(0, json["validationErrors"]!.GetValue<int>());
            AssertSavedPath(json);
        }
    }

    private static void AssertRemoveSavedJson(CliRun reference, CliRun mdl, string removed)
    {
        var referenceJson = AssertJsonObject(reference);
        var mdlJson = AssertJsonObject(mdl);

        Assert.Equal(removed, referenceJson["removed"]!.GetValue<string>());
        Assert.Equal(removed, mdlJson["removed"]!.GetValue<string>());
        AssertSavedPath(referenceJson);
        AssertSavedPath(mdlJson);
    }

    private static void AssertMoveSavedJson(CliRun reference, CliRun mdl, string moved, string to)
    {
        var referenceJson = AssertJsonObject(reference);
        var mdlJson = AssertJsonObject(mdl);

        foreach (var json in new[] { referenceJson, mdlJson })
        {
            Assert.Equal(moved, json["moved"]!.GetValue<string>());
            Assert.Equal(to, json["to"]!.GetValue<string>());
            AssertSavedPath(json);
        }
    }

    private static void AssertReplaceSavedJson(
        CliRun reference,
        CliRun mdl,
        string pattern,
        string replacement,
        int expectedChangeCount)
    {
        var referenceJson = AssertJsonObject(reference);
        var mdlJson = AssertJsonObject(mdl);

        foreach (var json in new[] { referenceJson, mdlJson })
        {
            Assert.Equal(pattern, json["pattern"]!.GetValue<string>());
            Assert.Equal(replacement, json["replacement"]!.GetValue<string>());
            Assert.Equal(expectedChangeCount, json["changeCount"]!.GetValue<int>());
            AssertSavedPath(json);
        }
    }

    private static JsonObject AssertJsonObject(CliRun run)
    {
        Assert.Equal(0, run.ExitCode);
        return JsonNode.Parse(CompatibilityText.JsonPrefix(run.StdOut))!.AsObject();
    }

    private static void AssertSavedPath(JsonObject json)
        => Assert.False(string.IsNullOrWhiteSpace(json["saved"]!.GetValue<string>()));

    private static void AssertSaveJson(CliRun reference, CliRun mdl, string expectedFormat)
    {
        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);

        var referenceJson = JsonNode.Parse(CompatibilityText.JsonPrefix(reference.StdOut))!.AsObject();
        var mdlJson = JsonNode.Parse(CompatibilityText.JsonPrefix(mdl.StdOut))!.AsObject();

        Assert.Equal(expectedFormat, referenceJson["format"]!.GetValue<string>());
        Assert.Equal(expectedFormat, mdlJson["format"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(referenceJson["saved"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(mdlJson["saved"]!.GetValue<string>()));
    }

    private static void AssertInitJson(
        CliRun reference,
        CliRun mdl,
        string expectedFormat,
        string expectedName,
        int expectedCompatibilityLevel,
        string expectedCompatibilityMode)
    {
        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);

        var referenceJson = JsonNode.Parse(CompatibilityText.JsonPrefix(reference.StdOut))!.AsObject();
        var mdlJson = JsonNode.Parse(CompatibilityText.JsonPrefix(mdl.StdOut))!.AsObject();

        Assert.Equal(expectedFormat, referenceJson["format"]!.GetValue<string>());
        Assert.Equal(expectedFormat, mdlJson["format"]!.GetValue<string>());
        Assert.Equal(expectedName, referenceJson["name"]!.GetValue<string>());
        Assert.Equal(expectedName, mdlJson["name"]!.GetValue<string>());
        Assert.Equal(expectedCompatibilityLevel, referenceJson["compatibilityLevel"]!.GetValue<int>());
        Assert.Equal(expectedCompatibilityLevel, mdlJson["compatibilityLevel"]!.GetValue<int>());
        Assert.Equal(expectedCompatibilityMode, referenceJson["compatibilityMode"]!.GetValue<string>());
        Assert.Equal(expectedCompatibilityMode, mdlJson["compatibilityMode"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(referenceJson["created"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(mdlJson["created"]!.GetValue<string>()));
    }

    private static void AssertSameTextIgnoringSpacingAndFooter(CliRun reference, CliRun mdl)
        => Assert.Equal(
            CollapseWhitespace(CompatibilityText.WithoutPreviewFooter(reference.StdOut)),
            CollapseWhitespace(mdl.StdOut));

    private static string CollapseWhitespace(string text)
        => string.Join(
            '\n',
            text.Split('\n')
                .Select(line => string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries))));

    private static string ToMdlCompletionScript(string referenceScript)
    {
        var text = CompatibilityText.WithoutPreviewFooter(referenceScript);
        return text
            .Replace("# te shell completion", "# mdl shell completion", StringComparison.Ordinal)
            .Replace("te completion", "mdl completion", StringComparison.Ordinal)
            .Replace("__te_complete", "__mdl_complete", StringComparison.Ordinal)
            .Replace("_te_complete", "_mdl_complete", StringComparison.Ordinal)
            .Replace("$__teNames", "$__mdlNames", StringComparison.Ordinal)
            .Replace("te.exe", "mdl.exe", StringComparison.Ordinal)
            .Replace(".\\te", ".\\mdl", StringComparison.Ordinal)
            .Replace("'te'", "'mdl'", StringComparison.Ordinal)
            .Replace(" te.fish", " mdl.fish", StringComparison.Ordinal)
            .Replace("/te.fish", "/mdl.fish", StringComparison.Ordinal)
            .Replace("te \"[suggest:", "mdl \"[suggest:", StringComparison.Ordinal)
            .Replace("complete -o default -F _mdl_complete te", "complete -o default -F _mdl_complete mdl", StringComparison.Ordinal)
            .Replace("compdef _mdl_complete te", "compdef _mdl_complete mdl", StringComparison.Ordinal)
            .Replace("complete -c te", "complete -c mdl", StringComparison.Ordinal);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, directory));
            Directory.CreateDirectory(target);
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, target);
        }
    }

    private static void BreakTotalSales(string model)
    {
        var salesTable = Path.Combine(model, "tables", "Sales.tmdl");
        File.WriteAllText(
            salesTable,
            ReplaceTotalSalesExpression(
                File.ReadAllText(salesTable),
                "SUM(Sales[Missing])"));
    }

    private static string ReplaceTotalSalesExpression(string text, string replacement)
    {
        var updated = text
            .Replace("SUM(Sales[Amount])", replacement, StringComparison.Ordinal)
            .Replace("SUM ( Sales[Amount] )", replacement, StringComparison.Ordinal);

        if (updated == text)
            throw new InvalidOperationException("The Total Sales sample expression was not found.");

        return updated;
    }

    private const string BasicBim = """
        {
          "name": "basic-bim",
          "compatibilityLevel": 1601,
          "model": {
            "tables": [
              {
                "name": "Sales",
                "columns": [
                  {
                    "name": "Amount",
                    "dataType": "decimal",
                    "sourceColumn": "Amount"
                  }
                ],
                "partitions": [
                  {
                    "name": "Sales",
                    "mode": "import",
                    "source": {
                      "type": "m",
                      "expression": "let Source = #table({\"Amount\"}, {{1}}) in Source"
                    }
                  }
                ],
                "measures": [
                  {
                    "name": "Total Sales",
                    "expression": "SUM(Sales[Amount])"
                  }
                ]
              }
            ]
          }
        }
        """;
}
