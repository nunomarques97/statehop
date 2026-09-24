# DESIGN — Statehop

**Chosen direction:** **A — Day Strip**, on 6 Sep 2026.
Mock and captures: `docs/design/mocks/faixa-do-dia.html` and
`docs/design/mocks/shots/`.

Why it was chosen, so the intent is built rather than the picture: **the strip
answers "where did my day go" in a single reading, before any text is
processed**: shape, proportion and interruptions are all visible at once. And it
scales: it is the same strip with 6 blocks or with 40.

All UI work follows this file.

---

# Part 1 — Product rules (closed)

These win over any aesthetic consideration. They are not reopened without an explicit decision.

## 1. Statehop must never look like surveillance

Statehop observes **everything** the user does, all day long. The design has
to read as **"this is yours, and it stays here"**, never as **"your activity is
being measured"**.

- **The product observes and helps. It does not grade the user.** No focus
  scores, productive-versus-unproductive, streaks or goals.
- **No comparison.** Not with other people, not with averages, and not with
  "your last week" presented as a judgement. There is no cloud and there will
  not be one.
- **Nothing may suggest that data leaves the machine.** When in doubt, the
  screen says so: *"only on this machine"*.
- **No AI language anywhere in the interface.** AI is implementation, not
  positioning (`docs/PRODUCT.md`).
- **Instrument framing is forbidden.** Profiler-style views, "tracks",
  telemetry: however technically elegant, they read as surveillance.

Why, concretely: the target audience is power users who **already rejected
RescueTime for exactly this reason** (complaints about transparency and
privacy), and the answer is the *"local-first + extremely simple + concrete
action"* route.

## 2. The whole day fits without scrolling

The main screen's job is to answer *"where did my day go"* in ten seconds. A
full working day fits in the window **without scrolling**.

That is where the density comes from, and the density comes from instrument
discipline **without** the instrument framing.

## 3. Colour only encodes meaning, it never decorates

If a colour does not distinguish **an app**, **an absence** or **"now"**, it
does not go in. No brand colour applied to surfaces, no gradients, no
decorative highlights.

## 4. Native to Windows 11, light and dark

Tone: **calm, precise, native**. It should feel like part of Windows 11, not a
web page inside a window. The light theme is **not** an afterthought.

## 5. Suggestions are worded probabilistically

*"Docker appears unlikely to be needed in this context"*, never
*"Docker is inactive, it is safe to close"*. Inactivity is not uselessness
(`docs/PRODUCT.md`).

---

# Part 2 — Visual system (direction A)

## Colours

Roles, not colour names. The values below are the starting point; what is
binding are the **roles** and rule 3.

| Token | Role | Light | Dark |
|---|---|---|---|
| `surface` | Window background (where Mica sits) | `#F9F9F9` | `#1F1F1F` |
| `layer` | Cards and lists | `#FFFFFF` | `#2B2B2B` |
| `layer-alt` | Grooves, strip background, hover states | `#F3F3F3` | `#262626` |
| `stroke` | Outlines and separators | `#E5E5E5` | `#383838` |
| `stroke-strong` | Emphasis outline | `#D2D2D2` | `#4A4A4A` |
| `text` | Main text | `#1B1B1B` | `#F2F2F2` |
| `text-2` | Secondary: dates, "with X", labels | `#5D5D5D` | `#B8B8B8` |
| `text-3` | Tertiary: axis, notes, **absence** | `#8A8A8A` | `#8A8A8A` |
| `accent` | **Only** the "now" marker | `#0F6CBD` | `#4CC2FF` |

### App palette

**Contradiction resolved on 7 Sep 2026.** The previous version had category
names (`app-code`, `app-web`, …) and at the same time a hash-based assignment
rule. The two cannot coexist: under a hash, PokerStars lands in `app-code`.
Decision: **neutral slot names, stable hash assignment.** Classifying by
category would mean maintaining a table of apps forever, and it would fail on
everything unknown, which on a typical machine is almost everything.

Ten slots, deliberately desaturated and ordered so that **consecutive** slots
are also distinct from each other (the repair below moves to the next slot, so
neighbouring slots often appear together).

