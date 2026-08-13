namespace GB_NewCadPlus_IV.UploadApi.Models;

/// <summary>
/// 管子规范专业扩展。实际字段以导入模板和规范样表为准。
/// </summary>
public sealed class PipeStandardExtensionDto
{
    public decimal? OuterDiameter { get; init; }
    public decimal? WallThickness { get; init; }
    public string Schedule { get; init; } = string.Empty;
    public decimal? UnitWeight { get; init; }
    public decimal? StandardLength { get; init; }
}

/// <summary>
/// 管子模板建议使用的字段编码，避免客户端和服务器手写字符串不一致。
/// </summary>
public static class PipeStandardFieldCodes
{
    public const string OuterDiameter = "OuterDiameter";
    public const string WallThickness = "WallThickness";
    public const string Schedule = "Schedule";
    public const string UnitWeight = "UnitWeight";
    public const string StandardLength = "StandardLength";
}
