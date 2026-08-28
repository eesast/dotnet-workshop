using System.Collections.Concurrent;
using LogAnalyzer;

namespace LogAnalyzerAgent.Auth
{
    /// <summary>
    /// 每个合法 token 对应一个独立的用户；不同用户的日志分析操作完全互不干扰（T5.1.a.b）。
    ///
    /// 实现方式：以 token 为键，为每个用户懒加载一个独立的 <see cref="LogFileAnalyzer"/> 实例。
    /// 由于 <see cref="LogFileAnalyzer"/> 内部以私有字段保存「当前目录 / 文件列表 / 分析结果」，
    /// 给每个用户各分配一个实例，即可天然地实现：
    /// <list type="bullet">
    ///   <item>目录隔离：A 改的目录不影响 B；</item>
    ///   <item>分析结果隔离：A 分析过的文件、缓存的结果，B 看不到也改不了。</item>
    /// </list>
    /// </summary>
    public sealed class SessionManager
    {
        private readonly ConcurrentDictionary<string, LogFileAnalyzer> _analyzers = new();

        /// <summary>
        /// 取得（或懒创建）某个 token 对应用户的 Analyzer。token 由 <see cref="TokenStore"/> 校验通过后传入。
        /// </summary>
        public LogFileAnalyzer GetOrCreate(string token)
        {
            // 传入 null 目录：构造一个尚未设置目录的空 Analyzer（HasDirectory == false），
            // 用户随后通过 ChangeDirectory 选择自己的日志目录。
            return _analyzers.GetOrAdd(token, _ => new LogFileAnalyzer(null));
        }
    }
}
