# Embedded topology provenance

Local subset adapted from `Majordean0303/Visio-Creator` migration commit `8bab17b8d593f8037079f887b59d71bf8dcad51e` (draft PR #1), which adopts graph/layout fixes from Network Health Reporter `8ee7280b373653fc1eb94c5ead03f4bd2d7e1d99`.

`Topology/VisioCreator.Core` contains only the domain/port/host/media contracts, graph, diagram layout and Cisco evidence parser. `Topology/VisioCreator.Export` retains the validated OpenXML/stencil exporter and guarded Windows COM fallback. These are local projects in this repository: builds need no sibling checkout or package feed. Export invariants are copied into `.agents/rules/visio-export-invariants.md`.

The handover adapter groups stack inventory rows into devices, attaches parsed port-channel members, reconciles only unambiguous observed IPs, and adds links only from captured neighbor evidence. Blank/unknown values never become invented management addresses or switch links. Conflicting addresses stay unresolved with notes. Unknown neighbor devices remain external, with an IP only when unambiguously observed. Reciprocal observations deduplicate; physical parallel members remain visible.

The adapter generates a single DiagramLayout. The native preview edits that layout; VSDX export uses its independent snapshot. Excel/CSV report models and sheet meanings are retained separately. The Reporter telemetry bridge and its inferred parent/port fallbacks are not included.

Validation includes adopted graph/layout regressions and handover-specific parser, workbook, archive, transport and topology checks. Native Visio/custom stencil rendering is a manual review item, not certified by XML package checks.
