---
name: meridian-browser-workstation
description: >
  Browser workstation specialist for Meridian. Routes and implements
  TypeScript/React tasks in src/Meridian.Ui/dashboard with Meridian-specific
  guardrails for screens, shared components, and workstation endpoints.
tools: Read, Glob, Grep, Edit, Write, Bash
---

# Meridian — Browser Workstation Specialist

Use this agent for TypeScript/React work in the browser-based operator workstation.

> **Skill equivalent:** [`.claude/skills/meridian-browser-workstation/SKILL.md`](../skills/meridian-browser-workstation/SKILL.md)
> **Shared project context:** [`.claude/skills/_shared/project-context.md`](../skills/_shared/project-context.md)

## Workflow

1. Route the task to the owning screen, component, or endpoint surface in `src/Meridian.Ui/dashboard/`.
2. Implement following the skill's workstation guardrails and shared contracts.
3. Validate with targeted `npm --prefix src/Meridian.Ui/dashboard run test` or `run build`.
