using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Hosting;

namespace TechStrap.Architecture.Tests;

/// <summary>One entry point: Kind is http, hub or loop; Key is the route ("GET api/tags/{id}"), the hub method ("TicketHub.JoinTicket") or the loop class; Handler is the named handler without the leading I.</summary>
public sealed record EntryPoint(string Kind, string Key, string Handler)
{
    public string Id => $"{Kind} {Key}";
}

/// <summary>
/// The conformance gate (P12-T19): reads the entry-point tables of docs/architecture/02-ARCHITECTURE.md sections 7.1 to 7.5 (7.6 is the exempt list and is not read) and collects the same
/// facts from the compiled code (controllers, hub methods, hosted loops), so the two can be compared.
/// </summary>
public static partial class EntryPointCatalog
{
    [GeneratedRegex(@"^### 7\.([1-5]) ")]
    private static partial Regex InScopeHeading();

    [GeneratedRegex(@"`(GET|POST|PUT|DELETE|PATCH) (/[^`\s]*)`")]
    private static partial Regex HttpToken();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex Backticked();

    [GeneratedRegex(@"^[A-Z]\w*$")]
    private static partial Regex ClassName();

    [GeneratedRegex(@"\{(\w+):[^}]*\}")]
    private static partial Regex RouteConstraint();

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex CellSeparator();

    [GeneratedRegex(@"\bI([A-Z]\w*Handler)\b")]
    private static partial Regex HandlerInterface();

    /// <summary>What the document parse found: the entries, the 7.x table rows it could not read (the gate fails on any), and how many entries each of 7.1 to 7.5 yielded.</summary>
    public sealed record ParsedCatalog(IReadOnlyList<EntryPoint> Entries, IReadOnlyList<string> Unparsed, IReadOnlyDictionary<string, int> EntriesPerSection);

    [GeneratedRegex(@"^#{2,3} ")]
    private static partial Regex ScopeBoundary();

    [GeneratedRegex(@"no entry point|\bn/a\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitNonEntry();

    public static IReadOnlyList<EntryPoint> FromDocument(string markdown) => Parse(markdown).Entries;

    public static ParsedCatalog Parse(string markdown)
    {
        var result = new List<EntryPoint>();
        var unparsed = new List<string>();
        var perSection = new Dictionary<string, int>(StringComparer.Ordinal);
        string? section = null;
        var inFence = false;
        foreach (var line in markdown.Split('\n').Select(text => text.TrimEnd('\r')))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                continue;
            }

            // Only ## and ### headings end a section; #### sub-headings stay inside it.
            if (ScopeBoundary().IsMatch(line))
            {
                var heading = InScopeHeading().Match(line);
                section = heading.Success ? "7." + heading.Groups[1].Value : null;
                if (section is not null)
                {
                    perSection.TryAdd(section, 0);
                }

                continue;
            }

            if (section is null || !line.StartsWith('|'))
            {
                continue;
            }

            var cells = CellSeparator().Split(line.Trim().Trim('|')).Select(cell => cell.Trim()).ToList();
            if (cells.Count < 2 || cells[0].StartsWith("---", StringComparison.Ordinal) || cells[0] == "Entry point/use case")
            {
                continue;
            }

            if (ExplicitNonEntry().IsMatch(cells[0]) || ExplicitNonEntry().IsMatch(cells[1]))
            {
                continue;
            }

            var handler = Backticked().Matches(cells[1]).Select(match => match.Groups[1].Value).FirstOrDefault(token => token.EndsWith("Handler", StringComparison.Ordinal));
            var entries = handler is null ? [] : ParseEntryCell(cells[0], handler).ToList();
            if (entries.Count == 0)
            {
                unparsed.Add($"{section}: {line}");
                continue;
            }

            result.AddRange(entries);
            perSection[section] += entries.Count;
        }

        return new ParsedCatalog(result, unparsed, perSection);
    }

    private static IEnumerable<EntryPoint> ParseEntryCell(string cell, string handler)
    {
        if (cell.StartsWith('`') && HttpToken().Match(cell) is { Success: true, Index: 0 } http)
        {
            var path = http.Groups[2].Value.TrimStart('/');
            var query = path.IndexOf('?', StringComparison.Ordinal);
            if (query >= 0)
            {
                path = path[..query];
            }

            yield return new EntryPoint("http", $"{http.Groups[1].Value} {RouteConstraint().Replace(path, "{$1}")}", handler);
            yield break;
        }

        if (cell.StartsWith("SignalR ", StringComparison.Ordinal))
        {
            var head = cell.Split(" (", 2)[0];
            var tokens = Backticked().Matches(head).Select(match => match.Groups[1].Value).ToList();
            if (tokens.Count == 0)
            {
                yield break;
            }

            var hub = tokens[0].Split('.')[0];
            foreach (var token in tokens)
            {
                yield return new EntryPoint("hub", token.Contains('.', StringComparison.Ordinal) ? token : $"{hub}.{token}", handler);
            }

            yield break;
        }

        if (cell.Contains("hosted", StringComparison.OrdinalIgnoreCase))
        {
            var loop = Backticked().Matches(cell).Select(match => match.Groups[1].Value).FirstOrDefault(token => ClassName().IsMatch(token));
            if (loop is not null)
            {
                yield return new EntryPoint("loop", loop, handler);
            }
        }
    }

