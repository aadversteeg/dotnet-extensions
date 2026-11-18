using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace UnitTests.Extensions.Documents.Images;

/// <summary>Thrown by a test to report itself as skipped, with the reason, when it finds out only while running that it
/// cannot run here (xUnit 2 decides skips when tests are listed; <see cref="RuntimeSkipTheoryAttribute"/> adds this).</summary>
/// <param name="reason">Why the test is skipped.</param>
public sealed class SkipTestException(string reason) : Exception(reason);

/// <summary>A theory whose rows may skip themselves at run time by throwing <see cref="SkipTestException"/>.</summary>
[XunitTestCaseDiscoverer("UnitTests.Extensions.Documents.Images.RuntimeSkipTheoryDiscoverer", "UnitTests.Extensions.Documents.Images")]
public sealed class RuntimeSkipTheoryAttribute : TheoryAttribute;

/// <summary>Makes each row of a <see cref="RuntimeSkipTheoryAttribute"/> theory a <see cref="RuntimeSkipTestCase"/>.</summary>
/// <param name="diagnosticMessageSink">xUnit's diagnostic sink.</param>
public sealed class RuntimeSkipTheoryDiscoverer(IMessageSink diagnosticMessageSink) : TheoryDiscoverer(diagnosticMessageSink)
{
    /// <inheritdoc/>
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow) =>
        [new RuntimeSkipTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, dataRow)];
}

/// <summary>A test case that reports a <see cref="SkipTestException"/> as a skip with its message, not as a failure.</summary>
public sealed class RuntimeSkipTestCase : XunitTestCase
{
    /// <summary>For deserialisation by xUnit.</summary>
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public RuntimeSkipTestCase()
    {
    }

    /// <summary>Creates the test case.</summary>
    /// <param name="diagnosticMessageSink">xUnit's diagnostic sink.</param>
    /// <param name="defaultMethodDisplay">How test names are shown.</param>
    /// <param name="defaultMethodDisplayOptions">More display options.</param>
    /// <param name="testMethod">The test method.</param>
    /// <param name="testMethodArguments">The row's arguments.</param>
    public RuntimeSkipTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, object[] testMethodArguments)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
    {
    }

    /// <inheritdoc/>
    public override async Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
    {
        var skipping = new SkippingBus(messageBus);
        var summary = await base.RunAsync(diagnosticMessageSink, skipping, constructorArguments, aggregator, cancellationTokenSource);
        summary.Failed -= skipping.Skipped;
        summary.Skipped += skipping.Skipped;
        return summary;
    }

    /// <summary>Turns the failure of a test that threw <see cref="SkipTestException"/> into a skip.</summary>
    private sealed class SkippingBus(IMessageBus inner) : IMessageBus
    {
        public int Skipped { get; private set; }

        public bool QueueMessage(IMessageSinkMessage message)
        {
            if (message is ITestFailed failed && failed.ExceptionTypes.FirstOrDefault() == typeof(SkipTestException).FullName)
            {
                Skipped++;
                return inner.QueueMessage(new TestSkipped(failed.Test, failed.Messages.FirstOrDefault()));
            }

            return inner.QueueMessage(message);
        }

        public void Dispose()
        {
        }
    }
}
