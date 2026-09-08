# Third-party notices

Logic Lab includes the material below under its own license. The repository's
`MIT OR Apache-2.0` grant does not replace these terms.

## Atkinson Hyperlegible Next

- **Bundled file:** `src/LogicLab.Web/wwwroot/fonts/AtkinsonHyperlegibleNext-Regular.woff2`
- **Copyright:** 2020–2024 The Atkinson Hyperlegible Next Project Authors
- **License:** SIL Open Font License, Version 1.1
- **Source:** [googlefonts/atkinson-hyperlegible-next](https://github.com/googlefonts/atkinson-hyperlegible-next)
- **Upstream Git blob:** `6deb0f465c28ded75d46cd29309d3bf1ba807a12`
- **SHA-256:** `378aea0f5c1d179f4e0b5382c06bfc87571b98cfcc4fd1352bc979e2e2259c54`
- **License text:** [`OFL.txt`](./src/LogicLab.Web/wwwroot/fonts/OFL.txt)

The bundled binary is byte-for-byte identical to upstream
`fonts/webfonts/AtkinsonHyperlegibleNext-Regular.woff2`. The existing license
text stays beside the font and remains part of Web publication output.

## Noto Sans CJK SC

- **Bundled file:** `src/LogicLab.Web/wwwroot/fonts/NotoSansSC-Regular.woff2`
- **Copyright:** 2014–2021 Adobe, with Reserved Font Name 'Source'
- **License:** SIL Open Font License, Version 1.1
- **Source:** [Noto CJK Sans 2.004](https://github.com/notofonts/noto-cjk/blob/Sans2.004/Sans/SubsetOTF/SC/NotoSansSC-Regular.otf)
- **Source SHA-256:** `faa6c9df652116dde789d351359f3d7e5d2285a2b2a1f04a2d7244df706d5ea9`
- **Bundled SHA-256:** `39f19dcb9a64e1204b3298b888e4e01d9d96fcc6da2efec14fe1da1f1debc957`
- **License text:** [`Noto-OFL.txt`](./src/LogicLab.Web/wwwroot/fonts/Noto-OFL.txt)

The upstream OTF was converted losslessly with fontTools 4.64.0, Brotli 1.2.0,
and `TTFont(recalcTimestamp=False).flavor = "woff2"`; glyphs were not subsetted
or modified. This fixed scene font covers Chinese labels and core component
symbols. Its cmap manifest and verified asset identity live together in
`symbol-font.js`. Atkinson remains the Web chrome font.
