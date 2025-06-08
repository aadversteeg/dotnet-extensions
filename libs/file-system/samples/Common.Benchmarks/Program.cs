using BenchmarkDotNet.Running;

namespace FileSystemSamples.Common.Benchmarks;

/// <summary>
/// Entry point for running performance benchmarks.
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}