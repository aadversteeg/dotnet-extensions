using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Traversal.Handlers;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.TestHelpers;

namespace UnitTests.Extensions.FileSystem.Traversal.Handlers
{
    /// <summary>
    /// The streaming handlers are the consumer side of the traversal lifecycle: they decide what a
    /// caller of EnumerateEntriesAsync/EnumerateFilesAsync experiences when a traversal fails, and
    /// how much the streaming itself costs per entry.
    /// </summary>
    public class StreamingTraversalTests
    {
        private static readonly TimeSpan HangBudget = TimeSpan.FromSeconds(5);

        [Fact(DisplayName = "STR-001: A failed traversal ends EnumerateEntriesAsync with an IOException instead of hanging")]
        public async Task STR001()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();

            // Act - enumerating a path that cannot be resolved used to leave the consumer polling
            // an empty queue forever, because nothing ever marked the stream complete.
            var enumeration = Task.Run(async () =>
            {
                await foreach (var _ in fileSystem.EnumerateEntriesAsync("/does-not-exist".ToCanonicalPath()))
                {
                }
            });

            // Assert
            var finished = await Task.WhenAny(enumeration, Task.Delay(HangBudget));
            finished.Should().BeSameAs(enumeration, "a failed traversal must end the stream, not hang the consumer");
            Func<Task> drain = () => enumeration;
            await drain.Should().ThrowAsync<IOException>();
        }

        [Fact(DisplayName = "STR-002: A failed traversal ends EnumerateFilesAsync with an IOException instead of hanging")]
        public async Task STR002()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();

            // Act
            var enumeration = Task.Run(async () =>
            {
                await foreach (var _ in fileSystem.EnumerateFilesAsync("/does-not-exist".ToCanonicalPath()))
                {
                }
            });

