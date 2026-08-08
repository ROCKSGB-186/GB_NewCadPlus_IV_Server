using System.Security.Cryptography;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 规范附件本地文件存储实现。
/// 文件名使用随机值，避免用户文件名造成路径穿越或同名覆盖。
/// </summary>
public sealed class LocalStandardFileStorage : IStandardFileStorage
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalStandardFileStorage> _logger;

    public LocalStandardFileStorage(
        IConfiguration configuration,
        ILogger<LocalStandardFileStorage> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StandardStoredFileResult> SaveAsync(
        Stream content,
        string originalFileName,
        long versionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (versionId <= 0) throw new ArgumentException("版本 ID 必须大于 0。", nameof(versionId));

        string extension = Path.GetExtension(Path.GetFileName(originalFileName ?? string.Empty)).ToLowerInvariant();
        string storedFileName = $"{Guid.NewGuid():N}{extension}";
        string relativePath = Path.Combine($"version_{versionId}", storedFileName);
        string fullPath = GetSafeFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (content.CanSeek) content.Position = 0;
        await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        FileInfo fileInfo = new(fullPath);
        string sha256;
        await using (FileStream input = File.OpenRead(fullPath))
        {
            byte[] hash = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
            sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        }

        _logger.LogInformation(
            "规范附件落盘完成：VersionId={VersionId}, Extension={Extension}, Size={FileSize}, Sha256Prefix={Sha256Prefix}",
            versionId,
            extension,
            fileInfo.Length,
            sha256[..8]);

        return new StandardStoredFileResult
        {
            StoredFileName = storedFileName,
            RelativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            Extension = extension,
            FileSize = fileInfo.Length,
            Sha256 = sha256
        };
    }

    public Task<Stream?> OpenReadAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = GetSafeFullPath(relativePath);
        if (!File.Exists(fullPath)) return Task.FromResult<Stream?>(null);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    private string GetSafeFullPath(string relativePath)
    {
        string root = _configuration["Storage:StandardRoot"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, "StandardFiles");
        }

        string rootFullPath = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(rootFullPath, relativePath ?? string.Empty));
        string rootPrefix = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("规范附件路径不合法。");
        }

        return fullPath;
    }
}
