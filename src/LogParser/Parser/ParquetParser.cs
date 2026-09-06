using LogParser.Models;
using Parquet.Serialization;

namespace LogParser.Parser
{
    public sealed class ParquetParser
    {
        public static async Task<IEnumerable<LogEntry>> ParseAsync(string parquetFilePath)
        {
            DeserializationResult<ParquetLogEntry> result = await ParquetSerializer.DeserializeAsync<ParquetLogEntry>(parquetFilePath);
            return result.Data.Select(entry => entry.ToLogEntry());
        }
    }
}