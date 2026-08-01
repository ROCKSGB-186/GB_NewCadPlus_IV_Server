using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 分类查询服务。
/// 数据库连接和 SQL 只保留在服务器端，客户端通过 HTTP 获取查询结果。
/// </summary>
public sealed class CategoryQueryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CategoryQueryService> _logger;

    public CategoryQueryService(IConfiguration configuration, ILogger<CategoryQueryService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 查询全部主分类和子分类。
    /// </summary>
    public async Task<CategoryTreeResponse> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();

        try
        {
            if (databaseType == "DM")
            {
                return await QueryDmAsync(schema, cancellationToken).ConfigureAwait(false);
            }

            return await QueryMySqlAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询分类树失败。DatabaseType={DatabaseType}, Schema={Schema}", databaseType, schema);
            throw;
        }
    }

    /// <summary>
    /// 查询 MySQL 分类数据。
    /// </summary>
    private async Task<CategoryTreeResponse> QueryMySqlAsync(CancellationToken cancellationToken)
    {
        string connectionString = GetConnectionString("MySQL");
        const string categorySql = @"
            SELECT
                id AS Id,
                name AS Name,
                display_name AS DisplayName,
                subcategory_ids AS SubcategoryIds,
                sort_order AS SortOrder,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM cad_categories
            ORDER BY sort_order, id";
        const string subcategorySql = @"
            SELECT
                id AS Id,
                parent_id AS ParentId,
                name AS Name,
                display_name AS DisplayName,
                sort_order AS SortOrder,
                level AS Level,
                subcategory_ids AS SubcategoryIds,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM cad_subcategories
            ORDER BY parent_id, sort_order, id";

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var categories = (await connection.QueryAsync<CategoryDto>(
            new CommandDefinition(categorySql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
        var subcategories = (await connection.QueryAsync<SubcategoryDto>(
            new CommandDefinition(subcategorySql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();

        return CreateSuccessResponse(categories, subcategories);
    }

    /// <summary>
    /// 查询达梦分类数据。
    /// </summary>
    private async Task<CategoryTreeResponse> QueryDmAsync(string schema, CancellationToken cancellationToken)
    {
        string connectionString = GetConnectionString("DM");
        string categorySql = $@"
            SELECT
                id AS Id,
                name AS Name,
                display_name AS DisplayName,
                subcategory_ids AS SubcategoryIds,
                sort_order AS SortOrder,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM {schema}.CAD_CATEGORIES
            ORDER BY sort_order, id";
        string subcategorySql = $@"
            SELECT
                id AS Id,
                parent_id AS ParentId,
                name AS Name,
                display_name AS DisplayName,
                sort_order AS SortOrder,
                level AS Level,
                subcategory_ids AS SubcategoryIds,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM {schema}.CAD_SUBCATEGORIES
            ORDER BY parent_id, sort_order, id";

        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var categories = (await connection.QueryAsync<CategoryDto>(
            new CommandDefinition(categorySql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
        var subcategories = (await connection.QueryAsync<SubcategoryDto>(
            new CommandDefinition(subcategorySql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();

        return CreateSuccessResponse(categories, subcategories);
    }

    /// <summary>
    /// 创建统一响应，并防止历史 NULL 值影响客户端树构建。
    /// </summary>
    private static CategoryTreeResponse CreateSuccessResponse(
        List<CategoryDto> categories,
        List<SubcategoryDto> subcategories)
    {
        return new CategoryTreeResponse
        {
            Success = true,
            Message = "分类查询成功",
            Categories = categories,
            Subcategories = subcategories
        };
    }

    /// <summary>
    /// 读取并规范化数据库类型。
    /// </summary>
    private string GetDatabaseType()
    {
        string value = (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant();
        return value == "MYSQL" ? "MYSQL" : "DM";
    }

    /// <summary>
    /// 读取并校验达梦 Schema，避免把任意输入拼接到 SQL 中。
    /// </summary>
    private string GetSchemaName()
    {
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim();
        if (string.IsNullOrWhiteSpace(schema) || !schema.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            throw new InvalidOperationException("Database:Schema 配置无效。");
        }

        return schema.ToUpperInvariant();
    }

    /// <summary>
    /// 优先使用通用连接串，否则使用对应数据库的连接串。
    /// </summary>
    private string GetConnectionString(string databaseType)
    {
        string connectionString = (_configuration["Database:ConnectionString"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            string key = databaseType == "MYSQL" ? "MySQL" : "DM";
            connectionString = (_configuration.GetConnectionString(key) ?? string.Empty).Trim();
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"缺少 {databaseType} 数据库连接字符串配置。");
        }

        return connectionString;
    }
}
