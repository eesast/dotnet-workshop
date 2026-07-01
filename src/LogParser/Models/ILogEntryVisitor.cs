namespace LogParser.Models
{
    public interface ILogEntryVisitor<TResult>
    {
        TResult Visit(CallLogEntry entry);
        TResult Visit(RequestLogEntry entry);
        TResult Visit(InternalLogEntry entry);
    }
}
