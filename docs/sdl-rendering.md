# SDL figure rendering (graphml → SVG)

The `*.graphml` files in `spec-sdl/**/sdl/` are the canonical transcriptions
of the AX.25 SDL figures, but raw XML is unreviewable: a PR that rewires a
branch or relabels a box needs a **visual** before/after for a human to
check it against the spec figure. This pipeline provides that, reusably,
for every graphml change.

## How it works

`tools/render/render_graphml_svg.py` (Python stdlib only, no dependencies)
re-renders a yEd graphml page into a standalone SVG using only what the
file already contains:

- node geometry + the 13-shape SDL palette embedded as SVG resources,
  scaled to each node's visual bounds exactly as yEd does
  (`usingVisualBounds="true"`);
- polyline edge paths with their source/target offsets, clipped to node
  borders, with arrowheads;
- node labels (word-wrapped at node width, like yEd's cropping labels) and
  Yes/No edge labels (white-halo'd for legibility, nudged off nodes when
  the cached yEd offset collides).

Output is **deterministic** — the same graphml always produces the same
bytes — so renders are committed as generated artifacts and drift-checked
in CI (`render-drift` job), exactly like the code backends:

```sh
# regenerate all committed renders (repo root)
python3 tools/render/render_all.py
```

Committed outputs live next to the sources: `spec-sdl/**/svg/<Page>.svg`
(full page) and `<Page>.<viewport>.svg` (named crops).

## Viewports

The original spec presents each state machine as several figure pages
(figc4.5a–e etc.); a full graphml page is one huge canvas. A
`viewports.json` sidecar in each `sdl/` directory names crops that map
back to those figure-page regions, so review can focus on one column:

```json
{
  "DataLink_TimerRecovery": {
    "srej-received": {
      "comment": "SREJ-received column (the figc4.5e page region)",
      "nodes": ["n205", "n206", "..."],
      "restrict": true,
      "pad": 50
    }
  }
}
```

- `nodes` — anchor node ids; the crop box is their bounding box (plus
  `pad`). Anchors that no longer exist are ignored with a warning, so a
  correction that deletes nodes doesn't invalidate the viewport.
- `restrict: true` — render *only* the listed nodes and the edges between
  them (recommended for column crops; without it, unrelated columns that
  happen to fall inside the bounding box appear too).

Ad-hoc crops without a sidecar entry: `--nodes n205,n233 --pad 60`.

## Reviewing graphml PRs

Because the SVGs are committed, any PR that touches a graphml also carries
regenerated SVGs, and GitHub renders **rich image diffs** for modified SVG
files (Files changed → the image's "rich diff" view: 2-up, swipe,
onion-skin). That is the primary review surface for figure changes; PR
descriptions can additionally pin before/after images via
`raw.githubusercontent.com` URLs (see `docs/renders/figc45-srej/` for the
worked example from ax25sdl#55 / PR #69).

When you add a new state-machine page or a correction viewport:

1. draw / edit the graphml in yEd as usual;
2. add or extend the `viewports.json` entry if a focused crop helps review;
3. `python3 tools/render/render_all.py` and commit the SVGs with the
   graphml + regenerated yaml/backends.

## Fidelity caveats

The renderer reproduces yEd geometry faithfully but is not pixel-identical
to yEd: fonts are approximated (Helvetica stack), yEd's "smart" edge-label
placement model is honoured via its cached offsets with a collision
fallback, and multi-line labels taller than their node (as drawn in some
processing boxes) overflow exactly as they do in yEd. The renders are a
review aid grounded in the drawn figure — the graphml stays canonical.
