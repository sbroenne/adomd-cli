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

## Install

Download `adomd-cli-win-x64.zip` from the latest GitHub Release, extract it, and run the self-contained Windows executable:

```powershell
.\adomd.exe --help
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
| `--connection-string <value>` | Full ADOMD.NET connection string |
| `--query <text>` | Query text; use `--query -` to read from stdin |
| `--query-file <path>` | File containing query text |
| `--limit <n>` | Maximum rows per result set; default `200` |
| `--connect-timeout <sec>` | Connection timeout; default `15` |
| `--query-timeout <sec>` | Query timeout; default `120` |

## CI/CD

- `CI` runs on pushes to `main` and pull requests. It restores, verifies formatting, builds, runs tests when test projects exist, publishes the Windows executable, and uploads the zipped artifact.
- `CodeQL` runs on pushes, pull requests, and a weekly schedule.
- `Release` runs for semantic version tags like `v1.2.3`. It publishes the Windows executable, creates a GitHub Release, and attaches the zipped artifact.

To create a release:

```powershell
git tag v0.1.0
git push origin v0.1.0
```
