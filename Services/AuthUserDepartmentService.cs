using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;

namespace GB_NewCadPlus_IV.UploadApi.Services;

public sealed class AuthUserDepartmentService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthUserDepartmentService> _logger;

    public AuthUserDepartmentService(IConfiguration configuration, ILogger<AuthUserDepartmentService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
            return FailureLogin("用户名和密码不能为空。");

        UserSecret? user = await QueryUserSecretAsync(request.Username.Trim(), cancellationToken).ConfigureAwait(false);
        if (user == null || !user.IsActive || !VerifyPassword(request.Password, user.Salt, user.PasswordHash))
            return FailureLogin("用户不存在或密码错误。");

        return new LoginResponse { Success = true, Message = "登录成功", User = ToUserDto(user) };
    }

    public async Task<MutationResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        // 修改密码必须同时验证账号、手机号和邮箱，避免只凭账号重置密码。
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.NewPassword))
            return Failure("登录账号、手机号、邮箱和新密码均不能为空。");
        if (request.NewPassword.Trim().Length < 6)
            return Failure("新密码长度不能少于6位。");

        string table = GetDatabaseType() == "DM" ? $"{GetSchemaName()}.USERS" : "users";
        string p = GetDatabaseType() == "DM" ? ":" : "@";
        string lookupSql = $"SELECT ID AS Id, SALT AS Salt FROM {table} WHERE UPPER(USERNAME)=UPPER({p}Username) AND PHONE={p}Phone AND UPPER(EMAIL)=UPPER({p}Email)";
        UserPasswordIdentity? identity = await QuerySingleAsync<UserPasswordIdentity>(lookupSql, new
        {
            Username = request.Username.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email.Trim()
        }, cancellationToken).ConfigureAwait(false);
        if (identity == null)
            return Failure("账号、手机号或邮箱不匹配。");

        // 重新生成盐值，避免新密码继续使用旧密码的盐值。
        string salt = GenerateSalt();
        string hash = ComputeHash(request.NewPassword, salt);
        int rows = await ExecuteAsync($"UPDATE {table} SET PASSWORD_HASH={p}PasswordHash, SALT={p}Salt WHERE ID={p}Id",
            new { PasswordHash = hash, Salt = salt, Id = identity.Id }, cancellationToken).ConfigureAwait(false);
        return rows > 0 ? new MutationResponse { Success = true, Message = "密码修改成功。", Id = identity.Id } : Failure("密码修改失败。");
    }

    public async Task<MutationResponse> RegisterAsync(RegisterUserRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Failure("用户名和密码不能为空。");

        if (await QueryUserSecretAsync(request.Username.Trim(), cancellationToken).ConfigureAwait(false) != null)
            return Failure("用户名已存在。");

        string salt = GenerateSalt();
        string hash = ComputeHash(request.Password, salt);
        string departmentName = request.DepartmentName ?? string.Empty;
        if (request.DepartmentId > 0 && string.IsNullOrWhiteSpace(departmentName))
            departmentName = await QueryDepartmentNameAsync(request.DepartmentId, cancellationToken).ConfigureAwait(false);

        // 注册时只保存手机号和邮箱，邮箱同时用于后续修改密码的身份核验。
        string sql = GetDatabaseType() == "DM"
            ? @"INSERT INTO {SCHEMA}.USERS (USERNAME, PASSWORD_HASH, SALT, REAL_NAME, GENDER, PHONE, EMAIL, DEPARTMENT_ID, DEPARTMENT_NAME, ROLE, IS_ACTIVE, CREATED_AT)
               VALUES (:Username, :PasswordHash, :Salt, :RealName, :Gender, :Phone, :Email, :DepartmentId, :DepartmentName, :Role, 1, SYSDATE)"
            : @"INSERT INTO users (username, password_hash, salt, real_name, gender, phone, email, department_id, department_name, role, is_active, created_at)
               VALUES (@Username, @PasswordHash, @Salt, @RealName, @Gender, @Phone, @Email, @DepartmentId, @DepartmentName, @Role, 1, CURRENT_TIMESTAMP)";

        try
        {
            int id = await ExecuteInsertAsync(sql, new
            {
                Username = request.Username.Trim(), PasswordHash = hash, Salt = salt,
                RealName = string.IsNullOrWhiteSpace(request.RealName) ? request.Username.Trim() : request.RealName,
                Gender = request.Gender ?? "无信息", Phone = request.Phone ?? "未填写",
                Email = request.Email ?? "未填写", DepartmentId = request.DepartmentId,
                DepartmentName = departmentName, Role = string.IsNullOrWhiteSpace(request.Role) ? "user" : request.Role
            }, cancellationToken).ConfigureAwait(false);
            return new MutationResponse { Success = true, Message = "注册成功", Id = id };
        }

        catch (Exception ex)
        {
            _logger.LogError(ex, "注册用户失败。Username={Username}", request.Username);
            return Failure("注册失败，用户名可能已存在。");
        }

    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(int departmentId, CancellationToken cancellationToken)
    {
        string table = GetDatabaseType() == "DM" ? $"{GetSchemaName()}.USERS" : "users";
        string sql = $@"SELECT ID AS Id, USERNAME AS Username, COALESCE(REAL_NAME, USERNAME) AS RealName,
                COALESCE(REAL_NAME, USERNAME) AS DisplayName, COALESCE(REAL_NAME, USERNAME) AS FullName,
                COALESCE(GENDER, '') AS Gender, COALESCE(EMAIL, '') AS Email, COALESCE(PHONE, '') AS Phone,
                COALESCE(ROLE, '') AS Role, COALESCE(DEPARTMENT_NAME, '') AS DepartmentName, COALESCE(IS_ACTIVE, 1) AS IsActive
                FROM {table} WHERE DEPARTMENT_ID = {(GetDatabaseType() == "DM" ? ":DepartmentId" : "@DepartmentId")} ORDER BY ID";
        return await QueryAsync<UserDto>(sql, new { DepartmentId = departmentId }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MutationResponse> AddUserAsync(UserMutationRequest request, CancellationToken cancellationToken)
    {
        return await RegisterAsync(new RegisterUserRequest
        {
            Username = request.Username, Password = request.Password ?? string.Empty, DepartmentId = request.DepartmentId ?? 0,
            DepartmentName = request.DepartmentName, RealName = request.RealName, Gender = request.Gender,
            Phone = request.Phone, Email = request.Email, Role = request.Role
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MutationResponse> UpdateUserAsync(int id, UserMutationRequest request, CancellationToken cancellationToken)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(request.Username)) return Failure("用户参数无效。");
        string table = GetDatabaseType() == "DM" ? $"{GetSchemaName()}.USERS" : "users";
        string p = GetDatabaseType() == "DM" ? ":" : "@";
        string passwordPart = string.IsNullOrWhiteSpace(request.Password) ? string.Empty : $", PASSWORD_HASH={p}PasswordHash, SALT={p}Salt";
        var values = new Dictionary<string, object?>
        {
            ["Username"] = request.Username.Trim(), ["RealName"] = request.RealName ?? request.Username.Trim(), ["Gender"] = request.Gender ?? "无信息",
            ["Phone"] = request.Phone ?? "未填写", ["Email"] = request.Email ?? "未填写", ["Role"] = request.Role ?? "user",
            ["IsActive"] = request.IsActive ? 1 : 0, ["DepartmentId"] = request.DepartmentId ?? 0,
            ["DepartmentName"] = request.DepartmentName ?? string.Empty, ["Id"] = id
        };
        if (!string.IsNullOrWhiteSpace(request.Password)) { values["Salt"] = GenerateSalt(); values["PasswordHash"] = ComputeHash(request.Password, (string)values["Salt"]!); }
        string sql = $@"UPDATE {table} SET USERNAME={p}Username, REAL_NAME={p}RealName, GENDER={p}Gender, PHONE={p}Phone,
            EMAIL={p}Email, ROLE={p}Role, IS_ACTIVE={p}IsActive, DEPARTMENT_ID={p}DepartmentId, DEPARTMENT_NAME={p}DepartmentName{passwordPart}
            WHERE ID={p}Id";
        int rows = await ExecuteAsync(sql, values, cancellationToken).ConfigureAwait(false);
        return rows > 0 ? new MutationResponse { Success = true, Message = "用户更新成功", Id = id } : Failure("用户不存在。");
    }

    public async Task<MutationResponse> DeleteUserAsync(int id, CancellationToken cancellationToken)
    {
        string table = GetDatabaseType() == "DM" ? $"{GetSchemaName()}.USERS" : "users";
        string p = GetDatabaseType() == "DM" ? ":" : "@";
        int rows = await ExecuteAsync($"DELETE FROM {table} WHERE ID={p}Id", new { Id = id }, cancellationToken).ConfigureAwait(false);
        return rows > 0 ? new MutationResponse { Success = true, Message = "用户删除成功", Id = id } : Failure("用户不存在。");
    }

    public async Task<MutationResponse> AssignUserToDepartmentAsync(string username, int departmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || departmentId <= 0)
            return Failure("用户名或部门参数无效。");

        UserSecret? user = await QueryUserSecretAsync(username.Trim(), cancellationToken).ConfigureAwait(false);
        if (user == null)
            return Failure("用户不存在。");

        string departmentName = await QueryDepartmentNameAsync(departmentId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(departmentName))
            return Failure("部门不存在。");

        string table = GetDatabaseType() == "DM" ? $"{GetSchemaName()}.USERS" : "users";
        string p = GetDatabaseType() == "DM" ? ":" : "@";
        int rows = await ExecuteAsync($"UPDATE {table} SET DEPARTMENT_ID={p}DepartmentId, DEPARTMENT_NAME={p}DepartmentName WHERE ID={p}Id", new { Id = user.Id, DepartmentId = departmentId, DepartmentName = departmentName }, cancellationToken).ConfigureAwait(false);
        return rows > 0 ? new MutationResponse { Success = true, Message = "用户分配成功", Id = user.Id } : Failure("用户分配失败。");
    }

    public async Task<MutationResponse> AddDepartmentAsync(DepartmentMutationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return Failure("部门名称不能为空。");
        string databaseType = GetDatabaseType();
        string table = databaseType == "DM" ? $"{GetSchemaName()}.DEPARTMENTS" : "departments";
        string p = databaseType == "DM" ? ":" : "@";
        var values = new
        {
            request.CadCategoryId,
            request.Name,
            DisplayName = request.DisplayName ?? request.Name,
            request.Description,
            request.ManagerUserId,
            request.SortOrder,
            IsActive = request.IsActive ? 1 : 0
        };
        string sql = $"INSERT INTO {table} (CAD_CATEGORY_ID, NAME, DISPLAY_NAME, DESCRIPTION, MANAGER_USER_ID, SORT_ORDER, IS_ACTIVE, CREATED_AT) VALUES ({p}CadCategoryId, {p}Name, {p}DisplayName, {p}Description, {p}ManagerUserId, {p}SortOrder, {p}IsActive, {(databaseType == "DM" ? "SYSDATE" : "CURRENT_TIMESTAMP")})";
        int id = databaseType == "DM"
            ? await ExecuteDmDepartmentInsertAsync(sql, values, cancellationToken).ConfigureAwait(false)
            : await ExecuteInsertAsync($"{sql}; SELECT LAST_INSERT_ID();", values, cancellationToken).ConfigureAwait(false);
        return id > 0 ? new MutationResponse { Success = true, Message = "部门新增成功", Id = id } : Failure("部门新增失败。");
    }

    private async Task<int> ExecuteDmDepartmentInsertAsync(string sql, object parameters, CancellationToken token)
    {
        await using var connection = new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(token).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: token)).ConfigureAwait(false);

        // 达梦新增部门不能复用用户 ID 查询；这里按部门名称读取本次生成的最大 ID。
        string table = $"{GetSchemaName()}.DEPARTMENTS";
        return Convert.ToInt32(await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            $"SELECT ID FROM {table} WHERE NAME=:Name ORDER BY ID DESC FETCH FIRST 1 ROWS ONLY",
            parameters,
            cancellationToken: token)).ConfigureAwait(false) ?? 0);
    }

    public async Task<MutationResponse> SyncDepartmentsFromCategoriesAsync(CancellationToken cancellationToken)
    {
        string databaseType = GetDatabaseType();
        string departmentTable = databaseType == "DM" ? $"{GetSchemaName()}.DEPARTMENTS" : "departments";
        string categoryTable = databaseType == "DM" ? $"{GetSchemaName()}.CAD_CATEGORIES" : "cad_categories";
        string p = databaseType == "DM" ? ":" : "@";
        string now = databaseType == "DM" ? "SYSDATE" : "CURRENT_TIMESTAMP";
        string categorySql = $"SELECT ID AS Id, NAME AS Name, DISPLAY_NAME AS DisplayName, SORT_ORDER AS SortOrder FROM {categoryTable} ORDER BY SORT_ORDER, ID";
        var categories = await QueryAsync<CategoryDto>(categorySql, new { }, cancellationToken).ConfigureAwait(false);
        int created = 0;
        int updated = 0;
        int skipped = 0;

        foreach (CategoryDto category in categories)
        {
            if (category.Id <= 0 || string.IsNullOrWhiteSpace(category.Name))
            {
                skipped++;
                continue;
            }

            string findSql = $"SELECT ID FROM {departmentTable} WHERE CAD_CATEGORY_ID={p}CategoryId OR (CAD_CATEGORY_ID IS NULL AND NAME={p}Name) ORDER BY ID";
            int? departmentId = await QueryScalarAsync<int?>(findSql, new { CategoryId = category.Id, Name = category.Name.Trim() }, cancellationToken).ConfigureAwait(false);
            var values = new
            {
                CategoryId = category.Id,
                Name = category.Name.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(category.DisplayName) ? category.Name.Trim() : category.DisplayName.Trim(),
                SortOrder = category.SortOrder
            };

            if (departmentId.HasValue && departmentId.Value > 0)
            {
                string updateSql = $"UPDATE {departmentTable} SET CAD_CATEGORY_ID={p}CategoryId, NAME={p}Name, DISPLAY_NAME={p}DisplayName, SORT_ORDER={p}SortOrder, UPDATED_AT={now} WHERE ID={p}Id";
                await ExecuteAsync(updateSql, new { values.CategoryId, values.Name, values.DisplayName, values.SortOrder, Id = departmentId.Value }, cancellationToken).ConfigureAwait(false);
                updated++;
            }
            else
            {
                string insertSql = $"INSERT INTO {departmentTable} (CAD_CATEGORY_ID, NAME, DISPLAY_NAME, SORT_ORDER, IS_ACTIVE, CREATED_AT, UPDATED_AT) VALUES ({p}CategoryId, {p}Name, {p}DisplayName, {p}SortOrder, 1, {now}, {now})";
                await ExecuteAsync(insertSql, values, cancellationToken).ConfigureAwait(false);
                created++;
            }
        }

        return new MutationResponse
        {
            Success = true,
            Message = $"部门同步完成：分类 {categories.Count} 个，新增 {created} 个，更新 {updated} 个，跳过 {skipped} 个。",
            Id = created + updated,
            Departments = await QueryDepartmentsForResponseAsync(cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<IReadOnlyList<DepartmentDto>> QueryDepartmentsForResponseAsync(CancellationToken cancellationToken)
    {
        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string departments = databaseType == "DM" ? $"{schema}.DEPARTMENTS" : "departments";
        string users = databaseType == "DM" ? $"{schema}.USERS" : "users";
        string categoryTable = databaseType == "DM" ? $"{schema}.CAD_CATEGORIES" : "cad_categories";
        string sql = $@"SELECT d.ID AS Id, d.CAD_CATEGORY_ID AS CadCategoryId,
                d.NAME AS Name, COALESCE(d.DISPLAY_NAME, d.NAME) AS RealName,
                COALESCE(d.DISPLAY_NAME, d.NAME) AS DisplayName,
                COALESCE(d.DESCRIPTION, '') AS Description, COALESCE(d.SORT_ORDER, 0) AS SortOrder,
                d.MANAGER_USER_ID AS ManagerUserId, COALESCE(d.IS_ACTIVE, 1) AS IsActive,
                (SELECT COUNT(1) FROM {users} u WHERE u.DEPARTMENT_ID = d.ID) AS UserCount
                FROM {departments} d
                WHERE d.CAD_CATEGORY_ID IS NULL
                   OR EXISTS (SELECT 1 FROM {categoryTable} c WHERE c.ID = d.CAD_CATEGORY_ID)
                ORDER BY d.SORT_ORDER, d.ID";
        return await QueryAsync<DepartmentDto>(sql, new { }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MutationResponse> UpdateDepartmentAsync(int id, DepartmentMutationRequest request, CancellationToken cancellationToken)
    {
        string table = GetDatabaseType() == "DM" ? $"{GetSchemaName()}.DEPARTMENTS" : "departments";
        string p = GetDatabaseType() == "DM" ? ":" : "@";
        int rows = await ExecuteAsync($"UPDATE {table} SET NAME={p}Name, DISPLAY_NAME={p}DisplayName, DESCRIPTION={p}Description, MANAGER_USER_ID={p}ManagerUserId, SORT_ORDER={p}SortOrder, IS_ACTIVE={p}IsActive, UPDATED_AT={(GetDatabaseType() == "DM" ? "SYSDATE" : "CURRENT_TIMESTAMP")} WHERE ID={p}Id", new { Id = id, request.Name, DisplayName = request.DisplayName ?? request.Name, request.Description, request.ManagerUserId, request.SortOrder, IsActive = request.IsActive ? 1 : 0 }, cancellationToken).ConfigureAwait(false);
        return rows > 0 ? new MutationResponse { Success = true, Message = "部门更新成功", Id = id } : Failure("部门不存在。");
    }

    public async Task<MutationResponse> DeleteDepartmentAsync(int id, CancellationToken cancellationToken)
    {
        string table = GetDatabaseType() == "DM" ? $"{GetSchemaName()}.DEPARTMENTS" : "departments";
        string users = GetDatabaseType() == "DM" ? $"{GetSchemaName()}.USERS" : "users";
        string p = GetDatabaseType() == "DM" ? ":" : "@";
        await ExecuteAsync($"UPDATE {users} SET DEPARTMENT_ID=0, DEPARTMENT_NAME='' WHERE DEPARTMENT_ID={p}Id", new { Id = id }, cancellationToken).ConfigureAwait(false);
        int rows = await ExecuteAsync($"DELETE FROM {table} WHERE ID={p}Id", new { Id = id }, cancellationToken).ConfigureAwait(false);
        return rows > 0 ? new MutationResponse { Success = true, Message = "部门删除成功", Id = id } : Failure("部门不存在。");
    }

    private async Task<UserSecret?> QueryUserSecretAsync(string username, CancellationToken cancellationToken)
    {
        string table = GetDatabaseType() == "DM" ? $"{GetSchemaName()}.USERS" : "users";
        string p = GetDatabaseType() == "DM" ? ":" : "@";
        return await QuerySingleAsync<UserSecret>($"SELECT ID AS Id, USERNAME AS Username, PASSWORD_HASH AS PasswordHash, SALT AS Salt, REAL_NAME AS RealName, GENDER AS Gender, EMAIL AS Email, PHONE AS Phone, ROLE AS Role, DEPARTMENT_NAME AS DepartmentName, IS_ACTIVE AS IsActive FROM {table} WHERE UPPER(USERNAME)=UPPER({p}Username)", new { Username = username }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> QueryDepartmentNameAsync(int id, CancellationToken cancellationToken)
    {
        string table = GetDatabaseType() == "DM" ? $"{GetSchemaName()}.DEPARTMENTS" : "departments";
        string p = GetDatabaseType() == "DM" ? ":" : "@";
        return await QueryScalarAsync<string>($"SELECT NAME FROM {table} WHERE ID={p}Id", new { Id = id }, cancellationToken).ConfigureAwait(false) ?? string.Empty;
    }

    private sealed class UserSecret : UserDto
    {
        public string PasswordHash { get; init; } = string.Empty;
        public string Salt { get; init; } = string.Empty;
    }

    private sealed class UserPasswordIdentity
    {
        public int Id { get; init; }
        public string Salt { get; init; } = string.Empty;
    }

    private static LoginResponse FailureLogin(string message) => new() { Success = false, Message = message };
    private static MutationResponse Failure(string message) => new() { Success = false, Message = message };
    private static UserDto ToUserDto(UserSecret user) => user;
    private static string GenerateSalt() { byte[] bytes = new byte[32]; RandomNumberGenerator.Fill(bytes); return Convert.ToBase64String(bytes); }
    private static string ComputeHash(string password, string salt) { using var sha = SHA256.Create(); return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt))); }
    private static bool VerifyPassword(string password, string salt, string hash) => string.Equals(ComputeHash(password, salt), hash, StringComparison.OrdinalIgnoreCase);

    private async Task<List<T>> QueryAsync<T>(string sql, object parameters, CancellationToken token)
    {
        if (GetDatabaseType() == "DM") { await using var c = new DmConnection(GetConnectionString("DM")); await c.OpenAsync(token); return (await c.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: token))).AsList(); }
        await using var m = new MySqlConnection(GetConnectionString("MYSQL")); await m.OpenAsync(token); return (await m.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: token))).AsList();
    }
    private async Task<T?> QuerySingleAsync<T>(string sql, object parameters, CancellationToken token)
    {
        if (GetDatabaseType() == "DM") { await using var c = new DmConnection(GetConnectionString("DM")); await c.OpenAsync(token); return await c.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: token)); }
        await using var m = new MySqlConnection(GetConnectionString("MYSQL")); await m.OpenAsync(token); return await m.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: token));
    }
    private async Task<T?> QueryScalarAsync<T>(string sql, object parameters, CancellationToken token)
    {
        if (GetDatabaseType() == "DM") { await using var c = new DmConnection(GetConnectionString("DM")); await c.OpenAsync(token); return await c.ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: token)); }
        await using var m = new MySqlConnection(GetConnectionString("MYSQL")); await m.OpenAsync(token); return await m.ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: token));
    }
    private async Task<int> ExecuteAsync(string sql, object parameters, CancellationToken token)
    {
        if (GetDatabaseType() == "DM") { await using var c = new DmConnection(GetConnectionString("DM")); await c.OpenAsync(token); return await c.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: token)); }
        await using var m = new MySqlConnection(GetConnectionString("MYSQL")); await m.OpenAsync(token); return await m.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: token));
    }
    private async Task<int> ExecuteInsertAsync(string sql, object parameters, CancellationToken token)
    {
        if (GetDatabaseType() == "DM") { await using var c = new DmConnection(GetConnectionString("DM")); await c.OpenAsync(token); await c.ExecuteAsync(new CommandDefinition(sql.Replace("{SCHEMA}", GetSchemaName()), parameters, cancellationToken: token)); return Convert.ToInt32(await c.ExecuteScalarAsync<int?>(new CommandDefinition($"SELECT ID FROM {GetSchemaName()}.USERS WHERE USERNAME={(GetDatabaseType() == "DM" ? ":" : "@")}Username ORDER BY ID DESC", parameters, cancellationToken: token)) ?? 0); }
        await using var m = new MySqlConnection(GetConnectionString("MYSQL")); await m.OpenAsync(token); return Convert.ToInt32(await m.ExecuteScalarAsync<long>(new CommandDefinition(sql, parameters, cancellationToken: token)));
    }
    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";
    private string GetSchemaName() { string s = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim(); if (string.IsNullOrWhiteSpace(s) || !s.All(c => char.IsLetterOrDigit(c) || c == '_')) throw new InvalidOperationException("Database:Schema 配置无效。"); return s.ToUpperInvariant(); }
    private string GetConnectionString(string type) => (_configuration["Database:ConnectionString"] ?? string.Empty).Trim() is { Length: > 0 } common ? common : _configuration.GetConnectionString(type == "MYSQL" ? "MySQL" : "DM") ?? throw new InvalidOperationException("缺少数据库连接字符串。");
}
