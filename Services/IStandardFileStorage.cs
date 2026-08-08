namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 规范附件存储抽象。
/// 数据库只保存元数据，实际文件由该抽象负责落盘和读取。
/// </summary>
public interface IStandardFileStorage
{
    /// <summary>
    /// 将上传文件保存到指定版本目录，并返回相对路径、存储文件名和哈希值。
    /// </summary>
    Task<StandardStoredFileResult> SaveAsync(
        Stream content,
        string originalFileName,
        long versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 打开已保存的规范附件。
    /// </summary>
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// 文件保存结果。
/// </summary>
public sealed class StandardStoredFileResult
{
    public string StoredFileName { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}
