using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 规范管理写入服务。
/// 当前阶段由控制器使用 sa、SYSDBA、admin 进行管理员校验；后续可替换为正式认证上下文。
/// </summary>
public sealed class StandardManagementCommandService
{
    private readonly IConfiguration _configuration;
    private readonly IStandardFileStorage _fileStorage;
    private readonly ILogger<StandardManagementCommandService> _logger;

    public StandardManagementCommandService(
        IConfiguration configuration,
        IStandardFileStorage fileStorage,
        ILogger<StandardManagementCommandService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 修改动态规范细分的显示名称，不修改版本号和动态内容。
    /// </summary>
    public async Task RenameVersionAsync(long versionId, string name, string operatorName, CancellationToken cancellationToken = default)
    {
        if (versionId <= 0) throw new ArgumentException("规范版本 ID 必须大于 0。", nameof(versionId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("规范版本名称不能为空。", nameof(name));
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("操作用户名不能为空。", nameof(operatorName));

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENT_VERSIONS" : "standard_document_versions";
        string p = databaseType == "DM" ? ":" : "@";
        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        int affected = await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {table} SET VERSION_LABEL={p}Name,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID={p}Id AND IS_DELETED=0 AND SOURCE_TYPE='DYNAMIC_IMPORT'",
            new { Id = versionId, Name = name.Trim() }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
            throw new KeyNotFoundException("动态规范细分不存在、已删除或不允许重命名。");

        _logger.LogInformation("动态规范细分重命名成功：VersionId={VersionId}, Name={Name}, Operator={OperatorName}", versionId, name.Trim(), operatorName);
    }

    public async Task MoveSeriesAsync(
        long seriesId,
        long categoryId,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0) throw new ArgumentException("规范系列 ID 必须大于 0。", nameof(seriesId));
        if (categoryId <= 0) throw new ArgumentException("目标分类 ID 必须大于 0。", nameof(categoryId));
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("操作用户名不能为空。", nameof(operatorName));

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string seriesTable = databaseType == "DM" ? $"{schema}.STANDARD_SERIES" : "standard_series";
        string categoryTable = databaseType == "DM" ? $"{schema}.STANDARD_CATEGORIES" : "standard_categories";
        string p = databaseType == "DM" ? ":" : "@";

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = connection.BeginTransaction();
        try
        {
            if (!await CategoryExistsAsync(connection, transaction, categoryTable, p, categoryId, cancellationToken).ConfigureAwait(false))
                throw new KeyNotFoundException("目标规范分类不存在或已停用。");

            long? existingSeriesId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                $"SELECT ID FROM {seriesTable} WHERE ID={p}Id AND IS_ACTIVE=1",
                new { Id = seriesId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (!existingSeriesId.HasValue)
                throw new KeyNotFoundException("规范系列不存在或已停用。");

            int affected = await connection.ExecuteAsync(new CommandDefinition(
                $"UPDATE {seriesTable} SET CATEGORY_ID={p}CategoryId,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID={p}Id AND IS_ACTIVE=1",
                new { Id = seriesId, CategoryId = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (affected == 0) throw new KeyNotFoundException("规范系列不存在或已停用。");

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("规范系列归类成功：SeriesId={SeriesId}, CategoryId={CategoryId}, Operator={OperatorName}", seriesId, categoryId, operatorName);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 修改规范系列名称，不修改规范编码和实际规范内容记录。
    /// </summary>
    public async Task RenameSeriesAsync(
        long seriesId,
        string name,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0) throw new ArgumentException("规范系列 ID 必须大于 0。", nameof(seriesId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("规范名称不能为空。", nameof(name));
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("操作用户名不能为空。", nameof(operatorName));

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_SERIES" : "standard_series";
        string parameter = databaseType == "DM" ? ":" : "@";
        string normalizedName = name.Trim();

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        int affected = await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {table} SET SERIES_NAME={parameter}Name,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID={parameter}Id AND IS_ACTIVE=1",
            new { Id = seriesId, Name = normalizedName }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (affected == 0)
            throw new KeyNotFoundException("规范系列不存在或已停用。");

        _logger.LogInformation(
            "规范系列重命名成功：SeriesId={SeriesId}, Name={Name}, Operator={OperatorName}",
            seriesId, normalizedName, operatorName);
    }

    public async Task MoveCategoryAsync(
        long categoryId,
        long? parentId,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        if (categoryId <= 0) throw new ArgumentException("分类 ID 必须大于 0。", nameof(categoryId));
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("操作用户名不能为空。", nameof(operatorName));
        if (parentId == categoryId) throw new InvalidOperationException("规范分类不能移动到自身下面。");

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_CATEGORIES" : "standard_categories";
        string p = databaseType == "DM" ? ":" : "@";

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = connection.BeginTransaction();
        try
        {
            CategoryHierarchyRow? source = await connection.QuerySingleOrDefaultAsync<CategoryHierarchyRow>(new CommandDefinition(
                $"SELECT ID AS Id,PARENT_ID AS ParentId,CODE AS Code,NAME AS Name FROM {table} WHERE ID={p}Id AND IS_ACTIVE=1",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (source == null) throw new KeyNotFoundException("规范分类不存在或已停用。");

            await EnsureParentCategoryAsync(connection, transaction, databaseType, schema, parentId, cancellationToken).ConfigureAwait(false);
            if (parentId.HasValue && await IsDescendantCategoryAsync(connection, transaction, table, p, categoryId, parentId.Value, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("不能将规范分类移动到自己的下级分类下面。");

            IReadOnlyList<StandardCategoryDuplicateDto> duplicates = await FindCategoryDuplicatesAsync(
                connection, transaction, table, p, parentId, source.Code, source.Name, categoryId, cancellationToken).ConfigureAwait(false);
            if (duplicates.Count > 0)
                throw new StandardCategoryConflictException("目标层级下已经存在相同名称或编码的规范分类。", duplicates);

            int affected = await connection.ExecuteAsync(new CommandDefinition(
                $"UPDATE {table} SET PARENT_ID={p}ParentId,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID={p}Id AND IS_ACTIVE=1",
                new { Id = categoryId, ParentId = parentId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (affected == 0) throw new KeyNotFoundException("规范分类不存在或已删除。");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("规范分类移动成功：CategoryId={CategoryId}, ParentId={ParentId}, Operator={OperatorName}", categoryId, parentId, operatorName);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 在同一个父分类下调整规范分类顺序。
    /// </summary>
    public async Task ReorderCategoryAsync(
        long categoryId,
        int direction,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        if (categoryId <= 0) throw new ArgumentException("分类 ID 必须大于 0。", nameof(categoryId));
        if (direction != -1 && direction != 1) throw new ArgumentException("排序方向必须是 -1 或 1。", nameof(direction));
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("操作用户名不能为空。", nameof(operatorName));

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_CATEGORIES" : "standard_categories";
        string p = databaseType == "DM" ? ":" : "@";

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = connection.BeginTransaction();
        try
        {
            long? parentId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                $"SELECT PARENT_ID FROM {table} WHERE ID={p}Id AND IS_ACTIVE=1",
                new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            List<CategoryOrderRow> siblings = (await connection.QueryAsync<CategoryOrderRow>(new CommandDefinition(
                $"SELECT ID AS Id,PARENT_ID AS ParentId,SORT_ORDER AS SortOrder FROM {table} WHERE IS_ACTIVE=1 AND ((PARENT_ID IS NULL AND {p}ParentId IS NULL) OR PARENT_ID={p}ParentId) ORDER BY SORT_ORDER,ID",
                new { ParentId = parentId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

            int currentIndex = siblings.FindIndex(item => item.Id == categoryId);
            int targetIndex = currentIndex + direction;
            if (currentIndex < 0) throw new KeyNotFoundException("规范分类不存在或已停用。");
            if (targetIndex < 0 || targetIndex >= siblings.Count)
                throw new InvalidOperationException(direction < 0 ? "当前规范库已经在最上面。" : "当前规范库已经在最下面。");

            (siblings[currentIndex], siblings[targetIndex]) = (siblings[targetIndex], siblings[currentIndex]);
            for (int index = 0; index < siblings.Count; index++)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    $"UPDATE {table} SET SORT_ORDER={p}SortOrder,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID={p}Id AND IS_ACTIVE=1",
                    new { Id = siblings[index].Id, SortOrder = (index + 1) * 10 },
                    transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("规范分类排序成功：CategoryId={CategoryId}, Direction={Direction}, Operator={OperatorName}", categoryId, direction, operatorName);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private sealed class CategoryOrderRow
    {
        public long Id { get; init; }
        public long? ParentId { get; init; }
        public int SortOrder { get; init; }
    }

    private sealed class CategoryHierarchyRow
    {
        public long Id { get; init; }
        public long? ParentId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    private static async Task<bool> IsDescendantCategoryAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        string parameterPrefix,
        long sourceId,
        long targetParentId,
        CancellationToken cancellationToken)
    {
        long? currentId = targetParentId;
        while (currentId.HasValue)
        {
            if (currentId.Value == sourceId) return true;
            currentId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                $"SELECT PARENT_ID FROM {table} WHERE ID={parameterPrefix}Id AND IS_ACTIVE=1",
                new { Id = currentId.Value }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        return false;
    }

    public async Task<long> CreateCategoryAsync(
        StandardCategoryCommandRequest request,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        ValidateCategoryRequest(request, operatorName);
        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_CATEGORIES" : "standard_categories";
        string p = databaseType == "DM" ? ":" : "@";
        string now = "CURRENT_TIMESTAMP";

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = connection.BeginTransaction();
        try
        {
            await EnsureParentCategoryAsync(connection, transaction, databaseType, schema, request.ParentId, cancellationToken).ConfigureAwait(false);
            string normalizedCode = request.Code.Trim().ToUpperInvariant();
            IReadOnlyList<StandardCategoryDuplicateDto> duplicates = await FindCategoryDuplicatesAsync(
                connection, transaction, table, p, request.ParentId, normalizedCode, request.Name.Trim(), null, cancellationToken).ConfigureAwait(false);
            if (duplicates.Count > 0)
                throw new StandardCategoryConflictException("当前层级下已经存在相同名称或编码的规范分类。", duplicates);

            long id = await NextIdAsync(connection, transaction, table, cancellationToken).ConfigureAwait(false);
            string sql = $"INSERT INTO {table}(ID,PARENT_ID,CODE,NAME,DESCRIPTION,SORT_ORDER,IS_ACTIVE,CREATED_BY,CREATED_AT,UPDATED_AT) VALUES({p}Id,{p}ParentId,{p}Code,{p}Name,{p}Description,{p}SortOrder,1,{p}OperatorName,{now},{now})";
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = id,
                request.ParentId,
                Code = normalizedCode,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                request.SortOrder,
                OperatorName = operatorName.Trim()
            }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return id;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task UpdateCategoryAsync(
        long categoryId,
        StandardCategoryCommandRequest request,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        if (categoryId <= 0) throw new ArgumentException("分类 ID 必须大于 0。", nameof(categoryId));
        ValidateCategoryRequest(request, operatorName);
        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_CATEGORIES" : "standard_categories";
        string p = databaseType == "DM" ? ":" : "@";

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = connection.BeginTransaction();
        try
        {
            long? existingParent = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                $"SELECT PARENT_ID FROM {table} WHERE ID={p}Id AND IS_ACTIVE=1", new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (!existingParent.HasValue && request.ParentId == categoryId)
                throw new ArgumentException("分类不能挂接到自身。", nameof(request));
            if (!existingParent.HasValue && !await CategoryExistsAsync(connection, transaction, table, p, categoryId, cancellationToken).ConfigureAwait(false))
                throw new KeyNotFoundException("规范分类不存在或已停用。");

            await EnsureParentCategoryAsync(connection, transaction, databaseType, schema, request.ParentId, cancellationToken).ConfigureAwait(false);
            string normalizedCode = request.Code.Trim().ToUpperInvariant();
            IReadOnlyList<StandardCategoryDuplicateDto> duplicates = await FindCategoryDuplicatesAsync(
                connection, transaction, table, p, request.ParentId, normalizedCode, request.Name.Trim(), categoryId, cancellationToken).ConfigureAwait(false);
            if (duplicates.Count > 0)
                throw new StandardCategoryConflictException("当前层级下已经存在相同名称或编码的其他规范分类。", duplicates);

            int affected = await connection.ExecuteAsync(new CommandDefinition(
                $"UPDATE {table} SET PARENT_ID={p}ParentId,CODE={p}Code,NAME={p}Name,DESCRIPTION={p}Description,SORT_ORDER={p}SortOrder,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID={p}Id AND IS_ACTIVE=1",
                new
                {
                    Id = categoryId,
                    request.ParentId,
                    Code = normalizedCode,
                    Name = request.Name.Trim(),
                    Description = request.Description?.Trim(),
                    request.SortOrder
                }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (affected == 0) throw new KeyNotFoundException("规范分类不存在或已停用。");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteCategoryAsync(
        long categoryId,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        if (categoryId <= 0) throw new ArgumentException("分类 ID 必须大于 0。", nameof(categoryId));
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("操作用户名不能为空。", nameof(operatorName));
        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_CATEGORIES" : "standard_categories";
        string seriesTable = databaseType == "DM" ? $"{schema}.STANDARD_SERIES" : "standard_series";
        string p = databaseType == "DM" ? ":" : "@";

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = connection.BeginTransaction();
        try
        {
            long childCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT COUNT(1) FROM {table} WHERE PARENT_ID={p}Id AND IS_ACTIVE=1", new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (childCount > 0) throw new InvalidOperationException("该分类下仍有启用的子分类，不能删除。");
            long seriesCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT COUNT(1) FROM {seriesTable} WHERE CATEGORY_ID={p}Id AND IS_ACTIVE=1", new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (seriesCount > 0) throw new InvalidOperationException("该分类下仍有启用的规范系列，不能删除。");
            int affected = await connection.ExecuteAsync(new CommandDefinition(
                $"UPDATE {table} SET IS_ACTIVE=0,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID={p}Id AND IS_ACTIVE=1", new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (affected == 0) throw new KeyNotFoundException("规范分类不存在或已删除。");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static void ValidateCategoryRequest(StandardCategoryCommandRequest request, string operatorName)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Code)) throw new ArgumentException("分类编码不能为空。", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("分类名称不能为空。", nameof(request));
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("操作用户名不能为空。", nameof(operatorName));
    }

    private static async Task EnsureParentCategoryAsync(DbConnection connection, DbTransaction transaction, string databaseType, string schema, long? parentId, CancellationToken cancellationToken)
    {
        if (!parentId.HasValue) return;
        string table = databaseType == "DM" ? $"{schema}.STANDARD_CATEGORIES" : "standard_categories";
        string p = databaseType == "DM" ? ":" : "@";
        if (!await CategoryExistsAsync(connection, transaction, table, p, parentId.Value, cancellationToken).ConfigureAwait(false))
            throw new KeyNotFoundException("父分类不存在或已停用。");
    }

    private static async Task<bool> CategoryExistsAsync(DbConnection connection, DbTransaction transaction, string table, string parameterPrefix, long categoryId, CancellationToken cancellationToken)
    {
        long? id = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            $"SELECT ID FROM {table} WHERE ID={parameterPrefix}Id AND IS_ACTIVE=1", new { Id = categoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return id.HasValue;
    }

    private static async Task<IReadOnlyList<StandardCategoryDuplicateDto>> FindCategoryDuplicatesAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        string parameterPrefix,
        long? parentId,
        string code,
        string name,
        long? excludedCategoryId,
        CancellationToken cancellationToken)
    {
        string parentCondition = parentId.HasValue
            ? $"PARENT_ID={parameterPrefix}ParentId"
            : "PARENT_ID IS NULL";
        string excludedCondition = excludedCategoryId.HasValue
            ? $" AND ID<>{parameterPrefix}ExcludedId"
            : string.Empty;
        string sql = $"SELECT ID AS Id,PARENT_ID AS ParentId,CODE AS Code,NAME AS Name,COALESCE(DESCRIPTION,'') AS Description,SORT_ORDER AS SortOrder FROM {table} WHERE {parentCondition} AND (UPPER(CODE)=UPPER({parameterPrefix}Code) OR NAME={parameterPrefix}Name) AND IS_ACTIVE=1{excludedCondition} ORDER BY ID";
        IEnumerable<StandardCategoryDuplicateDto> rows = await connection.QueryAsync<StandardCategoryDuplicateDto>(new CommandDefinition(
            sql,
            new
            {
                ParentId = parentId,
                Code = code,
                Name = name,
                ExcludedId = excludedCategoryId
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(item => new StandardCategoryDuplicateDto
        {
            Id = item.Id,
            ParentId = item.ParentId,
            Code = item.Code,
            Name = item.Name,
            Description = item.Description,
            SortOrder = item.SortOrder,
            DuplicateReason = string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Name, name, StringComparison.Ordinal)
                ? "名称和 CODE 都重复"
                : string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase)
                    ? "CODE 重复"
                    : "名称重复"
        }).ToList();
    }

    public async Task<IReadOnlyList<StandardDocumentFileManagementDto>> GetFilesAsync(
        long versionId,
        CancellationToken cancellationToken = default)
    {
        if (versionId <= 0) throw new ArgumentException("版本 ID 必须大于 0。", nameof(versionId));
        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENT_FILES" : "standard_document_files";
        string p = databaseType == "DM" ? ":" : "@";
        string sql = $"SELECT ID AS Id, VERSION_ID AS VersionId, FILE_ROLE AS FileRole, ORIGINAL_FILE_NAME AS OriginalFileName, EXTENSION AS Extension, COALESCE(CONTENT_TYPE, '') AS ContentType, FILE_SIZE AS FileSize, COALESCE(DESCRIPTION, '') AS Description FROM {table} WHERE VERSION_ID={p}VersionId AND IS_DELETED=0 ORDER BY ID";
        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        IEnumerable<StandardDocumentFileManagementDto> rows = await connection.QueryAsync<StandardDocumentFileManagementDto>(
            new CommandDefinition(sql, new { VersionId = versionId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <summary>
    /// 创建一个新的规范版本，并将其设为当前版本。
    /// </summary>
    public async Task<long> CreateVersionAsync(
        StandardVersionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SeriesId <= 0) throw new ArgumentException("规范系列 ID 必须大于 0。", nameof(request));
        if (string.IsNullOrWhiteSpace(request.VersionNo)) throw new ArgumentException("版本号不能为空。", nameof(request));

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENT_VERSIONS" : "standard_document_versions";
        string seriesTable = databaseType == "DM" ? $"{schema}.STANDARD_SERIES" : "standard_series";
        string p = databaseType == "DM" ? ":" : "@";
        string now = databaseType == "DM" ? "CURRENT_TIMESTAMP" : "CURRENT_TIMESTAMP";

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = connection.BeginTransaction();
        try
        {
            long? seriesExists = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                $"SELECT ID FROM {seriesTable} WHERE ID={p}SeriesId AND IS_ACTIVE=1",
                new { request.SeriesId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (!seriesExists.HasValue) throw new KeyNotFoundException("规范系列不存在或已停用。");

            long versionId = databaseType == "DM"
                ? await NextIdAsync(connection, transaction, table, cancellationToken).ConfigureAwait(false)
                : 0;

            await connection.ExecuteAsync(new CommandDefinition(
                $"UPDATE {table} SET IS_CURRENT=0, UPDATED_AT={now} WHERE SERIES_ID={p}SeriesId AND IS_DELETED=0",
                new { request.SeriesId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

            string insert = databaseType == "DM"
                ? $"INSERT INTO {table}(ID,SERIES_ID,VERSION_NO,VERSION_LABEL,CHANGE_SUMMARY,SOURCE_TYPE,STATUS,IS_CURRENT,IS_DELETED,CREATED_BY,CREATED_AT,UPDATED_AT) VALUES({p}Id,{p}SeriesId,{p}VersionNo,{p}VersionLabel,{p}ChangeSummary,{p}SourceType,'ACTIVE',1,0,{p}OperatorName,{now},{now})"
                : $"INSERT INTO {table}(SERIES_ID,VERSION_NO,VERSION_LABEL,CHANGE_SUMMARY,SOURCE_TYPE,STATUS,IS_CURRENT,IS_DELETED,CREATED_BY,CREATED_AT,UPDATED_AT) VALUES({p}SeriesId,{p}VersionNo,{p}VersionLabel,{p}ChangeSummary,{p}SourceType,'ACTIVE',1,0,{p}OperatorName,{now},{now})";
            var values = new
            {
                Id = versionId,
                request.SeriesId,
                VersionNo = request.VersionNo.Trim(),
                VersionLabel = request.VersionLabel?.Trim(),
                ChangeSummary = request.ChangeSummary?.Trim(),
                SourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "DOCUMENT" : request.SourceType.Trim().ToUpperInvariant(),
                OperatorName = request.OperatorName.Trim()
            };
            await connection.ExecuteAsync(new CommandDefinition(insert, values, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (databaseType != "DM")
            {
                versionId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT LAST_INSERT_ID()", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("规范版本创建完成：SeriesId={SeriesId}, VersionId={VersionId}, VersionNo={VersionNo}, Operator={Operator}", request.SeriesId, versionId, request.VersionNo, request.OperatorName);
            return versionId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 打开规范附件并返回下载所需的文件信息。
    /// </summary>
    public async Task<StandardFileDownloadResult?> OpenFileAsync(
        long fileId,
        CancellationToken cancellationToken = default)
    {
        if (fileId <= 0) throw new ArgumentException("文件 ID 必须大于 0。", nameof(fileId));

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENT_FILES" : "standard_document_files";
        string p = databaseType == "DM" ? ":" : "@";
        string sql = $"SELECT ORIGINAL_FILE_NAME AS OriginalFileName, RELATIVE_PATH AS RelativePath, CONTENT_TYPE AS ContentType FROM {table} WHERE ID={p}FileId AND IS_DELETED=0";

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        StandardFileStorageMetadata? metadata = await connection.QuerySingleOrDefaultAsync<StandardFileStorageMetadata>(
            new CommandDefinition(sql, new { FileId = fileId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (metadata == null) return null;

        Stream? content = await _fileStorage.OpenReadAsync(metadata.RelativePath, cancellationToken).ConfigureAwait(false);
        if (content == null) return null;

        return new StandardFileDownloadResult
        {
            Content = content,
            FileName = string.IsNullOrWhiteSpace(metadata.OriginalFileName) ? "standard-file" : metadata.OriginalFileName,
            ContentType = string.IsNullOrWhiteSpace(metadata.ContentType) ? "application/octet-stream" : metadata.ContentType
        };
    }

    /// <summary>
    /// 向指定版本上传 PDF、Word、Excel 或 JSON 附件。
    /// </summary>
    public async Task<StandardFileUploadResponse> UploadFileAsync(
        long versionId,
        IFormFile file,
        string? fileRole,
        string? description,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        if (versionId <= 0) throw new ArgumentException("版本 ID 必须大于 0。", nameof(versionId));
        if (file == null || file.Length == 0) throw new InvalidDataException("上传文件不能为空。");
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("操作用户名不能为空。", nameof(operatorName));

        StandardStoredFileResult stored;
        await using (Stream stream = file.OpenReadStream())
        {
            stored = await _fileStorage.SaveAsync(stream, file.FileName, versionId, cancellationToken).ConfigureAwait(false);
        }

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENT_FILES" : "standard_document_files";
        string versionTable = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENT_VERSIONS" : "standard_document_versions";
        string p = databaseType == "DM" ? ":" : "@";
        string now = databaseType == "DM" ? "CURRENT_TIMESTAMP" : "CURRENT_TIMESTAMP";

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = connection.BeginTransaction();
        try
        {
            long? exists = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                $"SELECT ID FROM {versionTable} WHERE ID={p}VersionId AND IS_DELETED=0",
                new { VersionId = versionId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (!exists.HasValue) throw new KeyNotFoundException("规范版本不存在或已删除。");

            long fileId = databaseType == "DM"
                ? await NextIdAsync(connection, transaction, table, cancellationToken).ConfigureAwait(false)
                : 0;
            string insert = databaseType == "DM"
                ? $"INSERT INTO {table}(ID,VERSION_ID,FILE_ROLE,ORIGINAL_FILE_NAME,STORED_FILE_NAME,RELATIVE_PATH,EXTENSION,CONTENT_TYPE,FILE_SIZE,SHA256,DESCRIPTION,IS_DELETED,CREATED_BY,CREATED_AT) VALUES({p}Id,{p}VersionId,{p}FileRole,{p}OriginalFileName,{p}StoredFileName,{p}RelativePath,{p}Extension,{p}ContentType,{p}FileSize,{p}Sha256,{p}Description,0,{p}OperatorName,{now})"
                : $"INSERT INTO {table}(VERSION_ID,FILE_ROLE,ORIGINAL_FILE_NAME,STORED_FILE_NAME,RELATIVE_PATH,EXTENSION,CONTENT_TYPE,FILE_SIZE,SHA256,DESCRIPTION,IS_DELETED,CREATED_BY,CREATED_AT) VALUES({p}VersionId,{p}FileRole,{p}OriginalFileName,{p}StoredFileName,{p}RelativePath,{p}Extension,{p}ContentType,{p}FileSize,{p}Sha256,{p}Description,0,{p}OperatorName,{now})";
            var values = new
            {
                Id = fileId,
                VersionId = versionId,
                FileRole = string.IsNullOrWhiteSpace(fileRole) ? "MAIN" : fileRole.Trim().ToUpperInvariant(),
                OriginalFileName = Path.GetFileName(file.FileName),
                stored.StoredFileName,
                stored.RelativePath,
                stored.Extension,
                ContentType = file.ContentType ?? string.Empty,
                stored.FileSize,
                Sha256 = stored.Sha256,
                Description = description?.Trim(),
                OperatorName = operatorName.Trim()
            };
            await connection.ExecuteAsync(new CommandDefinition(insert, values, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (databaseType != "DM")
            {
                fileId = await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT LAST_INSERT_ID()", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StandardFileUploadResponse { Success = true, Message = "规范附件上传成功。", VersionId = versionId, FileId = fileId, FileName = file.FileName };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 软删除规范版本。
    /// </summary>
    public async Task SoftDeleteVersionAsync(long versionId, string operatorName, CancellationToken cancellationToken = default)
    {
        await ExecuteVersionStateAsync(versionId, operatorName, "DELETED", false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 恢复历史版本并将其设为当前版本。
    /// </summary>
    public async Task RestoreVersionAsync(long versionId, string operatorName, CancellationToken cancellationToken = default)
    {
        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENT_VERSIONS" : "standard_document_versions";
        string p = databaseType == "DM" ? ":" : "@";
        string now = databaseType == "DM" ? "CURRENT_TIMESTAMP" : "CURRENT_TIMESTAMP";
        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = connection.BeginTransaction();
        try
        {
            long? seriesId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition($"SELECT SERIES_ID FROM {table} WHERE ID={p}VersionId AND IS_DELETED=0", new { VersionId = versionId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (!seriesId.HasValue) throw new KeyNotFoundException("历史规范版本不存在。");
            await connection.ExecuteAsync(new CommandDefinition($"UPDATE {table} SET IS_CURRENT=0,UPDATED_AT={now} WHERE SERIES_ID={p}SeriesId AND IS_DELETED=0", new { SeriesId = seriesId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            int affected = await connection.ExecuteAsync(new CommandDefinition($"UPDATE {table} SET IS_CURRENT=1,STATUS='ACTIVE',UPDATED_AT={now} WHERE ID={p}VersionId", new { VersionId = versionId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (affected == 0) throw new KeyNotFoundException("规范版本不存在。");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ExecuteVersionStateAsync(long versionId, string operatorName, string status, bool current, CancellationToken cancellationToken)
    {
        if (versionId <= 0) throw new ArgumentException("版本 ID 必须大于 0。", nameof(versionId));
        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENT_VERSIONS" : "standard_document_versions";
        string p = databaseType == "DM" ? ":" : "@";
        string now = databaseType == "DM" ? "CURRENT_TIMESTAMP" : "CURRENT_TIMESTAMP";
        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        int affected = await connection.ExecuteAsync(new CommandDefinition($"UPDATE {table} SET STATUS={p}Status,IS_CURRENT={p}Current,IS_DELETED={p}Deleted,UPDATED_AT={now} WHERE ID={p}VersionId", new { Status = status, Current = current ? 1 : 0, Deleted = status == "DELETED" ? 1 : 0, VersionId = versionId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (affected == 0) throw new KeyNotFoundException("规范版本不存在。");
        _logger.LogInformation("规范版本状态更新：VersionId={VersionId}, Status={Status}, Operator={Operator}", versionId, status, operatorName);
    }

    private async Task<DbConnection> OpenConnectionAsync(string databaseType, CancellationToken cancellationToken)
    {
        DbConnection connection = databaseType == "DM" ? new DmConnection(GetConnectionString("DM")) : new MySqlConnection(GetConnectionString("MYSQL"));
        try { await connection.OpenAsync(cancellationToken).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static async Task<long> NextIdAsync(DbConnection connection, DbTransaction transaction, string table, CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT COALESCE(MAX(ID),0)+1 FROM {table}", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed class StandardFileStorageMetadata
    {
        public string OriginalFileName { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public string? ContentType { get; init; }
    }

    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";
    private string GetSchemaName() => (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant();
    private string GetConnectionString(string type) => !string.IsNullOrWhiteSpace(_configuration["Database:ConnectionString"]) ? _configuration["Database:ConnectionString"]! : _configuration.GetConnectionString(type == "MYSQL" ? "MySQL" : "DM") ?? throw new InvalidOperationException($"缺少 {type} 数据库连接字符串配置。");
}
