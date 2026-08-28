using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LogParser.Models;
using Parquet.Serialization;

namespace LogParser.Parquet
{
    /// <summary>
    /// 把一组 <see cref="LogEntry"/> 写成 Parquet 文件，列 schema 见 <see cref="ParquetLogRow"/>。
    /// 该格式可被 <see cref="ParquetLogReader"/> 读回，从而实现「.log → 分析 → 导出 Parquet → 再分析」的闭环。
    /// </summary>
    public static class ParquetLogWriter
    {
        /// <summary>
        /// 将日志条目写入 <paramref name="path"/> 指定的 Parquet 文件。
        /// 若文件已存在则覆盖。返回写入的条目数。
        /// </summary>
        public static async Task<int> WriteAsync(string path, IEnumerable<LogEntry> entries, CancellationToken cancellationToken = default)
        {
            var rows = new List<ParquetLogRow>();
            foreach (var entry in entries)
            {
                rows.Add(ToRow(entry));
            }

            using Stream fs = File.Create(path);
            // 用 Parquet.Net 的高级序列化 API：直接按 POCO 写入，列 schema 由 ParquetLogRow 的属性决定。
            await ParquetSerializer.SerializeAsync(rows, fs, null, null, cancellationToken);
            return rows.Count;
        }

        private static ParquetLogRow ToRow(LogEntry e)
        {
            var row = new ParquetLogRow
            {
                LineNo = e.LineNo,
                Timestamp = e.Timestamp.ToString("O"),
                PodName = e.PodName ?? "",
                Severity = e.Severity.ToString().ToLowerInvariant(),
                EventType = e.EventType.ToString().ToLowerInvariant(),
            };

            switch (e)
            {
                case CallLogEntry c:
                    row.RequestId = c.RequestId;
                    row.TargetService = c.TargetService;
                    row.DurationMs = c.DurationMs;
                    break;
                case RequestLogEntry r:
                    row.RequestId = r.RequestId;
                    row.Method = r.Method;
                    row.Path = r.Path;
                    row.StatusCode = r.StatusCode;
                    break;
                case InternalLogEntry ie:
                    row.ExceptionName = ie.ExceptionName;
                    row.ExceptionMessage = ie.ExceptionMessage;
                    break;
            }
            return row;
        }
    }
}
