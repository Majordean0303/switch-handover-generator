# Switch Handover migration checkpoint

- Baseline `61222f7a901d7e0892ed8b82c41fc6a06f33f420`; branch `migration/avalonia/switch-handover-generator`.
- Native .NET 10 solution retains core switch/AP parsers, Excel/CSV/ZIP semantics and WPF reference source. Product/version: Switch Handover Generator 2.0.0.
- Validated topology subset pinned in `docs/TOPOLOGY_PROVENANCE.md`; no external feed or sibling repository build dependency. Preview and VSDX use one physical-wire layout and independent export snapshots.
- New core entry point `HandoverAnalysis` handles archive/file ingestion, cleanup, bounded sizes and atomic report exports. Added detail-CDP parsing and standard status states to fill the legacy live-capture report gap.
- New `BoundedLiveCapture` replaces recursive unbounded discovery in the native UI. Injectable SSH transport; explicit worker/hop/device limits, CIDR filtering, phone exclusion, cancellation and completed capture persistence. Legacy LiveDiscoveryEngine remains a source reference.
- Validation: 45 passing core checks, 2 Skia headless UI checks, Release builds, synthetic CLI XLSX/VSDX export. No hardware connections.
- Morning review: native rendering/packages, actual Visio layout/custom stencils, switch/AP command compatibility, AAA/SSH and cancellation against lab equipment. Main/release unchanged.
