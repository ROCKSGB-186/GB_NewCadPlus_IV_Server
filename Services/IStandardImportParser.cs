using GB_NewCadPlus_IV.UploadApi.Models;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 专业规范导入解析器。解析器只负责将文件转换为统一的预览行，不负责数据库写入。
/// </summary>
public interface IStandardImportParser
{
    string FamilyCode { get; }
    string FileType { get; }
    Task<IReadOnlyList<StandardImportRowDto>> ParseAsync(
        Stream source,
        StandardSeriesDto series,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 规范导入解析器注册表，为后续增加管子、管件、阀门解析器提供统一入口。
/// </summary>
public sealed class StandardImportParserRegistry
{
    private readonly IReadOnlyDictionary<string, IStandardImportParser> _parsers;

    public StandardImportParserRegistry(IEnumerable<IStandardImportParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        _parsers = parsers
            .GroupBy(parser => parser.FamilyCode.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    public IStandardImportParser Get(string familyCode)
    {
        string normalizedCode = (familyCode ?? string.Empty).Trim().ToUpperInvariant();
        if (_parsers.TryGetValue(normalizedCode, out IStandardImportParser? parser))
            return parser;

        throw new InvalidDataException($"暂未配置规范类型 {normalizedCode} 的导入模板或解析器。");
    }
}
