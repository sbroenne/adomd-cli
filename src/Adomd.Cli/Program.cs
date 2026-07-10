using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.AnalysisServices.AdomdClient;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("adomd");
    config.SetApplicationVersion(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.1.0");

    config.AddCommand<ProbeCommand>("probe")
        .WithDescription("Open a connection and list visible catalogs.");
    config.AddCommand<CatalogsCommand>("catalogs")
        .WithDescription("List visible Analysis Services catalogs/databases.");
    config.AddCommand<SchemaCommand>("schema")
        .WithDescription("Return cubes, dimensions, hierarchies, levels, measures, and sets.");
    config.AddCommand<QueryCommand>("query")
        .WithDescription("Execute MDX, DAX, DMX, or DMV text and return rows as JSON.");
    config.AddCommand<QueryCommand>("dmv")
        .WithDescription("Alias for query.");
});

return app.Run(args);

/// <summary>Exit codes returned by JSON commands, exposed for tests and callers scripting against the CLI.</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Error = 2;
    public const int Cancelled = 130;
}

public sealed class ProbeCommand : JsonCommand<CommonSettings>
{
    protected override object ExecuteJson(CommonSettings settings, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var connection = AnalysisServices.OpenConnection(settings, cancellationToken);
        var catalogs = AnalysisServices.ReadCatalogs(connection, settings.Limit);

        return new
        {
            ok = true,
            command = "probe",
            server = settings.Server,
            catalog = settings.Catalog,
            os = RuntimeInformation.OSDescription,
            framework = RuntimeInformation.FrameworkDescription,
            elapsedMs = sw.ElapsedMilliseconds,
            catalogs = catalogs.ToJson()
        };
    }
}

public sealed class CatalogsCommand : JsonCommand<CommonSettings>
{
    protected override object ExecuteJson(CommonSettings settings, CancellationToken cancellationToken)
    {
        using var connection = AnalysisServices.OpenConnection(settings, cancellationToken);
        return new
        {
            ok = true,
            command = "catalogs",
            server = settings.Server,
            catalogs = AnalysisServices.ReadCatalogs(connection, settings.Limit).ToJson()
        };
    }
}

public sealed class SchemaSettings : CommonSettings
{
    [CommandOption("--rowset <GUID>")]
    [Description("Additional schema rowset GUID(s) to include beyond the built-in set (e.g. a DBSCHEMA_*/MDSCHEMA_* constant). Repeatable.")]
    public string[] Rowsets { get; init; } = [];

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }

        foreach (var rowset in Rowsets)
        {
            if (!Guid.TryParse(rowset, out _))
            {
                return ValidationResult.Error($"--rowset value '{rowset}' is not a valid GUID.");
            }
        }

        return ValidationResult.Success();
    }
}

public sealed class SchemaCommand : JsonCommand<SchemaSettings>
{
    protected override object ExecuteJson(SchemaSettings settings, CancellationToken cancellationToken)
    {
        using var connection = AnalysisServices.OpenConnection(settings, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var cubes = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Cubes, settings.Limit);
        cancellationToken.ThrowIfCancellationRequested();
        var dimensions = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Dimensions, settings.Limit);
        cancellationToken.ThrowIfCancellationRequested();
        var hierarchies = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Hierarchies, settings.Limit);
        cancellationToken.ThrowIfCancellationRequested();
        var levels = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Levels, settings.Limit);
        cancellationToken.ThrowIfCancellationRequested();
        var measures = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Measures, settings.Limit);
        cancellationToken.ThrowIfCancellationRequested();
        var sets = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Sets, settings.Limit);

        var custom = new Dictionary<string, object>();
        foreach (var rowset in settings.Rowsets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            custom[rowset] = AnalysisServices.ReadSchemaRowset(connection, Guid.Parse(rowset), settings.Limit).ToJson();
        }

        return new
        {
            ok = true,
            command = "schema",
            server = settings.Server,
            catalog = settings.Catalog,
            cubes = cubes.ToJson(),
            dimensions = dimensions.ToJson(),
            hierarchies = hierarchies.ToJson(),
            levels = levels.ToJson(),
            measures = measures.ToJson(),
            sets = sets.ToJson(),
            custom
        };
    }
}

