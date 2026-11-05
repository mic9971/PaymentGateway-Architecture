using Xunit;

// Financial tests share one temporary database; prevent races between separate test classes.
// Unit tests are in a different assembly and remain independent.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
