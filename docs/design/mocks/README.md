# Mocks — Phase 1, timeline screen

Three directions for the Phase 1 main screen.

**Chosen: A — Day Strip** (*Faixa do Dia*), on 6 Sep 2026. The visual system
that came out of it is in `DESIGN.md`. Directions B and C are kept as a record
of the decision and are not maintained.

Direction A was then revised with three changes: the three-number header came
from B, the text explaining how short switches are grouped came from B, and
"No use" (*Sem utilização*) was made to read lighter than real activity.

| File | Direction | Screenshot |
|---|---|---|
| `faixa-do-dia.html` | **A — Day Strip** | `shots/faixa-do-dia.png`, `shots/faixa-do-dia-dark.png` |
| `coluna-do-dia.html` | **B — Day Column** | `shots/coluna-do-dia.png`, `shots/coluna-do-dia-dark.png` |
| `cartoes-de-contexto.html` | **C — Context Cards** | `shots/cartoes-de-contexto.png` |

All captures are at **1180 × 820**, the real window size. There is also a check
at 900 wide (`shots/*-900.png`), because the window is resizable.

**Note of 7 Sep 2026:** the mocks use six colour variables with category names
(`--app-code`, `--app-web`, …). That naming was **replaced** in `DESIGN.md` by
ten neutral slots with stable hash assignment and neighbourhood repair. The look
of direction A does not change; the variable names in the HTML stay as they
were, because the mock is the record of the decision, not the source of truth.
The source of truth is `DESIGN.md`.

The mock also assumes **6 blocks**. A real day of use gives **131**: an open
question about density.

The mocks' interface copy is in Portuguese, the language of the app's current UI.

## What these mocks decide, and what they do not

**They decide:** the organisation of information, the palette and what colour
encodes, the type scale, the density, and each direction's signature element.

**They do not decide, and only the real WinUI 3 build can settle:**

- **Materials.** Mica and Acrylic are system materials that sample the desktop
  background. Here they are approximated with a solid colour.
- **System accent colour.** Windows lets the user choose their own; the mocks
  use a fixed blue. In the real app the "now" marker should follow the system
  accent colour.
- **Typefaces.** Segoe UI Variable has optical axes that Chrome does not apply
  the same way XAML does.
- **Scroll bars, keyboard focus, entrance animations, high contrast and screen
  readers.** All of that is native control behaviour.

The data is realistic but **composed**: the apps are common ones, and the day is
not a real day.

## Rules these directions already follow

From `DESIGN.md` (product rules, already closed):

- The whole day fits **without scrolling**.
- Colour **only encodes meaning**: which app, absence of use, "now". No
  decorative colour.
- No charts, no scores, no comparisons, no AI language.
- No instrument or profiler framing.
- The Phase 0 diagnostics live behind a **discreet affordance** in the status
  bar.
- Light and dark, both.
