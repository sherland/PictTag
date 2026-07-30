# XMP schema reference

Every field below is written by both `XmpCoreSidecarWriter` and `ExifToolSidecarWriter`
(`source/PictTag.Core/Xmp/`), with equivalent output. Field names use each namespace's
conventional prefix. Source of truth is always the code — this document explains *why* each field
is shaped the way it is, including several requirements that aren't obvious from reading the XMP
specs alone and were only found by testing against real exiftool/digiKam behavior.

## Namespaces

| Prefix | URI | Home fields |
|---|---|---|
| `dc` | `http://purl.org/dc/elements/1.1/` | `title`, `description`, `subject` (standard Dublin Core) |
| `xmp` | `http://ns.adobe.com/xap/1.0/` | `CreatorTool` |
| `Iptc4xmpCore` | `http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/` | `AltTextAccessibility`, `ExtDescrAccessibility`, `Scene` |
| `Iptc4xmpExt` | `http://iptc.org/std/Iptc4xmpExt/2008-02-29/` | `DigitalSourceType`, `Genre`, `ImageRegion` |
| `lr` | `http://ns.adobe.com/lightroom/1.0/` | `hierarchicalSubject` |
| `digiKam` | `http://www.digikam.org/ns/1.0/` | `TagsList` |
| `mwg-rs` | `http://www.metadataworkinggroup.com/schemas/regions/` | `Regions` |
| `stArea` | `http://ns.adobe.com/xmp/sType/Area#` | `mwg-rs:Regions`' area struct fields |
| `stDim` | `http://ns.adobe.com/xap/1.0/sType/Dimensions#` | `mwg-rs:Regions`' dimensions struct fields |
| `pictTag` | `https://github.com/sherland/PictTag/ns/1.0/` | `Medium`, `Setting`, `Symmetry`, `RuleOfThirds`, `ColorVariance`, `EdgeDensity`, `CompositionNotes` |

