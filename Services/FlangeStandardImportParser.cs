using GB_NewCadPlus_IV.UploadApi.Models;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 法兰解析器注册占位。当前法兰解析逻辑仍由 StandardImportService 执行，
/// 该类型用于建立专业解析器边界，待下一步迁移后接管现有实现。
/// </summary>
public sealed class FlangeStandardImportParser : IStandardImportParser
{
    public string FamilyCode => "FLANGE";
    public string FileType => "XLSX";

    public Task<IReadOnlyList<StandardImportRowDto>> ParseAsync(
        Stream source,
        StandardSeriesDto series,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "法兰解析器尚未从 StandardImportService 迁移；现有法兰导入入口仍保持兼容。 ");
    }
}
