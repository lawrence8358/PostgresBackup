using System.Text;
using System.Text.RegularExpressions;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>
/// Filters a pg_restore table-of-contents list so Normal mode only restores
/// object graphs that are absent from the target database.
/// </summary>
public static partial class RestoreArchivePlanner
{
    private static readonly string[] Descriptions =
    [
        "MATERIALIZED VIEW DATA", "SEQUENCE OWNED BY", "DATABASE PROPERTIES",
        "DEFAULT ACL", "FK CONSTRAINT", "CHECK CONSTRAINT", "TABLE ATTACH",
        "INDEX ATTACH", "TABLE DATA", "SEQUENCE SET", "MATERIALIZED VIEW",
        "FOREIGN TABLE", "SECURITY LABEL", "PUBLICATION TABLE", "LARGE OBJECT DATA",
        "PRE-DATA BOUNDARY", "POST-DATA BOUNDARY", "EVENT TRIGGER", "TEXT SEARCH CONFIGURATION",
        "TEXT SEARCH DICTIONARY", "TEXT SEARCH PARSER", "TEXT SEARCH TEMPLATE",
        "OPERATOR CLASS", "OPERATOR FAMILY", "ACCESS METHOD", "SHELL TYPE",
        "TABLE", "SEQUENCE", "VIEW", "INDEX", "CONSTRAINT", "DEFAULT", "TRIGGER",
        "RULE", "FUNCTION", "PROCEDURE", "AGGREGATE", "TYPE", "DOMAIN", "EXTENSION",
        "COLLATION", "SCHEMA", "COMMENT", "ACL", "OWNER", "DATABASE", "ENCODING",
        "STDSTRINGS", "SEARCHPATH", "TABLESPACE", "BLOB", "BLOBS", "LARGE OBJECT",
        "BLOB COMMENTS", "PUBLICATION", "SUBSCRIPTION", "POLICY", "ROW SECURITY",
        "STATISTICS", "TRANSFORM", "CAST", "CONVERSION", "OPERATOR", "LANGUAGE"
    ];

    private static readonly HashSet<string> MetadataDescriptions = new(StringComparer.Ordinal)
    {
        "COMMENT", "ACL", "DEFAULT ACL", "SECURITY LABEL", "OWNER", "DATABASE",
        "DATABASE PROPERTIES", "TABLESPACE", "BLOB", "BLOBS", "LARGE OBJECT",
        "LARGE OBJECT DATA", "BLOB COMMENTS", "PUBLICATION", "PUBLICATION TABLE",
        "SUBSCRIPTION"
    };

    private static readonly HashSet<string> SessionDescriptions = new(StringComparer.Ordinal)
    {
        "ENCODING", "STDSTRINGS", "SEARCHPATH", "PRE-DATA BOUNDARY", "POST-DATA BOUNDARY"
    };

    public static RestoreArchivePlan Build(string archiveList, RestoreTargetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(archiveList);
        ArgumentNullException.ThrowIfNull(catalog);

        var output = new StringBuilder();
        var included = 0;
        var actionable = 0;
        var skippedExisting = 0;
        var skippedMetadata = 0;
        var unsupported = new HashSet<string>(StringComparer.Ordinal);

        using var reader = new StringReader(archiveList);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(';'))
            {
                output.AppendLine(line);
                continue;
            }

            if (!TryParse(line, out var entry))
            {
                output.Append("; skipped-unsupported: ").AppendLine(line);
                unsupported.Add("unparseable");
                continue;
            }

            if (SessionDescriptions.Contains(entry.Description))
            {
                output.AppendLine(line);
                included++;
                continue;
            }

            if (MetadataDescriptions.Contains(entry.Description))
            {
                output.Append("; skipped-metadata: ").AppendLine(line);
                skippedMetadata++;
                continue;
            }

