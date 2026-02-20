---
allowed-tools: Bash(dotnet:*)
description: Create a new game project from BaseTemplate
argument-hint: <GameName> [--description "..."] [--author "..."] [--force]
---

# Create New Game Project

Create a new game project from BaseTemplate using the DerpTech CLI tool.

## Command

```bash
dotnet run --project Tools/DerpTech.Cli -- new-game $ARGUMENTS
```

## What This Does

1. Copies `Games/BaseTemplate/` to `Games/<GameName>/`
2. Renames all files and folders from `BaseTemplate.*` to `<GameName>.*`
3. Replaces `BaseTemplate` namespace with `<GameName>` in all source files
4. Generates `ProjectConfig.json` with project metadata
5. Adds all 6 sub-projects to the solution

## Examples

- `/new-game MyGame` - Create a basic game
- `/new-game MyGame --description "A tower defense game" --author "DerpTech"` - With metadata
- `/new-game MyGame --force` - Overwrite existing project

## After Creation

```bash
cd Games/<GameName>
dotnet build
bash scripts/run-game.sh <GameName>
```
