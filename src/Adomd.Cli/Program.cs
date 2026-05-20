using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AnalysisServices.AdomdClient;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("adomd");
    config.SetApplicationVersion("0.1.0");

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

public sealed class ProbeCommand : JsonCommand<CommonSettings>
{
    protected override object ExecuteJson(CommonSettings settings)
    {
        var sw = Stopwatch.StartNew();
        using var connection = AnalysisServices.OpenConnection(settings);
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
            catalogs
        };
    }
}

public sealed class CatalogsCommand : JsonCommand<CommonSettings>
{
    protected override object ExecuteJson(CommonSettings settings)
    {
        using var connection = AnalysisServices.OpenConnection(settings);
        return new
        {
            ok = true,
            command = "catalogs",
            server = settings.Server,
            catalogs = AnalysisServices.ReadCatalogs(connection, settings.Limit)
        };
    }
}

public sealed class SchemaCommand : JsonCommand<CommonSettings>
{
    protected override object ExecuteJson(CommonSettings settings)
    {
        using var connection = AnalysisServices.OpenConnection(settings);
        return new
        {
            ok = true,
            command = "schema",
            server = settings.Server,
            catalog = settings.Catalog,
            cubes = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Cubes, settings.Limit),
            dimensions = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Dimensions, settings.Limit),
            hierarchies = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Hierarchies, settings.Limit),
            levels = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Levels, settings.Limit),
            measures = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Measures, settings.Limit),
            sets = AnalysisServices.ReadSchemaRowset(connection, AdomdSchemaGuid.Sets, settings.Limit)
        };
    }
}

public sealed class QueryCommand : JsonCommand<QuerySettings>
{
    protected override object ExecuteJson(QuerySettings settings)
    {
        var query = settings.ResolveQuery();
        using var connection = AnalysisServices.OpenConnection(settings);
        var rows = AnalysisServices.ExecuteTabular(connection, query, settings.Limit, settings.QueryTimeoutSeconds);

        return new
        {
            ok = true,
            command = "query",
            server = settings.Server,
            catalog = settings.Catalog,
            rowCount = rows.Count,
            rows
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
            WriteJson(ExecuteJson(settings));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"adomd error: {ex.Message}");
            WriteJson(new
            {
                ok = false,
                error = ex.Message,
                exception = ex.GetType().FullName,
                inner = ex.InnerException?.Message
            });
            return 2;
        }
    }

    protected abstract object ExecuteJson(TSettings settings);

    private static void WriteJson(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }));
    }
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
    [Description("Full ADOMD.NET connection string.")]
    public string? ConnectionString { get; init; }

    [CommandOption("--limit <LIMIT>")]
    [Description("Maximum rows per result set.")]
    [DefaultValue(200)]
    public int Limit { get; init; }

    [CommandOption("--connect-timeout <SECONDS>")]
    [Description("Connection timeout in seconds.")]
    [DefaultValue(15)]
    public int ConnectTimeoutSeconds { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString) && string.IsNullOrWhiteSpace(Server))
        {
            return ValidationResult.Error("Specify --server or --connection-string.");
        }

        if (Limit <= 0)
        {
            return ValidationResult.Error("--limit must be a positive integer.");
        }

        if (ConnectTimeoutSeconds <= 0)
        {
            return ValidationResult.Error("--connect-timeout must be a positive integer.");
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

public static class AnalysisServices
{
    public static AdomdConnection OpenConnection(CommonSettings settings)
    {
        var connectionString = settings.ConnectionString;
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

        var connection = new AdomdConnection(connectionString);
        connection.Open();
        return connection;
    }

    public static IReadOnlyList<Dictionary<string, object?>> ReadCatalogs(AdomdConnection connection, int limit)
    {
        var dataSet = connection.GetSchemaDataSet(AdomdSchemaGuid.Catalogs, null);
        return dataSet.Tables.Count == 0 ? [] : DataTableToRows(dataSet.Tables[0], limit);
    }

    public static IReadOnlyList<Dictionary<string, object?>> ReadSchemaRowset(AdomdConnection connection, Guid schema, int limit)
    {
        try
        {
            var dataSet = connection.GetSchemaDataSet(schema, null);
            return dataSet.Tables.Count == 0 ? [] : DataTableToRows(dataSet.Tables[0], limit);
        }
        catch (Exception ex)
        {
            return
            [
                new Dictionary<string, object?>
                {
                    ["error"] = ex.Message,
                    ["exception"] = ex.GetType().FullName
                }
            ];
        }
    }

    public static List<Dictionary<string, object?>> ExecuteTabular(
        AdomdConnection connection,
        string query,
        int limit,
        int queryTimeoutSeconds)
    {
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = queryTimeoutSeconds;

        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read() && rows.Count < limit)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static List<Dictionary<string, object?>> DataTableToRows(DataTable table, int limit)
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

        return rows;
    }
}
