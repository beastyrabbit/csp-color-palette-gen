# Wiki source

These files are the source of truth for the CSP Palette Companion wiki at
<https://git.heerlab.com/beasty/csp-color-palette-gen/wiki>.

Forgejo serves a wiki from a separate git repository. Edit the pages here, commit
them with the code, then publish them to the wiki repo. Do not edit pages in the
Forgejo wiki UI — the next publish overwrites them.

## Page map

| File | Wiki page title |
| --- | --- |
| `Home.md` | Home |
| `Installation.md` | Installation |
| `Selection-Canvas-Auto-Action.md` | Selection Canvas Auto Action |
| `How-Palette-Extraction-Works.md` | How Palette Extraction Works |
| `Troubleshooting.md` | Troubleshooting |

Forgejo maps a hyphen in a wiki page name to a space in the title. Keep the flat
`Word-Word.md` naming so the mapping stays predictable.

## Images

Image links are written as `docs/assets/<name>.png`. Publishing copies
`docs/assets/` from this repository into the wiki repository at the same
relative path, so the links resolve without rewriting them.

## Publishing

```powershell
git clone https://git.heerlab.com/beasty/csp-color-palette-gen.wiki.git
Copy-Item docs/wiki/*.md            <wiki-clone>/            -Force
Copy-Item docs/assets/*.png         <wiki-clone>/docs/assets/ -Force
```

`README.md` in this folder is not published.
