# Source Diagram Standard

Diagrams must be registered in `docs/source/data/diagram-index.yml` before they are treated as maintained architecture artifacts.

Each diagram record declares:

- stable diagram ID
- source file
- output file
- diagram type
- linked source modules
- linked roadmap items
- update triggers

Use Mermaid sources under `docs/architecture/diagrams/` unless another deterministic renderer is explicitly required.