`pictTag` is a genuinely custom namespace — see [ARCHITECTURE.md](ARCHITECTURE.md#design-principle-standards-first)
for why it's kept deliberately small. Every other namespace above is a real, pre-existing standard.
`exiftool` recognizes all of them natively except `pictTag`, which needs the small `-config` block
built into `ExifToolSidecarWriter` (`PictTagConfig`) to become writable at all.

## Field-by-field

| Property | Type | Always written? | Source |
|---|---|---|---|
| `xmp:CreatorTool` | string | always | literal `"PictTag"` |
| `dc:title` | LangAlt | always | `ImageMetadata.Title` |
| `dc:description` | LangAlt | always | `ImageMetadata.Description` |
| `Iptc4xmpCore:AltTextAccessibility` | LangAlt | always | `ImageMetadata.AltText` — a distinct, purpose-written short caption, not a truncation of `Description` |
| `Iptc4xmpCore:ExtDescrAccessibility` | LangAlt | always | `ImageMetadata.Description`, reused verbatim — the two IPTC accessibility fields overlap by design (short alt text + a full description), and `Description` already *is* the full description |
| `pictTag:Medium` | string | always | `ImageMetadata.Medium` |
| `pictTag:Setting` | string | when `Setting != null` | `ImageMetadata.Setting` |
| `pictTag:Symmetry` / `RuleOfThirds` / `ColorVariance` / `EdgeDensity` | string | always | `ImageMetadata.Composition` |
| `pictTag:CompositionNotes` | string | when `Composition.Notes != null` | `ImageMetadata.Composition.Notes` |
| `Iptc4xmpExt:DigitalSourceType` | URI | when mapped (see below) | derived from `Medium` |
| `Iptc4xmpExt:Genre` | Bag of struct | when `ArtStyle != null` | `ImageMetadata.ArtStyle` |
| `Iptc4xmpCore:Scene` | Bag of 6-digit codes | when ≥1 code applies (model picks and/or one derived from `Setting`) | `ImageMetadata.Scene` + a code derived from `Setting` |
| `dc:subject` | Bag of strings | always (Medium/Symmetry always present) | flattened leaf of every hierarchical tag |
| `lr:hierarchicalSubject` | Bag of `Category\|Group\|Label` paths | always | see [Hierarchical tags](#hierarchical-tags) |
| `digiKam:TagsList` | **ordered** Seq of `Category/Group/Label` paths | always | see [Hierarchical tags](#hierarchical-tags) |
| `mwg-rs:Regions` | struct | when ≥1 entity detected | `ImageAnalysisResult.Entities` |
| `Iptc4xmpExt:ImageRegion` | Bag of struct | when ≥1 entity detected | `ImageAnalysisResult.Entities` |

### `DigitalSourceType` mapping

`IptcDigitalSourceType.ForMedium` only maps the two media whose IPTC *capture provenance* code
genuinely applies:

| `ImageMedium` | `DigitalSourceType` |
|---|---|
| `Photograph` | `https://cv.iptc.org/newscodes/digitalsourcetype/digitalCapture` |
| `Screenshot` | `https://cv.iptc.org/newscodes/digitalsourcetype/screenCapture` |
| everything else (`Painting`, `Drawing`, `DigitalIllustration`, `ThreeDRender`, `Other`) | omitted (`null`) |

There is no IPTC `DigitalSourceType` code for "painting" or "digital illustration" — that
vocabulary classifies how the *pixels* were captured (camera vs. scanner vs. screen vs.
AI-generated, etc.), not artistic technique, which is a different axis entirely and is what
`pictTag:Medium`/`Genre` are for. Don't be tempted to add codes here for the art media; there
genuinely aren't any that fit.

### `Genre` (art style) is a struct, not plain text

`ArtStyle` (e.g. `"impressionism"`) is written to `Iptc4xmpExt:Genre`, IPTC's real field for
artistic genre/style. It looks like it should be a plain string array, but exiftool's own tag
tables (`exiftool -listx -XMP-iptcExt:all`) define it as `type='struct'` — a Bag of `CVTerm`
structs (`CvId`, `CvTermId`, `CvTermName`, `CvTermRefinedAbout`). Writing a bare string fails with
`Improperly formed structure for XMP-iptcExt:Genre`. PictTag only populates `CvTermName` (free
text) and leaves the ID fields unset, since `ArtStyle` isn't sourced from a real controlled
vocabulary with term IDs — it's the model's own free-text guess.

### Scene codes

`Iptc4xmpCore:Scene` is a Bag of six-digit codes from the
[IPTC Scene-NewsCodes](https://cv.iptc.org/newscodes/scene) controlled vocabulary. `SceneType`
(in `Models.cs`) has one member per code; `IptcSceneCode.ForSceneType` is the mapping — read it
directly if this table drifts:

| `SceneType` | Code | `SceneType` | Code |
|---|---|---|---|
| `Headshot` | 010100 | `Satellite` | 011500 |
| `HalfLength` | 010200 | `ExteriorView` | 011600 |
| `FullLength` | 010300 | `InteriorView` | 011700 |
| `Profile` | 010400 | `CloseUp` | 011800 |
| `RearView` | 010500 | `Action` | 011900 |
| `Single` | 010600 | `Performing` | 012000 |
| `Couple` | 010700 | `Posing` | 012100 |
| `Two` | 010800 | `Symbolic` | 012200 |
| `Group` | 010900 | `OffBeat` | 012300 |
| `GeneralView` | 011000 | `MovieScene` | 012400 |
| `PanoramicView` | 011100 | | |
| `AerialView` | 011200 | | |
| `UnderWater` | 011300 | | |
| `NightScene` | 011400 | | |

The model picks zero or more codes directly (whichever genuinely apply — it's told not to force a
weak match). On top of the model's picks, PictTag derives and appends one more: `InteriorView`
when `Setting == Indoor`, or `ExteriorView` when `Setting == Outdoor` (skipped if the model
already picked it) — so the standard `Scene` field always carries that signal too, even though
`pictTag:Setting` has finer granularity (`Studio`/`Unknown`, which `Scene` has no equivalent for).

## Hierarchical tags

`dc:subject`, `lr:hierarchicalSubject`, and `digiKam:TagsList` are written together by
`AppendHierarchicalTag` (XmpCore) / `AppendHierarchicalTagArgs` (exiftool), for four kinds of
thing: `Medium`, `ArtStyle` (if present), `Symmetry`, and every detected entity. All the real
composition logic lives in `HierarchicalTagPath.cs`, shared by both writers.

**`digiKam:TagsList` must be an ordered `rdf:Seq`, not `rdf:Bag`** (unlike `dc:subject` and
`lr:hierarchicalSubject`, which are ordinary `Bag`s). Per the
[exiv2 digiKam namespace reference](https://exiv2.org/tags-xmp-digiKam.html), digiKam only builds
its tag tree correctly from this field when it's a `Seq` — as a `Bag` it silently flattens.
Verified empirically against real digiKam, not just against the docs.

### Variable-depth taxonomy chain (entities), or `Category > Group > Label` (fallback)

Each detected entity's tag path comes from `HierarchicalTagPath.BuildEntitySegments`, which picks
one of two shapes depending on whether taxonomy resolution (see
[ARCHITECTURE.md](ARCHITECTURE.md#taxonomy-resolution) and [TAXONOMY.md](TAXONOMY.md)) found a
confident match:

- **Resolved** (`DetectedEntity.Taxonomy` non-null): the full WordNet ancestor chain, title-cased,
  of whatever depth WordNet actually has for that concept — e.g. a golden retriever becomes
  `Animal > Chordate > Vertebrate > Mammal > Carnivore > Canine > Dog > Retriever > Golden
  Retriever`, and a chimney becomes `Artifact > Way > Passage > Conduit > Flue > Chimney` (WordNet
  classifies a chimney as a kind of conduit, not literally a "building part", even though that was
  the old raw-category guess for it). There's no fixed depth - the chain is exactly as deep as
  WordNet's real hypernym graph, once cut at the category's configured root anchor.
- **Unresolved** (`Taxonomy` is null): the original 3-level (or 2-level, if collapsed) shape - a
  broad `EntityCategory` (`Art`, `People`, `Objects`, ...), a `Group` a step more general than the
  label (e.g. "religious figure" for an "angel"), and the specific `Label`. This is the entity's
  only fallback, not a legacy-compatibility path - it exists so a detection the resolver can't
  confidently place is tagged exactly as well as it always was, never worse.

Either way, adjacent identical segments collapse (the model is told to repeat the label as its
group when nothing more general genuinely applies, and a resolved chain can likewise end in two
identical node names) - `HierarchicalTagPath.BuildSegments` handles this generically for a chain of
any length, not just a hardcoded 2-vs-3-level check.

`Medium` and `Symmetry` use the same `BuildSegments` machinery but as a plain 2-level `Category >
Label` with no group — and deliberately **without** title-casing, since their `Label` is already a
PascalCase enum token like `ThreeDRender` or `DigitalIllustration` that has no word boundaries to
title-case against; running it through `TitleCase` (which lowercases first) would collapse it to
`Threedrender`. `ArtStyle` *is* free text, so it does get `TitleCase`d, still with no group level.

Regions (`mwg-rs:Regions`/`Iptc4xmpExt:ImageRegion`, below) always use the entity's raw `Label`
text, never the taxonomy chain's node name — a region's name is meant to read as what the model
actually saw in that spot, not its normalized category.

`dc:subject` only ever gets the leaf-most segment (the specific `Label`) as a flat keyword —
`Category`/`Group` are never added to `dc:subject` on their own, matching how `Medium`'s category
token was never flattened there either. They're only reachable through the hierarchy.

### Why segments are sanitized (`Compose`)

`lr:hierarchicalSubject` uses `|` as its level separator; `digiKam:TagsList` uses `/`. Both are
built from the *same* label/group text. Early in this project, a detected entity's label came
back from the model as `"angel/religious figure"` — a single string containing a literal `/`.
Written verbatim into `TagsList` as `Art/angel/religious figure`, digiKam parsed it as **three**
levels (`Art > angel > religious figure`) instead of one leaf under `Art`, while the same string
in `hierarchicalSubject` (which uses `|`, unaffected by an embedded `/`) stayed a single leaf —
two different, mismatched tag trees in digiKam for what should have been one tag.

`HierarchicalTagPath.Compose` now replaces any `/` or `|` found inside a segment with `-` before
joining, regardless of which separator that particular call is using — a segment safe for one
separator can still contain the *other* property's separator character, so both must always be
scrubbed. This is a narrower, more reliable fix than trying to teach the model never to produce a
label containing those characters. The proper fix for the *specific* case that surfaced this bug
was adding the `Group` field described above, so the model has a structured way to express "this
is a kind of that" instead of ad hoc punctuation inside one string — but the sanitization step
stays regardless, as a safety net for whatever the model produces next.

## Regions: two schemas, two coordinate conventions

Both region schemas are written whenever `ImageAnalysisResult.Entities` is non-empty. They encode
the same boxes but are **not** the same numbers — a common source of bugs if you're comparing
them by eye:

| | `mwg-rs:Regions` (MWG) | `Iptc4xmpExt:ImageRegion` (IPTC) |
|---|---|---|
| Written by | `WriteRegions` / `BuildRegionInfoStruct` (`-RegionInfo`) | `WriteImageRegions` / `BuildImageRegionStruct` |
| `(X, Y)` anchor | **center** of the box | **top-left corner** of the box |
| Name field | `mwg-rs:Name` (plain string) | `Iptc4xmpExt:Name` (LangAlt) |
| Conversion helper | `MwgRegionArea.FromBoundingBox` | `IptcRegionBoundary.FromBoundingBox` |

`DetectedEntity.Box` is normalized 0–1000 (not 0–1 — that's the *model's* coordinate grid, chosen
because integer grids are what vision models are typically trained to emit reliably). Both
converters rescale to 0–1 for XMP, but around different anchors:

```
MwgRegionArea:        X = (XMin + XMax) / 2000,  Y = (YMin + YMax) / 2000   (center)
IptcRegionBoundary:    X = XMin / 1000,            Y = YMin / 1000           (top-left)
Width / Height are the same for both: (XMax - XMin) / 1000, (YMax - YMin) / 1000
```

The IPTC top-left convention was confirmed against exiftool's own maintained MWG↔IPTC region
conversion logic (`config_files/convert_regions.config`), which literally computes
`$rect[0] -= $rect[2]/2` to go from MWG's center point to IPTC's corner — i.e. IPTC's `X` is
MWG's `X` minus half the width. It is *not* documented plainly enough in the IPTC spec pages
themselves to take on faith; this is the kind of claim this codebase insists on checking against
a real, authoritative source rather than an LLM's or a doc page's recollection (see
[ARCHITECTURE.md](ARCHITECTURE.md#design-principle-standards-first)).

`Iptc4xmpExt:ImageRegion` also supports `rCtype`/`rRole` sub-structs (content-type/role
classification) — deliberately left unwritten, since no real controlled-vocabulary values for
them were verified. Guessing at vocabulary URIs would be worse than omitting the fields.

## Engine-specific notes (`ExifToolSidecarWriter`)

- **Custom namespace config**: `pictTag:*` properties need a `-config` file (`PictTagConfig`,
  written to a temp file once and reused) defining the namespace and its tags as a Perl
  `UserDefined` table — every other namespace above is natively known to exiftool.
- **List-field accumulation**: `dc:Subject`, `lr:HierarchicalSubject`, `digiKam:TagsList`,
  `Iptc4xmpExt:Genre`, and `Iptc4xmpCore:Scene` are all list tags — exiftool's `+=` only *appends*.
  Re-running against an existing sidecar without clearing first would accumulate old and new
  values together, so when the sidecar already exists, a clearing pass (`-XMP-dc:Subject=` etc.)
  runs before the real write. `RegionInfo`/`ImageRegion`/`dc:Title`/`dc:Description`/`pictTag:*`
  don't need this — they're set with a plain `=`, which replaces in one shot.
- **Always write real content**: exiftool refuses to create a brand-new sidecar file when the only
  operations given amount to no actual content — which would happen with zero detected entities if
  nothing else were written. PictTag guards against this by always including the Medium/Symmetry
  tags and `pictTag:*` properties, so there's always something to write even for an empty-entity
  image.
- **Struct value escaping**: exiftool's ["structured information"](https://exiftool.sourceforge.net/struct.html)
  syntax (`{key=value,...}`, `[...]` for arrays) treats `|`, `,`, `}`, `]` and a leading `{`/`[`/
  whitespace as special. `EscapeStructValue` prefixes a `|` before any of those wherever they'd
  otherwise be misread, so a label like `"a, tricky} label] with |pipes|"` round-trips intact
  (see `WriteSidecarAsync_LabelWithSpecialCharacters_RoundTripsCorrectly` in
  `ExifToolSidecarWriterTests.cs` for a real example).
- **`-overwrite_original`**: only passed when the sidecar already exists — passing it while
  creating a brand-new file makes exiftool expect one to already be there and fail with "File not
  found".
