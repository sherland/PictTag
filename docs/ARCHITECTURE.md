# Architecture

## Pipeline overview

```
image file
   │
   ▼
ImageDetectionService.DetectAsync()          [source/PictTag.Core/ImageDetectionService.cs]
   │  sends the image + DetectionPrompt to Ollama, requesting a JSON response
   │  constrained to DetectionResponseDto's schema (ChatResponseFormat.ForJsonSchema)
   ▼
one RawDetection per detected object       [Models.cs]
   │
   ▼
EntityTaxonomyResolver.ResolveAsync()        [source/PictTag.Core/Taxonomy/EntityTaxonomyResolver.cs]
   │  resolves each RawDetection against the WordNet-derived taxonomy - see
   │  "Taxonomy resolution" below and docs/TAXONOMY.md for the full picture
   ▼
ImageAnalysisResult { ImageMetadata, List<DetectedEntity> }     [Models.cs]
   │  each DetectedEntity carries both the raw LLM output and the resolved
   │  TaxonomyMatch (or null, if nothing resolved confidently)
   │
   ├─▶ ProcessAndAnnotateAsync() draws bounding boxes + labels onto a copy of the
   │    image (ImageSharp) and saves it — the "annotated preview" the CLI produces.
   │
   ▼
IXmpSidecarWriter.WriteSidecarAsync()          [source/PictTag.Core/Xmp/*SidecarWriter.cs]
   │  one of two interchangeable implementations, see "Two writer engines" below
   ▼
a .xmp sidecar file next to the image
```

`source/PictTag.Cli/Program.cs` is a thin `System.CommandLine` wrapper around this: it resolves
the `--input` glob to a file list, calls `ProcessAndAnnotateAsync` for each, prints a summary, and
optionally calls the selected `IXmpSidecarWriter`.

## Core types (`Models.cs`)

- **`ImageMetadata`** — whole-image facts: `Title`, `Description`, `AltText`, `Medium`
  (`ImageMedium`), `ArtStyle` (nullable — only set for non-photographic media), `Setting`
  (`ImageSetting`, nullable), `Scene` (`List<SceneType>`), `Composition` (`ImageComposition`).
- **`DetectedEntity`** — one detected object: `Raw` (`RawDetection` — exactly what the model
  returned, never modified: `Label` specific e.g. "angel", `Group` a step more general e.g.
  "religious figure", `Category` broad/`EntityCategory`), `Taxonomy` (`TaxonomyMatch?` — the
  resolved WordNet ancestor chain, or null if nothing resolved confidently; see "Taxonomy
  resolution" below), `Box` (`BoundingBox`, a 0–1000 normalized grid regardless of the image's
  actual pixel dimensions). Raw and resolved are kept as separate fields deliberately — `Raw` is
  what actually shipped in the model's response (useful for debugging/search), `Taxonomy` is the
  normalized, cross-image-consistent form the XMP writers prefer when it's available.
- **`ImageComposition`** — `Symmetry`, `RuleOfThirdsAdherence`, `ColorVarianceEstimate`,
  `EdgeDensityEstimate` (both 0.0–1.0 *impressions*, not measured statistics — the model is asked
  for a subjective read, not to run a histogram), and an optional free-text `Notes`.
  Composition is computed for every image (photographs included), not just paintings/drawings.

All the enums the model can return (`ImageMedium`, `ImageSetting`, `CompositionSymmetry`,
`SceneType`, `EntityCategory`) live in `Models.cs` next to the records that use them, and are
deserialized straight out of the model's JSON response via `JsonStringEnumConverter`. Adding a new
member makes it a legal value in the generated JSON schema automatically (`ChatResponseFormat.
ForJsonSchema<DetectionResponseDto>()` reflects the enum), but the *natural-language* instructions
in `DetectionPrompt` that spell out each enum's valid options for the model (e.g. "Symmetrical,
Asymmetrical, RadialSymmetry, or None") are plain prompt text, not generated from the enum — those
still need a manual edit alongside the C# change, or the model won't know the new option exists
even though the schema would accept it.

