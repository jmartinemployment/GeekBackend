# Contributing to GeekBackend

## Commits — no AI tool attribution

Commit messages must **not** include attribution to AI tools. Do not add:

- `Co-authored-by: Cursor` / `Claude` / `Anthropic` / `GitHub Copilot` (or similar)
- `Made-with: Cursor` trailers or `--trailer` flags
- `Co-Authored-By: Claude` or any variant

Authors on commits should be **humans only** (the developer who reviewed and approved the change).

### Cursor / CLI

1. **IDE:** Cursor Settings → Agents → Attribution → disable **Commit Attribution** and **PR Attribution**.
2. **CLI:** `~/.cursor/cli-config.json` should set:
   ```json
   "attribution": {
     "attributeCommitsToAgent": false,
     "attributePRsToAgent": false
   }
   ```
3. **Project override:** `.cursor/cli.json` in this repo (committed) repeats the same flags.

If a trailer was added anyway, amend before push:

```bash
git commit --amend
# remove Co-authored-by / Made-with lines, save, exit editor
```

Optional hook (strips common attribution lines automatically):

```bash
git config core.hooksPath scripts/git-hooks
chmod +x scripts/git-hooks/prepare-commit-msg
```

## Files that must not be committed

AI IDE state and agent instruction files are listed in `.gitignore` (`.cursor/` local state, `.claude/`, `CLAUDE.md`, `AGENTS.md`, `skills-lock.json`, agent transcripts, etc.).

Use **README.md**, **Architecture.md**, and this file for documentation that belongs in git.
