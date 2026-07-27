# How palette extraction works

Same picture in, same palette out, anywhere.

| Stage | What happens |
| --- | --- |
| Downscale | Longest side to 1200 px, never upscaled |
| Reject | Alpha under 128, channels all under 15 or all above 240 |
| Sample | Every 5th surviving pixel |
| Cluster | Seeded k-means++, 20 iterations at most |
| Major | Centres by population, descending, duplicates dropped |
| Minor | Second run at 2× the count, distance-filtered |
| Write | ACO v1 section plus a named v2 section |

Paper white and line-art black are dropped: they dominate a k-means run and say
nothing. Nothing surviving fails with "no eligible pixels".

The minor run is seeded independently and accepts a colour only at 50 or more
Euclidean RGB from every major and every accepted minor; short of the requested
count, that threshold drops by 5 and repeats, down to 0.

Counts run 1–20, minor from 0, default 6 each; a shortfall is reported, never
padded. The seed is an FNV-1a hash of the downscaled pixels plus a per-pass salt
— no clock, no default `Random()`.

## Measured example

`docs/assets/sample-artwork.png`, 1600 x 1000, major = 6, minor = 6, in
**220 ms**.

![Sample artwork](../assets/sample-artwork.png)

| Major | | | | | |
| --- | --- | --- | --- | --- | --- |
| `#1A2535` | `#48334D` | `#3D4B65` | `#804C58` | `#B26367` | `#F4B66E` |

| Minor | | | | | |
| --- | --- | --- | --- | --- | --- |
| `#EC6CA8` | `#77DAE6` | `#276060` | `#674153` | `#252346` | `#4A3F68` |

Sky, ridges and water fill the frame: those are the major colours. The cyan and
pink specks cover almost nothing but sit far from everything else in RGB space,
so they came out minor. Small and distinct beats large and dull.

The `.aco` is big-endian: a v1 section, then a v2 section naming each swatch
`Major 01`…, `Minor 01`…. Files land in
`%LOCALAPPDATA%\CSP Palette Companion\Palettes`; code in
`src/CspPaletteCompanion.Core/Palette/`.