| Token | Light | Dark | |
|---|---|---|---|
| `app-1` | `#3F6B8A` | `#6A9EC0` | blue |
| `app-2` | `#A8794F` | `#CFA274` | burnt orange |
| `app-3` | `#4F8A7B` | `#6FB8A5` | teal |
| `app-4` | `#96566B` | `#C98098` | mauve |
| `app-5` | `#7C6AA8` | `#A394D0` | violet |
| `app-6` | `#7A8290` | `#98A2B2` | blue-grey |
| `app-7` | `#A85F4F` | `#CF8A74` | terracotta |
| `app-8` | `#3F7F8A` | `#6AB4C0` | cyan |
| `app-9` | `#5A6BA8` | `#8494D0` | indigo |
| `app-10` | `#6F8A4F` | `#9BBA74` | olive |
| `idle` | `#DCDCDC` | `#3A3A3A` | absence — never an app slot |

**1. Base assignment: a stable hash of the normalised name.** FNV-1a of the
`AppKey` (`AppNormalizer`), modulo 10. Stable across sessions, days and
machines: the user learns the colours of their day.

> **Implementation trap:** do not use `string.GetHashCode()`. In .NET it is
> randomised per process, so colours would change **on every app start**,
> which is exactly the defect this rule exists to prevent.

**2. Neighbourhood repair.** Requirement: *within a day, every app with a
visible block must be distinguishable from its neighbours*. More slots alone
are not enough, as measured below. Therefore:

- The day's apps that have a block are ordered by **total time, descending**.
- Each takes its hash slot. If that slot is already taken by an app it
  **neighbours on the strip**, it moves to the next free slot.
- Because the walk is by time, **the dominant apps never move**: they are
  precisely the ones whose colour the user memorises.

**Why this is guaranteed and not a probability:** on the strip each block
touches at most two blocks, and the repair only has to avoid neighbours already
placed. As long as there are more slots than the day's maximum adjacency degree,
there is always a free slot. On the real day of 6 Sep the maximum degree was
**8**, against 10 slots.

**3. Seam on the strip.** Between segments there is a 1 px line in `surface`.
It is the structural safety net: even if a pathological day exhausts the slots,
two neighbouring segments **never read as a single block**, which is what broke
the reading of proportion. If no free slot exists, the app keeps its hash slot
and the seam handles the rest.

**Measured on a real day of use** (6 Sep 2026, 1,862 foreground events, 131
blocks, 21 apps observed, **13 with a block**):

| | 8 slots | 10 slots | 12 slots |
|---|---|---|---|
| Apps that change slot | 4 of 13 | **2 of 13** | 1 of 13 |
| Adjacent pairs with the same colour | 0 | **0** | 0 |

Without repair, only increasing slots, same-colour adjacent pairs remained in
**every** count tested (6, 8, 10, 12, 14 and 16 slots). That is why the repair
exists: more colour is a probability, the repair is a guarantee.

**Choice: 10 slots.** Twelve would reduce moves to one, but twelve desaturated
colours stop being distinguishable from each other, which trades a solved
problem for a worse one.

**Accepted residual cost:** an app can change colour between days **if and
only if** it collides with a new neighbour. In practice the top apps are
stable, and those are the ones the ordering protects. The alternative, perfect
stability with occasional adjacent duplicates, was rejected because the
requirement is about adjacency.

**Apps without a block get no colour.** The header announces 21 apps, but only
13 had a block. The numbers differ on purpose: only the second needs colour,
and that is what makes the problem tractable.

**The palette is not semantic.** No colour means "good" or "bad". Green is not
productive, red is not waste. This is product rule 1 applied to the palette.

## Typography

Segoe UI Variable, the system family on Windows 11.

| Role | Family | Size | Weight |
|---|---|---|---|
| Day title | Segoe UI Variable **Display** | 28 | 600 |
| Header numbers | Segoe UI Variable Text | 17 | 600 |
| Section header | Segoe UI Variable Text | 14 | 600 |
| Body, list rows | Segoe UI Variable Text | 14 | 400 |
| Secondary, labels | Segoe UI Variable Text | 12–13 | 400 |
| Axis, notes | Segoe UI Variable Text | 11 | 400 |

