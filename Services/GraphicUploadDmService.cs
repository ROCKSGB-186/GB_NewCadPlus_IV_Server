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
    /// <summary>
    /// 分类 ID，关联到达梦库中的 CAD_FILE_CATEGORY.CATEGORY_ID，用于区分不同类型的文件（如块、图块参照等）。
    /// </summary>
    public int CategoryId { get; set; }
    /// <summary>
    /// 分类类型字符串
    /// </summary>

    public string CategoryType { get; set; } = string.Empty;
    /// <summary>
    /// 文件名，包含扩展名，例如 "example.dwg"，仅用于展示和记录，不涉及文件存储路径。
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>
    /// 服务器中存储的文件名，通常是一个唯一标识符（如 UUID）加上原扩展名，例如 "a1b2c3d4-e5f6-7890-abcd-1234567890ef.dwg"，用于实际文件存储和访问。
    /// </summary>
    public string FileStoredName { get; set; } = string.Empty;
    /// <summary>
    /// 显示名称，供前端展示使用，可以与 FileName 相同或不同，例如 "电气符号块.dwg"。这个字段主要用于用户界面显示，而 FileName 则是原始文件名，两者的区分可以满足不同的展示需求。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>
    /// 文件类型，通常是文件扩展名（不带点），例如 "dwg"、"dxf"、"rvt" 等，用于快速识别文件格式和后续处理逻辑。这个字段可以从 FileName 或 FileStoredName 中提取，但也允许调用方直接传入以避免重复解析。
    /// </summary>
    public string FileType { get; set; } = string.Empty;
    /// <summary>
    /// 文件哈希值，使用 SHA-256 等算法计算得到的十六进制字符串，用于文件唯一性校验和重复上传检测。
    /// </summary>
    public string FileHash { get; set; } = string.Empty;
    /// <summary>
    /// 块名，针对 CAD 文件中特定的块定义，例如 "电气符号块"、"门窗块" 等，用于更细粒度的分类和属性关联。这个字段在处理图块参照时尤其重要，可以帮助系统识别和管理不同类型的块资源。
    /// </summary>
    public string BlockName { get; set; } = string.Empty;
    /// <summary>
    /// 图层名，针对 CAD 文件中的图层信息，例如 "0"、"电气"、"建筑" 等，用于更细粒度的分类和属性关联。虽然不是所有上传的文件都必须包含图层信息，但对于 CAD 文件来说，图层是一个重要的属性，可以帮助系统更好地组织和管理资源。
    /// </summary>
    public string LayerName { get; set; } = "0";
    /// <summary>
    /// 颜色索引，针对 CAD 文件中的颜色信息，通常是一个整数值，例如 256 代表 BYLAYER（按图层），0-255 代表具体颜色索引。
    /// </summary>
    public int ColorIndex { get; set; } = 7;
    /// <summary>
    /// 文件路径
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    /// <summary>
    /// 预览图文件名，包含扩展名，例如 "example_preview.png"，仅用于展示和记录，不涉及文件存储路径。这个字段是可选的，如果上传过程中生成了预览图，可以将其文件名传入；如果没有预览图，则可以留空或传 null。
    /// </summary>
    public string? PreviewImageName { get; set; }
    /// <summary>
    /// 预览图路径，服务器中存储的预览图文件路径，例如 "/previews/a1b2c3d4-e5f6-7890-abcd-1234567890ef.png"，用于实际预览图存储和访问。这个字段是可选的，如果上传过程中生成了预览图，可以将其路径传入；如果没有预览图，则可以留空或传 null。
    /// </summary>
    public string? PreviewImagePath { get; set; }
    /// <summary>
    /// 文件大小，单位字节，用于记录文件的实际大小，便于后续管理和展示。这个字段可以从上传的文件流中获取，并且在写入数据库时存储为 long 类型，以支持较大的文件尺寸。
    /// </summary>
    public long FileSize { get; set; }
    /// <summary>
    /// 文件描述，供用户输入的文本信息，用于补充说明文件内容、用途或其他相关信息。这个字段是可选的，可以留空或传 null，但如果调用方有相关描述信息，建议传入以丰富文件的元数据，提升用户体验和资源管理效率。
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// 创建人，记录上传文件的用户标识，例如用户名、用户 ID 或邮箱等，用于审计和管理目的。这个字段是必填的，建议调用方传入一个能够唯一标识用户的信息，以便后续追踪和权限管理。
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;
    /// <summary>
    /// 属性 JSON 字符串，包含文件的自定义属性信息，例如块属性、图层属性等，格式为 JSON 对象字符串，例如 {"属性1":"值1","属性2":"值2"}。这个字段是可选的，可以留空或传 null，但如果调用方有相关属性信息，建议传入以 JSON 格式组织的字符串，以便后续解析和使用。
    /// </summary>
    public string? AttributesJson { get; set; }
}

/// <summary>
/// 上传写库结果对象。
/// </summary>
public sealed class UploadWriteResult
{
    /// <summary>
    /// 构造函数。更新写入结果对象，包含 storageId 和 attrId 两个属性，分别对应 cad_file_storage 表的主键和 cad_block_attributes_json 表的主键。这两个 ID 是在 SaveUploadAsync 方法中成功写入数据库后获取到的，用于后续操作和关联查询。
    /// </summary>
    /// <param name="storageId"> 存储 ID</param>
    /// <param name="attrId"> 属性 ID</param>
    public UploadWriteResult(int storageId, int attrId)
    {
        StorageId = storageId;
        AttrId = attrId;
    }
    /// <summary>
    /// 存储 ID，对应 cad_file_storage 表的主键，用于唯一标识上传的文件记录。这个 ID 是在 SaveUploadAsync 方法中成功写入 cad_file_storage 表后获取到的，通常通过查询业务唯一键（如 file_stored_name）来回查获得，以避免达梦数据库 @@IDENTITY 返回 0 的问题。StorageId 是后续关联查询和更新操作的重要依据，例如在写入 cad_block_attributes_json 表时需要使用 StorageId 作为外键关联。
    /// </summary>
    public int StorageId { get; }
    /// <summary>
    /// 属性 ID，对应 cad_block_attributes_json 表的主键，用于唯一标识上传文件的属性记录。这个 ID 是在 SaveUploadAsync 方法中成功写入 cad_block_attributes_json 表后获取到的，通常通过查询业务唯一键（如 file_id 和 config_name）来回查获得。AttrId 是后续关联查询和更新操作的重要依据，例如在更新 cad_file_storage 表时需要使用 AttrId 来回写 file_attribute_id 字段，实现两表之间的关联。
    /// </summary>
    public int AttrId { get; }
}
