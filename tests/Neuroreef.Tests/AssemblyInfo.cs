// Revision M: a world of 256-unit brains takes ~155 KB per slot, so test worlds of 8,000-16,000 slots use
// 1-2.5 GB each; run the tests one at a time so they fit in memory.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
