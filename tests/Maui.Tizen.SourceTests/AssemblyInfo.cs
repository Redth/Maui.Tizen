using Xunit;

// Tests in this assembly must not run in parallel.
//
// Two shared, mutable resources make parallelism unsafe here, and both produced real order-dependent
// failures before this was added:
//
//   1. MAUI mapper state is process-global. Building the Controls host mutates
//      ViewHandler.ViewMapper in place (29 keys before, 36 after), so a test reading mapper state
//      concurrently with the host build sees a torn view of it.
//
//   2. docs/wave-b-mapper-parity.json is written by the regeneration path and read by the gap
//      check, so the two race over the same file.
//
// Serialising the assembly is the honest fix: the state genuinely is shared, and pretending
// otherwise buys a little speed in exchange for a flaky suite.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
