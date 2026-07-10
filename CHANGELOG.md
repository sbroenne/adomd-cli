# Changelog

All notable changes to this project are documented in this file.

## [0.2.0]

### Added
- `Adomd.Cli.Tests` xUnit test project covering settings validation, connection-string resolution, query resolution, and rowset truncation logic. Wired into CI via the existing test-discovery step.
- `ADOMD_CONNECTION_STRING` environment variable as an alternative to `--connection-string`, so secrets don't need to appear on the command line or in shell history.
- `--rowset <GUID>` option on `schema` to fetch additional schema rowsets beyond the built-in six.
- Truncation reporting: every rowset in the JSON output now reports `rowCount` and `truncated` so callers can tell whether `--limit` cut off results.
- Multiple result set support: `query`/`dmv` now return a `resultSets` array, so multi-statement batches no longer silently drop all but the first result set.
- Cancellation support: commands honor Ctrl+C during query execution and exit with code `130`.
- Release workflow now verifies the pushed tag matches `VersionPrefix` in `Adomd.Cli.csproj` before publishing.
- `--retries`/`--retry-delay-ms` options to retry opening the connection on transient failures, with a cancellable delay between attempts.
- Secrets (password/pwd/secret/client secret/access token fragments) are now redacted from error output before it's written to stderr or the JSON payload.
- Release artifacts now include a SHA256 checksum file alongside the zip.
- Dependabot now also tracks NuGet packages in `Adomd.Cli.Tests`.
- README table of common `schema --rowset` GUIDs.

### Changed
- **Breaking:** JSON output shape for `catalogs`, `schema`, and `query` changed. Row-bearing fields are now objects of the form `{ rowCount, truncated, rows }` instead of bare arrays, and `query`/`dmv` return `resultSets` instead of a top-level `rows`/`rowCount` pair.
- Updated dependencies: `Microsoft.AnalysisServices.AdomdClient` to `19.114.8`, `Spectre.Console` to `0.57.2`, and the .NET SDK pinned in `global.json` to `10.0.301`.

## [0.1.0]

- Initial release: `probe`, `catalogs`, `schema`, `query`, and `dmv` commands.
