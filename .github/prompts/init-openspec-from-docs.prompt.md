---
description: "Initialize project OpenSpec from docs/openspec guides and codebase domains"
name: "Init OpenSpec From Docs"
argument-hint: "Optional scope (e.g., domains, project path, strictness)"
agent: "agent"
---
Initialize OpenSpec for this repository using the codebase and the OpenSpec guidance documents.

Use these sources as the canonical style and structure references:
- [OpenSpec Conversion Guide](../../docs/openspec/OPENSPEC_CONVERSION_GUIDE.md)
- [SDD 101 Guide](../../docs/openspec/SDD_101_GUIDE.md)
- [SpecKit Conversion Guide](../../docs/openspec/SPECKIT_CONVERSION_GUIDE.md)
- [Documentation](../../docs/openspec/DOCUMENTATION.md)

Task:
1. Inspect the codebase and identify the core behavior domains.
2. Initialize a project-level `openspec/` folder if missing.
3. Create:
- `openspec/README.md`
- `openspec/config.yaml`
- `openspec/changes/.gitkeep`
- `openspec/changes/archive/.gitkeep`
4. Create baseline current-state specs in `openspec/specs/<domain>/spec.md` for each major domain.
5. Write requirements with RFC 2119 keywords (MUST, SHALL, SHOULD, MAY).
6. Provide testable scenarios in GIVEN/WHEN/THEN format for each requirement.
7. Keep specs focused on observable behavior, not implementation detail.

Quality requirements:
- Derive domain names from actual repository structure and runtime behavior.
- Prefer concise, precise requirements over broad or vague statements.
- Include both happy path and error/edge-case scenarios when behavior exists.
- Keep output consistent with OpenSpec format from the referenced guides.

Output in chat:
- List every created/updated file.
- Summarize each domain and its key requirements.
- Call out assumptions and uncertainties explicitly.
- Suggest next change package to add under `openspec/changes/`.

If user arguments are provided with this prompt invocation, treat them as scope constraints for domain selection, depth, or target path.