using System.Collections.Concurrent;
using LogAnalyzer;
using LogAnalyzerAgent.Applications;

namespace LogAnalyzerAgent.Infrastructure
{
    public class SessionManager
    {
        private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
        private readonly ILoggerFactory _loggerFactory;

        public SessionManager(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public AgentSession GetOrCreateSession(string token)
        {
            return _sessions.GetOrAdd(token, t =>
            {
                var analyzer = new LogFileAnalyzer(null);
                return new AgentSession(analyzer, _loggerFactory);
            });
        }

        public void RemoveSession(string token)
        {
            _sessions.TryRemove(token, out _);
        }
    }
}