            // Assert
            var finished = await Task.WhenAny(enumeration, Task.Delay(HangBudget));
            finished.Should().BeSameAs(enumeration, "a failed traversal must end the stream, not hang the consumer");
            Func<Task> drain = () => enumeration;
            await drain.Should().ThrowAsync<IOException>();
        }

        [Fact(DisplayName = "STR-003: Entries buffered when the stream is completed are all still delivered")]
        public async Task STR003()
        {
            // Arrange - completion arrives while entries are still sitting unread. Nothing may be
            // dropped: a consumer that starts late still gets everything the traversal produced.
            var handler = new StreamingEntryHandler<FileSystemEntry>("*", includeFiles: true, includeDirectories: true);
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());

            await handler.OnFileAsync(Entry("/test/first.txt"), context);
            await handler.OnFileAsync(Entry("/test/second.txt"), context);
            await handler.OnTraversalCompleteAsync(TraversalSummary.Empty, CancellationToken.None);

            // Act
            var received = new List<FileSystemEntry>();
            await foreach (var entry in handler.GetEntriesAsync())
            {
                received.Add(entry);
            }

            // Assert
            received.Should().HaveCount(2);
            received[0].Name.Should().Be("first.txt");
            received[1].Name.Should().Be("second.txt");
        }

        [Fact(DisplayName = "STR-004: Being told the traversal completed twice is a no-op")]
        public async Task STR004()
        {
            // Arrange - completion is now guaranteed by a finally, so a handler can be told twice
            // when a completion attempt is followed by a failure exit. It must absorb that.
            var handler = new StreamingEntryHandler<FileSystemEntry>("*", includeFiles: true, includeDirectories: true);
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());
            await handler.OnFileAsync(Entry("/test/only.txt"), context);

            // Act
            var first = await handler.OnTraversalCompleteAsync(TraversalSummary.Empty, CancellationToken.None);
            var second = await handler.OnTraversalCompleteAsync(TraversalSummary.Empty, CancellationToken.None);

            // Assert
            first.IsSuccess.Should().BeTrue();
            second.IsSuccess.Should().BeTrue();

            var received = new List<FileSystemEntry>();
            await foreach (var entry in handler.GetEntriesAsync())
            {
                received.Add(entry);
            }

            received.Should().ContainSingle().Which.Name.Should().Be("only.txt");
        }

        [Fact(DisplayName = "STR-005: A waiting consumer is handed the next entry without a polling delay")]
        public async Task STR005()
        {
            // The walk calls EnumerateEntriesAsync once per directory, so any per-entry wait the
            // streaming machinery imposes is multiplied by the size of the tree. Polling with
            // Task.Delay(1) cost one timer tick (~15.6ms on Windows) every time the consumer looked
            // before the producer wrote; 50 samples of that is hundreds of milliseconds.
            //
            // A busy machine (a full parallel test run, a CI runner) can only ADD time to a round:
            // thread-pool queueing, GC pauses. It can never take away the timer tick a polling
            // consumer pays on every sample. So the best of a few rounds is still a measurement of
            // the implementation's floor, and a polling implementation fails every round.
            const int samples = 50;
            const int rounds = 5;
            var budget = TimeSpan.FromMilliseconds(100);

            var measured = new List<TimeSpan>();
            var best = TimeSpan.MaxValue;
            for (var round = 0; round < rounds && best >= budget; round++)
            {
                var elapsed = await MeasureHandoffsAsync(samples);
                measured.Add(elapsed);
                if (elapsed < best)
                {
                    best = elapsed;
                }
            }

            best.Should().BeLessThan(
                budget,
                "{0} hand-offs to a waiting consumer must not each cost a timer tick (rounds measured: {1})",
                samples,
                string.Join(", ", measured.ConvertAll(elapsed => $"{elapsed.TotalMilliseconds:F1}ms")));
        }

        private static async Task<TimeSpan> MeasureHandoffsAsync(int samples)
        {
            var handler = new StreamingEntryHandler<FileSystemEntry>("*", includeFiles: true, includeDirectories: true);
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());
            var observed = new SemaphoreSlim(0);
            var consumed = 0;

            var consumer = Task.Run(async () =>
            {
                await foreach (var _ in handler.GetEntriesAsync())
                {
                    Interlocked.Increment(ref consumed);
                    observed.Release();
                }
            });

            var handoff = new Stopwatch();

            for (var i = 0; i < samples; i++)
            {
                // Let the consumer settle into its wait, so what we time below is purely the cost
                // of noticing an entry that arrives while nothing is queued.
                await Task.Delay(20);

                handoff.Start();
                await handler.OnFileAsync(Entry($"/test/file{i}.txt"), context);
                (await observed.WaitAsync(HangBudget)).Should().BeTrue("the consumer must see every entry");
                handoff.Stop();
            }

            await handler.OnTraversalCompleteAsync(TraversalSummary.Empty, CancellationToken.None);
            await consumer;

            consumed.Should().Be(samples);
            return handoff.Elapsed;
        }

        [Fact(DisplayName = "STR-006: A waiting consumer still honours cancellation")]
        public async Task STR006()
        {
            // Arrange - nothing will ever be written, so the consumer is parked in the wait.
            var handler = new StreamingEntryHandler<FileSystemEntry>("*", includeFiles: true, includeDirectories: true);
            using var cts = new CancellationTokenSource();

            var enumeration = Task.Run(async () =>
            {
                await foreach (var _ in handler.GetEntriesAsync(cts.Token))
                {
                }
            });

            // Act
            await Task.Delay(50);
            cts.Cancel();

            // Assert
            var finished = await Task.WhenAny(enumeration, Task.Delay(HangBudget));
            finished.Should().BeSameAs(enumeration);
            Func<Task> drain = () => enumeration;
            await drain.Should().ThrowAsync<OperationCanceledException>();
        }

        private static FileSystemEntry Entry(string path) =>
            new FileSystemEntry(
                path.ToCanonicalPath(),
                isDirectory: false,
                size: 1,
                extension: ".txt",
                createdUtc: DateTime.UtcNow,
                modifiedUtc: DateTime.UtcNow,
                lastAccessUtc: DateTime.UtcNow,
                attributes: FileAttributes.Normal);
    }
}


