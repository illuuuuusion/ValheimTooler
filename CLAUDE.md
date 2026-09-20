# CLAUDE.md

Project conventions for Claude Code working in this repository.

## Commits

**Never add a `Co-Authored-By` trailer.** No attribution lines of any kind —
not for Claude, not for any tool. The commit is authored by the repository
owner alone.

**Keep messages short.** One line, no body unless the change genuinely needs
one. No bullet lists, no explanation of why, no footers.

**Match the existing history.** Read `git log --oneline` before writing a
message and follow the style already there. The established pattern is a
capitalised imperative verb plus its object:

    Add refactor and no-BepInEx plans
    Update translations.cfg
    Update issue templates
    Fix translations and stamina features for other players
    Bump version to 1.9.1
    Release 1.12.0 for Valheim 1.0.7.

When in doubt, copy the phrasing of the most similar past commit rather than
inventing a new form.
