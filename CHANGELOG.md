# Changelog

All notable changes to this project are documented in this file.

## [0.2.2]

### Added
- Passive update notification: when run interactively, the CLI performs a best-effort, once-per-day check against the GitHub Releases API and prints a one-line notice on stderr if a newer version is available. It never writes to stdout, is skipped when stderr is redirected (scripts/CI), fails silently offline, and can be disabled with `ADOMD_NO_UPDATE_CHECK=1`.

## [0.2.1]

### Added
- `--compact` option on all commands to emit single-line (unindented) JSON for piping into line-based tools.
- `schema` now reports a top-level `partial` flag and a `warnings` array when one or more schema rowsets fail, instead of only signalling failure inside individual rows.

### Changed
- UTF-8 is now forced for console output so non-ASCII catalog, dimension, and measure names/values are emitted and parsed correctly regardless of the host code page.
- When a `schema` rowset fails, the error is now reported via top-level `warnings` and per-rowset `error`/`exception` fields rather than as a synthetic data row inside `rows`.
- Release builds now use ReadyToRun for faster startup, and the GitHub Release notes are populated from the matching `CHANGELOG.md` section.

### Fixed
- Corrected a stale hard-coded version fallback used when the assembly informational version attribute is unavailable.

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