A third thing needs to stay loosely in sync for the same reason: `EntityCategory`'s members and
the taxonomy build pipeline's `rootCollapseAnchors` (`data/wordnet/seeds/trim-config.json`, one
WordNet synset per category — see [TAXONOMY.md](TAXONOMY.md)). Adding an `EntityCategory` member
without a matching anchor just means that category never gets a resolved taxonomy chain (entities
tagged with it always fall back to the raw shape) - not a compile error, so it's easy to miss.

## The detection call

`ImageDetectionService.DetectAsync`:

1. Reads the image bytes and picks a MIME type from the file extension.
2. Sends one `ChatMessage` containing the fixed `DetectionPrompt` text plus the image as
   `DataContent`, via `Microsoft.Extensions.AI`'s `IChatClient` (backed by `OllamaSharp`).
3. Constrains the response with `ChatResponseFormat.ForJsonSchema<DetectionResponseDto>()` —
   Ollama enforces the shape, so parsing is a straight `JsonSerializer.Deserialize`, not
   best-effort text scraping.
4. Maps the DTO to `ImageMetadata` + `List<DetectedEntity>`.

`DetectionPrompt` is the single source of truth for what the model is asked to produce — read it
directly in the source rather than trusting a paraphrase, since it's the kind of thing that drifts
from any doc that isn't the code itself. Every field in `DetectionResponseDto` corresponds to an
instruction in that prompt.

Two things are currently hardcoded rather than configurable: the model name (`gemma4:26b`) and
the JSON schema/prompt text. Only the Ollama server URL is a CLI option.

## Taxonomy resolution

An LLM is an open-vocabulary generator: the same golden retriever might get labeled "dog",
"canine", or "pet" across different photos, which defeats consistent tag-based browsing. Rather
than trying to force the model into a closed vocabulary (impractical — a real taxonomy has
thousands of terms, far too many for a JSON-schema enum or a prompt), `RawDetection`'s free text
is resolved *after* the model responds, against a WordNet-derived taxonomy shipped with
`PictTag.Core`. See **[docs/TAXONOMY.md](TAXONOMY.md)** for the full picture (how the taxonomy
data is built, how to extend it, and the offline `PictTag.TaxonomyBuilder` tool) — this section
covers just the runtime pieces, in `source/PictTag.Core/Taxonomy/`:

- **`ITaxonomyProvider`** / **`WordNetTaxonomyProvider`** — exact-lemma lookup and ancestor-chain
  queries against the embedded `taxonomy.json`.
- **`ITaxonomyEmbeddingIndex`** / **`OllamaTaxonomyEmbeddingIndex`** — semantic fallback (a local
  embedding model, `nomic-embed-text` by default) for free text that doesn't literally match any
  WordNet lemma, via a brute-force cosine-similarity scan over the embedded
  `taxonomy-embeddings.bin`.
- **`EntityTaxonomyResolver`** — the actual per-entity pipeline: try an exact match on `Label`,
  then `Group`; if neither hits, try the embedding index the same way. Returns null (unresolved)
  if nothing clears the similarity threshold - `DetectedEntity.Taxonomy` is then null, and both
  XMP writers fall back to the raw `Category`/`Group`/`Label` shape for that entity rather than
  dropping it or guessing. This is the feature's *only* fallback path, not a legacy-compatibility
  concern — it's what keeps taxonomy resolution strictly additive.

`ImageDetectionService`'s constructor takes an optional `ITaxonomyProvider`/`ITaxonomyEmbeddingIndex`
pair, defaulting to lazy shared singletons so the embedded data and the embedding client are built
once, not per `DetectAsync` call. Swap in different instances to use a different/tuned dataset or
a non-default Ollama server without touching detection logic.

## Two writer engines

