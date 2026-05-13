# VaultMcp – UX-first Lexicon API Direction

> Status: design note only. This is a proposed follow-up direction, not behavior implemented by the current PR.

## Why this exists

The JSON vault migration makes storage structured and git-friendly.

That does **not** automatically mean the public MCP surface is the best possible one for coding agents.

A likely next step is to simplify the outward-facing tool design around a very small, very intuitive **lexicon-first** model:

- unknown term -> explain it
- similar terms -> compare them
- category of terms -> list them
- new knowledge -> capture or refine a term

The core design rule is:

**UX first, backend second.**

If an agent cannot discover or correctly use the API with high reliability, elegant storage does not save the product.

## Core idea

Treat the knowledge base first as a **lexicon of named domain concepts**.

Each entry has a canonical term plus a human-readable description. Other terms naturally appear in that description and become navigable lookup targets.

That gives us:

- a tiny mental model
- natural traversal through domain knowledge
- low authoring friction
- room to evolve later without forcing an early graph-shaped public API

This model can already cover more than glossary terms. Named workflows and data flows can also start life as lexicon entries:

- `Invoice Correction`
- `Invoice Export`
- `Batch Release`
- `Quarantine Stock`

Only when real usage proves that this is not enough should the public API grow more specialized surface area.

## Design principles

### 1. Prefer a tiny mental model

Agents should not need to learn a taxonomy of storage-oriented object types before they can retrieve useful knowledge.

### 2. Make the obvious tool the right tool

If the user asks “what is X?”, the tool to call should be obvious from the name alone.

### 3. Every field needs a believable source

For each field we expose, we should be able to answer:

- who writes it?
- when is it updated?
- can it be derived instead of maintained?

If the provenance is unclear, the field probably should not be part of the canonical model.

### 4. Derive soft navigation instead of over-modeling it

Fields like `seeAlso`, “next terms to read”, or broad “related terms” are often better treated as **query-time UX output** than as canonical stored truth.

### 5. Prefer answers over retrieval dumps

The public API should try to answer the user’s question directly, not merely expose note paths plus scores and ask the caller to reconstruct meaning.

## Proposed public tool surface

## 1. `explain_term`

Primary tool for unknown terms and named domain concepts.

### Request

```json
{
  "term": "Batch Release"
}
```

### Response

```json
{
  "term": "Batch Release",
  "summary": "Business approval of a production batch before downstream processing or shipment.",
  "description": "Batch release is not just a technical status flag. It is a domain decision, typically following quality inspection, that determines whether a batch may proceed.",
  "aliases": ["Chargenfreigabe"],
  "group": "Release Types",
  "mentions": [
    "Quality Inspection",
    "Quarantine Stock",
    "Shipping Release"
  ],
  "seeAlso": [
    "Quarantine Stock",
    "Setup Release",
    "Shipping Release"
  ],
  "nextQuestions": [
    "How does batch release differ from setup release?",
    "What role does quarantine stock play in the same process?"
  ]
}
```

## 2. `compare_terms`

For similar concepts, categories, and confusion-prone naming.

### Request

```json
{
  "terms": [
    "Setup Release",
    "Batch Release",
    "Incident Release"
  ]
}
```

### Response

```json
{
  "topic": "Release Types",
  "commonGround": "All three terms describe releases, but in different business scopes.",
  "differences": [
    {
      "term": "Setup Release",
      "oneLine": "Approval to start production after setup validation"
    },
    {
      "term": "Batch Release",
      "oneLine": "Approval of a batch after quality evaluation"
    },
    {
      "term": "Incident Release",
      "oneLine": "Exception approval despite a deviation or incident"
    }
  ],
  "confusionRisks": [
    "shared release vocabulary",
    "similar names with different business reach"
  ]
}
```

## 3. `list_terms`

List terms by group or by a category-like query.

### Request

```json
{
  "group": "Release Types"
}
```

### Response

```json
{
  "group": "Release Types",
  "summary": "Named concepts around business release decisions.",
  "terms": [
    {
      "term": "Setup Release",
      "oneLine": "Approval to start production after setup validation"
    },
    {
      "term": "Batch Release",
      "oneLine": "Approval of a batch after quality evaluation"
    },
    {
      "term": "Incident Release",
      "oneLine": "Exception approval in deviation scenarios"
    }
  ]
}
```