public sealed class QueryCommand : JsonCommand<QuerySettings>
{
    protected override object ExecuteJson(QuerySettings settings, CancellationToken cancellationToken)
    {
        var query = settings.ResolveQuery();
        using var connection = AnalysisServices.OpenConnection(settings, cancellationToken);
        var resultSets = AnalysisServices.ExecuteTabular(connection, query, settings.Limit, settings.QueryTimeoutSeconds, cancellationToken);

        return new
        {
            ok = true,
            command = "query",
            server = settings.Server,
            catalog = settings.Catalog,
            resultSetCount = resultSets.Count,
            rowCount = resultSets.Sum(r => r.RowCount),
            truncated = resultSets.Any(r => r.Truncated),
            resultSets = resultSets.Select(r => r.ToJson()).ToList()
        };
    }
}

public abstract class JsonCommand<TSettings> : Command<TSettings>
    where TSettings : CommandSettings
{
    protected sealed override int Execute(CommandContext context, TSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteJson(ExecuteJson(settings, cancellationToken));
            return ExitCodes.Success;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("adomd error: operation was cancelled.");
            WriteJson(new
            {
                ok = false,
                error = "Operation was cancelled.",
                exception = typeof(OperationCanceledException).FullName
            });
            return ExitCodes.Cancelled;
        }
        catch (Exception ex)
        {
            var message = SecretRedactor.Redact(ex.Message);
            var innerMessage = SecretRedactor.Redact(ex.InnerException?.Message);
            Console.Error.WriteLine($"adomd error: {message}");
            WriteJson(new
            {
                ok = false,
                error = message,
                exception = ex.GetType().FullName,
                inner = innerMessage
            });
            return ExitCodes.Error;
        }
    }

    protected abstract object ExecuteJson(TSettings settings, CancellationToken cancellationToken);

    private static void WriteJson(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }));
    }
}

/// <summary>Scrubs credential-bearing fragments (passwords, secrets, tokens) out of text before it is ever
/// written to stderr or the JSON error payload. Some ADOMD/OLE DB providers echo the full connection string
/// back in exception messages, which would otherwise leak secrets passed via --connection-string.</summary>
public static partial class SecretRedactor
{
    [GeneratedRegex(
        @"(?<key>password|pwd|secret|client secret|access token)\s*=\s*[^;]*",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();

    [return: NotNullIfNotNull(nameof(text))]
    public static string? Redact(string? text) =>
        text is null
            ? null
            : SecretPattern().Replace(text, match => $"{match.Groups["key"].Value.ToLowerInvariant()}=***");
}

public class CommonSettings : CommandSettings
{
    [CommandOption("--server <SERVER>")]
    [Description("Analysis Services server.")]
    public string? Server { get; init; }

    [CommandOption("--catalog <CATALOG>")]
    [Description("Initial catalog/database.")]
    public string? Catalog { get; init; }

    [CommandOption("--connection-string <CONNECTION_STRING>")]
    [Description("Full ADOMD.NET connection string. Falls back to the ADOMD_CONNECTION_STRING environment variable when omitted, which keeps secrets out of the process command line and shell history.")]
    public string? ConnectionString { get; init; }

    [CommandOption("--limit <LIMIT>")]
    [Description("Maximum rows per result set.")]
    [DefaultValue(200)]
    public int Limit { get; init; }

    [CommandOption("--connect-timeout <SECONDS>")]
    [Description("Connection timeout in seconds.")]
    [DefaultValue(15)]
    public int ConnectTimeoutSeconds { get; init; }

    [CommandOption("--retries <N>")]
    [Description("Number of times to retry opening the connection after a transient failure.")]
    [DefaultValue(0)]
    public int Retries { get; init; }

    [CommandOption("--retry-delay-ms <MILLISECONDS>")]
    [Description("Delay between connection retry attempts, in milliseconds.")]
    [DefaultValue(1000)]
    public int RetryDelayMilliseconds { get; init; }

    /// <summary>The connection string to use, preferring the explicit option and falling back to the
    /// <c>ADOMD_CONNECTION_STRING</c> environment variable so secrets don't need to appear on the command line.</summary>
    public string? ResolveConnectionString() =>
        string.IsNullOrWhiteSpace(ConnectionString)
            ? Environment.GetEnvironmentVariable("ADOMD_CONNECTION_STRING")
            : ConnectionString;

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ResolveConnectionString()) && string.IsNullOrWhiteSpace(Server))
        {
            return ValidationResult.Error("Specify --server, --connection-string, or set the ADOMD_CONNECTION_STRING environment variable.");
        }

