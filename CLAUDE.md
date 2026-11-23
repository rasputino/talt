# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

The shared guidance for every agent (rules, commands, architecture, tests, Spec Kit workflow) is in
`AGENTS.md`, imported here:

@AGENTS.md

## Claude Code specifics

- Spec Kit steps are available as skills: `/speckit-specify`, `/speckit-clarify`, `/speckit-plan`,
  `/speckit-tasks`, `/speckit-analyze`, `/speckit-implement`, `/speckit-checklist`,
  `/speckit-converge`, `/speckit-constitution` (in `.claude/skills/`). OpenCode has the same commands
  in `.opencode/commands/`.
- The shell is Windows (PowerShell 7 or Git Bash). The CI scripts in `ci/` are PowerShell and run
  locally with `pwsh`.
- Linux can be checked locally in WSL (Ubuntu with the .NET 10 SDK in `~/.dotnet`): clone the repo
  inside WSL instead of building from `/mnt/d/talt`, so Windows `bin/` and `obj/` folders are not
  shared.
