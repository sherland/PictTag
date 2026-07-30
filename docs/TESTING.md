# Testing

```bash
dotnet test source/PictTag.Core.Tests
```

The test project uses xUnit v3 on the Microsoft.Testing.Platform runner (`global.json` sets
`"test": { "runner": "Microsoft.Testing.Platform" }`), **not** VSTest — that matters for one
command-line detail below. `PictTag.Core`'s `AssemblyInfo.cs` grants the test project
`InternalsVisibleTo`, so tests exercise `internal` helper types (`HierarchicalTagPath`,
`IptcSceneCode`, `IptcDigitalSourceType`, `XmpNamespaces`, ...) directly rather than only through
the public writer classes.

There are three test categories, two of which are opt-in and skipped by default:

## 1. Unit tests — always run

Fast, no external dependencies: `MwgRegionAreaTests`, `SidecarPathResolverTests`,
`HierarchicalTagPathTests`, plus every `XmpCoreSidecarWriterTests` test (the pure-.NET `XmpCore`
engine has no external process to depend on) and the handful of `ExifToolSidecarWriterTests`
cases that specifically test *absence* of `exiftool`. This is what a default `dotnet test` run
exercises.

## 2. `exiftool`-conditional tests — need `exiftool` on `PATH`

Most of `ExifToolSidecarWriterTests` starts with:

```csharp
Assert.SkipUnless(ExifToolSidecarWriter.IsExifToolAvailable, "exiftool not found on PATH");
```

(and the one test that checks the *not-found* error path uses `Assert.SkipWhen` with the same
condition inverted). Install [exiftool](https://exiftool.org/) and make sure it's on `PATH`, then
a normal `dotnet test` run picks these up automatically — no flag needed, detection is automatic
via `ExifToolSidecarWriter.IsExifToolAvailable`.

These exist specifically to cross-check `XmpCoreSidecarWriter`'s hand-built XMP against exiftool's
own, independently-maintained tag tables — see
[ARCHITECTURE.md](ARCHITECTURE.md#two-writer-engines) for why that's worth having.

## 3. Live-model tests — need a real Ollama server + downloaded fixtures

`ArtStyleDetectionTests` runs the real detection pipeline against ~100 real, freely-licensed
art-style images (one theory case per downloaded fixture, sourced from
`data/art-styles-manifest.json`'s 37 art movements) and checks that the model's `ArtStyle` guess
contains one of each movement's expected keywords. This hits a real Ollama server, so every case
starts with:

```csharp
Assert.SkipUnless(LiveModelTestsEnabled, "set PICTTAG_RUN_LIVE_MODEL_TESTS=1 ...");
```

To run it:

```bash
# 1. Ollama running locally with the model pulled (see README prerequisites)
ollama pull gemma4:26b

# 2. Download the fixtures once (skips images already present on re-run)
pwsh ./Get-ArtStyleTestImages.ps1

# 3. Opt in and run
PICTTAG_RUN_LIVE_MODEL_TESTS=1 dotnet test source/PictTag.Core.Tests
```

The fixtures land under `data/test-images/art-styles/<slug>/` and are **git-ignored** — bulk
binary images that are cheap to regenerate from the manifest are not worth committing. If that
directory is empty or absent, `ArtStyleDetectionTests.Fixtures()` (in
`ArtStyleDetectionTests.cs`) simply finds nothing to add per style and returns an empty
`TheoryData` — check that method directly if you need to know exactly how the runner reports zero
discovered cases for a `[Theory]` in this xUnit v3/MTP setup, since that detail isn't verified
here.

### Running a specific test class

The MTP runner's `dotnet test` does **not** accept VSTest's `--filter`; using it produces
`Unknown option`. Use `--filter-class` after a `--` separator instead:

```bash
dotnet test source/PictTag.Core.Tests -- --filter-class "*.HierarchicalTagPathTests"
```

## Fixture-management scripts (repo root)

These are developer tools, not part of `dotnet test` — run them directly with PowerShell (`pwsh`):

| Script | Purpose |
|---|---|
| `Get-ArtStyleTestImages.ps1` | Downloads 2–3 freely-licensed images per art style from Wikimedia Commons (public domain / CC0 / CC-BY only, filtered by license string), resizes anything over 2000px, and skips images already downloaded. Feeds both `ArtStyleDetectionTests` and `Test-ArtStyleDetection.ps1`. |
| `Test-ArtStyleDetection.ps1` | Runs the CLI's detection + XMP pipeline over every downloaded art-style fixture and prints a pass/fail-style summary table (style vs. detected medium/art-style/composition) plus a CSV (`art-style-detection-results.csv`, git-ignored). This is a human-readable accuracy report, not an assertion-based test — use it to eyeball detection quality across the whole style set, or after prompt changes, without editing test code. Pass `-Overwrite` to regenerate every sidecar instead of only images that don't have one yet. |
| `Update-TestImageTags.ps1` | Regenerates the XMP sidecars for the small, **committed** fixture set under `data/test-images/*.xmp` (the non-art-style photos checked into git) by re-running the CLI against them with `--xmp-overwrite`. Run this after a change to the detection prompt or an XMP writer to refresh what's checked in — then review the resulting diff by hand, since the model's output is not byte-for-byte deterministic between runs. |

## What isn't covered

There's no CI configuration in this repository (no `.github/workflows`) — running `dotnet test`
(and, when relevant, the two opt-in categories above) locally before pushing is the current
verification step. `ImageDetectionService`'s prompt/schema and `Program.cs`'s CLI wiring don't
have dedicated unit tests beyond what `ArtStyleDetectionTests` exercises end-to-end; changes there
are best verified by actually running the CLI against a real image (`dotnet run --project
source/PictTag.Cli -- -i <photo> --xmp`) and reading the console output and sidecar by hand.