**Every number that is compared uses tabular figures**
(`font-variant-numeric: tabular-nums`; in XAML, the OpenType `tnum` feature).
Durations and times line up in a column or they cannot be read at a glance.

## Spacing

A scale of 4: **4, 8, 12, 16, 24, 32**. Nothing outside it.
Window margin: 32 horizontal, 8 at the top (the title bar already gives room).

## Radii

Three values at most: **4** (small elements, colour swatches), **6** (strip,
buttons), **8** (cards and lists).

## The signature element: the day strip

- Height **84 px**, radius 6, clipped corners (`overflow: hidden`).
- `layer-alt` background: the groove must be visible even before there is data.
- One segment per contiguous block, width **proportional to duration**. No
  minimum width: a two-minute block must **look** like two minutes.
- **1 px seam in `surface` between segments.** Two neighbouring blocks can
  never read as one; see the palette repair rule.
- Absence of use is an `idle` segment: present, not a hole.
- Axis underneath, hour by hour, in `text-3`, 11 px, tabular.
- **"Now" marker:** a 2 px vertical rule in `accent`, overhanging the strip by
  6 px above and below, with the label *now* underneath, right-aligned to the
  rule. It is the only use of `accent` on the screen.

## States

| State | How it reads |
|---|---|
| **Activity** | `text` at 400, the app's colour on the swatch, duration in `text` |
| **Absence** (`No use`) | **Everything in `text-3`**, including time and duration. No different background. It is an absence, not an event: it **recedes**, it does not stand out. A fill would draw more attention, not less. |
| **In progress** | Ends with *"— now"* instead of an end time. No badge, no pulsing. |
| **No data yet** | The strip appears empty with the groove visible and one line in `text-2`. Never a *spinner*: the app is observing, not loading. |

## Motion

One signature moment per screen, and on this screen it is **the "now" marker
moving forward**. Nothing else animates by default.

- No entrance animation for lists. New rows appear; they do not slide.
- No *fade* on the strip at each update.
- Always respect the system's **reduce motion** setting.

## Do / don't

**Do**
- Say *"only on this machine"* wherever the user might wonder.
- Distinguish **"at the computer"** from **"in use"**. They are different
  numbers, and giving only the first would inflate the day.
- Explain its own aggregations in plain text, next to what they aggregate
  (e.g. *"Focus switches shorter than a minute are grouped into the block
  where they happened."*).
- Put the Phase 0 diagnostics behind a **discreet affordance** in the status
  bar.

**Don't**
- Pie charts, sectors, KPI tiles, gauges.
- Scores, goals, streaks, comparisons.
- Emoji as icons.
- Cards inside cards.
- Colour without meaning.
- The word "AI" or equivalents anywhere in the interface.

---

# Part 3 — What only the real WinUI 3 build can settle

The mocks are HTML. They decide organisation, palette, typography, density and
the signature element. They do **not** decide the following, and none of these
lines should be treated as closed until there is a capture of the running app:

- **Mica and Acrylic.** They are system materials that sample the desktop
  background. `surface` in the mocks is a solid colour approximating them. In
  the real app the window should use **Mica**, with the cards on top of it.
- **System accent colour.** Windows lets the user choose their own. The "now"
  marker should **follow the system accent colour**, not the fixed blue of the
  mocks. The `accent` value in the table is only the *fallback*.
- **Scroll bars.** They are a native control, with Windows 11's overlay
  behaviour. Do not style them.
- **Keyboard focus.** The WinUI focus ring, tab order and keyboard access to
  the strip do not exist in the mocks. The strip **must** be keyboard
  navigable, and every block must have an accessible name.
- **High contrast.** In that mode the categorical palette is replaced by system
  colours, and telling apps apart relies on text, not colour. Check before
  calling Phase 1 closed.
- **Light theme.** Check on the machine, not on the mock: light Mica and a white
  `layer` have less contrast between them than the solid approximation
  suggests.
- **Typography.** Segoe UI Variable has optical axes that the browser does not
  apply the way XAML does.

**Definition of done for any UI change:** a capture of the **running app** in
light and dark, critiqued against this file, fixed and recaptured. A green
build is not proof that the UI exists; that is how three real bugs were caught
in Phase 0.
