using LogParser.Models;

namespace LogAnalyzer
{
    public enum AnalysisState
    {
        NotAnalyzed,
        Succeeded,
        Failed,
    }

    public record AnalysisResult(
        string FileName,
        string FullName,
        AnalysisState State,
        IReadOnlyList<LogEntry> Entries,
        string? ErrorMessage,
        int WorkerId
    );
}
