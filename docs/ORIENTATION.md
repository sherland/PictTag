# Orientation correction

## Why this exists

A real bug surfaced in `data/test-images/2025-02-08 17.56.14.xmp`:
`pictTag:CompositionNotes` claimed "The image is rotated 90 degrees clockwise", but the photo
displays upright in digiKam and Windows. The root cause: the file's EXIF `Orientation` tag is
`Rotate 90 CW` (raw pixel data is stored landscape, 4000x3000; viewers rotate it for display), but
`ImageDetectionService` sent the **raw, un-rotated bytes** straight to Ollama with no EXIF handling
at all. The model wasn't wrong - it genuinely was shown a sideways image. This wasn't just a
cosmetic text-field bug: composition/symmetry analysis was judged against the wrong framing, and
both XMP writers computed region coordinates from `Image.Identify`'s raw (also un-rotated)
dimensions - so bounding-box math was already silently wrong for any rotated photo, independent of
the composition-notes symptom.

It's also not safe to simply trust the EXIF tag and rotate accordingly as the whole fix - that tag
itself can be wrong (a common camera/phone/software bug). The design instead verifies the final
orientation with a purpose-built classifier, and only auto-corrects when it clears a high
confidence bar - the same "empirically tuned threshold" pattern `OllamaTaxonomyEmbeddingIndex.
DefaultMinSimilarity` already uses for taxonomy resolution.

## Model choice: a classifier, not a VLM

The obvious first idea - ask the vision-language model itself ("is this rotated?") - was rejected.
An LLM's self-reported confidence percentage isn't reliably calibrated; there's no guarantee "98%
sure" from a VLM means what it says. What's needed is a real softmax score from a model actually
trained for this narrow task.

