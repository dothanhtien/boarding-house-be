using Xunit;

// Integration tests spin up real Postgres containers via Testcontainers; running test
// classes in parallel starts multiple containers/hosts at once and causes flaky failures
// under constrained local resources, so collections are serialized within this assembly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
