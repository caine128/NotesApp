using Xunit;

// For database-backed tests we want full control over the order
// and we don't want multiple tests to drop/create the same database
// at the same time. So we disable xUnit's parallelization for this assembly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]