[**DuarteBarbosa/deep-image-orientation-detection**](https://huggingface.co/DuarteBarbosa/deep-image-orientation-detection)
([GitHub](https://github.com/duartebarbosadev/deep-image-orientation-detection)) fits: an
EfficientNetV2-S fine-tuned specifically for 4-way orientation classification (0°/90°CW/180°/
90°CCW), **98.82% accuracy on a real held-out validation set**, trained on 189,018 images (COCO +
AI-generated + text-heavy + personal photos), MIT licensed, with an ONNX export
(`orientation_model_v2_0.9882.onnx`, ~80MB) that needs no Python runtime to use.

## The model asset

The `.onnx` file is **not committed** - it's git-ignored (`data/models/`) and fetched on demand,
the same treatment as the bulk art-style test images:

- `OnnxImageOrientationClassifier` auto-downloads it into `data/models/orientation/` on first use
  if missing, verifying its SHA256 (`cffe911c1dff47fbfbbd90110aaab9c07134645c460d35b3ae8832079bea91ba`)
  and warning (not failing) on a mismatch, in case of an intentional upstream update.
- `Get-OrientationModel.ps1` (repo root) does the same download + checksum check by hand, for
  explicit pre-fetching or CI - running it is optional, since `dotnet run` alone already downloads
  the file when it's first needed.

## Preprocessing

Verified against the model's own reference `config.py`/`predict_onnx.py` source directly, not a
summarized description - resize/crop numbers in particular don't match what a first pass of
research suggested (544x544; the real value is 416x416):

1. Resize to 416x416 (`InputSize + 32`) - a non-aspect-preserving **stretch**, matching
   torchvision's `transforms.Resize((h, w))` tuple form exactly, not `Resize(single_int)`.
2. Center-crop to 384x384 (`InputSize` - EfficientNetV2-S's pretraining resolution).
3. Normalize per-channel with ImageNet mean/std (`[0.485, 0.456, 0.406]` / `[0.229, 0.224, 0.225]`).

The reference pipeline applies `exif_transpose` before inference - i.e. it expects an
already-EXIF-corrected image as input, which is exactly the order `ImageOrientationCorrector`
uses (see below). The model's raw output is 4 logits; softmax is computed manually in C#
(`OnnxImageOrientationClassifier.Softmax`) since the reference Python script only does argmax on
raw logits and never surfaces a confidence score itself.

## Runtime pieces (`source/PictTag.Core/Orientation/`)

- **`IImageOrientationClassifier`** / **`OnnxImageOrientationClassifier`** - loads/caches an ONNX
  Runtime `InferenceSession` (downloading the model file if needed), preprocesses, runs inference,
  and returns an `OrientationPrediction { PredictedClass, Confidence }`. `PredictedClass` is the
  *corrective* rotation needed to make the image upright (`Correct`, `Rotate90Cw`, `Rotate180`,
  `Rotate90Ccw`) - not a raw EXIF value.
- **`ExifOrientationMath`** - pure integer arithmetic (no image/classifier dependency, easy to unit
  test) that composes "what the current EXIF tag claims" with "what additional correction the
  classifier found necessary" into the single Orientation value that correctly describes the
  *original raw pixel data*. Returns `null` for the 4 mirrored EXIF variants (`TopRight`,
  `BottomLeft`, `LeftTop`, `RightBottom` - values 2/4/5/7) since composing a further rotation on
  top of an unknown mirror isn't safe to do generically; callers skip the original-file fix in that
  case rather than guessing.
- **`ImageOrientationCorrector`** - the orchestration:
  1. Load the file, read its current EXIF `Orientation`, apply `image.Mutate(x => x.AutoOrient())`
     to get a candidate upright image.
  2. Classify the candidate. If the classifier says `Correct`, or its confidence is below
     `DefaultConfidenceThreshold` (0.98) either way, trust the EXIF-based `AutoOrient()` result and
     stop - a low-confidence signal is not enough to justify a correction.
  3. If the classifier confidently (≥ 0.98) disagrees: apply the extra rotation to the in-memory
     image, and - unless declined - correct the *original file's* EXIF `Orientation` tag in place
     via `exiftool -Orientation#=N -overwrite_original` (the `-#` suffix forces numeric mode
     without needing exiftool's global `-n` flag, verified empirically). This is metadata-only -
     no pixel re-encoding, nothing lossy - so there's no backup kept; if `exiftool` isn't on
     `PATH` or the write fails, it logs a note and continues, since the in-memory image is already
     correctly-oriented for detection regardless.

## DirectML GPU acceleration

`OnnxImageOrientationClassifier` is the project's first non-Ollama inference runtime
(`Microsoft.ML.OnnxRuntime.DirectML`). It tries the DirectML execution provider first
(`EnableMemoryPattern = false` is required alongside it - a documented DirectML constraint, not
optional) and falls back to a plain CPU session on any failure. One try/catch covers "no GPU",
"non-Windows OS", and "driver/compatibility issue" uniformly, with no explicit hardware detection
needed. Which provider ended up active is logged once to the console
("`Orientation classifier: using DirectML (GPU) execution provider.`" or the CPU-fallback
equivalent).

## Wiring into detection

`ImageDetectionService`'s constructor takes an optional `IImageOrientationClassifier` (defaulting
to the shared `OnnxImageOrientationClassifier.Shared` singleton, so the ONNX session is built once)
and `fixOriginalFileOrientation: bool = true`. The corrected image is always used internally for
detection, the annotated preview, and XMP region math, regardless of that flag - it only controls
whether the *original file's* EXIF tag also gets rewritten. `ImageAnalysisResult.ImageWidth`/
`ImageHeight` carry the corrected dimensions; both XMP writers use those instead of independently
re-deriving dimensions from the file via `Image.Identify` (which reports raw, un-rotated encoded
pixel dimensions and would silently reintroduce the same swapped-dimensions bug for any rotated
photo).

The CLI exposes `--skip-orientation-fix` to opt out of the original-file rewrite; there's no flag
to disable the internal correction, since detection/preview/regions should never be computed
against the wrong orientation.

## Tuning the confidence threshold

`ImageOrientationCorrector.DefaultConfidenceThreshold` (0.98) is a constructor parameter, not a
hardcoded constant - retune it without a code change as real-world results accumulate, the same
pattern used for `OllamaTaxonomyEmbeddingIndex.DefaultMinSimilarity` in taxonomy resolution.

## Fixture regeneration note

Regenerating `data/test-images/*.xmp` via `Update-TestImageTags.ps1` runs every fixture through
this pipeline. For `2025-02-08 17.56.14.jpg` specifically, the file's own EXIF Orientation tag was
verified to already be correct (`Rotate 90 CW`, matching the actual raw pixel data) - so no rewrite
happened; the bug was purely that the detection pipeline never respected the tag, not that the tag
was wrong. If a fixture's classifier result ever *does* disagree confidently with its EXIF tag,
regenerating will rewrite that source JPEG's own metadata in place - review that diff deliberately,
not just the `.xmp` diff, since it would be the first time this project's tooling modifies a
tracked source image's own bytes/metadata rather than only writing new sidecar/preview files.
