# VaultMcp

VaultMcp is a local-first MCP server that gives coding agents a repo-native knowledge base backed by structured JSON files.

Large repos accumulate knowledge that code alone does not explain:

- what a domain term means
- why an invariant exists
- which workflow is the real one
- which pitfalls keep biting people
- which decision was intentional instead of accidental
- how important data actually flows through the system

That knowledge usually lives in chats, PR comments, wiki scraps, and people's heads. Agents lose it between sessions, then ask again or guess.

VaultMcp keeps that knowledge in the repo, under git, in files humans can read and edit. Agents can:

1. search existing knowledge first
2. load the most relevant notes
3. follow related notes when one file is not enough
4. write back durable learnings when they discover something worth keeping

The goal is simple:

- keep project knowledge close to the code
- make retrieval transparent and debuggable
- avoid hiding the source of truth in a database
- turn session memory into project memory

No SaaS. No opaque memory store. No AI notes app pretending to be infrastructure.

Just JSON notes under git, a small MCP surface, and a practical way to stop re-explaining the same repo context.

## When it makes sense

VaultMcp fits best when you want a repo-local knowledge base such as:

- `docs/domain/`
- `docs/architecture/`
- `docs/decisions/`
- `docs/glossary/`

Typical cases:

- a coding agent entering an unfamiliar repo area
- a reviewer who needs domain context before judging a change
- a long-running project where the same explanations keep repeating
- a team that wants shared repo knowledge without moving it into another system

## Core idea

Point VaultMcp at a JSON vault inside the repository, for example:

```text
docs/domain/
```

Agents use that vault through a small retrieval/capture loop:

1. `recall_context` as the default first tool for project, domain, and architecture knowledge
2. `explain_term` / `compare_terms` / `list_terms` when the task is clearly lexicon-style
3. `get_note`
4. `find_related_notes` if needed
5. do the actual task
6. `capture_term` or `capture_learning` for durable new knowledge

Use `search_notes` or `find_term` directly when the agent already knows the exact term, title, or phrase it wants to look up.

That turns session memory into reviewable project memory.

## Current tools

| Tool | Purpose |
| --- | --- |
| `explain_term` | Explain one domain term or named concept in a lexicon-style response, including nearby concepts and likely follow-up questions. |
| `compare_terms` | Compare multiple terms side by side when names are similar or easy to confuse. |
| `list_terms` | List terms by group or category-style query for quick domain exploration. |
| `capture_term` | Low-friction lexicon write path for new terms, aliases, and primary group assignment. |
| `get_note` | Load a structured JSON note by vault-relative path with an explicit character budget. |
| `search_notes` | Exact lexical search across titles, paths, headings, aliases, tags, kind, and content. Best when the agent already knows the term or phrase it wants. |
| `find_term` | Glossary-style lookup for canonical domain terms and aliases. |
| `recall_context` | Default first retrieval tool. Combines term lookup, lexical note search, full note loading, related-note expansion, and optional semantic matches. |
| `find_related_notes` | Expand from one known note into nearby notes via shared terms, tags, explicit links, and directory proximity. |
| `capture_learning` | Persist durable, repo-relevant knowledge into controlled JSON note buckets. Not for speculative, temporary, or chat-noise notes. |
| `semantic_search_notes` | Specialized semantic retrieval over persisted note chunks after a semantic index exists. Useful for fuzzy or conceptual exploration, not the default first lookup. |
| `reindex_vault` | Maintenance tool that rebuilds the semantic index from JSON source files under the vault root. Not routine retrieval workflow. |
| `index_status` | Diagnostics tool for semantic provider and index health, including model, dimensions, chunk count, and warnings. |

## Design direction: UX-first lexicon surface

The current JSON vault and capture model already make knowledge structured and git-friendly. This PR also adds a first **lexicon-style term lookup surface** on top of that storage.

That means future tool design should bias toward questions agents naturally ask:

- what does this term mean?
- how does term A differ from term B?
- which terms belong to this area?
- how do I add or refine a term safely?

The design goal is **UX first, backend second**:

- keep the mental model small
- make the obvious tool the right tool
- avoid leaking storage or graph complexity into the public API
- derive soft navigation such as `seeAlso` instead of over-modeling it up front

A broader direction for continuing that UX-first API work lives here:

- `design/LEXICON_API_DIRECTION.md` — proposed lexicon-first API surface and field-origin rules for a future UX-focused iteration

This design note still goes beyond the current implementation. The new lexicon tools are intentionally small; the design doc captures the larger direction without forcing the full model into one jump.

## Knowledge model

`capture_learning` currently supports these canonical knowledge kinds:

- `term`
- `workflow`
- `data-flow`
- `invariant`
- `pitfall`
- `decision`

Structured capture fields:

- `term` -> aliases, examples
- `workflow` -> steps
- `data-flow` -> source, sink, steps
- `invariant` -> scope, failureMode
- `pitfall` -> symptom, cause, fix
- `decision` -> context, choice, consequence

The important constraint is: **structured input stays structured in storage and is also exposed back to tools as structured output**.

Capture should stay disciplined. Good `capture_learning` inputs are:

- durable repo knowledge another agent would likely need again
- grounded in code, docs, tests, or repeated human guidance
- specific enough to be reviewed and corrected later

Bad `capture_learning` inputs are:

- speculative guesses
- temporary task status
- duplicate restatements of existing notes
- raw chat fragments or low-signal operational noise

That keeps the output:

- readable for humans
- diffable in git
- searchable without special infrastructure
- reusable by agents in later sessions

Each note can now carry both a rendered text view and directly usable structure:

- top-level metadata: `kind`, `tags`, `aliases`, `related`, `confidence`
- concise text: `summary`, `details`
- direct scalar fields: `scalars`
- direct list fields: `lists`
- typed sections: `sections`
- append-only captured learnings: `learnings`

That means tools do not have to recover lists or sections by reparsing prose.

## Retrieval behavior today

Current implementation highlights:

- lightweight in-memory note index with invalidation via file timestamp + size
- explicit UTF-8 file I/O
- frontmatter parsing for `kind`, `tags`, `aliases`, `related`, `confidence`
- lexical scoring across path, title, headings, aliases, tags, kind, and body
- optional persisted semantic index stored under `.vault/`
- deterministic learning hashes for idempotent `capture_learning` writes
- explicit output budgets for `get_note` and `recall_context`

## Opt-in semantic retrieval

Semantic retrieval stays derived and disposable:

- JSON notes remain the source of truth
- semantic metadata + vectors live under `.vault/`
- lexical retrieval keeps working when no embedding provider is configured

The default semantic model name is currently:

- `all-MiniLM-L6-v2`

VaultMcp can use a direct local ONNX model for semantic indexing. By default it looks for files here below the configured vault root:

```text
.vault/models/all-MiniLM-L6-v2/
  vocab.txt
  onnx/model_qint8_arm64.onnx
```

If those files exist, semantic indexing auto-enables with the local ONNX provider.

You can also configure explicit paths:

```bash
export VAULTMCP_EMBEDDINGS_PROVIDER=onnx
export VAULTMCP_EMBEDDINGS_MODEL=all-MiniLM-L6-v2
export VAULTMCP_EMBEDDINGS_MODEL_PATH='/absolute/path/to/model_qint8_arm64.onnx'
export VAULTMCP_EMBEDDINGS_VOCAB_PATH='/absolute/path/to/vocab.txt'
```

Then call `reindex_vault()` once to build the derived semantic index.

For the Pi / arm64 case, the intended V1 model file is the quantized ONNX export:

```text
onnx/model_qint8_arm64.onnx
```

Lexical retrieval still works when those ONNX assets are missing.

Note: the published `vaultmcp` dotnet tool can also ship with a bundled default model. In that case semantic indexing is available out-of-the-box; the vault-local `.vault/models/...` location still works as an override.

## Download helper for all-MiniLM-L6-v2

The repo includes a helper script that downloads the local ONNX model into the default cache location under the vault root (useful when running from source, or if you prefer vault-local model assets):

```bash
bash scripts/download-all-minilm.sh /absolute/path/to/docs/domain
```

That fetches:

- `vocab.txt`
- `onnx/model_qint8_arm64.onnx`

## Quick start

By default VaultMcp uses `docs/domain` below the current working directory.

Startup option:

- `--root <path>` overrides the vault root

Path behavior:

- relative paths are supported
- relative `--root` values are resolved against the current working directory of the `vaultmcp` process
- if `--root` is omitted, VaultMcp falls back to `./docs/domain` under that same working directory
- the path is not resolved relative to the MCP config file itself

Example MCP config:

```json
{
  "mcpServers": {
    "vault": {
      "command": "vaultmcp",
      "args": [
        "--root",
        "/absolute/path/to/your/docs/domain"
      ]
    }
  }
}
```

A practical way to start:

1. point `--root` at a repo-local vault such as `docs/domain` — or omit `--root` entirely if `vaultmcp` already starts in the repo root
2. create or capture 5-10 core domain terms
3. capture 1-2 critical workflows
4. capture a few invariants or pitfalls people keep re-explaining
5. teach your agent prompt to call VaultMcp before asking the user again

## Suggested agent workflow

A minimal retrieval loop:

1. unfamiliar repo/domain question appears
2. call `recall_context(query)` first
3. read returned notes
4. use `get_note`, `find_related_notes`, `search_notes`, or `find_term` for deeper drilldown when needed
5. continue implementation / planning / answering
6. persist durable new knowledge with `capture_learning`

Operational guidance:

- use `index_status` only when diagnosing semantic retrieval problems or checking provider readiness
- use `reindex_vault` only when semantic retrieval is unavailable, stale, broken, after bulk vault changes, or when explicitly requested
- use `semantic_search_notes` for conceptual or fuzzy exploration, not as the default retrieval entry point

The useful part is the loop itself: retrieval and persistence reinforce each other across sessions.

## Documentation

- `design/LEXICON_API_DIRECTION.md` — proposed UX-first, lexicon-centered follow-up API design
- `docs/STARTER_VAULT.md` — practical first vault layout
- `docs/AGENT_RETRIEVAL_WORKFLOW.md` — retrieval rules and prompt snippets
- `docs/CAPTURE_EXAMPLES.md` — example JSON output shapes
- `THIRD_PARTY_NOTICES.md` — third-party attributions (including bundled embedding model)

## Development

```bash
export PATH="$PATH:/home/bob/.dotnet"
dotnet build VaultMcp.sln
dotnet test VaultMcp.sln --verbosity minimal
```
