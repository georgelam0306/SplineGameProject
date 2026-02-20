# Deep Explain Mode

When the user asks to "explain", "teach me about", "walk me through", "how does X work", "break down", or "unravel" a system/concept, follow this teaching structure:

## Structure: Build Up from Basics

**You MUST build concepts from the ground up.** Don't start with the big picture — start with the smallest building block, then compose upward until the full system is revealed. The reader should never encounter a concept they haven't been introduced to yet.

### 1. TLDR (2-3 sentences)
What is this system? What problem does it solve? One-line mental model.

### 2. Concept #1: The Smallest Building Block
- Name the concept
- Explain the **intuition** (why does this exist? what problem does it solve?)
- Show the **actual code** (struct/class definition, key fields)
- Give a **concrete example** of how it's used

### 3. Concept #2: The Next Layer Up
- Same pattern: intuition → code → example
- **Show how it composes** with Concept #1 via ASCII diagram

### 4. Continue Building Up...
Each concept should reference the ones below it. Draw ASCII diagrams showing how they connect.

### 5. The Full Picture
- ASCII diagram of the **complete system** with all concepts wired together
- Show the **full data flow** for a concrete scenario (e.g., "what happens when the user types '42' into a cell?")
- Include **file paths** and **line numbers** for every call site

### 6. Key Invariants & Gotchas
End with things that are easy to get wrong or non-obvious.

## Required Elements

- **ASCII diagrams** at EVERY level (not just the top). Show data structures, call chains, data flow.
- **Real code snippets** from the codebase (not pseudocode). Include file:line references.
- **Intuition before mechanics** — always explain WHY before HOW.
- **Concrete examples** — trace a specific scenario end-to-end.
- **Call sites** — show where each function/method is actually invoked, not just its definition.

## Style

- Use `file_path:line` format for all code references
- Use bullet points, not paragraphs
- Bold key terms on first introduction
- Keep each concept section focused — if it's getting long, it's two concepts
- Use ```csharp blocks for code snippets
