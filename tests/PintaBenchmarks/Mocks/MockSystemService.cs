using System;
using Pinta.Core;

namespace PintaBenchmarks;

internal sealed class MockSystemService : ISystemService
{
	public int RenderThreads => Environment.ProcessorCount;

	public OS OperatingSystem { get; } = OS.Other;
}