        if (Limit <= 0)
        {
            return ValidationResult.Error("--limit must be a positive integer.");
        }

        if (ConnectTimeoutSeconds <= 0)
        {
            return ValidationResult.Error("--connect-timeout must be a positive integer.");
        }

        if (Retries < 0)
        {
            return ValidationResult.Error("--retries must be zero or a positive integer.");
        }

        if (RetryDelayMilliseconds < 0)
        {
            return ValidationResult.Error("--retry-delay-ms must be zero or a positive integer.");
        }

        return ValidationResult.Success();
    }
}

public sealed class QuerySettings : CommonSettings
{
    [CommandOption("--query <QUERY>")]
    [Description("Query text. Use '-' to read from stdin.")]
    public string? Query { get; init; }

    [CommandOption("--query-file <PATH>")]
    [Description("File containing query text.")]
    public string? QueryFile { get; init; }

    [CommandOption("--query-timeout <SECONDS>")]
    [Description("Query timeout in seconds.")]
    [DefaultValue(120)]
    public int QueryTimeoutSeconds { get; init; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
        {
            return baseResult;
        }

        if (string.IsNullOrWhiteSpace(Query) && string.IsNullOrWhiteSpace(QueryFile))
        {
            return ValidationResult.Error("Specify --query or --query-file.");
        }

        if (!string.IsNullOrWhiteSpace(Query) && !string.IsNullOrWhiteSpace(QueryFile))
        {
            return ValidationResult.Error("Specify only one of --query or --query-file.");
        }

        if (QueryTimeoutSeconds <= 0)
        {
            return ValidationResult.Error("--query-timeout must be a positive integer.");
        }

        return ValidationResult.Success();
    }

    public string ResolveQuery()
    {
        if (!string.IsNullOrWhiteSpace(Query))
        {
            return Query == "-" ? Console.In.ReadToEnd() : Query;
        }

        if (!string.IsNullOrWhiteSpace(QueryFile))
        {
            return File.ReadAllText(QueryFile);
        }

        throw new InvalidOperationException("Query settings were not validated.");
    }
}

/// <summary>A set of rows capped at a caller-supplied limit, along with whether more rows existed than were returned.</summary>
public sealed class RowsetResult
{
    public required IReadOnlyList<Dictionary<string, object?>> Rows { get; init; }
    public required bool Truncated { get; init; }

    public int RowCount => Rows.Count;

    /// <summary>Shapes this result for JSON output as an object carrying its own row count and truncation flag,
    /// so callers never have to guess whether a short result was complete or cut off by --limit.</summary>
    public object ToJson() => new { rowCount = RowCount, truncated = Truncated, rows = Rows };

    public static RowsetResult Error(Exception ex) => new()
    {
        Truncated = false,
        Rows =
        [
            new Dictionary<string, object?>
            {
                ["error"] = ex.Message,
                ["exception"] = ex.GetType().FullName
            }
        ]
    };
}

