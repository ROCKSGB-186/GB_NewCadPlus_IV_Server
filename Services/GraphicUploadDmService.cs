using Dapper;
using Dm;
using System.Data;
using System.Text.Json;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 上传成功后负责将文件元数据与属性 JSON 写入达梦数据库。
/// </summary>
public class GraphicUploadDmService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GraphicUploadDmService> _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    public GraphicUploadDmService(IConfiguration configuration, ILogger<GraphicUploadDmService> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 将上传结果写入 cad_file_storage 与 cad_block_attributes_json，并回写 file_attribute_id。
    /// </summary>
    public async Task<UploadWriteResult> SaveUploadAsync(UploadWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string connectionString = (_configuration["Database:ConnectionString"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("未配置 Database:ConnectionString，无法写入达梦数据库。");
        }

        string normalizedAttributesJson = NormalizeAttributesJson(request.AttributesJson);

        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            DateTime now = DateTime.Now;

            const string insertStorageSql = @"
INSERT INTO CAD_SW_LIBRARY.CAD_FILE_STORAGE
(
    category_id,
    category_type,
    file_name,
    file_stored_name,
    display_name,
    file_type,
    file_hash,
    block_name,
    layer_name,
    color_index,
    file_path,
    preview_image_name,
    preview_image_path,
    file_size,
    is_preview,
    version,
    description,
    is_active,
    created_by,
    is_public,
    scale,
    created_at,
    updated_at
)
VALUES
(
    :CategoryId,
    :CategoryType,
    :FileName,
    :FileStoredName,
    :DisplayName,
    :FileType,
    :FileHash,
    :BlockName,
    :LayerName,
    :ColorIndex,
    :FilePath,
    :PreviewImageName,
    :PreviewImagePath,
    :FileSize,
    :IsPreview,
    :Version,
    :Description,
    :IsActive,
    :CreatedBy,
    :IsPublic,
    :Scale,
    :CreatedAt,
    :UpdatedAt
)";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    insertStorageSql,
                    new
                    {
                        request.CategoryId,
                        request.CategoryType,
                        request.FileName,
                        request.FileStoredName,
                        request.DisplayName,
                        request.FileType,
                        request.FileHash,
                        request.BlockName,
                        request.LayerName,
                        request.ColorIndex,
                        request.FilePath,
                        request.PreviewImageName,
                        request.PreviewImagePath,
                        request.FileSize,
                        IsPreview = string.IsNullOrWhiteSpace(request.PreviewImagePath) ? 0 : 1,
                        Version = 1,
                        request.Description,
                        IsActive = 1,
                        request.CreatedBy,
                        IsPublic = 1,
                        Scale = 1.0,
                        CreatedAt = now,
                        UpdatedAt = now
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            // 优先通过“业务唯一键”回查刚插入记录，避免达梦环境下 @@IDENTITY 返回 0 的问题。
            string storageIdColumn = await ResolveIdColumnNameAsync(
                connection,
                transaction,
                "CAD_SW_LIBRARY",
                "CAD_FILE_STORAGE",
                new[] { "ID", "FILE_ID", "STORAGE_ID" },
                cancellationToken).ConfigureAwait(false);

            string selectStorageIdSql = $@"
SELECT {storageIdColumn}
FROM CAD_SW_LIBRARY.CAD_FILE_STORAGE
WHERE file_stored_name = :FileStoredName
ORDER BY created_at DESC
FETCH FIRST 1 ROWS ONLY";

            int storageId = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    selectStorageIdSql,
                    new { request.FileStoredName },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (storageId <= 0)
            {
                throw new InvalidOperationException($"写入 cad_file_storage 后未获取到有效 storageId。fileStoredName={request.FileStoredName}");
            }

            const string insertAttrSql = @"
INSERT INTO CAD_SW_LIBRARY.CAD_BLOCK_ATTRIBUTES_JSON
(
    file_id,
    config_name,
    attributes_json,
    created_at
)
VALUES
(
    :FileId,
    :ConfigName,
    :AttributesJson,
    :CreatedAt
)";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    insertAttrSql,
                    new
                    {
                        FileId = storageId,
                        ConfigName = "default",
                        AttributesJson = normalizedAttributesJson,
                        CreatedAt = now
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            string attrIdColumn = await ResolveIdColumnNameAsync(
                connection,
                transaction,
                "CAD_SW_LIBRARY",
                "CAD_BLOCK_ATTRIBUTES_JSON",
                new[] { "ID", "ATTR_ID", "ATTRIBUTE_ID" },
                cancellationToken).ConfigureAwait(false);

            string selectAttrIdSql = $@"
SELECT {attrIdColumn}
FROM CAD_SW_LIBRARY.CAD_BLOCK_ATTRIBUTES_JSON
WHERE file_id = :FileId
  AND config_name = :ConfigName
ORDER BY created_at DESC
FETCH FIRST 1 ROWS ONLY";

            int attrId = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    selectAttrIdSql,
                    new
                    {
                        FileId = storageId,
                        ConfigName = "default"
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (attrId <= 0)
            {
                throw new InvalidOperationException($"写入 cad_block_attributes_json 后未获取到有效 attrId。fileId={storageId}");
            }

            string updateStorageSql = $"UPDATE CAD_SW_LIBRARY.CAD_FILE_STORAGE SET file_attribute_id = :AttrId, updated_at = :UpdatedAt WHERE {storageIdColumn} = :StorageId";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    updateStorageSql,
                    new
                    {
                        AttrId = attrId,
                        UpdatedAt = now,
                        StorageId = storageId
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new UploadWriteResult(storageId, attrId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "达梦写入上传记录失败。fileStoredName={FileStoredName}, fileHash={FileHash}", request.FileStoredName, request.FileHash);
            throw;
        }
    }

    /// <summary>
    /// 规范化属性 JSON，避免调用方传入非法片段导致入库失败。
    /// </summary>
    private static string NormalizeAttributesJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "{}";
        }

        string trimmed = raw.Trim();

        // Swagger 表单中常见传入片段："键":"值"，这里自动补大括号。
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            trimmed = "{" + trimmed + "}";
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(trimmed);
            return trimmed;
        }
        catch
        {
            // 兜底写入 raw 字段，保证 JSON 一定合法。
            return JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["raw"] = raw
            });
        }
    }

    /// <summary>
    /// 按候选名解析目标表的主键列名，优先命中候选；否则回退第一列（并记录日志）。
    /// </summary>
    private async Task<string> ResolveIdColumnNameAsync(
        DmConnection connection,
        IDbTransaction transaction,
        string owner,
        string tableName,
        string[] preferredColumns,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT COLUMN_NAME
FROM ALL_TAB_COLUMNS
WHERE OWNER = :Owner
  AND TABLE_NAME = :TableName";

        var columns = (await connection.QueryAsync<string>(
            new CommandDefinition(
                sql,
                new
                {
                    Owner = owner.ToUpperInvariant(),
                    TableName = tableName.ToUpperInvariant()
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"未读取到列信息：{owner}.{tableName}");
        }

        foreach (var candidate in preferredColumns)
        {
            var hit = columns.FirstOrDefault(c => string.Equals(c, candidate, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(hit))
            {
                return hit;
            }
        }

        string fallback = columns[0];
        _logger.LogWarning("未命中首选主键列，回退使用首列。table={Table}, fallbackColumn={Column}", $"{owner}.{tableName}", fallback);
        return fallback;
    }
}

/// <summary>
/// 上传写库请求对象。
/// </summary>
public sealed class UploadWriteRequest
{
    public int CategoryId { get; set; }

    public string CategoryType { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string FileStoredName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string FileType { get; set; } = string.Empty;

    public string FileHash { get; set; } = string.Empty;

    public string BlockName { get; set; } = string.Empty;

    public string LayerName { get; set; } = "0";

    public int ColorIndex { get; set; } = 256;

    public string FilePath { get; set; } = string.Empty;

    public string? PreviewImageName { get; set; }

    public string? PreviewImagePath { get; set; }

    public long FileSize { get; set; }

    public string? Description { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string? AttributesJson { get; set; }
}

/// <summary>
/// 上传写库结果对象。
/// </summary>
public sealed class UploadWriteResult
{
    public UploadWriteResult(int storageId, int attrId)
    {
        StorageId = storageId;
        AttrId = attrId;
    }

    public int StorageId { get; }

    public int AttrId { get; }
}
