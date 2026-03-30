
# Saqqara LLC — Branding Assets

This folder contains the Saqqara LLC brand assets used by the API and future web clients.

## Files
- `saqqara-logo-flat.svg` — flat gold (#C9A227), transparent background.
- `saqqara-logo-gradient.svg` — subtle gold gradient.
- `favicon-16.png`, `favicon-32.png` — favicons.
- `icon-192.png`, `icon-512.png` — PWA/app icons.
- `site.webmanifest` — web app manifest.
- `swagger-custom.css` — Swagger UI theme overrides.

## Notes on Outlined Text
The wordmark is set in **Cinzel**. In this repository version, the lettering is included as SVG `<text>` using `font-family: Cinzel`. If you require **hard outlines** (paths with no font dependency), run the following on a machine with **Inkscape** and the **Cinzel** font installed:

```bash
# Outline text and save a production-ready variant
inkscape wwwroot/branding/saqqara-logo-flat.svg   --export-plain-svg=wwwroot/branding/saqqara-logo-flat.outlined.svg   --export-text-to-path

inkscape wwwroot/branding/saqqara-logo-gradient.svg   --export-plain-svg=wwwroot/branding/saqqara-logo-gradient.outlined.svg   --export-text-to-path
```

This converts the wordmark to **pure paths** while preserving shapes and colors.
