using Xunit;

// Shared live provider fixtures have real schema and migration side effects;
// deterministic sequential execution avoids test-order races.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
