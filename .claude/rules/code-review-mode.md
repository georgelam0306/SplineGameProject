# Code Review Mode

When reviewing code (user says "code review", "step me through", "explain this code", etc.), follow this structure:

## 1. TLDR (Always Start Here)

Begin with a 2-3 sentence summary of what the code does and what changed. This gives immediate context before diving into details.

Example:
> **TLDR:** This adds dock preview detection so users see a visual overlay when dragging windows. When a window is dragged over another, the system detects which dock zone (Left/Right/Top/Bottom/Center) the mouse is in and draws a colored rectangle preview.

## 2. Architecture Context

Explain the overall system architecture and where this code fits.

**REQUIRED: Draw an ASCII diagram** showing:
- The class/component hierarchy relevant to this code
- The data flow (what calls what, what data moves where)

Then answer:
- What subsystem does this belong to?
- What are the key abstractions/classes involved?
- What invariants or assumptions does the system rely on?

Example diagram:
```
ImGui Docking System
├── DockController (central manager)
│   ├── MainLayout (viewport background)
│   └── FloatingLayouts[] (1:1 with windows)
│
├── DockingLayout
│   └── Root: ImDockNode (tree structure)
│
└── ImWindow (decoupled - knows nothing about docking)

Data flow:
  Im.EndFrame() → ProcessDragInput(mousePos) → Hit-test layouts
                                             → GetDockZone() → zone enum
                                             → DrawPreview()
```

## 3. Section-by-Section Walkthrough

For each logical section of code:

1. **Name the section** (e.g., "Step 1: Find Dragging Window")
2. **Show the relevant code snippet**
3. **Explain what it does**
4. **Relate it to the architecture** - how does this piece connect to the larger system?

Format:
```
### Section Name

[code snippet]

**What:** Brief explanation of mechanics
**Why:** Purpose in the larger system
**Architecture note:** How this connects to other components
```

## 4. Review Questions (Optional)

End with pointed questions about:
- Potential bugs or edge cases
- Performance concerns
- Design decisions that might need revisiting
- TODOs or incomplete implementations

## Style Guidelines

- Use code snippets liberally - show, don't just tell
- Keep explanations concise - 1-3 sentences per point
- Use bullet points over paragraphs
- Highlight key invariants or assumptions the code makes
- Call out any "magic" values or non-obvious logic
