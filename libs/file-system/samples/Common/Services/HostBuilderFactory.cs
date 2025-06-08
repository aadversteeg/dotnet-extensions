using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ave.Extensions.FileSystem.Local;
using FileSystemSamples.Common.Options;

namespace FileSystemSamples.Common.Services;

public static class HostBuilderFactory
{
    public static IHost CreateHost<TOptions>(TOptions options) where TOptions : class, ICommonOptions
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole(consoleOptions =>
                {
                    consoleOptions.LogToStandardErrorThreshold = LogLevel.Trace;
                });
                logging.SetMinimumLevel(options.LogLevel);
                logging.AddFilter("Microsoft", LogLevel.Warning);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddFileSystem();
                services.AddSingleton(options);
            })
            .Build();
    }
}