using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.AnalysisServices.AdomdClient;
using Spectre.Console;
using Spectre.Console.Cli;

// Emit UTF-8 so non-ASCII catalog/dimension/measure names and data values survive being
// written to the console and parsed downstream, regardless of the host's default code page.
Console.OutputEncoding = Encoding.UTF8;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("adomd");
    config.SetApplicationVersion(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0");

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

var exitCode = app.Run(args);
UpdateNotifier.Notify();
return exitCode;

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
        var customResults = new List<(string Name, RowsetResult Result)>();
        foreach (var rowset in settings.Rowsets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = AnalysisServices.ReadSchemaRowset(connection, Guid.Parse(rowset), settings.Limit);
            customResults.Add((rowset, result));
            custom[rowset] = result.ToJson();
        }

        var named = new (string Name, RowsetResult Result)[]
        {
            ("cubes", cubes),
            ("dimensions", dimensions),
            ("hierarchies", hierarchies),
            ("levels", levels),
            ("measures", measures),
            ("sets", sets)
        };

        var warnings = named.Concat(customResults)
            .Where(n => n.Result.IsError)
            .Select(n => new { rowset = n.Name, error = n.Result.ErrorMessage, exception = n.Result.ErrorType })
            .ToList();

        return new
        {
            ok = warnings.Count == 0,
            partial = warnings.Count > 0,
            command = "schema",
            server = settings.Server,
            catalog = settings.Catalog,
            cubes = cubes.ToJson(),
            dimensions = dimensions.ToJson(),
            hierarchies = hierarchies.ToJson(),
            levels = levels.ToJson(),
            measures = measures.ToJson(),
            sets = sets.ToJson(),
            custom,
            warnings
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
        var compact = settings is CommonSettings common && common.Compact;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteJson(ExecuteJson(settings, cancellationToken), compact);
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
            }, compact);
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
            }, compact);
            return ExitCodes.Error;
        }
    }

    protected abstract object ExecuteJson(TSettings settings, CancellationToken cancellationToken);

    private static void WriteJson(object value, bool compact)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = !compact,
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

    [CommandOption("--compact")]
    [Description("Emit single-line (unindented) JSON, which is friendlier for piping into line-based tools.")]
    [DefaultValue(false)]
    public bool Compact { get; init; }

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

    /// <summary>Non-null when the rowset could not be read; carries the failure message so callers can tell an
    /// empty result apart from a failed one.</summary>
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }

    public int RowCount => Rows.Count;

    public bool IsError => ErrorMessage is not null;

    /// <summary>Shapes this result for JSON output as an object carrying its own row count and truncation flag,
    /// so callers never have to guess whether a short result was complete or cut off by --limit.</summary>
    public object ToJson() => ErrorMessage is null
        ? new { rowCount = RowCount, truncated = Truncated, rows = Rows }
        : new { rowCount = RowCount, truncated = Truncated, rows = Rows, error = ErrorMessage, exception = ErrorType };

    public static RowsetResult Error(Exception ex) => new()
    {
        Truncated = false,
        Rows = [],
        ErrorMessage = ex.Message,
        ErrorType = ex.GetType().FullName
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
        var connectionString = BuildConnectionString(settings);

        return RetryHelper.Execute(settings.Retries, settings.RetryDelayMilliseconds, cancellationToken, () =>
        {
            var connection = new AdomdConnection(connectionString);
            connection.Open();
            return connection;
        });
    }

    /// <summary>Resolves the connection string to use: an explicit connection string (option or env var) wins,
    /// otherwise a Windows-integrated (SSPI) string is composed from --server/--catalog/--connect-timeout.
    /// Extracted so the composition logic can be unit tested without opening a live connection.</summary>
    internal static string BuildConnectionString(CommonSettings settings)
    {
        var connectionString = settings.ResolveConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

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

        return string.Join(';', parts);
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

/// <summary>Best-effort, fail-silent nudge that tells the user (on stderr only) when a newer release exists.
/// It never touches stdout, is throttled to at most one network call per day, uses a tight HTTP timeout, and
/// is easy to silence for automation. Any failure is swallowed so it can never affect the command outcome.</summary>
public static class UpdateNotifier
{
    private const string Repository = "sbroenne/adomd-cli";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(2);

    public static void Notify() => Notify(Console.Error);

    internal static void Notify(TextWriter stderr)
    {
        try
        {
            if (IsDisabled())
            {
                return;
            }

            if (!TryParseVersion(GetCurrentInformationalVersion(), out var current))
            {
                return;
            }

            var latest = GetLatestKnownRelease();
            if (latest is { } release && IsNewer(current, release.Version))
            {
                stderr.WriteLine(
                    $"adomd: version {release.Version} is available (you have {current}). {release.Url}");
                stderr.WriteLine("adomd: set ADOMD_NO_UPDATE_CHECK=1 to silence this check.");
            }
        }
        catch
        {
            // Update notification is strictly best-effort; never let it affect the command result.
        }
    }

    /// <summary>True when the check should be skipped: explicitly opted out, or stderr is being captured
    /// (typical of scripts/CI) where an unsolicited message would be noise.</summary>
    private static bool IsDisabled() => IsDisabled(
        Environment.GetEnvironmentVariable("ADOMD_NO_UPDATE_CHECK"),
        Console.IsErrorRedirected);

    internal static bool IsDisabled(string? optOutValue, bool stderrRedirected)
    {
        var explicitlyDisabled = !string.IsNullOrEmpty(optOutValue)
            && optOutValue != "0"
            && !string.Equals(optOutValue, "false", StringComparison.OrdinalIgnoreCase);

        return explicitlyDisabled || stderrRedirected;
    }

    /// <summary>Returns the latest release we know about, refreshing from GitHub at most once per
    /// <see cref="CheckInterval"/> and otherwise serving the cached value so invocations stay fast.</summary>
    private static (Version Version, string Url)? GetLatestKnownRelease()
    {
        var cachePath = GetCachePath();
        var cache = ReadCache(cachePath);

        var due = cache is null || DateTimeOffset.UtcNow - cache.Value.LastCheckUtc >= CheckInterval;
        if (due)
        {
            var fetched = FetchLatestRelease();
            // Record the attempt time regardless of success so an offline machine isn't slowed on every run,
            // while keeping any previously cached version if this refresh failed.
            var version = fetched?.Version.ToString() ?? cache?.Version;
            var url = fetched?.Url ?? cache?.Url;
            WriteCache(cachePath, new CacheEntry(DateTimeOffset.UtcNow, version, url));
            cache = new CacheEntry(DateTimeOffset.UtcNow, version, url);
        }

        if (cache?.Version is { } cachedVersion
            && cache.Value.Url is { } cachedUrl
            && TryParseVersion(cachedVersion, out var parsed))
        {
            return (parsed, cachedUrl);
        }

        return null;
    }

    private static (Version Version, string Url)? FetchLatestRelease()
    {
        using var http = new HttpClient { Timeout = HttpTimeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("adomd-cli");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var response = http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest")
            .GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("tag_name", out var tagElement)
            || !TryParseVersion(tagElement.GetString(), out var version))
        {
            return null;
        }

        var url = root.TryGetProperty("html_url", out var urlElement)
            ? urlElement.GetString() ?? $"https://github.com/{Repository}/releases/latest"
            : $"https://github.com/{Repository}/releases/latest";

        return (version, url);
    }

    private static string GetCurrentInformationalVersion() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>Parses a release tag or version string into a comparable <see cref="Version"/>, tolerating a
    /// leading 'v' and stripping any pre-release/build metadata (e.g. "v1.2.3-rc1+sha" -> 1.2.3).</summary>
    internal static bool TryParseVersion(string? raw, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var metadataStart = trimmed.IndexOfAny(['-', '+']);
        if (metadataStart >= 0)
        {
            trimmed = trimmed[..metadataStart];
        }

        return Version.TryParse(trimmed, out version);
    }

    internal static bool IsNewer(Version current, Version latest) => latest > current;

    private static string GetCachePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "adomd-cli",
            "update-check.json");

    private static CacheEntry? ReadCache(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("lastCheckUtc", out var lastCheckElement)
                || !lastCheckElement.TryGetDateTimeOffset(out var lastCheck))
            {
                return null;
            }

            var version = root.TryGetProperty("latestVersion", out var v) ? v.GetString() : null;
            var url = root.TryGetProperty("latestUrl", out var u) ? u.GetString() : null;
            return new CacheEntry(lastCheck, version, url);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteCache(string path, CacheEntry entry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = JsonSerializer.Serialize(new
            {
                lastCheckUtc = entry.LastCheckUtc,
                latestVersion = entry.Version,
                latestUrl = entry.Url
            });
            File.WriteAllText(path, payload);
        }
        catch
        {
            // A non-writable cache just means we re-check next time; not worth surfacing.
        }
    }

    private readonly record struct CacheEntry(DateTimeOffset LastCheckUtc, string? Version, string? Url);
}
