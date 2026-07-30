# PictTag

PictTag is a .NET CLI that looks at a photo with a local [Ollama](https://ollama.com) vision
model and writes the results as an [XMP](https://www.adobe.com/products/xmp.html) sidecar file —
titles, descriptions, alt text, object detections with bounding boxes, hierarchical keyword tags,
composition analysis, and art-style/genre classification. Everything runs locally against your
own Ollama server; no image data leaves your machine.

The sidecars are written using real, standard XMP fields (IPTC Photo Metadata, Dublin Core,
Metadata Working Group regions, Lightroom/digiKam tag conventions) wherever a standard exists, so
the output is readable by digiKam, Adobe Lightroom, and other photo managers without a plugin —
not just by PictTag itself. See [`docs/XMP-SCHEMA.md`](docs/XMP-SCHEMA.md) for the full field
reference.

## Features

- **Title, description, and accessibility alt text** for the image as a whole.
- **Object detection** — every salient object gets a label, a bounding box, and a normalized
  hierarchical tag written both as MWG regions and as IPTC `ImageRegion` structs. Tags are resolved
  against a WordNet-derived taxonomy for cross-photo consistency (the same golden retriever tags
  the same way whether the model calls it "dog", "canine", or "golden retriever"), producing a
  chain as deep as WordNet's real ancestry (e.g. `Animal > Chordate > ... > Retriever > Golden
  Retriever`) rather than a fixed 3 levels — falling back to a `Category > Group > Label` shape for
  whatever a fixed local taxonomy can't confidently place. See
  [`docs/TAXONOMY.md`](docs/TAXONOMY.md).
- **Hierarchical keyword tags** compatible with digiKam's tag tree and Lightroom's keyword
  hierarchy, plus flat `dc:subject` keywords for apps that only read those.
- **Medium and art-style/genre detection** (photograph, painting, screenshot, digital
  illustration, 3D render, ...), including the specific art style for non-photographic images
  (e.g. "impressionism", "ukiyo-e").
- **Composition analysis** — symmetry, rule-of-thirds adherence, color variance, edge density, and
  a free-text note — computed for every image, not just paintings.
- **IPTC Scene-NewsCodes** framing/genre tags (headshot, group, close-up, exterior/interior view,
  etc.) inferred by the model plus derived from the detected setting.
- Two interchangeable **XMP writer engines**: a pure-.NET one (`xmpcore`, default, no external
  dependencies) and one that shells out to the real `exiftool` binary (`exiftool`, useful as a
  cross-check or if you already depend on exiftool elsewhere).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (see [`global.json`](global.json)).
- A running [Ollama](https://ollama.com) server with a vision-capable model pulled. The model
  name is currently hardcoded in
  [`ImageDetectionService.cs`](source/PictTag.Core/ImageDetectionService.cs) (`gemma4:26b`) — pull
  it with `ollama pull gemma4:26b`. There is no CLI flag to change the model yet; only the server
  URL is configurable (`--ollama-url`).
- The same Ollama server also needs a small embedding model pulled for taxonomy resolution's
  semantic fallback (see [`docs/TAXONOMY.md`](docs/TAXONOMY.md)): `ollama pull nomic-embed-text`.
- A free [SixLabors](https://sixlabors.com) license file for `ImageSharp`/`ImageSharp.Drawing`
  (used to draw the annotated preview image), saved as `sixlabors.lic` in the repo root. It's
  git-ignored on purpose — obtain your own from SixLabors and drop it there; `Directory.Build.props`
  already points every project at it.
- Optional: [`exiftool`](https://exiftool.org/) on `PATH` if you want to use `--xmp-engine
  exiftool` or run the exiftool-backed tests.

## Quick start

```bash
dotnet build

dotnet run --project source/PictTag.Cli -- \
  -i path/to/photo.jpg \
  -o annotated_photo.jpg \
  --xmp
```

This writes `annotated_photo.jpg` (the image with detection boxes drawn on it) and
`path/to/photo.xmp` (the metadata sidecar), and prints a summary to the console:

```
Title: Fresco of praying figures and an angel
Description: A photograph capturing an ancient religious fresco painted on a wall within a
historical building. The mural depicts several kneeling figures in prayer beneath a large winged
angel, set against a deep blue background. An ornate, carved stone pillar frames the right side of
the scene.
AltText: An old religious fresco on a wall featuring praying figures and an angel, positioned next
to a detailed stone architectural pillar.
Medium: Photograph
Setting: Indoor
Scene: InteriorView, GeneralView, Group, Symbolic
Composition: Asymmetrical, ruleOfThirds=False, colorVariance=0.60, edgeDensity=0.70 (diagonal
lines created by the archway and pillar)
[Art] religious figure > angel: ymin=215 xmin=438 ymax=507 xmax=619
[People] person > praying woman: ymin=408 xmin=255 ymax=704 xmax=365
Annotated image saved to annotated_photo.jpg
XMP sidecar written to path/to/photo.xmp
```

### Batch mode

Pass a glob and `-o` becomes an output *directory* instead of a file:

```bash
dotnet run --project source/PictTag.Cli -- \
  -i 'data/photos/**/*.jpg' \
  -o out/ \
  --xmp
```

## CLI reference

| Option | Default | Description |
|---|---|---|
| `--input`, `-i` | `../../data/test-images/IMG_0922.JPG` | Path or glob for the input image(s), e.g. `photo.jpg` or `data/photos/**/*.jpg`. |
| `--output`, `-o` | `annotated_sample.jpg` | Output path for the annotated image. Treated as a directory when `--input` matches more than one file. |
| `--ollama-url`, `-u` | `http://localhost:11434` | Base URL of the Ollama server. |
| `--xmp` | off | Write an XMP sidecar alongside each input image. |
| `--xmp-naming` | `replace` | `replace` → `photo.xmp` (Adobe/Lightroom convention) or `append` → `photo.jpg.xmp` (digiKam/darktable convention). |
| `--xmp-engine` | `xmpcore` | `xmpcore` (pure .NET) or `exiftool` (shells out to the real binary). |
| `--xmp-overwrite` | off | Regenerate the sidecar even if one already exists. Default is to skip files that already have one — cheap to re-run over a folder as you add new photos. |

Run `dotnet run --project source/PictTag.Cli -- --help` for the full, generated help text.

## Project layout

```
source/
  PictTag.Core/                 Detection service + taxonomy resolution + XMP sidecar writers
    ImageDetectionService.cs      Ollama call, prompt, JSON schema, entity/composition mapping
    Models.cs                     ImageMetadata, DetectedEntity, RawDetection/TaxonomyMatch, enums
    Taxonomy/                     ITaxonomyProvider/ITaxonomyEmbeddingIndex + EntityTaxonomyResolver
    Taxonomy/taxonomy.json,       Embedded, WordNet-derived taxonomy data (built offline, see below)
      taxonomy-embeddings.bin
    Xmp/                           Two IXmpSidecarWriter implementations + XMP schema helpers
  PictTag.Cli/                   System.CommandLine entry point (Program.cs)
  PictTag.Core.Tests/             xUnit v3 test suite (see docs/TESTING.md)
  PictTag.TaxonomyBuilder/        Offline pipeline that builds Taxonomy/taxonomy.json (see docs/TAXONOMY.md)
  PictTag.TaxonomyBuilder.Tests/  Its test suite
data/
  test-images/              Checked-in fixture photos + their generated .xmp sidecars
  test-images/art-styles/   Bulk-downloaded art-style fixtures (git-ignored, regenerable)
  art-styles-manifest.json  37 art movements used to drive art-style fixture download + testing
  wordnet/raw/              Committed WordNet 3.0 + ImageNet-1k source data (see docs/TAXONOMY.md)
  wordnet/seeds/            Hand-authored seed/trim config for the taxonomy build
Get-ArtStyleTestImages.ps1  Downloads freely-licensed art-style images from Wikimedia Commons
Get-WordNetData.ps1         Downloads/verifies the WordNet + ImageNet-1k source data
Test-ArtStyleDetection.ps1  Runs detection over the art-style fixtures and summarizes accuracy
Update-TestImageTags.ps1    Regenerates the checked-in data/test-images/*.xmp fixtures
docs/
  ARCHITECTURE.md           How detection and XMP writing fit together, and why
  XMP-SCHEMA.md             Full field-by-field XMP reference, with the empirical gotchas
  TAXONOMY.md               Where the taxonomy data comes from, how to extend/retune it
  TESTING.md                Test categories, how to run each, and the fixture scripts
```

## Testing

```bash
dotnet test source/PictTag.Core.Tests
dotnet test source/PictTag.TaxonomyBuilder.Tests
```

runs the fast unit-test suites (no external dependencies). Two more test categories exist and are
skipped by default — one needs `exiftool` on `PATH`, the other needs a real Ollama server and
downloaded fixtures. See [`docs/TESTING.md`](docs/TESTING.md) for how to run them.

## Further reading

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — the detection → taxonomy resolution → metadata
  → XMP pipeline, why there are two sidecar writer engines, and the project's "standards first"
  design principle.
- [`docs/XMP-SCHEMA.md`](docs/XMP-SCHEMA.md) — every XMP property PictTag writes, which namespace
  it lives in, and the non-obvious, empirically-verified quirks behind several of them (e.g. why
  `digiKam:TagsList` has to be an `rdf:Seq`, why `Iptc4xmpExt:Genre` has to be a struct, and why
  hierarchical tag segments are sanitized and title-cased).
- [`docs/TAXONOMY.md`](docs/TAXONOMY.md) — where the WordNet-derived taxonomy data comes from, how
  the offline build pipeline works, and how to add a seed or retune it.
- [`docs/TESTING.md`](docs/TESTING.md) — the test categories and the fixture-management scripts.

## License

[MIT](LICENSE).
