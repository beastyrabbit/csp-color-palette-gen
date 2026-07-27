# How palette extraction works

Same picture in, same palette out, every time. This page is the exact pipeline,
the constants it uses, and the file it writes.

## The pipeline

| Stage | What happens | Constant |
| --- | --- | --- |
| 1. Downscale | Longest side reduced to at most 1200 px. Never upscaled. | `MaximumDimension = 1200` |
| 2. Reject | Pixels with alpha below 128, near-black pixels, and near-white pixels are dropped. | `AlphaThreshold = 128`, `EdgeColorThreshold = 15` |
| 3. Sample | Every 5th surviving pixel is kept. | `SamplingStride = 5` |
| 4. Cluster | Deterministically seeded k-means++, at most 20 iterations. | `MaximumKMeansIterations = 20` |
| 5. Major | Cluster centres ordered by population, descending. | — |
| 6. Minor | Second clustering run at 2× the requested count, brightness-ordered, filtered by RGB distance. | `InitialMinorDistance = 50`, `MinorDistanceStep = 5` |
| 7. Write | RGB ACO v1 section plus a named UTF-16BE ACO v2 section. | — |

### Rejection thresholds

Near-black means all three channels below 15. Near-white means all three above
240 (`255 − 15`). Both are dropped because paper white and line-art black
dominate a k-means run and say nothing about the palette.

If nothing survives, extraction fails with "no eligible pixels" rather than
returning a palette of noise.

### Major colours

One k-means++ run at exactly the requested count. Centres are sorted by
population descending, with the RGB channels as tie-breakers so equal-population
clusters keep a stable order. Duplicates are removed. The biggest areas of the
picture come out first.

### Minor colours

A second, independent k-means++ run at **twice** the requested minor count, seeded
differently from the major run. Its centres are sorted by perceived brightness
(`0.299·R + 0.587·G + 0.114·B`), ascending.

Candidates are then accepted by Euclidean RGB distance, starting at a threshold
of 50: a candidate is accepted only if it is at least that far from every major
colour and every already-accepted minor colour. If the requested count is not
reached, the threshold drops by 5 and the pass repeats, down to 0.

This is what keeps a small, saturated accent out of the shadow of a large, dull
area. A big sky produces a major colour; three cyan dots produce a minor one.

### Counts

| Setting | Range | Default |
| --- | --- | --- |
| Major colours | 1–20 | 6 |
| Minor colours | 0–20 | 6 |

When the image has fewer distinct colours than requested, the result reports the
shortfall instead of padding it with duplicates.

## Determinism

The k-means++ seed is an FNV-1a hash of the **downscaled** pixel data, mixed with
a per-pass salt — one for major, one for minor. No system clock, no `Random()`
default seed, no thread ordering.

Consequences:

- The same image always gives the same palette, on any machine.
- Two different images almost never share a seed, so they do not accidentally share an initialisation.
- Major and minor runs of the same image start from different initialisations, which is what makes the second pass find different structure.

## The ACO file

Adobe Color Swatch, big-endian throughout, written as two sections back to back.

| Section | Contents |
| --- | --- |
| v1 | `version=1`, `count`, then one entry per colour |
| v2 | `version=2`, `count`, then one entry per colour, each followed by its name |

Each colour entry is five 16-bit big-endian values:

| Field | Value |
| --- | --- |
| Colour space | `0` (RGB) |
| Red | byte expanded to 16 bits as `(v << 8) \| v` |
| Green | same |
| Blue | same |
| Unused | `0` |

Names in the v2 section are a 32-bit length in UTF-16 code units **including the
terminator**, then the name in UTF-16 big-endian, then a `0x0000` terminator.
Swatches are named `Major 01`…`Major NN`, then `Minor 01`…`Minor NN`.

The v1 section exists for readers that stop at version 1. CSP reads the file and
creates a new Color Set from it.

Generated files stay in `%LOCALAPPDATA%\CSP Palette Companion\Palettes`.

## Measured example

Source: `docs/assets/sample-artwork.png`, 1600 x 1000. Extracted in **220 ms**
with major = 6, minor = 6.

![Sample artwork](../assets/sample-artwork.png)

| Major | | | | | |
| --- | --- | --- | --- | --- | --- |
| `#1A2535` | `#48334D` | `#3D4B65` | `#804C58` | `#B26367` | `#F4B66E` |

| Minor | | | | | |
| --- | --- | --- | --- | --- | --- |
| `#EC6CA8` | `#77DAE6` | `#276060` | `#674153` | `#252346` | `#4A3F68` |

The picture is mostly sky, ridges and water. Those became the six major colours,
ordered by how much of the frame they cover. The cyan, yellow and pink specks
cover almost nothing, but they are far from everything else in RGB space, so the
distance filter picked them up as minor colours.

Small and distinct beats large and dull. That is the whole point of the
major/minor split.

## Source

| Type | File |
| --- | --- |
| Pipeline | `src/CspPaletteCompanion.Core/Palette/PaletteExtractor.cs` |
| Options and bounds | `src/CspPaletteCompanion.Core/Palette/PaletteExtractionOptions.cs` |
| ACO writer | `src/CspPaletteCompanion.Core/Palette/AdobeColorSwatchWriter.cs` |
| Tests | `tests/CspPaletteCompanion.Core.Tests/PaletteExtractorTests.cs` |

`CspPaletteCompanion.Core` is a plain `net8.0` library with no Windows
dependency, so it builds and tests on any platform.
