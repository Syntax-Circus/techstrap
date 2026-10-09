namespace TechStrap.Architecture.Tests;

/// <summary>The conformance gate (P12-T19): section 7 of the architecture document and the code agree on every entry point and its handler, in both directions.</summary>
public sealed class EntryPointCatalogTests(ITestOutputHelper output)
{
    private static readonly string DocumentPath = Path.Combine(ProjectGraph.FindRepositoryRoot(), "docs", "architecture", "02-ARCHITECTURE.md");

    private static IReadOnlyList<EntryPoint> Document() => EntryPointCatalog.FromDocument(File.ReadAllText(DocumentPath));

    private void Write(IReadOnlyList<EntryPoint> doc, IReadOnlyList<EntryPoint> code, (IReadOnlyList<string> InDocNotInCode, IReadOnlyList<string> InCodeNotInDoc, IReadOnlyList<string> HandlerMismatches) diff)
    {
        output.WriteLine(EntryPointCatalog.Report(doc, code));
        output.WriteLine("IN DOC, NOT IN CODE: " + string.Join("; ", diff.InDocNotInCode));
        output.WriteLine("IN CODE, NOT IN DOC: " + string.Join("; ", diff.InCodeNotInDoc));
        output.WriteLine("HANDLER MISMATCHES: " + string.Join("; ", diff.HandlerMismatches));
    }

    [Fact]
    public void Every_documented_entry_point_exists_in_code_with_the_same_handler()
    {
        var doc = Document();
        var code = EntryPointCatalog.FromCode();
        var diff = EntryPointCatalog.Diff(doc, code);
        Write(doc, code, diff);

        doc.ShouldNotBeEmpty();
        diff.InDocNotInCode.ShouldBeEmpty("documented but not in code");
        diff.HandlerMismatches.ShouldBeEmpty("handler differs between document and code");
    }

    [Fact]
    public void Every_coded_entry_point_is_in_the_catalog()
    {
        var doc = Document();
        var code = EntryPointCatalog.FromCode();
        var diff = EntryPointCatalog.Diff(doc, code);
        Write(doc, code, diff);

        code.ShouldNotBeEmpty();
        diff.InCodeNotInDoc.ShouldBeEmpty("in code but not in section 7");
    }

    [Fact]
    public void The_conformance_report_lists_every_entry_point_once()
    {
        var doc = Document();
        var code = EntryPointCatalog.FromCode();
        var diff = EntryPointCatalog.Diff(doc, code);
        Write(doc, code, diff);
        output.WriteLine($"{doc.Count} entry points: {doc.Count(entry => entry.Kind == "http")} http, {doc.Count(entry => entry.Kind == "hub")} hub, {doc.Count(entry => entry.Kind == "loop")} loops");

        doc.GroupBy(entry => entry.Id).Where(group => group.Count() > 1).Select(group => group.Key).ShouldBeEmpty("documented twice");
        code.GroupBy(entry => entry.Id).Where(group => group.Count() > 1).Select(group => group.Key).ShouldBeEmpty("coded twice or resolving two handlers");
        (diff.InDocNotInCode.Count + diff.InCodeNotInDoc.Count + diff.HandlerMismatches.Count).ShouldBe(0);
    }
}
