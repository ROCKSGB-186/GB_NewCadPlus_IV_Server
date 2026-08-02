using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;

namespace GB_NewCadPlus_IV.UploadApi.Services;

public sealed class GraphicQueryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GraphicQueryService> _logger;

    public GraphicQueryService(IConfiguration configuration, ILogger<GraphicQueryService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GraphicListResponse> GetByCategoryAsync(int categoryId, string categoryType, CancellationToken cancellationToken = default)
    {
        if (categoryId <= 0)
            throw new ArgumentOutOfRangeException(nameof(categoryId), "分类 ID 必须大于 0。");

        categoryType = (categoryType ?? string.Empty).Trim().ToLowerInvariant();
        if (categoryType is not ("main" or "sub"))
            throw new ArgumentException("categoryType 必须是 main 或 sub。", nameof(categoryType));

        string databaseType = GetDatabaseType();
        try
        {
            var files = databaseType == "DM"
                ? await QueryDmAsync(categoryId, categoryType, cancellationToken).ConfigureAwait(false)
                : await QueryMySqlAsync(categoryId, categoryType, cancellationToken).ConfigureAwait(false);

            return new GraphicListResponse
            {
                Success = true,
                Message = "文件查询成功",
                Files = files
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按分类查询文件失败。CategoryId={CategoryId}, CategoryType={CategoryType}, DatabaseType={DatabaseType}", categoryId, categoryType, databaseType);
            throw;
        }
    }

    private async Task<IReadOnlyList<GraphicDto>> QueryMySqlAsync(int categoryId, string categoryType, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT id AS Id, category_id AS CategoryId, category_type AS CategoryType,
                   file_attribute_id AS FileAttributeId, file_name AS FileName,
                   file_stored_name AS FileStoredName, display_name AS DisplayName,
                   file_type AS FileType, file_hash AS FileHash, block_name AS BlockName,
                   layer_name AS LayerName, color_index AS ColorIndex, scale AS Scale,
                   file_path AS FilePath, preview_image_name AS PreviewImageName,
                   preview_image_path AS PreviewImagePath, file_size AS FileSize,
                   is_preview AS IsPreview, version AS Version, description AS Description,
                   is_active AS IsActive, created_by AS CreatedBy, title AS Title,
                   keywords AS Keywords, is_public AS IsPublic, updated_by AS UpdatedBy,
                   last_accessed_at AS LastAccessedAt, created_at AS CreatedAt,
                   updated_at AS UpdatedAt
            FROM cad_file_storage
            WHERE category_id = @CategoryId AND category_type = @CategoryType
            ORDER BY created_at DESC";

        await using var connection = new MySqlConnection(GetConnectionString("MySQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await connection.QueryAsync<GraphicDto>(new CommandDefinition(sql, new { CategoryId = categoryId, CategoryType = categoryType }, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
    }

    private async Task<IReadOnlyList<GraphicDto>> QueryDmAsync(int categoryId, string categoryType, CancellationToken cancellationToken)
    {
        string schema = GetSchemaName();
        string sql = $@"
            SELECT id AS Id, category_id AS CategoryId, category_type AS CategoryType,
                   file_attribute_id AS FileAttributeId, file_name AS FileName,
                   file_stored_name AS FileStoredName, display_name AS DisplayName,
                   file_type AS FileType, file_hash AS FileHash, block_name AS BlockName,
                   layer_name AS LayerName, color_index AS ColorIndex, scale AS Scale,
                   file_path AS FilePath, preview_image_name AS PreviewImageName,
                   preview_image_path AS PreviewImagePath, file_size AS FileSize,
                   is_preview AS IsPreview, version AS Version, description AS Description,
                   is_active AS IsActive, created_by AS CreatedBy, title AS Title,
                   keywords AS Keywords, is_public AS IsPublic, updated_by AS UpdatedBy,
                   last_accessed_at AS LastAccessedAt, created_at AS CreatedAt,
                   updated_at AS UpdatedAt
            FROM {schema}.CAD_FILE_STORAGE
            WHERE category_id = :CategoryId AND category_type = :CategoryType
            ORDER BY created_at DESC";

        await using var connection = new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await connection.QueryAsync<GraphicDto>(new CommandDefinition(sql, new { CategoryId = categoryId, CategoryType = categoryType }, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
    }

    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";

    private string GetSchemaName()
    {
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim();
        if (string.IsNullOrWhiteSpace(schema) || !schema.All(c => char.IsLetterOrDigit(c) || c == '_'))
            throw new InvalidOperationException("Database:Schema 配置无效。");
        return schema.ToUpperInvariant();
    }

    private string GetConnectionString(string databaseType)
    {
        string connectionString = (_configuration["Database:ConnectionString"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = (_configuration.GetConnectionString(databaseType == "MYSQL" ? "MySQL" : "DM") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"缺少 {databaseType} 数据库连接字符串配置。");
        return connectionString;
    }
}
