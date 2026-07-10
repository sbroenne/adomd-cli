# adomd-cli

[![CI](https://github.com/sbroenne/adomd-cli/actions/workflows/ci.yml/badge.svg)](https://github.com/sbroenne/adomd-cli/actions/workflows/ci.yml)
[![CodeQL](https://github.com/sbroenne/adomd-cli/actions/workflows/codeql.yml/badge.svg)](https://github.com/sbroenne/adomd-cli/actions/workflows/codeql.yml)

`adomd-cli` is a small JSON-first command-line wrapper for querying Analysis Services through ADOMD.NET.

It is intended for automation, agents, and scripts that need predictable JSON output from SSAS, Azure Analysis Services, or Power BI/XMLA-compatible Analysis Services endpoints supported by `Microsoft.AnalysisServices.AdomdClient`.

The CLI uses `Spectre.Console.Cli` for command parsing/help and `Spectre.Console` for console infrastructure.

## Requirements

- .NET 10 SDK/runtime
- Network access to the Analysis Services endpoint
- Authentication supported by the connection string

When `--connection-string` is omitted, the CLI builds a connection string with `Integrated Security=SSPI`. Native SSAS TCP with Windows Integrated Auth generally requires Windows. The CI/CD workflows also run on Windows runners because ADOMD.NET is Windows-oriented.

Instead of passing `--connection-string` on the command line (which can leak into shell history and process listings), you can set the `ADOMD_CONNECTION_STRING` environment variable. The explicit option always takes precedence when both are set.

## Install

Download `adomd-cli-win-x64.zip` from the latest GitHub Release, extract it, and run the self-contained Windows executable:

```powershell
.\adomd.exe --help
```

Each release also publishes an `adomd-cli-win-x64.zip.sha256` checksum file. Verify the download before running it:

```powershell
(Get-FileHash .\adomd-cli-win-x64.zip -Algorithm SHA256).Hash -eq (Get-Content .\adomd-cli-win-x64.zip.sha256).Split(' ')[0].ToUpperInvariant()
```

From a local clone:

```powershell
dotnet publish .\src\Adomd.Cli\Adomd.Cli.csproj --configuration Release --runtime win-x64 --self-contained true --output .\artifacts\publish\win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
.\artifacts\publish\win-x64\adomd.exe --help
```

## Usage

```powershell
dotnet run --project src\Adomd.Cli -- probe --server <server>
dotnet run --project src\Adomd.Cli -- catalogs --server <server>
dotnet run --project src\Adomd.Cli -- schema --server <server> --catalog <catalog>
dotnet run --project src\Adomd.Cli -- query --server <server> --catalog <catalog> --query "<MDX-or-DAX>"
```

You can also pass a full ADOMD.NET connection string:

```powershell
dotnet run --project src\Adomd.Cli -- query --connection-string "<connection string>" --query-file query.mdx
```

## Commands

| Command | Purpose |
| --- | --- |
| `probe` | Open a connection and list visible catalogs |
| `catalogs` | List visible Analysis Services catalogs/databases |
| `schema` | Return cubes, dimensions, hierarchies, levels, measures, and sets |
| `query` | Execute MDX, DAX, DMX, or DMV text and return rows as JSON |
| `dmv` | Alias for `query` |

## Options

| Option | Description |
| --- | --- |
| `--server <name>` | Analysis Services server |
| `--catalog <name>` | Initial catalog/database |
| `--connection-string <value>` | Full ADOMD.NET connection string (falls back to the `ADOMD_CONNECTION_STRING` environment variable) |
| `--query <text>` | Query text; use `--query -` to read from stdin |
| `--query-file <path>` | File containing query text |
| `--limit <n>` | Maximum rows per result set; default `200` |
| `--connect-timeout <sec>` | Connection timeout; default `15` |
| `--query-timeout <sec>` | Query timeout; default `120` |
| `--retries <n>` | Number of times to retry opening the connection after a transient failure; default `0` |
| `--retry-delay-ms <ms>` | Delay between connection retry attempts; default `1000` |
| `--rowset <guid>` | `schema` only, repeatable; fetch additional schema rowsets by GUID beyond the built-in six |
| `--compact` | Emit single-line (unindented) JSON, friendlier for piping into line-based tools |

Retries only cover opening the connection (useful for Azure AS auto-resume/auto-scale delays); they do not retry a failed query execution. Retry waits are interruptible with Ctrl+C.

### Common schema rowset GUIDs

For use with `schema --rowset <guid>`, beyond the six built into every `schema` response (Cubes, Dimensions, Hierarchies, Levels, Measures, Sets):

| Rowset | GUID |
| --- | --- |
| MDSCHEMA_MEASUREGROUPS | `e1625ebf-fa96-42fd-bea6-db90adafd96b` |
| MDSCHEMA_KPIS | `2ae44109-ed3d-4842-b16f-b694d1cb0e3f` |
| MDSCHEMA_ACTIONS | `a07ccd08-8148-11d0-87bb-00c04fc33942` |
| MDSCHEMA_FUNCTIONS | `a07ccd07-8148-11d0-87bb-00c04fc33942` |
| DBSCHEMA_TABLES | `c8b52229-5cf3-11ce-ade5-00aa0044773d` |
| DISCOVER_XML_METADATA | `3444b255-171e-4cb9-ad98-19e57888a75f` |
| TMSCHEMA_ROLES (Tabular) | `a07ccd6b-8148-11d0-87bb-00c04fc33942` |
| TMSCHEMA_PERSPECTIVES (Tabular) | `a07ccd66-8148-11d0-87bb-00c04fc33942` |
| TMSCHEMA_CALCULATION_GROUPS (Tabular) | `a07ccd76-8148-11d0-87bb-00c04fc33942` |

The full set of ~150 rowset GUIDs is defined by `Microsoft.AnalysisServices.AdomdClient.AdomdSchemaGuid`.

## Output shape

Every row-returning field (`catalogs`, `cubes`, `dimensions`, `hierarchies`, `levels`, `measures`, `sets`, each `schema --rowset` entry, and each entry in `query`'s `resultSets`) is an object with `rowCount`, `truncated`, and `rows`, so you can always tell whether `--limit` cut off the result:

```json
{
  "ok": true,
  "command": "query",
  "resultSetCount": 1,
  "rowCount": 2,
  "truncated": false,
  "resultSets": [
    { "rowCount": 2, "truncated": false, "rows": [ { "col": 1 }, { "col": 2 } ] }
  ]
}
```

`query`/`dmv` can return more than one entry in `resultSets` when a batch produces multiple result sets.

If a `schema` rowset can't be read (for example, a rowset that isn't supported on the target model), `schema` reports `"partial": true` and lists the failures in a top-level `warnings` array, and the affected rowset object carries its own `error`/`exception` fields. The other rowsets are still returned.

By default JSON is pretty-printed; pass `--compact` to emit it on a single line for piping into tools like `jq`.

Errors produce `{ "ok": false, "error": ..., "exception": ..., "inner": ... }` and a non-zero exit code: `2` for a general failure, `130` if the command was cancelled (e.g. Ctrl+C). Credential-bearing fragments (`password=`, `pwd=`, `secret=`, `client secret=`, `access token=`) are redacted from `error`/`inner` and stderr before they're written, in case an ADOMD/OLE DB provider echoes the connection string back in an exception message.

## CI/CD

- `CI` runs on pushes to `main` and pull requests. It restores, verifies formatting, builds, runs tests when test projects exist, publishes the Windows executable, and uploads the zipped artifact.
- `CodeQL` runs on pushes, pull requests, and a weekly schedule.
- `Release` runs for semantic version tags like `v1.2.3`. It verifies the tag matches `VersionPrefix` in `Adomd.Cli.csproj`, publishes the Windows executable, creates a GitHub Release, and attaches the zipped artifact plus a SHA256 checksum file.

To create a release:

```powershell
git tag v0.1.0
git push origin v0.1.0
```
