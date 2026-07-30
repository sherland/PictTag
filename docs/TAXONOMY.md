# Taxonomy

## Why this exists

`gemma4:26b` is an open-vocabulary generator: the same golden retriever might get labeled "dog",
"canine", or "pet" across different photos, and the same "notebook pc" might show up as "laptop"
in another. That inconsistency defeats tag-based browsing/filtering in digiKam/Lightroom. Rather
than trying to force the model into a closed vocabulary at generation time (impractical - a real
taxonomy has thousands of terms, far too many for a JSON-schema enum or a prompt to enumerate),
PictTag resolves each detection's free text *after* the model responds, against a real WordNet
noun taxonomy with full ancestor chains (e.g. ImageNet synset `n02099601` resolves to `Entity >
Organism > ... > Retriever > Golden Retriever`). See
[ARCHITECTURE.md](ARCHITECTURE.md#taxonomy-resolution) for how this fits into the detection
pipeline and the runtime types involved (`ITaxonomyProvider`, `ITaxonomyEmbeddingIndex`,
`EntityTaxonomyResolver`). This document covers where the taxonomy data itself comes from, how
it's built, and how to extend or retune it.

## Data provenance and license

`data/wordnet/raw/` holds unmodified upstream data, committed as-is so the builder runs fully
offline and deterministically:

| File | Source |
|---|---|
| `data.noun`, `index.noun` | Princeton WordNet 3.0 database files (`https://wordnetcode.princeton.edu/3.0/WNdb-3.0.tar.gz`, `dict/` subfolder) - only the noun files are extracted, since PictTag only tags nouns |
| `LICENSE` | `https://wordnetcode.princeton.edu/3.0/LICENSE` |
| `imagenet_class_index.json` | `https://storage.googleapis.com/download.tensorflow.org/data/imagenet_class_index.json` - ImageNet-1k's 1000 `wnid -> name` pairs, used as the primary seed set (see below) |

Exact URLs, retrieval dates, and checksums are recorded in `data/wordnet/raw/SOURCES.md`. Re-run
`Get-WordNetData.ps1` (repo root) to re-download and re-verify all four files against that table.

**License:** WordNet 3.0's license permits use, copying, modification, and redistribution without
fee, provided the copyright notice and disclaimer are preserved in all copies including derived
works. The shipped `taxonomy.json` (see below) carries its own `license` field for exactly this
reason - it's the artifact that actually ends up compiled into `PictTag.Core`, not the raw-data
folder. `imagenet_class_index.json` is used only as a build-time seed input and is never
redistributed as part of the shipped product.

## The build pipeline (`PictTag.TaxonomyBuilder`)

A console project (`source/PictTag.TaxonomyBuilder`) that turns the raw WordNet database into the
small, tree-shaped subset PictTag.Core actually ships. It's a C# project referencing
`PictTag.Core` (not a throwaway script) so the builder and the runtime share the exact same
`TaxonomyNode`-shaped types - one source of truth for the data contract.

```bash
dotnet run --project source/PictTag.TaxonomyBuilder -- \
  --wordnet-dir data/wordnet/raw \
  --imagenet-index data/wordnet/raw/imagenet_class_index.json \
  --manual-seeds data/wordnet/seeds/manual-seeds.json \
  --expand-domains data/wordnet/seeds/expand-domains.json \
  --trim-config data/wordnet/seeds/trim-config.json \
  --out-dir source/PictTag.Core/Taxonomy \
  --embedding-model nomic-embed-text \
  --debug-out data/wordnet/build-debug/taxonomy-full-debug.json   # optional
```

All arguments shown above are also the defaults, so a bare `dotnet run --project
source/PictTag.TaxonomyBuilder` (with a local Ollama running `nomic-embed-text`) reproduces the
committed output. Pass `--skip-embeddings` to iterate on seeding/trimming quickly without needing
Ollama running at all - `taxonomy-embeddings.bin` just won't be (re)written.

Pipeline stages:

1. **Parse** `data.noun`/`index.noun` (`WordNetParser`) into every noun synset: its lemma/alias
   list, hypernym pointers (for walking up), and hyponym pointers (for walking down).
2. **Seed** from three sources (`--imagenet-index`, `--manual-seeds`, `--expand-domains`):
   - Every ImageNet-1k leaf (1000 synsets) automatically.
   - Hand-picked single-node seeds in `manual-seeds.json`, for concepts ImageNet-1k barely
     covers (e.g. `child`, `angel`, `chimney`, `push button` - each entry records which of
     WordNet's several senses was picked and why).
   - Domain anchors in `expand-domains.json`, expanded **downward** through their own hyponyms to
     a configured depth - this sweeps in a whole domain's standard subtypes (e.g. seeding `boat`
     this way pulls in kayak, canoe, sailboat, motorboat -> speedboat, houseboat, ...) instead of
     hand-listing every one.
3. **Walk upward** from every seed to a configured category anchor or true WordNet root,
   resolving any synset with more than one hypernym (WordNet is occasionally a DAG, not a tree) to
   a single linear parent - the first-listed hypernym wins. This happens once, here, so runtime
   code never has to deal with more than one parent per node.
4. **Trim** via `trim-config.json`: `rootCollapseAnchors` (one WordNet synset per `EntityCategory`
   member, at which the emitted chain is cut - dropping WordNet's abstract upper ontology `entity
   -> physical_entity -> object -> whole -> living_thing -> organism -> ...`), `excludeSynsets`/
   `excludeSubtrees` (denylists, empty by default - populated only after real fixture review finds
   a specific oddity, never guessed upfront), and `stripLatinLemmas` (drops binomial-nomenclature
   lemmas like "Canis familiaris" from the alias list).
5. **Embed** one canonical text (`"{name}: {gloss}"`) per surviving node via a local Ollama
   embedding model, cached by `(synsetId, textHash)` in `data/wordnet/build-debug/
   embedding-cache.json` (not committed) so a rerun after a config tweak only re-embeds new/changed
   nodes.
6. **Emit** `source/PictTag.Core/Taxonomy/taxonomy.json` (indented/pretty-printed - it's
   hand-reviewed in code review every time trim config changes) and `taxonomy-embeddings.bin` (a
   packed binary array of vectors, parallel-indexed to `taxonomy.json`'s node list). Both are
   embedded resources in `PictTag.Core.csproj`.

## How resolution actually works at runtime

`EntityTaxonomyResolver.ResolveAsync` (`source/PictTag.Core/Taxonomy/EntityTaxonomyResolver.cs`):

1. Exact lemma match on `Label`, then `Group` (case-insensitive, normalized - no stemming). Cheap,
   unambiguous when it hits.
2. If a lemma has more than one surviving node (a real WordNet homonym, e.g. "crane" the bird vs.
   "crane" the lifting device, or "button" the flower bud vs. the push-button), the detection's
   own `Category` guess disambiguates by preferring whichever candidate resolves to that category.
3. If neither `Label` nor `Group` matches a lemma exactly, fall back to the embedding index: embed
   `Label`, brute-force cosine-similarity scan over the precomputed node vectors, same-category
   search tried first then an unrestricted search. If `Label`'s embedding search also misses, try
   `Group`'s embedding too (unless `Label == Group`, avoiding a redundant call).
4. If nothing clears the similarity threshold (`OllamaTaxonomyEmbeddingIndex.DefaultMinSimilarity`,
   currently 0.65), the entity is unresolved - both XMP writers then fall back to the raw
   `Category`/`Group`/`Label` shape for it. This is the feature's only fallback path, not a
   legacy-compatibility concern: an unresolved entity is tagged exactly as well as it always was,
   never worse.

## Adding a seed

Manual seed-list/domain-expansion completeness is the real ongoing risk here, not the mechanics
(ImageNet-1k skews heavily animal/object; people/buildings/nature/UI-adjacent concepts need
deliberate seeding). Three concrete gaps were actually found and fixed this way, empirically,
during initial validation - not guessed upfront:

- **A missing single concept** ("tree" had no seed at all, so a real photo of a tree fell through
  to the embedding tier and wrongly matched "tree squirrel" purely on shared substring/lexical
  overlap). Fix: add a domain expansion for `plant.n.01` (depth 3, enough to reach "tree" via
  `plant -> vascular_plant -> woody_plant -> tree` without exploding into individual species one
  level further, which would reintroduce the same over-representation problem this design avoids
  for dog breeds/fish species).
- **A wrong category anchor** ("house" the dwelling resolved to "house" the theater sense, because
  `Buildings`'s anchor was `building.n.01`, and WordNet treats "building" and "housing/dwelling" as
  *sibling* hyponyms of `structure.n.01`, not parent/child - despite the colloquial expectation
  that a house IS a building). Fix: anchor `Buildings` at `structure.n.01` instead - `building.n.01`
  is that node's own hypernym, so covers both senses correctly, with "building" becoming an
  ordinary intermediate node instead of the anchor.
- **A missing sense competing with a wrong one** (a screenshot's "back button"/"cancel button" UI
  elements matched WordNet's *botanical* "button" - an unopened flower bud, swept in incidentally
  by the plant-part domain expansion - because no mechanical/UI-control sense of "button" existed
  anywhere in the taxonomy to compete with it). Fix: add `push_button` (WordNet's actual "an
  electrical switch operated by pressing" sense, whose own lemma list literally includes "button")
  as a manual seed, giving both the exact-match tier (via `categoryHint`) and the embedding tier a
  correct candidate to prefer.

To add a seed yourself:

1. Look up the lemma in `data/wordnet/raw/index.noun` (`grep -m1 "^<lemma> n " index.noun`) to see
   how many senses WordNet has and their synset offsets.
2. Check each candidate's gloss in `data.noun` (`grep -m1 "^<offset> " data.noun`) to pick the
   right sense - don't guess from the lemma alone, WordNet's senses are often surprising.
3. Add an entry to `manual-seeds.json` (single node) or `expand-domains.json` (whole subtree,
   with a depth chosen to reach what you actually want without sweeping in an unrelated species-
   or subtype-level explosion - verify the depth empirically against the real data, the same way
   the `boat`/`plant` depths above were chosen).
4. Rebuild (`dotnet run --project source/PictTag.TaxonomyBuilder`) and check the new node's
   ancestor chain looks right before considering it done.

## Known limitation: concepts outside WordNet's physical-entity branch

All ten `rootCollapseAnchors` sit under WordNet's `physical_entity` branch (the animal/vehicle/
building/etc. side of `entity`). A manually-seeded concept whose real WordNet classification falls
on the *other* top branch, `abstraction`, never hits any configured anchor and walks all the way
up to the true root - e.g. `angel` (seeded for the Art category, per the religious-figure example
in [XMP-SCHEMA.md](XMP-SCHEMA.md)) resolves to `entity > abstraction > psychological_feature >
cognition > content > belief > spiritual_being > angel`, since WordNet classifies a spiritual being
as a believed-in *abstraction*, not a physical thing - despite a painting or statue *depicting* one
being straightforwardly physical "art". This isn't a bug (the walk still terminates correctly, at
the true root, exactly as designed for any node with no matching anchor) but it does mean such
chains come out longer and more abstract-flavored than the simple picture this project's docs
otherwise show. Not yet fixed - a plausible future fix is a dedicated anchor for this branch (or
simply excluding such seeds and letting them fall back to the raw `Category`/`Group`/`Label`
shape), but it needs more real examples to know what the right anchor even is before choosing one.

## Tuning the similarity threshold

`OllamaTaxonomyEmbeddingIndex.DefaultMinSimilarity` (currently 0.65) was tuned empirically against
`nomic-embed-text`, not picked blind: legitimate paraphrase matches measured ~0.69-0.78 ("wooden
boat" -> boat 0.739, "rigid inflatable boat" -> motorboat 0.693, "notebook pc" -> notebook 0.776),
while a genuinely wrong guess ("macbook", briefly confused with "macaque") maxed out at ~0.60. It's
a constructor parameter specifically so it can be retuned without a code change as more real
fixture data accumulates (see [TESTING.md](TESTING.md#reviewing-a-fixture-diff-after-a-taxonomy-change)).