## 4. `capture_term`

Single low-friction write path for both new entries and safe incremental refinement.

### Request

```json
{
  "term": "Batch Release",
  "description": "Business approval of a batch before downstream processing or shipment.",
  "aliases": ["Chargenfreigabe"],
  "group": "Release Types"
}
```

### Behavior

- if the term does not exist -> create a new entry
- if the term already exists -> merge carefully
- if an alias already maps to an existing canonical term -> merge into that entry instead of creating a duplicate

### Response

```json
{
  "term": "Batch Release",
  "action": "merged",
  "created": false,
  "updatedFields": [
    "aliases",
    "description"
  ]
}
```

## 5. Optional: `explore_term`

Useful only if term-to-term navigation proves valuable enough to deserve explicit surface area.

### Request

```json
{
  "term": "Batch Release",
  "depth": 1
}
```

### Response

```json
{
  "center": "Batch Release",
  "neighbors": [
    {
      "term": "Quality Inspection",
      "reason": "mentioned in the description"
    },
    {
      "term": "Quarantine Stock",
      "reason": "same topic area and natural contrast concept"
    },
    {
      "term": "Shipping Release",
      "reason": "common downstream follow-up term"
    }
  ],
  "readingGuide": [
    "Read Quality Inspection first",
    "Then Quarantine Stock as the contrast concept",
    "Then Shipping Release"
  ]
}
```

## Field provenance rules

A big part of this design is being explicit about where each field comes from.

### Canonical stored fields

#### `term`
Source: explicit authoring.

#### `description`
Source: explicit authoring. This is the primary knowledge payload.

#### `aliases`
Source: explicit authoring, optionally expanded when the same alternate naming repeatedly appears in code or docs.

#### `group`
Source: explicit authoring, optionally inferred from curated grouping or list-oriented content.

### Derived fields

#### `mentions`
Source: derived from `description`, but only for terms that already exist in the lexicon.

#### `seeAlso`
Source: derived at query time from signals such as:

- shared `group`
- mutual mention patterns
- lexical / semantic neighborhood
- repeated co-occurrence

This should usually be treated as UX output, not canonical stored truth.

#### `nextQuestions`
Source: derived at query time from mentions, group neighbors, comparison candidates, and known confusion patterns.

## Minimal storage shape

The whole point is to keep the canonical model extremely small.

```json
{
  "term": "Batch Release",
  "description": "Batch release is the business approval of a batch before downstream processing or shipment. It typically follows quality inspection and stands in contrast to quarantine stock.",
  "aliases": ["Chargenfreigabe"],
  "group": "Release Types"
}
```

That is enough for a surprisingly useful first product.

Additional stored fields should be added only when their provenance and repeated product value are both clear.

## How workflows and data flows fit

The question is not “do workflows deserve their own type?”

The earlier question is:

> can a named workflow or data flow start as a lexicon entry without hurting UX?

Very often the answer is yes.

Example:

```json
{
  "term": "Invoice Correction",
  "description": "Invoice Correction is the business workflow from rejection through corrected re-approval of an invoice. Neighbor concepts include rejection reason, approval status, and approval request.",
  "group": "Workflows"
}
```

Or:

```json
{
  "term": "Invoice Export",
  "description": "Invoice Export is the data flow that moves approved invoices from the billing system toward reporting. Important terms include export DTO, queue, and approval status.",
  "group": "Data Flows"
}
```

This is often enough for the first useful version, provided that descriptions are well written and neighboring concepts also exist as terms.

## What should not be in the first public API

Avoid prematurely exposing:

- generic graph operations
- file-path-oriented public calls
- storage-shaped CRUD surfaces
- mandatory typed relations everywhere
- a taxonomy that callers must memorize before they can retrieve value

## Recommended smallest useful version

### Read

- `explain_term`
- `compare_terms`
- `list_terms`

### Write

- `capture_term`

### Optional later

- `explore_term`

That gives agents a small, high-acceptance API while keeping enough room to grow once usage teaches us what structure is actually worth paying for.
