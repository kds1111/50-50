## Agent skills

### Issue tracker

GitHub issues, via `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical roles, each label string equal to its name. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — root `CONTEXT.md` + `docs/adr/`. See `docs/agents/domain.md`.

### Hardcoded gameplay values

The dev tunes in the Inspector and should never have to edit a script to change how the game
plays. When a change hardcodes a value that affects how the game plays, feels or reads, add it
to the index on [#28](https://github.com/kds1111/50-50/issues/28) **in the same change** — script,
symbol, line, and one line on what changing it would do.

The bar is "would the dev ever ask to change this?". Epsilons, buffer sizes, loop bounds, string
keys and layout constants are implementation details and stay out; an index nobody can finish
reading is an index nobody reads.

Better still, expose it. The index is for values that genuinely cannot be fields yet, not a
place to file the ones you did not feel like wiring.

### Recommend skills at the end of every reply

Close every substantive reply with a short **Skills** line naming the skills that fit *this* moment, as slash commands, each with a few words of why. Recommend only what applies right now; if nothing does, write nothing rather than padding the list.

The pipeline is **grill → ticket → implement → review**. The dev's habit is to stop after ticketing, so the last two steps are the ones worth naming:

- `/grilling` before writing any code — a standing preference, not just for big decisions. `/grill-with-docs` when the decision deserves an ADR in `docs/adr/`.
- `/wayfinder` to pick the next ticket off map [#1](https://github.com/kds1111/50-50/issues/1); `/to-tickets` to break a plan into tickets; `/to-spec` to publish one.
- `/implement` to build from a ticket or spec, rather than from chat.
- `/tdd` for rules code only — bank, grinds, match flow, trick classification. Never for feel; feel is verified by the dev playing it.
- `/code-review` before any merge. The dev does not read diffs himself, so this is the only review the code gets.
- `/codebase-design` for seam decisions; `/improve-codebase-architecture` when a file has grown into a god object.
- `/prototype` for a feel question, `/research` for outside facts, `/diagnosing-bugs` when something is broken or slow.
- `/domain-modeling` when a new term belongs in `CONTEXT.md`; `/triage` once the issue count outgrows reading the list.
