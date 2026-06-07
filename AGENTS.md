# AGENTS.md

Use the `specs/` folder as the first stop for understanding the project before making larger changes.

- [specs/README.md](specs/README.md) - overview and reading order.
- [specs/architecture.md](specs/architecture.md) - runtime topology, request flow, and system boundaries.
- [specs/code-map.md](specs/code-map.md) - where code sits and which files own which behaviors.
- [specs/design-decisions.md](specs/design-decisions.md) - current tradeoffs and de facto architectural decisions.
- [specs/change-planning.md](specs/change-planning.md) - change entry points, hotspots, and planning guidance for future work.

When a change alters runtime flow, ownership boundaries, or major design tradeoffs, update the relevant file in `specs/` as part of the same work.

When a change affects user-facing behavior, configuration, or developer workflow, update `README.md` or the relevant docs in the same change.

All three C# projects in this repo enable `ImplicitUsings`. When creating or editing `.cs` files, do not add explicit `using` directives for namespaces already provided by the SDK (for example `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Net.Http`, `System.Threading`, and `System.Threading.Tasks`).
