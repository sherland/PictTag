# Raw taxonomy data provenance

These files are unmodified upstream data, committed as-is so `PictTag.TaxonomyBuilder`
runs fully offline and deterministically. Only the noun files are kept — PictTag only
tags nouns, so `data.verb`/`data.adj`/`data.adv`/`index.verb`/`index.adj`/`index.adv`
(also present in the upstream tarball) are intentionally not extracted.

| File | Source | Retrieved | SHA-256 |
|---|---|---|---|
| `data.noun` | `https://wordnetcode.princeton.edu/3.0/WNdb-3.0.tar.gz` (`dict/data.noun`) | 2026-07-30 | `489f145e0f68877c0be5bd0eb4117adaaac52f38f6204eb8d85dbe2158b614cc` |
| `index.noun` | `https://wordnetcode.princeton.edu/3.0/WNdb-3.0.tar.gz` (`dict/index.noun`) | 2026-07-30 | `a490d99d93d017bf4822fe2f0ffa51fd73911ce271dc7535fade21f8814b5a04` |
| `LICENSE` | `https://wordnetcode.princeton.edu/3.0/LICENSE` | 2026-07-30 | (short text file, see content) |
| `imagenet_class_index.json` | `https://storage.googleapis.com/download.tensorflow.org/data/imagenet_class_index.json` | 2026-07-30 | `a1e7a966a1f601d39e4b43e119b3e7dd4a2ad3ea08cf69847cbaf021013767bc` |

Checksum of the full source tarball (`WNdb-3.0.tar.gz`, not itself committed —
`.gitignore` already excludes `*.tar.gz`), for verifying a future re-download still
matches: `658b1ba191f5f98c2e9bae3e25c186013158f30ef779f191d2a44e5d25046dc8`.

## File format notes (verified against the real files, not guessed)

- Both `data.noun` and `index.noun` begin with a 29-line copyright/license header
  (each header line is literal file content prefixed with its own line number, e.g.
  `  14 WordNet 3.0 Copyright 2006 by Princeton University...`) before the real data
  starts on line 30. A parser should skip to the first line matching the real data
  shape rather than hardcoding "skip 29 lines", in case a future WordNet version's
  header length differs.
- `data.noun` line shape (per the documented `wndb.5` format): an 8-digit
  zero-padded synset offset, followed by lexicographer file number, `n` (part of
  speech), word count, the lemma list (this is the synset's alias/synonym set),
  pointer count, then pointer records. Pointer symbol `@` = hypernym, `@i` =
  instance hypernym (walk these upward for an ancestor chain), `~` = hyponym
  (walk these downward to expand a domain anchor's subtypes). Example (first real
  line): `00001740 03 n 01 entity 0 003 ~ 00001930 n 0000 ~ 00002137 n 0000 ~
  04424418 n 0000 | that which is perceived or known or inferred to have its own
  distinct existence (living or nonliving)`.
- `index.noun` maps a lemma to its candidate synset offsets, ordered by sense
  frequency (sense 1 first) — the natural "most common sense wins" tiebreaker for
  an ambiguous lemma.
- `imagenet_class_index.json` maps ImageNet-1k class id (as a string "0".."999") to
  `[wnid, name]`, e.g. `{"0": ["n01440764", "tench"]}`. `wnid` is `"n" + ` the
  8-digit WordNet 3.0 noun synset offset — identical format to what we use as
  `SynsetId` in our own taxonomy, so ImageNet leaves map onto our ids with zero
  translation.

## Licensing

WordNet 3.0's license (`LICENSE`, committed verbatim above) permits use, copying,
modification and redistribution without fee, provided the copyright notice and
disclaimer are preserved in all copies including derived works. Any artifact
derived from this data (in particular `source/PictTag.Core/Taxonomy/taxonomy.json`)
must carry its own attribution — see the `license` field embedded in that file's
generated header — since that artifact, not this raw-data folder, is what actually
ships inside the compiled `PictTag.Core` assembly.

`imagenet_class_index.json` is a convenience copy hosted by Google, ultimately
sourced from the ILSVRC devkit; it is used here only as a build-time seed input
(a list of `wnid -> name` pairs) and is never redistributed as part of the shipped
product.

## Refreshing this data

Re-run `Get-WordNetData.ps1` from the repo root to re-download and re-verify all
four files against the checksums in this table. If Princeton ever ships a newer
WordNet version, update the checksums/URLs here and re-run the full
`PictTag.TaxonomyBuilder` pipeline (see `docs/TAXONOMY.md`) to regenerate the
derived `taxonomy.json`/`taxonomy-embeddings.bin`.
