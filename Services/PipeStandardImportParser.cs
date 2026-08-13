using GB_NewCadPlus_IV.UploadApi.Models;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 管子解析器骨架。真实样表确认后，在此实现 Sheet、表头别名、单位和校验规则。
/// </summary>
public sealed class PipeStandardImportParser : IStandardImportParser
{
    public string FamilyCode => "PIPE";
    public string FileType => "XLSX";

    public Task<IReadOnlyList<StandardImportRowDto>> ParseAsync(
        Stream source,
        StandardSeriesDto series,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(series);
        throw new InvalidDataException(
            "管子规范模板已建立，但尚未绑定实际 Excel 解析器。请提供一份真实管子规范样表后配置列和校验规则。");
    }
}
