# Validation record

Validation environment:

- Windows 11 Pro 64-bit, build 26200
- Clip Studio Paint PRO 4.0.10.0
- German CSP user interface
- .NET SDK 8.0.319

## Automated checks

- Core unit tests cover option ranges, RGBA input, transparent/black/white
  filtering, deterministic clustering, population ordering, minor selection,
  shortage behavior, and ACO v1/v2 binary layout.
- The independent ACO reader in the tests verifies big-endian fields, channel
  expansion, ordering, and UTF-16BE names.
- Debug and Release builds treat warnings as errors.
- Release verification: 21/21 tests passed, with zero build warnings or errors.
- Formatting verification and `git diff --check` passed.
- The 4K synthetic smoke fixture completed in a 98.7 ms median and 143.4 ms
  p95 over ten warm Release runs.

## Live CSP smoke test

Disposable source:

- 256 × 256 BMP
- four opaque color bands
- one distinct purple accent
- a single CSP raster layer

Observed results:

1. The app located CSP by PID-owned top-level window enumeration.
2. Layer mode activated CSP and copied a 256 × 256 bitmap.
3. Clipboard DIB zero-alpha compatibility was handled as opaque pixels.
4. The app generated five distinct named colors and reported the requested
   shortfall instead of inventing colors.
5. CSP 4.0.10 imported the generated paired v1/v2 ACO as a new Color Set.
6. The imported set contained five swatches in generated order.
7. The first swatch read back as RGB `205, 62, 74`.
8. CSP's **Change color name** dialog showed the imported name `Major 01`.
9. Canvas mode consumed a user-prepared merged clipboard image and generated
   the same five-color palette.
10. Selection mode copied the active-layer pixels inside a live CSP
    full-canvas selection and generated a fresh five-color palette.

The existing CSP document remained open while the disposable BMP was used in a
separate document tab.

## Not yet claimed

- Merged visible Canvas acquisition without a user-prepared clipboard image.
- Merged-visible Selection semantics across multiple layers; Selection mode is
  intentionally active-layer-only.
- Generic restoration of private/delayed clipboard formats.
- Version-independent automatic CSP import.
- Automated proof of cross-window OLE dragging; manual/menu ACO import itself
  was validated.
