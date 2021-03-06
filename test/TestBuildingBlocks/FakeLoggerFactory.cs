using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace TestBuildingBlocks
{
    public sealed class FakeLoggerFactory : ILoggerFactory, ILoggerProvider
    {
        public FakeLogger Logger { get; }

        public FakeLoggerFactory()
        {
            Logger = new FakeLogger();
        }

        public ILogger CreateLogger(string categoryName) => Logger;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        public sealed class FakeLogger : ILogger
        {
            private readonly ConcurrentBag<FakeLogMessage> _messages = new ConcurrentBag<FakeLogMessage>();

            public IReadOnlyCollection<FakeLogMessage> Messages => _messages;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Clear()
            {
                _messages.Clear();
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                var message = formatter(state, exception);
                _messages.Add(new FakeLogMessage(logLevel, message));
            }

            public IDisposable BeginScope<TState>(TState state) => null;
        }

        public sealed class FakeLogMessage
        {
            public LogLevel LogLevel { get; }
            public string Text { get; }

            public FakeLogMessage(LogLevel logLevel, string text)
            {
                LogLevel = logLevel;
                Text = text;
            }
        }
    }
}
