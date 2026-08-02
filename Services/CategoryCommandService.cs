using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 分类写入服务。
/// 主分类的新增操作只在服务器端执行，客户端不直接写 MySQL 或达梦。
/// </summary>
public sealed class CategoryCommandService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CategoryCommandService> _logger;

    /// <summary>
    /// 创建分类写入服务。
    /// </summary>
    public CategoryCommandService(
        IConfiguration configuration,
        ILogger<CategoryCommandService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 新增 MySQL 子分类。
    /// </summary>
    private async Task<SubcategoryMutationResponse> AddSubcategoryMySqlAsync(
        int parentId,
        string name,
        string displayName,
        int requestedSortOrder,
        CancellationToken cancellationToken)
    {
        // 创建并打开 MySQL 连接。
        await using var connection = new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 查询父级层级；主分类的层级为 0，子分类从父级层级加 1。
            int level = parentId >= 10000
                ? await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT level FROM cad_subcategories WHERE id = @ParentId",
                    new { ParentId = parentId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false) + 1
                : 1;

            // 按现有项目约定生成不小于 10000 的子分类 ID。
            int id = await GetNextSubcategoryIdAsync(connection, transaction, "cad_subcategories", cancellationToken).ConfigureAwait(false);

            // 排序号为空时按父级计算最大值加一。
            int sortOrder = requestedSortOrder > 0
                ? requestedSortOrder
                : await GetNextParentSortOrderAsync(connection, transaction, "cad_subcategories", parentId, "@", cancellationToken).ConfigureAwait(false);

            // 插入子分类记录。
            const string insertSql = @"
                INSERT INTO cad_subcategories
                    (id, parent_id, name, display_name, sort_order, level, subcategory_ids)
                VALUES
                    (@Id, @ParentId, @Name, @DisplayName, @SortOrder, @Level, @SubcategoryIds)";
            await connection.ExecuteAsync(new CommandDefinition(insertSql, new
            {
                Id = id,
                ParentId = parentId,
                Name = name,
                DisplayName = displayName,
                SortOrder = sortOrder,
                Level = level,
                SubcategoryIds = string.Empty
            }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

            // 在同一事务中追加父级子分类列表。
            await AppendParentSubcategoryIdAsync(connection, transaction, "cad_categories", "cad_subcategories", parentId, id, "@", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return CreateSubcategoryMutationResponse(id, parentId, name, displayName, sortOrder, level);
        }
        catch (Exception ex)
        {
            // 记录具体数据库阶段和异常，便于定位达梦表结构或参数兼容问题。
            _logger.LogError(
                ex,
                "新增 MySQL 子分类失败。ParentId={ParentId}, Name={Name}",
                parentId,
                name);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 新增达梦子分类。
    /// </summary>
    private async Task<SubcategoryMutationResponse> AddSubcategoryDmAsync(
        int parentId,
        string name,
        string displayName,
        int requestedSortOrder,
        CancellationToken cancellationToken)
    {
        // 创建并打开达梦连接。
        string schema = GetSchemaName();
        await using var connection = new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 子分类父级的层级决定新增记录层级。
            int level = parentId >= 10000
                ? await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    $"SELECT LEVEL FROM {schema}.CAD_SUBCATEGORIES WHERE ID = :ParentId",
                    new { ParentId = parentId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false) + 1
                : 1;

            // 按现有项目约定生成不小于 10000 的子分类 ID。
            int id = await GetNextSubcategoryIdAsync(connection, transaction, $"{schema}.CAD_SUBCATEGORIES", cancellationToken).ConfigureAwait(false);

            // 排序号为空时按父级计算最大值加一。
            int sortOrder = requestedSortOrder > 0
                ? requestedSortOrder
                : await GetNextParentSortOrderAsync(connection, transaction, $"{schema}.CAD_SUBCATEGORIES", parentId, ":", cancellationToken).ConfigureAwait(false);

            // 插入达梦子分类记录。
            string insertSql = $@"
                INSERT INTO {schema}.CAD_SUBCATEGORIES
                    (ID, PARENT_ID, NAME, DISPLAY_NAME, SORT_ORDER, LEVEL, SUBCATEGORY_IDS)
                VALUES
                    (:Id, :ParentId, :Name, :DisplayName, :SortOrder, :Level, :SubcategoryIds)";
            await connection.ExecuteAsync(new CommandDefinition(insertSql, new
            {
                Id = id,
                ParentId = parentId,
                Name = name,
                DisplayName = displayName,
                SortOrder = sortOrder,
                Level = level,
                SubcategoryIds = string.Empty
            }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

            // 在同一达梦事务中追加父级子分类列表。
            await AppendParentSubcategoryIdAsync(connection, transaction, $"{schema}.CAD_CATEGORIES", $"{schema}.CAD_SUBCATEGORIES", parentId, id, ":", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return CreateSubcategoryMutationResponse(id, parentId, name, displayName, sortOrder, level);
        }
        catch (Exception ex)
        {
            // 记录具体数据库阶段和异常，避免客户端只能看到笼统的 HTTP 500。
            _logger.LogError(
                ex,
                "新增达梦子分类失败。Schema={Schema}, ParentId={ParentId}, Name={Name}",
                schema,
                parentId,
                name);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 生成下一个子分类 ID。
    /// </summary>
    private static async Task<int> GetNextSubcategoryIdAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        // 子分类 ID 必须不小于 10000，兼容客户端已有层级判断。
        int maxId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COALESCE(MAX(id), 9999) FROM {tableName}",
            transaction: transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return Math.Max(10000, maxId + 1);
    }

    /// <summary>
    /// 生成父级下一个排序号。
    /// </summary>
    private static async Task<int> GetNextParentSortOrderAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName,
        int parentId,
        string parameterPrefix,
        CancellationToken cancellationToken)
    {
        // 只统计同一父级下的子分类排序号。
        int maxSortOrder = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COALESCE(MAX(sort_order), 0) FROM {tableName} WHERE parent_id = {parameterPrefix}ParentId",
            new { ParentId = parentId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return Math.Max(1, maxSortOrder + 1);
    }

    /// <summary>
    /// 把新增子分类 ID 追加到父级的子分类 ID 列表。
    /// </summary>
    private static async Task AppendParentSubcategoryIdAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string categoryTableName,
        string subcategoryTableName,
        int parentId,
        int childId,
        string parameterPrefix,
        CancellationToken cancellationToken)
    {
        // 根据父级 ID 判断父级属于主分类表还是子分类表。
        string tableName = parentId >= 10000 ? subcategoryTableName : categoryTableName;

        // 读取当前父级子分类列表。
        string currentIds = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            $"SELECT COALESCE(subcategory_ids, '') FROM {tableName} WHERE id = {parameterPrefix}ParentId",
            new { ParentId = parentId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false) ?? string.Empty;

        // 清理空白项并避免重复追加。
        List<string> ids = currentIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim())
            .Where(id => id.Length > 0)
            .ToList();
        if (!ids.Contains(childId.ToString(), StringComparer.Ordinal))
        {
            ids.Add(childId.ToString());
        }

        // 使用参数更新父级记录，避免拼接用户输入。
        await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {tableName} SET subcategory_ids = {parameterPrefix}SubcategoryIds WHERE id = {parameterPrefix}ParentId",
            new { ParentId = parentId, SubcategoryIds = string.Join(",", ids) },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// 创建新增子分类响应。
    /// </summary>
    private static SubcategoryMutationResponse CreateSubcategoryMutationResponse(
        int id,
        int parentId,
        string name,
        string displayName,
        int sortOrder,
        int level)
    {
        // 返回客户端刷新分类树所需的子分类数据。
        return new SubcategoryMutationResponse
        {
            Success = true,
            Message = "子分类新增成功",
            Subcategory = new SubcategoryDto
            {
                Id = id,
                ParentId = parentId,
                Name = name,
                DisplayName = displayName,
                SortOrder = sortOrder,
                Level = level,
                SubcategoryIds = string.Empty,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            }
        };
    }

    /// <summary>
    /// 新增子分类，并在同一事务中更新父级子分类 ID 列表。
    /// </summary>
    public async Task<SubcategoryMutationResponse> AddSubcategoryAsync(
        int parentId,
        AddSubcategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        // 校验父级 ID，主分类和子分类 ID 都必须是正数。
        if (parentId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentId), "父分类ID必须大于0。");
        }

        // 校验请求对象。
        ArgumentNullException.ThrowIfNull(request);

        // 去除名称首尾空格。
        string name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("子分类名称不能为空。", nameof(request));
        }

        // 显示名称为空时使用内部名称。
        string displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? name
            : request.DisplayName.Trim();

        // 根据服务端数据库配置选择双数据库实现。
        if (GetDatabaseType() == "DM")
        {
            return await AddSubcategoryDmAsync(
                parentId,
                name,
                displayName,
                request.SortOrder.GetValueOrDefault(),
                cancellationToken).ConfigureAwait(false);
        }

        return await AddSubcategoryMySqlAsync(
            parentId,
            name,
            displayName,
            request.SortOrder.GetValueOrDefault(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新主分类或子分类的名称、显示名称和排序号。
    /// </summary>
    public async Task<CategoryUpdateResponse> UpdateCategoryAsync(
        int categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        // 分类 ID 必须为正数；10000 及以上的 ID 按项目约定属于子分类。
        if (categoryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(categoryId), "分类ID必须大于0。");
        }

        // 校验客户端请求对象。
        ArgumentNullException.ThrowIfNull(request);

        // 清理并校验分类名称。
        string name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("分类名称不能为空。", nameof(request));
        }

        // 显示名称为空时沿用分类名称。
        string displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? name
            : request.DisplayName.Trim();

        // 小于等于零表示保留当前排序号，避免编辑名称时意外重置排序。
        int? sortOrder = request.SortOrder > 0 ? request.SortOrder : null;

        // 根据服务端配置选择 MySQL 或达梦事务实现。
        if (GetDatabaseType() == "DM")
        {
            return await UpdateCategoryDmAsync(
                categoryId,
                name,
                displayName,
                sortOrder,
                cancellationToken).ConfigureAwait(false);
        }

        return await UpdateCategoryMySqlAsync(
            categoryId,
            name,
            displayName,
            sortOrder,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 使用 MySQL 更新主分类或子分类。
    /// </summary>
    private async Task<CategoryUpdateResponse> UpdateCategoryMySqlAsync(
        int categoryId,
        string name,
        string displayName,
        int? sortOrder,
        CancellationToken cancellationToken)
    {
        // 创建 MySQL 连接和事务，保证读取、更新、回读结果一致。
        await using var connection = new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 按 ID 范围选择主分类表或子分类表。
            string tableName = categoryId >= 10000 ? "cad_subcategories" : "cad_categories";
            string idColumn = categoryId >= 10000 ? "id" : "id";

            // 排序号未提供时读取数据库当前值。
            int currentSortOrder = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                $"SELECT sort_order FROM {tableName} WHERE {idColumn} = @Id",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("未找到要更新的分类。");

            // 更新分类基础属性。
            string updateSql = $@"
                UPDATE {tableName}
                SET name = @Name,
                    display_name = @DisplayName,
                    sort_order = @SortOrder,
                    updated_at = CURRENT_TIMESTAMP
                WHERE {idColumn} = @Id";
            await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                new
                {
                    Id = categoryId,
                    Name = name,
                    DisplayName = displayName,
                    SortOrder = sortOrder ?? currentSortOrder
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CreateUpdateResponse(categoryId, name, displayName, sortOrder ?? currentSortOrder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新 MySQL 分类失败。CategoryId={CategoryId}", categoryId);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 使用达梦更新主分类或子分类。
    /// </summary>
    private async Task<CategoryUpdateResponse> UpdateCategoryDmAsync(
        int categoryId,
        string name,
        string displayName,
        int? sortOrder,
        CancellationToken cancellationToken)
    {
        // 创建达梦连接和事务，所有分类写入由服务器完成。
        string schema = GetSchemaName();
        await using var connection = new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 按 ID 范围选择达梦主分类表或子分类表。
            string tableName = categoryId >= 10000
                ? $"{schema}.CAD_SUBCATEGORIES"
                : $"{schema}.CAD_CATEGORIES";

            // 排序号未提供时读取数据库当前值；达梦参数必须使用冒号。
            int currentSortOrder = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                $"SELECT SORT_ORDER FROM {tableName} WHERE ID = :Id",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("未找到要更新的分类。");

            // 更新达梦分类基础属性。
            string updateSql = $@"
                UPDATE {tableName}
                SET NAME = :Name,
                    DISPLAY_NAME = :DisplayName,
                    SORT_ORDER = :SortOrder,
                    UPDATED_AT = CURRENT_TIMESTAMP
                WHERE ID = :Id";
            await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                new
                {
                    Id = categoryId,
                    Name = name,
                    DisplayName = displayName,
                    SortOrder = sortOrder ?? currentSortOrder
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CreateUpdateResponse(categoryId, name, displayName, sortOrder ?? currentSortOrder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新达梦分类失败。Schema={Schema}, CategoryId={CategoryId}", schema, categoryId);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 创建分类更新成功响应。
    /// </summary>
    private static CategoryUpdateResponse CreateUpdateResponse(
        int categoryId,
        string name,
        string displayName,
        int sortOrder)
    {
        // 返回服务器最终采用的值，客户端可据此刷新界面。
        return new CategoryUpdateResponse
        {
            Success = true,
            Message = "分类更新成功",
            UpdatedId = categoryId,
            Name = name,
            DisplayName = displayName,
            SortOrder = sortOrder
        };
    }

    /// <summary>
    /// 删除子分类，并在同一事务中清理父级子分类 ID 列表。
    /// </summary>
    public async Task<CategoryDeleteResponse> DeleteSubcategoryAsync(
        int subcategoryId,
        CancellationToken cancellationToken = default)
    {
        // 按现有项目约定，子分类 ID 必须大于等于 10000。
        if (subcategoryId < 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(subcategoryId), "子分类ID必须大于等于10000。");
        }

        // 根据服务器数据库配置选择对应实现。
        if (GetDatabaseType() == "DM")
        {
            return await DeleteSubcategoryDmAsync(subcategoryId, cancellationToken).ConfigureAwait(false);
        }

        return await DeleteSubcategoryMySqlAsync(subcategoryId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除没有子分类的主分类。
    /// </summary>
    public async Task<CategoryDeleteResponse> DeleteCategoryAsync(
        int categoryId,
        CancellationToken cancellationToken = default)
    {
        // 主分类 ID 必须是正数且小于 10000。
        if (categoryId <= 0 || categoryId >= 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(categoryId), "主分类ID必须大于0且小于10000。");
        }

        // 根据数据库配置选择事务实现。
        if (GetDatabaseType() == "DM")
        {
            return await DeleteCategoryDmAsync(categoryId, cancellationToken).ConfigureAwait(false);
        }

        return await DeleteCategoryMySqlAsync(categoryId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 使用 MySQL 删除主分类。
    /// </summary>
    private async Task<CategoryDeleteResponse> DeleteCategoryMySqlAsync(
        int categoryId,
        CancellationToken cancellationToken)
    {
        // 创建 MySQL 连接和事务，保证检查与删除原子完成。
        await using var connection = new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 先统计主分类记录，不能用 SUBCATEGORY_IDS 是否为 NULL 判断记录是否存在。
            int categoryCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM cad_categories WHERE id = @Id",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (categoryCount <= 0)
            {
                throw new KeyNotFoundException("未找到要删除的主分类。");
            }

            // 读取主分类子分类列表；NULL 表示空列表，而不是主分类不存在。
            string subcategoryIds = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT subcategory_ids FROM cad_categories WHERE id = @Id",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
                ?? string.Empty;

            // 同时检查列表字段和实际子分类记录，防止脏数据导致孤儿记录。
            int childCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM cad_subcategories WHERE parent_id = @Id",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (childCount > 0 || !string.IsNullOrWhiteSpace(subcategoryIds))
            {
                throw new InvalidOperationException("该主分类下还有子分类，请先删除所有子分类。");
            }

            // 删除空主分类。
            int deletedRows = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM cad_categories WHERE id = @Id",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (deletedRows <= 0)
            {
                throw new KeyNotFoundException("主分类删除失败，目标记录不存在。");
            }

            // 分类删除后同步处理关联部门：无人员的部门删除，有人员的部门停用但保留历史数据。
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE departments SET is_active = 0 WHERE cad_category_id = @Id AND EXISTS (SELECT 1 FROM users WHERE users.department_id = departments.id)",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM departments WHERE cad_category_id = @Id AND NOT EXISTS (SELECT 1 FROM users WHERE users.department_id = departments.id)",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CreateCategoryDeleteResponse(categoryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除 MySQL 主分类失败。CategoryId={CategoryId}", categoryId);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 使用达梦删除主分类。
    /// </summary>
    private async Task<CategoryDeleteResponse> DeleteCategoryDmAsync(
        int categoryId,
        CancellationToken cancellationToken)
    {
        // 创建达梦连接和事务，所有主分类删除由服务端完成。
        string schema = GetSchemaName();
        await using var connection = new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 先统计主分类记录，不能用 SUBCATEGORY_IDS 是否为 NULL 判断记录是否存在。
            int categoryCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COUNT(1) FROM {schema}.CAD_CATEGORIES WHERE ID = :Id",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (categoryCount <= 0)
            {
                throw new KeyNotFoundException("未找到要删除的主分类。");
            }

            // 读取主分类子分类列表；NULL 表示空列表，而不是主分类不存在。
            string subcategoryIds = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                $"SELECT SUBCATEGORY_IDS FROM {schema}.CAD_CATEGORIES WHERE ID = :Id",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
                ?? string.Empty;

            // 同时检查列表字段和实际子分类记录，防止脏数据导致孤儿记录。
            int childCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COUNT(1) FROM {schema}.CAD_SUBCATEGORIES WHERE PARENT_ID = :Id",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (childCount > 0 || !string.IsNullOrWhiteSpace(subcategoryIds))
            {
                throw new InvalidOperationException("该主分类下还有子分类，请先删除所有子分类。");
            }

            // 删除空主分类。
            int deletedRows = await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {schema}.CAD_CATEGORIES WHERE ID = :Id",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (deletedRows <= 0)
            {
                throw new KeyNotFoundException("主分类删除失败，目标记录不存在。");
            }

            // 分类删除后同步处理关联部门：无人员的部门删除，有人员的部门停用但保留历史数据。
            await connection.ExecuteAsync(new CommandDefinition(
                $"UPDATE {schema}.DEPARTMENTS SET IS_ACTIVE = 0 WHERE CAD_CATEGORY_ID = :Id AND EXISTS (SELECT 1 FROM {schema}.USERS WHERE {schema}.USERS.DEPARTMENT_ID = {schema}.DEPARTMENTS.ID)",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {schema}.DEPARTMENTS WHERE CAD_CATEGORY_ID = :Id AND NOT EXISTS (SELECT 1 FROM {schema}.USERS WHERE {schema}.USERS.DEPARTMENT_ID = {schema}.DEPARTMENTS.ID)",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CreateCategoryDeleteResponse(categoryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除达梦主分类失败。Schema={Schema}, CategoryId={CategoryId}", schema, categoryId);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 创建主分类删除成功响应。
    /// </summary>
    private static CategoryDeleteResponse CreateCategoryDeleteResponse(int categoryId)
    {
        // 使用统一删除响应，客户端据此确认服务器已完成删除。
        return new CategoryDeleteResponse
        {
            Success = true,
            Message = "主分类删除成功",
            DeletedId = categoryId
        };
    }

    /// <summary>
    /// 删除 MySQL 子分类。
    /// </summary>
    private async Task<CategoryDeleteResponse> DeleteSubcategoryMySqlAsync(
        int subcategoryId,
        CancellationToken cancellationToken)
    {
        // 创建 MySQL 连接和事务。
        await using var connection = new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 读取父级 ID，删除时必须同步清理父级列表。
            int? parentId = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT parent_id FROM cad_subcategories WHERE id = @Id",
                new { Id = subcategoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (!parentId.HasValue)
            {
                throw new KeyNotFoundException("未找到要删除的子分类。");
            }

            // 拥有下级子分类时拒绝删除，避免产生孤儿记录。
            int childCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM cad_subcategories WHERE parent_id = @Id",
                new { Id = subcategoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (childCount > 0)
            {
                throw new InvalidOperationException("该子分类下还有下级子分类，不能删除。");
            }

            // 删除目标子分类。
            int deletedRows = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM cad_subcategories WHERE id = @Id",
                new { Id = subcategoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (deletedRows <= 0)
            {
                throw new KeyNotFoundException("子分类删除失败，目标记录不存在。");
            }

            // 在同一事务中从父级列表移除目标 ID。
            await RemoveParentSubcategoryIdAsync(
                connection,
                transaction,
                parentId.Value,
                subcategoryId,
                "cad_categories",
                "cad_subcategories",
                "@",
                cancellationToken).ConfigureAwait(false);

            // 提交删除和父级更新。
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CreateDeleteResponse(subcategoryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除 MySQL 子分类失败。SubcategoryId={SubcategoryId}", subcategoryId);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 删除达梦子分类。
    /// </summary>
    private async Task<CategoryDeleteResponse> DeleteSubcategoryDmAsync(
        int subcategoryId,
        CancellationToken cancellationToken)
    {
        // 创建达梦连接和事务。
        string schema = GetSchemaName();
        await using var connection = new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 读取父级 ID，使用达梦参数格式。
            int? parentId = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                $"SELECT PARENT_ID FROM {schema}.CAD_SUBCATEGORIES WHERE ID = :Id",
                new { Id = subcategoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (!parentId.HasValue)
            {
                throw new KeyNotFoundException("未找到要删除的子分类。");
            }

            // 拥有下级子分类时拒绝删除，避免产生孤儿记录。
            int childCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COUNT(1) FROM {schema}.CAD_SUBCATEGORIES WHERE PARENT_ID = :Id",
                new { Id = subcategoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (childCount > 0)
            {
                throw new InvalidOperationException("该子分类下还有下级子分类，不能删除。");
            }

            // 删除达梦目标子分类。
            int deletedRows = await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {schema}.CAD_SUBCATEGORIES WHERE ID = :Id",
                new { Id = subcategoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (deletedRows <= 0)
            {
                throw new KeyNotFoundException("子分类删除失败，目标记录不存在。");
            }

            // 在同一事务中从父级列表移除目标 ID。
            await RemoveParentSubcategoryIdAsync(
                connection,
                transaction,
                parentId.Value,
                subcategoryId,
                $"{schema}.CAD_CATEGORIES",
                $"{schema}.CAD_SUBCATEGORIES",
                ":",
                cancellationToken).ConfigureAwait(false);

            // 提交达梦事务。
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CreateDeleteResponse(subcategoryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除达梦子分类失败。Schema={Schema}, SubcategoryId={SubcategoryId}", schema, subcategoryId);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 从父级子分类 ID 列表中移除指定 ID。
    /// </summary>
    private static async Task RemoveParentSubcategoryIdAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        int parentId,
        int deletedId,
        string categoryTableName,
        string subcategoryTableName,
        string parameterPrefix,
        CancellationToken cancellationToken)
    {
        // 父级 ID 小于 10000 时是主分类，否则是子分类。
        string tableName = parentId >= 10000 ? subcategoryTableName : categoryTableName;

        // 读取当前父级列表。
        string currentIds = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            $"SELECT COALESCE(SUBCATEGORY_IDS, '') FROM {tableName} WHERE ID = {parameterPrefix}ParentId",
            new { ParentId = parentId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false) ?? string.Empty;

        // 移除目标 ID，同时清理空白项和重复项。
        string newIds = string.Join(",", currentIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim())
            .Where(id => id.Length > 0 && id != deletedId.ToString())
            .Distinct(StringComparer.Ordinal));

        // 更新父级列表。
        await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {tableName} SET SUBCATEGORY_IDS = {parameterPrefix}SubcategoryIds WHERE ID = {parameterPrefix}ParentId",
            new { ParentId = parentId, SubcategoryIds = newIds },
            transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// 创建删除成功响应。
    /// </summary>
    private static CategoryDeleteResponse CreateDeleteResponse(int deletedId)
    {
        // 返回删除的 ID，客户端据此确认服务器删除成功。
        return new CategoryDeleteResponse
        {
            Success = true,
            Message = "子分类删除成功",
            DeletedId = deletedId
        };
    }

    /// <summary>
    /// 新增主分类，并返回新增记录。
    /// </summary>
    public async Task<CategoryMutationResponse> AddCategoryAsync(
        AddCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        // 校验请求对象，防止空请求进入数据库事务。
        ArgumentNullException.ThrowIfNull(request);

        // 去除名称首尾空格，避免出现不可见空格造成重复分类。
        string name = (request.Name ?? string.Empty).Trim();

        // 分类名称是业务必填字段。
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("分类名称不能为空。", nameof(request));
        }

        // 显示名称为空时使用分类名称，保持客户端原有行为。
        string displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? name
            : request.DisplayName.Trim();

        // 排序号小于等于 0 时交给服务器自动计算。
        int requestedSortOrder = request.SortOrder.GetValueOrDefault();

        // 根据服务器配置选择数据库实现。
        string databaseType = GetDatabaseType();

        // 记录本次写入使用的数据库类型，但不记录数据库密码。
        _logger.LogInformation(
            "开始新增主分类。DatabaseType={DatabaseType}, Name={Name}",
            databaseType,
            name);

        // 根据数据库类型进入对应事务实现。
        if (databaseType == "DM")
        {
            return await AddDmAsync(
                name,
                displayName,
                requestedSortOrder,
                cancellationToken).ConfigureAwait(false);
        }

        return await AddMySqlAsync(
            name,
            displayName,
            requestedSortOrder,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 新增 MySQL 主分类。
    /// </summary>
    private async Task<CategoryMutationResponse> AddMySqlAsync(
        string name,
        string displayName,
        int requestedSortOrder,
        CancellationToken cancellationToken)
    {
        // 读取 MySQL 连接字符串。
        string connectionString = GetConnectionString("MYSQL");

        // 创建 MySQL 连接，并确保连接在结束时释放。
        await using var connection = new MySqlConnection(connectionString);

        // 打开数据库连接。
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // 使用事务保证“计算排序号、插入记录、读取记录”属于同一个操作。
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // 如果调用方没有提供排序号，则在当前事务中取最大值加一。
            int sortOrder = requestedSortOrder > 0
                ? requestedSortOrder
                : await GetNextSortOrderAsync(
                    connection,
                    transaction,
                    "cad_categories",
                    cancellationToken).ConfigureAwait(false);

            // 插入主分类；ID 由 MySQL 自增列生成。
            const string insertSql = @"
                INSERT INTO cad_categories
                    (name, display_name, sort_order)
                VALUES
                    (@Name, @DisplayName, @SortOrder)";

            // 执行插入并绑定参数，避免字符串拼接造成 SQL 注入。
            await connection.ExecuteAsync(
                new CommandDefinition(
                    insertSql,
                    new
                    {
                        Name = name,
                        DisplayName = displayName,
                        SortOrder = sortOrder
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            // 读取当前事务刚刚生成的自增 ID。
            long id = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    "SELECT LAST_INSERT_ID()",
                    transaction: transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            // ID 无效时主动抛出异常，让事务回滚。
            if (id <= 0)
            {
                throw new InvalidOperationException("MySQL 新增主分类后未获取到有效 ID。");
            }

            // 提交事务。
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            // 返回统一的新增结果。
            return CreateMutationResponse(
                (int)id,
                name,
                displayName,
                sortOrder);
        }
        catch
        {
            // 任何异常都回滚事务，避免只写入半条数据。
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            // 继续抛出异常，由控制器统一返回 HTTP 500。
            throw;
        }
    }

    /// <summary>
    /// 新增达梦主分类。
    /// </summary>
    private async Task<CategoryMutationResponse> AddDmAsync(
        string name,
        string displayName,
        int requestedSortOrder,
        CancellationToken cancellationToken)
    {
        // 读取达梦连接字符串。
        string connectionString = GetConnectionString("DM");

        // 读取并校验 Schema，防止不安全字符串拼接。
        string schema = GetSchemaName();

        // 创建达梦连接。
        await using var connection = new DmConnection(connectionString);

        // 打开达梦连接。
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // 开启事务，保证达梦新增操作具有原子性。
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // 如果调用方没有提供排序号，则读取当前最大排序号加一。
            int sortOrder = requestedSortOrder > 0
                ? requestedSortOrder
                : await GetNextSortOrderAsync(
                    connection,
                    transaction,
                    $"{schema}.CAD_CATEGORIES",
                    cancellationToken).ConfigureAwait(false);

            // 达梦主键通常由 IDENTITY 生成，因此插入时不主动写入 ID。
            string insertSql = $@"
                INSERT INTO {schema}.CAD_CATEGORIES
                    (NAME, DISPLAY_NAME, SORT_ORDER)
                VALUES
                    (:Name, :DisplayName, :SortOrder)";

            // 执行达梦插入，并使用命名参数绑定数据。
            await connection.ExecuteAsync(
                new CommandDefinition(
                    insertSql,
                    new
                    {
                        Name = name,
                        DisplayName = displayName,
                        SortOrder = sortOrder
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            // 达梦驱动环境中不统一支持 LAST_INSERT_ID，因此按业务字段回查新记录。
            string selectIdSql = $@"
                SELECT ID
                FROM {schema}.CAD_CATEGORIES
                WHERE NAME = :Name
                  AND SORT_ORDER = :SortOrder
                ORDER BY ID DESC
                FETCH FIRST 1 ROWS ONLY";

            // 查询本次新增记录的 ID。
            int id = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    selectIdSql,
                    new
                    {
                        Name = name,
                        SortOrder = sortOrder
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            // 没有查到 ID 时回滚，避免接口返回虚假的成功结果。
            if (id <= 0)
            {
                throw new InvalidOperationException("达梦新增主分类后未回查到有效 ID。");
            }

            // 提交达梦事务。
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            // 返回统一的新增结果。
            return CreateMutationResponse(
                id,
                name,
                displayName,
                sortOrder);
        }
        catch
        {
            // 发生异常时回滚达梦事务。
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            // 继续抛出异常，由控制器统一处理。
            throw;
        }
    }

    /// <summary>
    /// 获取下一个排序号。
    /// </summary>
    private static async Task<int> GetNextSortOrderAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        // 表名只由代码内部传入，不来自客户端请求。
        string sql = $"SELECT COALESCE(MAX(sort_order), 0) + 1 FROM {tableName}";

        // 执行聚合查询并转换为整数。
        int nextSortOrder = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                sql,
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        // 防止数据库异常返回非正数。
        return nextSortOrder > 0 ? nextSortOrder : 1;
    }

    /// <summary>
    /// 创建统一的分类新增响应。
    /// </summary>
    private static CategoryMutationResponse CreateMutationResponse(
        int id,
        string name,
        string displayName,
        int sortOrder)
    {
        // 返回客户端后续刷新或显示所需的最小分类对象。
        return new CategoryMutationResponse
        {
            Success = true,
            Message = "主分类新增成功",
            Category = new CategoryDto
            {
                Id = id,
                Name = name,
                DisplayName = displayName,
                SubcategoryIds = string.Empty,
                SortOrder = sortOrder,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            }
        };
    }

    /// <summary>
    /// 获取数据库类型。
    /// </summary>
    private string GetDatabaseType()
    {
        // 未配置时默认使用达梦，符合当前项目部署环境。
        string value = (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant();

        // 只有明确配置 MYSQL 时才使用 MySQL，否则使用达梦。
        return value == "MYSQL" ? "MYSQL" : "DM";
    }

    /// <summary>
    /// 获取并校验达梦 Schema。
    /// </summary>
    private string GetSchemaName()
    {
        // 读取 Schema 配置，默认使用当前项目 Schema。
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim();

        // Schema 只允许字母、数字和下划线，避免 SQL 标识符注入。
        if (string.IsNullOrWhiteSpace(schema)
            || !schema.All(character => char.IsLetterOrDigit(character) || character == '_'))
        {
            throw new InvalidOperationException("Database:Schema 配置无效。");
        }

        // 达梦未加引号的对象名通常使用大写，统一转成大写。
        return schema.ToUpperInvariant();
    }

    /// <summary>
    /// 获取数据库连接字符串。
    /// </summary>
    private string GetConnectionString(string databaseType)
    {
        // 优先读取通用连接字符串，方便通过环境变量覆盖部署配置。
        string connectionString = (_configuration["Database:ConnectionString"] ?? string.Empty).Trim();

        // 通用连接字符串为空时，回退到对应数据库连接字符串。
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            string key = databaseType == "MYSQL" ? "MySQL" : "DM";
            connectionString = (_configuration.GetConnectionString(key) ?? string.Empty).Trim();
        }

        // 没有连接字符串时直接报错，避免出现难以定位的空连接异常。
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"缺少 {databaseType} 数据库连接字符串配置。");
        }

        // 返回最终连接字符串。
        return connectionString;
    }
}
