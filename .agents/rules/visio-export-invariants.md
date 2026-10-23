# Visio OpenXML Export Invariants

When authoring or modifying Visio OpenXML export logic:
1. Every 2D shape (nodes, containers, highlights, port tags) must have an explicit `<Section N='Geometry'>` with accurate pre-evaluated `V` attributes matching shape dimensions.
2. In `WriteMasters`, never write static `V='1'` in `LineTo` rows; evaluate using the master's `DefaultWidth` and `DefaultHeight`.
3. Shape instances overriding width/height must emit their own Geometry section to ensure Visio displays exact dimensions without relying on delayed formula recalculation.
4. Text in shapes must declare `VerticalAlign='1'`, paragraph `HorzAlign='1'`, explicit `Char.Size`, `Char.Color`, and tight text margins (`TopMargin`, `BottomMargin`).
5. Ensure `document.xml` registers `StyleSheet ID='3'` so that `TextStyle='3'` references resolve cleanly.