`IXmpSidecarWriter` has exactly one method, `WriteSidecarAsync`, and two implementations that are
meant to be interchangeable — same inputs, equivalent XMP output (verified by the largely
parallel test suites in `XmpCoreSidecarWriterTests.cs` and `ExifToolSidecarWriterTests.cs`):

- **`XmpCoreSidecarWriter`** — builds the XMP document directly using `XmpCore` (a pure-managed
  port of Adobe's XMP Toolkit). No external process, no PATH dependency. This is the default.
- **`ExifToolSidecarWriter`** — shells out to the real `exiftool` binary. Useful as an independent
  cross-check against the hand-built XmpCore output (exiftool is the de facto reference
  implementation most photo apps interoperate against), and for projects that already depend on
  exiftool for other tasks. Requires `exiftool` on `PATH` (`IsExifToolAvailable` gates both the
  writer and its tests).

Why keep both instead of picking one? Because `XmpCoreSidecarWriter` is hand-building XMP
structures (arrays, structs, localized text) against namespace specs read from documentation —
having `ExifToolSidecarWriter` independently produce the same fields via exiftool's own,
separately-maintained tag tables is a real correctness check, not just redundancy. Several bugs
in this codebase were caught exactly this way (see [XMP-SCHEMA.md](XMP-SCHEMA.md) for specifics).

Both engines share small, focused helper types in `source/PictTag.Core/Xmp/` rather than
duplicating logic:

- `SidecarPathResolver` — turns an image path + `XmpSidecarNamingConvention` into a sidecar path.
- `HierarchicalTagPath` — builds and sanitizes the hierarchical tag path segments both engines
  write into `dc:subject`/`lr:hierarchicalSubject`/`digiKam:TagsList`. `BuildEntitySegments` picks
  a resolved taxonomy ancestor chain (arbitrary depth, e.g. `Animal > Chordate > ... > Retriever >
  Golden Retriever`) when available, or the raw `Category`/`Group`/`Label` shape otherwise;
  `BuildSegments` (a variable-length collapse of adjacent duplicate segments) and `Compose`
  (separator-safe joining) are the shared primitives both paths go through.
- `MwgRegionArea` / `IptcRegionBoundary` — convert a `BoundingBox` into each region schema's own
  coordinate convention (center-based vs. top-left-corner-based — see XMP-SCHEMA.md).
- `IptcDigitalSourceType` / `IptcSceneCode` — map PictTag's own enums to IPTC controlled-
  vocabulary codes/URIs.

## Design principle: standards first

Every field PictTag writes is placed under a real, pre-existing XMP standard (Dublin Core, IPTC
Photo Metadata Core/Extension, Metadata Working Group Regions, Lightroom's hierarchical-subject
convention, digiKam's tag-list convention) whenever one genuinely covers it. The custom
`pictTag:` namespace (`XmpNamespaces.PictTag`) is reserved only for fields with no standard home
at all — currently `Medium`, `Setting`, and the composition metrics. Two fields
(`ArtStyle`→`Iptc4xmpExt:Genre`, and the old ad hoc region format→`Iptc4xmpExt:ImageRegion`)
started out custom in earlier phases of this project and were migrated once a real standard field
was found for them — see the commit history and [XMP-SCHEMA.md](XMP-SCHEMA.md) for what moved and
why.

This matters practically, not just architecturally: fields under real standards show up correctly
in digiKam, Lightroom, and other photo managers with zero configuration on their end. A custom
namespace only ever displays as raw, unstructured metadata to anything that isn't PictTag itself.

The corollary is a habit visible throughout the codebase's comments and commit messages: claims
about what a given XMP property requires (its structure, its array type, its coordinate
convention) are verified against a real, authoritative source — an official controlled-vocabulary
registry, exiftool's own tag tables, or an actual write-then-read round trip — rather than trusted
from recalled documentation. Several non-obvious requirements were only discovered this way (e.g.
`digiKam:TagsList` silently mis-rendering as a flat list unless it's serialized as `rdf:Seq`
specifically). If you're changing how a field is written, hold yourself to the same bar.
