# figc4.5 SREJ correction — before / after

Documentation example for the SDL render pipeline (see
[`docs/sdl-rendering.md`](../../sdl-rendering.md)): the `srej-received`
viewport of `DataLink_TimerRecovery.graphml` rendered before and after the
correction adjudicated in [ax25sdl#55](https://github.com/packet-net/ax25sdl/issues/55)
/ [PR #69](https://github.com/packet-net/ax25sdl/pull/69).

- **`before.svg`** — figc4.5e as drawn: the four "frames outstanding" SREJ
  paths use the fresh-DL-DATA **Push Frame Onto Queue** verb and end in the
  go-back-N **Invoke Retransmission** subroutine (both on the command side
  `V(s) == V(a)?`-No branch and at the end of the response chain).
- **`after.svg`** — the corrected transcription: both branches converge on
  the single-frame selective **Push Old I Frame N(r) on Queue** chain
  (LM-DATA Request, Stop T3 / Start T1 / Clear Acknowledge Pending), per
  §4.3.2.4 / §4.4.4 / §6.4.8 and the figc4.4 Connected SREJ handler; the
  Invoke Retransmission boxes are gone.

Unlike `spec-sdl/**/svg/` these files are **not** drift-checked — they are a
frozen illustration of one correction, kept so the PR description's images
have a stable home in the repo history.
