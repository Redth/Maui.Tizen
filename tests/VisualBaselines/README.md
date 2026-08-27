# Visual baselines

Reference screenshots for the control catalog, addressed as:

```
tests/VisualBaselines/{profile}/{apiLevel}/{theme}/{density}/{caseId}.png
tests/VisualBaselines/{profile}/{apiLevel}/{theme}/{density}/{caseId}.json
```

Nothing is checked in yet. Baselines are produced by the device lane, which is blocked on the
Samsung workload.

The conventions — why each path segment exists, what the `.json` sidecar must contain, tolerances,
and how baselines are updated — are documented in
[docs/validation/visual-baselines.md](../../docs/validation/visual-baselines.md).

Both the layout and the sidecar contents are enforced by
`Maui.Tizen.Validation.Tests.CatalogAndBaselineConventionTests`.
