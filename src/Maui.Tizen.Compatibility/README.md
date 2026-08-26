# Maui.Tizen.Compatibility

**Status: provisional. This project currently has no sources, and that is expected.**

## Why it is empty

`src/Compatibility` was **deleted from dotnet/maui on the `net11.0` branch**. Its 70
Tizen files exist only at tag `9.0.120`, which this repository imported and retains as
`upstream/9.0.120`.

So the sources are not lost — they are in git history, one command away:

```bash
git show upstream/9.0.120 -- src/Compatibility
git checkout upstream/9.0.120 -- src/Compatibility
```

They are simply not checked out at `HEAD`, because materialising 70 files of a
compatibility layer that upstream itself has dropped would be presenting a decision as
though it were already made.

## The decision that is pending

.NET MAUI 11 drops the Compatibility layer. The question for this repository is narrower
than "port Compatibility or not":

> Do any of the net11-era Tizen handlers depend on implementation that exists **only**
> in the old Compatibility Tizen code?

The agreed disposition is:

- **Move** individual files that net11 Tizen handlers genuinely require.
- **Exclude** everything that is redundant with, or superseded by, the net11 handlers —
  which is expected to be the large majority.

That audit is recorded per-file in the source-disposition manifest under
[`eng/manifests/`](../../eng/manifests/), not decided here.

## When this project gets deleted

If the audit concludes that nothing is required, this directory and its project are
removed outright. That is the anticipated outcome. The project exists now purely so the
question has a visible home and does not get silently dropped between phases.

See [`docs/migration.md`](../../docs/migration.md).