    public static IReadOnlyList<EntryPoint> FromCode()
    {
        var result = new List<EntryPoint>();
        var api = typeof(TechStrap.Api.Program).Assembly;

        foreach (var controller in api.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract))
        {
            var routes = controller.GetCustomAttributes<RouteAttribute>().Select(route => route.Template.Replace("[controller]", controller.Name.Replace("Controller", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal)).ToList();
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(method => !method.IsSpecialName))
            {
                var handler = action.GetParameters().Where(parameter => parameter.GetCustomAttribute<FromServicesAttribute>() is not null).Select(parameter => TrimInterface(parameter.ParameterType.Name)).SingleOrDefault();
                foreach (var verb in action.GetCustomAttributes<HttpMethodAttribute>(inherit: true))
                {
                    foreach (var route in routes)
                    {
                        var path = string.Join('/', new[] { route, verb.Template }.Where(part => !string.IsNullOrEmpty(part)).Select(part => part!.Trim('/')));
                        path = RouteConstraint().Replace(path, "{$1}");
                        foreach (var method in verb.HttpMethods)
                        {
                            result.Add(new EntryPoint("http", $"{method} {path}", handler ?? "(none)"));
                        }
                    }
                }
            }
        }

        var hub = api.GetType("TechStrap.Api.Live.TicketHub", throwOnError: true)!;
        var hubHandler = hub.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType.Name).Single(name => name.EndsWith("Handler", StringComparison.Ordinal));
        foreach (var method in hub.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(method => !method.IsSpecialName && method.GetBaseDefinition().DeclaringType == hub))
        {
            result.Add(new EntryPoint("hub", $"{hub.Name}.{method.Name}", TrimInterface(hubHandler)));
        }

        var root = ProjectGraph.FindRepositoryRoot();
        var loopAssemblies = new[] { api, typeof(TechStrap.Infrastructure.InfrastructureAssemblyMarker).Assembly, typeof(TechStrap.Worker.Outbox.EmailOutboxWorker).Assembly };
        foreach (var loop in loopAssemblies.SelectMany(assembly => assembly.GetTypes()).Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IHostedService).IsAssignableFrom(type)))
        {
            foreach (var handler in LoopHandlers(root, loop))
            {
                result.Add(new EntryPoint("loop", loop.Name, handler));
            }
        }

        return result;
    }

    /// <summary>A loop resolves its scoped handler from a scope factory, so the handler is found by reading the loop's source for the I...Handler it names.</summary>
    private static IReadOnlyList<string> LoopHandlers(string root, Type loop)
    {
        var file = Directory.GetFiles(Path.Combine(root, "src"), loop.Name + ".cs", SearchOption.AllDirectories).SingleOrDefault(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        if (file is null)
        {
            return ["(source not found)"];
        }

        var handlers = HandlerInterface().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        return handlers.Count == 0 ? ["(none)"] : handlers;
    }

    private static string TrimInterface(string name) => name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]) ? name[1..] : name;

    public static (IReadOnlyList<string> InDocNotInCode, IReadOnlyList<string> InCodeNotInDoc, IReadOnlyList<string> HandlerMismatches) Diff(
        IReadOnlyList<EntryPoint> doc, IReadOnlyList<EntryPoint> code)
    {
        var docIds = doc.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        var codeIds = code.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        var mismatches = doc
            .Where(entry => codeIds.Contains(entry.Id) && !code.Any(other => other.Id == entry.Id && other.Handler == entry.Handler))
            .Select(entry => $"{entry.Id}: document says {entry.Handler}, code has {string.Join(" / ", code.Where(other => other.Id == entry.Id).Select(other => other.Handler))}")
            .ToList();
        return (
            doc.Where(entry => !codeIds.Contains(entry.Id)).Select(entry => entry.Id).Distinct(StringComparer.Ordinal).ToList(),
            code.Where(entry => !docIds.Contains(entry.Id)).Select(entry => entry.Id).Distinct(StringComparer.Ordinal).ToList(),
            mismatches);
    }

    public static string Report(IReadOnlyList<EntryPoint> doc, IReadOnlyList<EntryPoint> code)
    {
        var rows = doc.Concat(code).GroupBy(entry => entry.Id, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var inDoc = doc.Any(entry => entry.Id == group.Key) ? "yes" : "NO";
                var inCode = code.Any(entry => entry.Id == group.Key) ? "yes" : "NO";
                var handler = string.Join(" / ", group.Select(entry => entry.Handler).Distinct(StringComparer.Ordinal));
                return $"{first.Kind} | {first.Key} | {handler} | {inDoc} | {inCode}";
            });
        return "KIND | KEY | HANDLER | IN DOC | IN CODE" + Environment.NewLine + string.Join(Environment.NewLine, rows);
    }
}
