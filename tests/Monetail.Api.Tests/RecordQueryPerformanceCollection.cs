using Xunit;

namespace Monetail.Api.Tests;

// 計測中に他のAPIテストのCPU・SQLite I/O負荷を重ねない。
[CollectionDefinition(nameof(RecordQueryPerformanceCollection), DisableParallelization = true)]
public sealed class RecordQueryPerformanceCollection;
