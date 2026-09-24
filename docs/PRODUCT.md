# PRODUCT — Statehop

## Positioning

> **Windows that adapts to what you are doing.**

Alternative taglines: *"Switch context. Your PC adapts."* /
*"Switch contexts, not windows."*

Do not position it as an "AI-powered Windows productivity assistant": generic,
and already taken by Copilot/Recall. AI is implementation, not positioning.

## Category

Context-aware Windows utility. It is not a "workspace manager" (PowerToys
already dominates that for free), nor an "AI desktop assistant" (Microsoft is
building that into the platform itself: Recall/Click to Do).

## Core product loop

```
Observe → Understand (infer context) → Suggest → Learn → Act
```

The system observes local usage, infers which context the user is in, suggests
actions (never acting destructively without approval), learns from the user's
decisions, and acts autonomously only on safe, already approved actions.

## Target user (after Phase 5, if validated)

Power users: developers, gamers, creators, traders/analysts, people with many
monitors, 20+ open apps and heavy keyboard use. Not "every Windows user".

## Competition (summary)

| Competitor | What it does | Why it does not kill the thesis |
|---|---|---|
| PowerToys Workspaces | Saves/launches groups of apps, free, 135k+ stars | Static, needs manual setup; no inference |
| DisplayFusion ($34) | Profiles, hotkeys, C#/VB.NET scripts | "Configure it yourself", not "I figure it out for you" |
| Actual Window Manager ($59.95) | Deep startup/window rules | Does not decide on its own when to apply a rule |
| ActivityWatch (free/OSS, 18.6k stars) | Observes local activity | Only observes; does not infer→suggest→act |
| ManicTime ($7/month) / RescueTime ($7–16/month) | Automatic time tracking | Dashboards/abstract productivity, not concrete action |
| AutoHotkey | Powerful contextual automation | Requires scripting; we sell zero configuration |
| Power Automate Desktop | General automation | Heavy workflow builder; we have no builder at all |
| Raycast (Windows 2.0, Aug 2026) | Launcher + AI layer | Validates the "smart user↔PC layer" thesis, not the same product |
| Microsoft Recall / Click to Do | Local activity memory + actions | Validates the direction; a long-term structural risk and a reason not to take years to validate |

## MVP — what to build

```
Local activity tracking (foreground app, process, window, idle, CPU/RAM)
+ context timeline (a simple view of the day)
+ automatic pattern detection (co-occurrence, simple clustering)
+ manual workspace creation/launch
+ safe cleanup suggestions ("Docker does not seem to be needed right now")
+ persistent preferences (never suggest / never close / always keep)
+ tray app, global hotkey, start with Windows
```

## Out of scope for the MVP (not built without an explicit decision)

```
Voice assistant
Fully autonomous AI agent
Screenshot recording / computer vision
Browser content monitoring
Account / cloud / sync between machines
Team collaboration
Plugin marketplace
Complex scripting/macro language
Arbitrary PC control by an LLM
Automatic process closing without an approved rule
Full virtual desktop orchestration
Mobile app / multi-platform support
Calendar / Outlook / Gmail integrations
"AI everything": AI only where language ambiguity justifies it
```

## AI rule

AI must not be introduced before the deterministic architecture (Phases 0–3)
is solid. When it comes in, it is only a natural-language intent classifier
producing structured output (e.g.
`{"intent": "activate_workspace", "workspace": "Development",
"confidence": 0.94}`), never direct action on the system. A deterministic rules
engine always decides what to do with that intent.

## Critical UX rule

**Inactivity does not equal uselessness.** A process at 0% CPU can be essential
(e.g. Docker). The wording of any closing suggestion must be probabilistic and
reversible: *"Docker appears unlikely to be needed in your current context"*,
never *"Docker is inactive, so it is safe to close"*.

## Action safety levels

```
Level 0 — Observe only
Level 1 — Suggest (asks before acting)
Level 2 — Auto-act on safe actions (open/focus an app, restore a position)
Level 3 — Destructive actions (closing apps) — requires explicit authorisation
Level 4 — Force-kill — never automatic by default
```

## Monetisation (not a priority until Phase 5/6)

A model to test, only after personal validation:

- **Free**: observer, context discovery, up to 3 workspaces, basic
  suggestions.
- **Pro — €19–29 one-time**: unlimited workspaces, advanced automation,
  learned preferences, advanced rules, optional local/cloud AI.
- **Possible cloud/AI/sync add-on**: €3–5/month, only with real usage.

Do not validate pricing or build a pricing page before Phase 5.

## Distribution (relevant only after personal validation)

No cold outreach: GitHub (issues/releases), communities where the problem is
already discussed (r/Windows11, r/PowerToys, r/AutoHotkey, r/SideProject),
Microsoft Store/winget once stable, Product Hunt/HN once there is a strong
demo. No paid ads.