/// <summary>Runs an operation with a bounded number of retries and a cancellable delay between attempts.
/// Extracted as a standalone helper so retry/backoff behavior can be unit tested without a live connection.</summary>
public static class RetryHelper
{
    public static T Execute<T>(int retries, int delayMilliseconds, CancellationToken cancellationToken, Func<T> action)
    {
        var totalAttempts = retries + 1;
        for (var attempt = 1; attempt <= totalAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return action();
            }
            catch when (attempt < totalAttempts)
            {
                // Transient failure with attempts remaining: wait (interruptibly) and try again.
                cancellationToken.WaitHandle.WaitOne(delayMilliseconds);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        throw new UnreachableException("Retry loop exited without returning or throwing.");
    }
}

public static class AnalysisServices
{
    public static AdomdConnection OpenConnection(CommonSettings settings, CancellationToken cancellationToken)
    {
        var connectionString = settings.ResolveConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var parts = new List<string>
            {
                $"Data Source={settings.Server}",
                "Integrated Security=SSPI",
                $"Connect Timeout={settings.ConnectTimeoutSeconds}"
            };

            if (!string.IsNullOrWhiteSpace(settings.Catalog))
            {
                parts.Add($"Initial Catalog={settings.Catalog}");
            }

            connectionString = string.Join(';', parts);
        }

        return RetryHelper.Execute(settings.Retries, settings.RetryDelayMilliseconds, cancellationToken, () =>
        {
            var connection = new AdomdConnection(connectionString);
            connection.Open();
            return connection;
        });
    }

    public static RowsetResult ReadCatalogs(AdomdConnection connection, int limit)
    {
        var dataSet = connection.GetSchemaDataSet(AdomdSchemaGuid.Catalogs, null);
        return dataSet.Tables.Count == 0
            ? new RowsetResult { Rows = [], Truncated = false }
            : DataTableToRows(dataSet.Tables[0], limit);
    }

    public static RowsetResult ReadSchemaRowset(AdomdConnection connection, Guid schema, int limit)
    {
        try
        {
            var dataSet = connection.GetSchemaDataSet(schema, null);
            return dataSet.Tables.Count == 0
                ? new RowsetResult { Rows = [], Truncated = false }
                : DataTableToRows(dataSet.Tables[0], limit);
        }
        catch (Exception ex)
        {
            return RowsetResult.Error(ex);
        }
    }

    /// <summary>Executes a query and returns one <see cref="RowsetResult"/> per result set, since a single
    /// MDX/DAX/DMX batch can return multiple result sets that were previously silently dropped.</summary>
    public static List<RowsetResult> ExecuteTabular(
        AdomdConnection connection,
        string query,
        int limit,
        int queryTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = queryTimeoutSeconds;

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                command.Cancel();
            }
            catch
            {
                // Best-effort: the command may have already completed or the provider may not support cancellation.
            }
        });

        cancellationToken.ThrowIfCancellationRequested();
        using var reader = command.ExecuteReader();
        var resultSets = new List<RowsetResult>();

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = new List<Dictionary<string, object?>>();
            var truncated = false;

            while (reader.Read())
            {
                if (rows.Count < limit)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }

                    rows.Add(row);
                }
                else
                {
                    // Keep draining so NextResult() can advance cleanly, but stop storing rows past the limit.
                    truncated = true;
                }
            }

            resultSets.Add(new RowsetResult { Rows = rows, Truncated = truncated });
        }
        while (reader.NextResult());

        return resultSets;
    }

    internal static RowsetResult DataTableToRows(DataTable table, int limit)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (DataRow dataRow in table.Rows)
        {
            if (rows.Count >= limit)
            {
                break;
            }

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn column in table.Columns)
            {
                var value = dataRow[column];
                row[column.ColumnName] = value == DBNull.Value ? null : value;
            }

            rows.Add(row);
        }

        return new RowsetResult { Rows = rows, Truncated = table.Rows.Count > limit };
    }
}