            var exists = Exists(entry, catalog);
            if (exists is null)
            {
                output.Append("; skipped-unsupported: ").AppendLine(line);
                unsupported.Add(entry.Description);
            }
            else if (exists.Value)
            {
                output.Append("; skipped-existing: ").AppendLine(line);
                skippedExisting++;
            }
            else
            {
                output.AppendLine(line);
                included++;
                actionable++;
            }
        }

        return new RestoreArchivePlan(
            output.ToString(), included, actionable, skippedExisting, skippedMetadata, unsupported.Order().ToArray());
    }

    public static RestoreDataPlan BuildDataOnly(string archiveList, RestoreTargetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(archiveList);
        ArgumentNullException.ThrowIfNull(catalog);

        var tables = new HashSet<RestoreTableIdentity>();
        var tableDataLines = new Dictionary<RestoreTableIdentity, List<string>>();
        var sequenceSetLines = new List<string>();
        var commentLines = new List<string>();
        var missing = new HashSet<string>(StringComparer.Ordinal);
        var unsupported = new HashSet<string>(StringComparer.Ordinal);
        var dataEntries = 0;

        using var reader = new StringReader(archiveList);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(';'))
            {
                commentLines.Add(line);
                continue;
            }

            if (!TryParse(line, out var entry)) continue;

            if (entry.Description == "TABLE DATA" && entry.Values.Count >= 2)
            {
                var table = new RestoreTableIdentity(entry.Values[0], entry.Values[1]);
                tables.Add(table);
                if (!tableDataLines.TryGetValue(table, out var lines))
                {
                    lines = [];
                    tableDataLines.Add(table, lines);
                }

                lines.Add(line);
                dataEntries++;
                if (!catalog.Relations.Contains(RestoreTargetCatalog.Key(table.Schema, table.Name)))
                {
                    missing.Add(table.ToString());
                }
            }
            else if (entry.Description == "SEQUENCE SET" && entry.Values.Count >= 2)
            {
                dataEntries++;
                sequenceSetLines.Add(line);
                if (!catalog.Relations.Contains(RestoreTargetCatalog.Key(entry.Values[0], entry.Values[1])))
                {
                    missing.Add($"{entry.Values[0]}.{entry.Values[1]}");
                }
            }
            else if (entry.Description is "MATERIALIZED VIEW DATA" or "BLOB" or "BLOBS"
                     or "LARGE OBJECT" or "LARGE OBJECT DATA")
            {
                unsupported.Add(entry.Description);
            }
        }

        var orderedTables = OrderTablesByForeignKeys(
            tables,
            catalog.ForeignKeyDependencies,
            out var cyclicTables);
        var output = new StringBuilder();
        foreach (var line in commentLines) output.AppendLine(line);
        output.AppendLine("; Data entries ordered by target foreign-key dependencies");
        foreach (var table in orderedTables)
        {
            if (!tableDataLines.TryGetValue(table, out var lines)) continue;
            foreach (var line in lines) output.AppendLine(line);
        }

        foreach (var line in sequenceSetLines) output.AppendLine(line);

        return new RestoreDataPlan(
            output.ToString(),
            orderedTables,
            dataEntries,
            missing.Order(StringComparer.Ordinal).ToArray(),
            unsupported.Order(StringComparer.Ordinal).ToArray(),
            cyclicTables.Select(table => table.ToString()).ToArray());
    }

    private static IReadOnlyList<RestoreTableIdentity> OrderTablesByForeignKeys(
        IReadOnlySet<RestoreTableIdentity> tables,
        IEnumerable<RestoreForeignKeyDependency> dependencies,
        out IReadOnlyList<RestoreTableIdentity> cyclicTables)
    {
        var comparer = Comparer<RestoreTableIdentity>.Create((left, right) =>
        {
            var schemaComparison = StringComparer.Ordinal.Compare(left.Schema, right.Schema);
            return schemaComparison != 0
                ? schemaComparison
                : StringComparer.Ordinal.Compare(left.Name, right.Name);
        });
        var indegrees = tables.ToDictionary(table => table, _ => 0);
        var childrenByParent = tables.ToDictionary(
            table => table,
            _ => new SortedSet<RestoreTableIdentity>(comparer));

        foreach (var dependency in dependencies)
        {
            if (dependency.Child == dependency.Parent
                || !tables.Contains(dependency.Child)
                || !tables.Contains(dependency.Parent))
            {
                continue;
            }

            if (childrenByParent[dependency.Parent].Add(dependency.Child))
            {
                indegrees[dependency.Child]++;
            }
        }

        var ready = new SortedSet<RestoreTableIdentity>(
            indegrees.Where(pair => pair.Value == 0).Select(pair => pair.Key),
            comparer);
        var ordered = new List<RestoreTableIdentity>(tables.Count);

        while (ready.Count > 0)
        {
            var parent = ready.Min;
            ready.Remove(parent);
            ordered.Add(parent);

            foreach (var child in childrenByParent[parent])
            {
                indegrees[child]--;
                if (indegrees[child] == 0) ready.Add(child);
            }
        }

        var blocked = indegrees
            .Where(pair => pair.Value > 0)
            .Select(pair => pair.Key)
            .Order(comparer)
            .ToArray();
        ordered.AddRange(blocked);
        cyclicTables = blocked;
        return ordered;
    }

    private static bool? Exists(ArchiveEntry entry, RestoreTargetCatalog catalog)
    {
        var values = entry.Values;

        return entry.Description switch
        {
            "SCHEMA" when values.Count >= 2 => catalog.Schemas.Contains(values[1]),

            "TABLE" or "FOREIGN TABLE" or "VIEW" or "MATERIALIZED VIEW" or "SEQUENCE" or "INDEX"
                when values.Count >= 2 => catalog.Relations.Contains(RestoreTargetCatalog.Key(values[0], values[1])),

            "TABLE DATA" or "MATERIALIZED VIEW DATA" or "SEQUENCE SET" or "SEQUENCE OWNED BY"
                when values.Count >= 2 => catalog.Relations.Contains(RestoreTargetCatalog.Key(values[0], values[1])),

            "CONSTRAINT" or "CHECK CONSTRAINT" or "FK CONSTRAINT"
                when values.Count >= 3 => catalog.Constraints.Contains(
                    RestoreTargetCatalog.Key(values[0], values[1], values[2])),

            "DEFAULT" when values.Count >= 3 => catalog.Defaults.Contains(
                RestoreTargetCatalog.Key(values[0], values[1], values[2])),

            "TRIGGER" when values.Count >= 3 => catalog.Triggers.Contains(
                RestoreTargetCatalog.Key(values[0], values[1], values[2])),

            "RULE" when values.Count >= 3 => catalog.Rules.Contains(
                RestoreTargetCatalog.Key(values[0], values[1], values[2])),

            "FUNCTION" or "PROCEDURE" or "AGGREGATE" when values.Count >= 3 => catalog.Routines.Contains(
                RestoreTargetCatalog.Key(values[0], JoinTag(values))),

            "TYPE" or "DOMAIN" when values.Count >= 2 => catalog.Types.Contains(
                RestoreTargetCatalog.Key(values[0], values[1])),

            "EXTENSION" when values.Count >= 2 => catalog.Extensions.Contains(values[1]),
            "COLLATION" when values.Count >= 2 => catalog.Collations.Contains(
                RestoreTargetCatalog.Key(values[0], values[1])),

            _ => null
        };
    }

    private static string JoinTag(IReadOnlyList<string> values) =>
        string.Join(' ', values.Skip(1).Take(values.Count - 2));

    private static bool TryParse(string line, out ArchiveEntry entry)
    {
        entry = default;
        var match = TocPrefixRegex().Match(line);
        if (!match.Success) return false;

        var remainder = match.Groups[1].Value;
        var description = Descriptions.FirstOrDefault(candidate =>
            remainder.Equals(candidate, StringComparison.Ordinal) ||
            remainder.StartsWith(candidate + " ", StringComparison.Ordinal));
        if (description is null) return false;

        var valueText = remainder.Length == description.Length
            ? string.Empty
            : remainder[(description.Length + 1)..];
        entry = new ArchiveEntry(description, Tokenize(valueText));
        return true;
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < value.Length && value[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    [GeneratedRegex(@"^\s*\d+;\s+\d+\s+\d+\s+(.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TocPrefixRegex();

    private readonly record struct ArchiveEntry(string Description, IReadOnlyList<string> Values);
}
