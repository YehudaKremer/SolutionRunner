using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace SolutionRunnerLogging
{
    public class SolutionRunnerLoggingProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SolutionRunnerLogger();
        public void Dispose() { }
    }

    public class SolutionRunnerLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (formatter != null)
            {
                Console.WriteLine("11111" + formatter(state, exception));
            }
        }
    }

    public static class SolutionRunnerLoggingExtensions
    {
        public static ILoggingBuilder AddSolutionRunnerLogging(this ILoggingBuilder builder)
        {
            builder.Services.AddSingleton<ILoggerProvider, SolutionRunnerLoggingProvider>();
            return builder;
        }
    }

}




//builder.Services.AddLogging(config =>
//{
//    config.AddSolutionRunnerLogging();
//    config.AddSerilog(new LoggerConfiguration()
//        .WriteTo.Console()
//        .CreateLogger());
//});


//    <PackageReference Include="Serilog.AspNetCore" Version="8.0.3" />
//    <PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />