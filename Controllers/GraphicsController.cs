using Dapper;               // 引入 Dapper ORM 框架
using Dm;                   // 引入达梦数据库驱动
using GB_NewCadPlus_IV.UploadApi.Filters;
using Microsoft.AspNetCore.Mvc; // 引入 ASP.NET Core MVC 核心功能
using MySql.Data.MySqlClient;   // 引入 MySQL 数据库驱动
using System.Data;          // 引入数据操作相关命名空间
using System.Data.Common;
using System.Security.Cryptography; // 引入加密哈希算法命名空间
using System.Text.Json;     // 引入 JSON 序列化命名空间

namespace GB_NewCadPlus_IV.UploadApi.Controllers
{
    /// <summary>
    /// 图元上传/读取控制器（支持 MySQL 与 DM 达梦双数据库兼容）
    /// </summary>
    [ApiController]         // 标记为 API 控制器，启用模型绑定等特性
    [Route("api/[controller]")] // 定义路由模板，例如 /api/graphics
    [ServiceFilter(typeof(OperationLogFilter))]// 应用操作日志过滤器，记录每次请求的详细信息
    public class GraphicsController : ControllerBase
    {
        private readonly IConfiguration _configuration; // 注入配置管理器，用于读取 appsettings.json 中的配置项
        private readonly ILogger<GraphicsController> _logger; // 注入日志记录器，用于记录调试信息和错误日志
        private static readonly HashSet<string> AllowedCategoryTypes = new(StringComparer.OrdinalIgnoreCase) { "sub", "root" }; // 允许的分类类型集合，忽略大小写

        /// <summary>
        /// 构造函数，注入配置和日志服务
        /// </summary>
        public GraphicsController(IConfiguration configuration, ILogger<GraphicsController> logger)
        {
            _configuration = configuration; // 初始化配置管理器
            _logger = logger;               // 初始化日志记录器
        }

        #region 上传接口

        /// <summary>
        /// 上传图元文件：dwgFile 为必填项，其他字段若缺失后端会自动填充默认值
        /// 路由地址：POST /api/graphics/upload
        /// </summary>
        [HttpPost("upload")]
        [RequestSizeLimit(1024L * 1024L * 1024L)]
        public async Task<IActionResult> UploadAsync()
        {
            // ========== 新增：处理“仅替换预览图”的请求 ==========
            if (Request.Form.TryGetValue("replacePreviewOnly", out var replaceOnly) && replaceOnly == "true")
            {
                _logger.LogInformation("[ReplacePreview] 检测到替换预览图请求（replacePreviewOnly=true）");
                if (!int.TryParse(Request.Form["replacePreviewId"], out int replaceStorageId) || replaceStorageId <= 0)
                {
                    return BadRequest(new { success = false, message = "replacePreviewId 无效。" });
                }
                return await ReplacePreviewCoreAsync(replaceStorageId);
            }
            // ============================
            // 1. 提取文件（这两个必须通过 IFormFile 参数接收，这里改为直接从 Request.Form.Files 获取）
            // ============================
            IFormFile dwgFile = Request.Form.Files.GetFile("dwgFile");
            IFormFile? previewFile = Request.Form.Files.GetFile("previewFile");

            // 基础校验
            if (dwgFile == null || dwgFile.Length == 0)
            {
                _logger.LogWarning("上传失败：dwgFile 为空");
                return BadRequest(new { success = false, message = "dwgFile 不能为空。" });
            }

            // ============================
            // 2. 从 Form 中提取所有字段（彻底绕过模型绑定）
            // ============================
            string? categoryIdStr = Request.Form["categoryId"];
            string? categoryType = Request.Form["categoryType"];
            string? displayName = Request.Form["displayName"];
            string? description = Request.Form["description"];
            string? createdBy = Request.Form["createdBy"];
            string? attributesJson = Request.Form["attributesJson"];
            string? blockNameStr = Request.Form["blockName"];
            string? layerNameStr = Request.Form["layerName"];
            string? colorIndexStr = Request.Form["colorIndex"];
            string? scaleStr = Request.Form["scale"];

            // 诊断日志
            _logger.LogInformation("=== [DIAGNOSTIC] Start Dumping Form Data ===");
            foreach (var key in Request.Form.Keys)
            {
                _logger.LogInformation($"[DIAGNOSTIC] Key: '{key}', Value: '{Request.Form[key]}'");
            }
            _logger.LogInformation("=== [DIAGNOSTIC] End Dumping Form Data ===");

            // ============================
            // 3. 解析并验证关键字段
            // ============================
            // CategoryId：必填，且必须大于 0
            int finalCategoryId = 0;
            if (!string.IsNullOrWhiteSpace(categoryIdStr))
            {
                int.TryParse(categoryIdStr, out finalCategoryId);
            }
            if (finalCategoryId <= 0)
            {
                _logger.LogWarning("上传失败：categoryId 无效或缺失");
                return BadRequest(new { success = false, message = "categoryId 必须为有效的正整数。" });
            }

            // ColorIndex
            int finalColorIndex = 0;
            if (!string.IsNullOrWhiteSpace(colorIndexStr))
            {
                int.TryParse(colorIndexStr, out finalColorIndex);
            }

            // Scale
            double finalScale = 1.0;
            if (!string.IsNullOrWhiteSpace(scaleStr))
            {
                double.TryParse(scaleStr, out finalScale);
            }

            // 其他字段默认值填充
            if (string.IsNullOrWhiteSpace(categoryType))
                categoryType = "sub";
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = Path.GetFileNameWithoutExtension(dwgFile.FileName);
            if (string.IsNullOrWhiteSpace(description))
                description = "Auto-uploaded via API";
            if (string.IsNullOrWhiteSpace(createdBy))
            {
                var currentUser = HttpContext.User.Identity?.Name;
                createdBy = string.IsNullOrWhiteSpace(currentUser) ? "System" : currentUser;
            }
            if (string.IsNullOrWhiteSpace(attributesJson))
                attributesJson = "{}";

            // 解析并合并扩展属性
            var attrs = ParseAttributes(attributesJson);
            string finalCreatedBy = FirstNonEmpty(createdBy, GetAttr(attrs, "CreatedBy"), GetAttr(attrs, "创建者"));
            string finalLayerName = !string.IsNullOrWhiteSpace(layerNameStr)
                ? layerNameStr.Trim()
                : FirstNonEmpty(null, GetAttr(attrs, "LayerName"), GetAttr(attrs, "图层名"));
            string finalBlockName = !string.IsNullOrWhiteSpace(blockNameStr)
                ? blockNameStr.Trim()
                : FirstNonEmpty(null, GetAttr(attrs, "BlockName"), GetAttr(attrs, "图块名"));
            finalColorIndex = finalColorIndex > 0
                ? finalColorIndex
                : FirstValidInt(null, GetAttr(attrs, "ColorIndex"), GetAttr(attrs, "颜色索引"), 0);
            finalScale = finalScale > 0
                ? finalScale
                : FirstValidDouble(null, GetAttr(attrs, "Scale"), GetAttr(attrs, "比例"), 1.0);

            // 更新属性字典
            attrs["CreatedBy"] = finalCreatedBy;
            attrs["LayerName"] = finalLayerName;
            attrs["ColorIndex"] = finalColorIndex.ToString();
            attrs["BlockName"] = finalBlockName;
            attrs["Scale"] = finalScale.ToString();
            attrs["DisplayName"] = displayName;
            string finalAttributesJson = JsonSerializer.Serialize(attrs);

            _logger.LogInformation($"[Step-2] 属性处理完成 - CreatedBy: {finalCreatedBy}, Layer: {finalLayerName}");

            // ============================
            // 4. 文件存储
            // ============================
            string root;
            try
            {
                root = GetStorageRoot();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "获取存储根路径失败");
                return StatusCode(500, new { success = false, message = "服务器存储配置错误，请联系管理员。" });
            }

            string categoryDir = Path.Combine(root, categoryType!, finalCategoryId.ToString());

            try
            {
                Directory.CreateDirectory(categoryDir);
                _logger.LogInformation($"[Step-3] 文件目录准备就绪: {categoryDir}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[Step-3 Error] 创建目录失败: {categoryDir}");
                return StatusCode(500, new { success = false, message = "服务器存储路径不可用。" });
            }

            string dwgExt = Path.GetExtension(dwgFile.FileName);
            if (string.IsNullOrWhiteSpace(dwgExt)) dwgExt = ".dwg";
            string dwgStoredName = Guid.NewGuid().ToString() + dwgExt;
            string dwgPath = Path.Combine(categoryDir, dwgStoredName);

            await using (var fs = new FileStream(dwgPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await dwgFile.CopyToAsync(fs);
            }
            _logger.LogInformation($"[Step-4] 主文件落盘成功: {dwgPath}, Size: {dwgFile.Length} bytes");

            // 预览图（可选）
            string? previewStoredName = null;
            string? previewPath = null;
            if (previewFile != null && previewFile.Length > 0)
            {
                string pe = Path.GetExtension(previewFile.FileName);
                if (string.IsNullOrWhiteSpace(pe)) pe = ".png";
                previewStoredName = Guid.NewGuid().ToString() + pe;
                previewPath = Path.Combine(categoryDir, previewStoredName);
                await using (var ps = new FileStream(previewPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await previewFile.CopyToAsync(ps);
                }
                _logger.LogInformation($"[Step-4] 预览图落盘成功: {previewPath}");
            }

            // 文件哈希与大小
            long fileSize = new FileInfo(dwgPath).Length;
            string fileHash = await ComputeSha256Async(dwgPath);
            _logger.LogInformation($"[Step-5] 文件指纹: {fileHash[..8]}...");

            // ============================
            // 5. 写入数据库
            // ============================
            try
            {
                var dbType = GetDatabaseType();
                _logger.LogInformation($"[Step-6] 开始写库 - Type: {dbType}, CategoryId: {finalCategoryId}");

                var req = new UploadDbRequest
                {
                    CategoryId = finalCategoryId,
                    CategoryType = categoryType!,
                    FileName = dwgFile.FileName,
                    FileStoredName = dwgStoredName,
                    DisplayName = displayName!,
                    FileType = dwgExt.ToLowerInvariant(),
                    FileHash = fileHash,
                    BlockName = finalBlockName,
                    LayerName = finalLayerName,
                    ColorIndex = finalColorIndex,
                    Scale = finalScale,
                    FilePath = dwgPath,
                    PreviewImageName = previewStoredName,
                    PreviewImagePath = previewPath,
                    FileSize = fileSize,
                    Description = description!,
                    CreatedBy = finalCreatedBy,
                    AttributesJson = finalAttributesJson
                };

                UploadDbResult dbResult = dbType == "DM"
                    ? await SaveUploadToDmAsync(req)
                    : await SaveUploadToMySqlAsync(req);

                _logger.LogInformation($"[Step-7] 入库成功 - StorageId: {dbResult.StorageId}");

                return Ok(new
                {
                    success = true,
                    message = "上传成功",
                    storageId = dbResult.StorageId,
                    attrId = dbResult.AttrId,
                    filePath = dwgPath,
                    previewImagePath = previewPath,
                    fileStoredName = dwgStoredName,
                    previewImageName = previewStoredName,
                    fileHash,
                    fileSize
                });
            }
            catch (Exception ex)
            {
                TryDeleteFile(dwgPath);
                TryDeleteFile(previewPath);
                _logger.LogError(ex, "[Step-7 Error] 写库失败，已回滚文件");
                return StatusCode(500, new { success = false, message = "上传失败: " + ex.Message });
            }
        }


        /// <summary>
        /// 替换预览图的核心业务逻辑（供 UploadAsync 和 ReplacePreviewAsync 共用）
        /// </summary>
        private async Task<IActionResult> ReplacePreviewCoreAsync(int storageId)
        {
            try
            {
                // 1. 检查记录是否存在
                var row = await GetStorageRowAsync(storageId);
                if (row == null)
                    return NotFound(new { success = false, message = "图元记录不存在。" });

                // 2. 提取上传的预览图文件
                IFormFile? previewFile = Request.Form.Files.GetFile("previewFile");
                if (previewFile == null || previewFile.Length == 0)
                    return BadRequest(new { success = false, message = "previewFile 不能为空。" });

                // 3. 确定存储目录（基于已有记录的路径，保持目录不变）
                string directory = Path.GetDirectoryName(ResolvePhysicalPath(row))!;
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    return StatusCode(500, new { success = false, message = "原始存储目录不可用。" });

                // 4. 删除旧预览图（如果存在）
                string? oldPreviewPath = row.PreviewImagePath;
                if (!string.IsNullOrWhiteSpace(oldPreviewPath) && System.IO.File.Exists(oldPreviewPath))
                {
                    try { System.IO.File.Delete(oldPreviewPath); } catch { }
                }

                // 5. 保存新预览图
                string ext = Path.GetExtension(previewFile.FileName);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                string newFileName = Guid.NewGuid().ToString() + ext;
                string newPath = Path.Combine(directory, newFileName);

                await using (var stream = new FileStream(newPath, FileMode.Create))
                {
                    await previewFile.CopyToAsync(stream);
                }

                // 6. 更新数据库记录
                var dbType = GetDatabaseType();
                string updateSql = dbType == "DM"
                    ? $"UPDATE {_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY"}.CAD_FILE_STORAGE SET preview_image_name = :Name, preview_image_path = :Path WHERE id = :Id"
                    : "UPDATE cad_file_storage SET preview_image_name = @Name, preview_image_path = @Path WHERE id = @Id";

                using var conn = dbType == "DM" ? (DbConnection)new DmConnection(GetActiveConnectionString("DM"))
                                                 : new MySqlConnection(GetActiveConnectionString("MySQL"));
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = updateSql;

                var pName = cmd.CreateParameter();
                pName.ParameterName = dbType == "DM" ? ":Name" : "@Name";
                pName.Value = newFileName;
                cmd.Parameters.Add(pName);

                var pPath = cmd.CreateParameter();
                pPath.ParameterName = dbType == "DM" ? ":Path" : "@Path";
                pPath.Value = newPath;
                cmd.Parameters.Add(pPath);

                var pId = cmd.CreateParameter();
                pId.ParameterName = dbType == "DM" ? ":Id" : "@Id";
                pId.Value = storageId;
                cmd.Parameters.Add(pId);

                await cmd.ExecuteNonQueryAsync();

                _logger.LogInformation($"预览图已替换：storageId={storageId}, newPath={newPath}");
                return Ok(new { success = true, message = "预览图替换成功", previewImagePath = newPath });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "替换预览图失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误: " + ex.Message });
            }
        }

        [HttpPut("{storageId}/preview")]
        [RequestSizeLimit(10L * 1024L * 1024L)]
        public async Task<IActionResult> ReplacePreviewAsync([FromRoute] int storageId)
        {
            return await ReplacePreviewCoreAsync(storageId);
        }
        #endregion

        #region 读取接口

        /// <summary>
        /// 下载图元主文件：GET /api/graphics/{id}/file
        /// </summary>
        [HttpGet("{storageId:int}/file")] // 路由参数 storageId 必须为整数
        public async Task<IActionResult> DownloadGraphicFileAsync([FromRoute] int storageId)
        {
            var row = await GetStorageRowAsync(storageId); // 从数据库查询文件记录
            if (row == null) return NotFound(new { success = false, message = "未找到图元记录。" }); // 记录不存在返回 404

            string filePath = ResolvePhysicalPath(row); // 解析文件的物理路径
            // 检查路径是否有效且文件确实存在
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                return NotFound(new { success = false, message = "图元文件不存在。" });

            // 以只读共享模式打开文件流，允许其他进程同时读取
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // 返回文件流，自动识别内容类型
            return File(stream, GetContentType(filePath), Path.GetFileName(filePath));
        }

        /// <summary>
        /// 下载预览图：GET /api/graphics/{id}/preview
        /// </summary>
        [HttpGet("{storageId:int}/preview")]
        public async Task<IActionResult> DownloadPreviewAsync([FromRoute] int storageId)
        {
            var row = await GetStorageRowAsync(storageId); // 查询记录
            if (row == null) return NotFound(new { success = false, message = "未找到图元记录。" });

            string previewPath = ResolvePreviewPath(row); // 解析预览图路径
            // 检查预览图是否存在
            if (string.IsNullOrWhiteSpace(previewPath) || !System.IO.File.Exists(previewPath))
                return NotFound(new { success = false, message = "预览图不存在。" });

            // 打开预览图文件流
            var stream = new FileStream(previewPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return File(stream, GetContentType(previewPath), Path.GetFileName(previewPath));
        }

        /// <summary>
        /// 兼容路由：GET /api/graphics/download/{id}
        /// </summary>
        [HttpGet("download/{storageId:int}")]
        public Task<IActionResult> DownloadAliasAsync([FromRoute] int storageId)
        {
            return DownloadGraphicFileAsync(storageId); // 复用主文件下载逻辑
        }

        /// <summary>
        /// 兼容路由：GET /api/graphics/file/{id}
        /// </summary>
        [HttpGet("file/{storageId:int}")]
        public Task<IActionResult> DownloadFileAliasAsync([FromRoute] int storageId)
        {
            return DownloadGraphicFileAsync(storageId); // 复用主文件下载逻辑
        }

        /// <summary>
        /// 兼容路由：GET /api/graphics/preview/{id}
        /// </summary>
        [HttpGet("preview/{storageId:int}")]
        public Task<IActionResult> DownloadPreviewAliasAsync([FromRoute] int storageId)
        {
            return DownloadPreviewAsync(storageId); // 复用预览图下载逻辑
        }

        #endregion

        #region MySQL 写库

        /// <summary>
        /// 保存上传记录到 MySQL 数据库
        /// </summary>
        private async Task<UploadDbResult> SaveUploadToMySqlAsync(UploadDbRequest req)
        {
            // 1. 获取连接字符串并建立连接
            string connStr = GetActiveConnectionString("MySQL"); // 从配置中获取 MySQL 连接串
            using var conn = new MySqlConnection(connStr);       // 创建 MySQL 连接对象
            await conn.OpenAsync();                              // 异步打开数据库连接
            _logger.LogInformation("[MySQL] 数据库连接已打开。");

            using var tx = conn.BeginTransaction();              // 开启数据库事务，确保原子性

            try
            {
                // ✅ 关键调试日志：打印即将写入的 CategoryId
                _logger.LogInformation($"[MySQL Debug] Preparing to insert. CategoryId={req.CategoryId}, FileName={req.FileName}");

                // 2. 定义插入 Storage 表的 SQL 语句
                const string insertStorageSql = @"
                                                INSERT INTO cad_file_storage
                                                (
                                                    category_id, category_type, file_name, file_stored_name, display_name,
                                                    file_type, file_hash, block_name, layer_name, color_index, scale,
                                                    file_path, preview_image_name, preview_image_path, file_size, is_preview,
                                                    version, description, is_active, created_by, is_public, created_at, updated_at
                                                )
                                                VALUES
                                                (
                                                    @CategoryId, @CategoryType, @FileName, @FileStoredName, @DisplayName,
                                                    @FileType, @FileHash, @BlockName, @LayerName, @ColorIndex, @Scale,
                                                    @FilePath, @PreviewImageName, @PreviewImagePath, @FileSize, @IsPreview,
                                                    1, @Description, 1, @CreatedBy, 1, NOW(), NOW()
                                                );
                                                SELECT LAST_INSERT_ID();"; // 插入后返回新生成的自增 ID

                // 3. 执行插入并获取新生成的 ID
                // ✅ 修复：显式指定匿名对象的属性名，确保 Dapper 能正确映射参数
                long storageId = await conn.ExecuteScalarAsync<long>(
                    insertStorageSql,
                    new
                    {
                        CategoryId = req.CategoryId,          // 显式映射分类 ID
                        CategoryType = req.CategoryType,      // 显式映射分类类型
                        FileName = req.FileName,
                        FileStoredName = req.FileStoredName,
                        DisplayName = req.DisplayName,
                        FileType = req.FileType,
                        FileHash = req.FileHash,
                        BlockName = req.BlockName,
                        LayerName = req.LayerName,
                        ColorIndex = req.ColorIndex,
                        Scale = req.Scale,
                        FilePath = req.FilePath,
                        PreviewImageName = req.PreviewImageName,
                        PreviewImagePath = req.PreviewImagePath,
                        FileSize = req.FileSize,
                        IsPreview = string.IsNullOrWhiteSpace(req.PreviewImagePath) ? 0 : 1, // 如果有预览图路径则标记为 1
                        Description = req.Description,
                        CreatedBy = req.CreatedBy
                    },
                    tx
                );

                // 4. 校验 Storage ID 是否有效
                if (storageId <= 0)
                {
                    throw new Exception("MySQL 插入 cad_file_storage 失败，未返回有效 ID。");
                }
                _logger.LogInformation($"[MySQL] Storage 记录插入成功，StorageId={storageId}");

                // 5. 定义插入属性 JSON 表的 SQL 语句
                const string insertAttrSql = @"
                                             INSERT INTO cad_block_attributes_json
                                             (file_id, config_name, attributes_json, created_at, updated_at)
                                             VALUES
                                             (@FileId, @ConfigName, @AttributesJson, NOW(), NOW());
                                             SELECT LAST_INSERT_ID();"; // 返回新生成的属性 ID

                // 6. 执行插入并获取 Attr ID
                long attrId = await conn.ExecuteScalarAsync<long>(
                    insertAttrSql,
                    new
                    {
                        FileId = storageId,                   // 关联刚才生成的 StorageId
                        ConfigName = "default",               // 配置名称默认为 default
                        AttributesJson = req.AttributesJson   // 显式映射属性 JSON
                    },
                    tx
                );

                // 7. 校验 Attr ID 是否有效
                if (attrId <= 0)
                {
                    throw new Exception("MySQL 插入 cad_block_attributes_json 失败，未返回有效 ID。");
                }
                _logger.LogInformation($"[MySQL] Attribute 记录插入成功，AttrId={attrId}");

                // 8. 更新 Storage 表，将属性 ID 关联回去
                const string updateSql = @"
                                         UPDATE cad_file_storage
                                         SET file_attribute_id = @AttrId, updated_at = NOW()
                                         WHERE id = @StorageId;";

                await conn.ExecuteAsync(updateSql, new { AttrId = attrId, StorageId = storageId }, tx);
                _logger.LogInformation($"[MySQL] Storage 表关联 AttributeId 更新成功。");

                // 9. 提交事务，永久保存更改
                tx.Commit();
                _logger.LogInformation($"[MySQL] 事务提交成功。");

                return new UploadDbResult((int)storageId, (int)attrId); // 返回结果对象
            }
            catch (Exception ex)
            {
                // 发生任何异常则回滚事务，保证数据一致性
                tx.Rollback();
                _logger.LogError(ex, "[MySQL Error] 事务回滚，保存记录失败。");
                throw; // 向上抛出异常，由 Controller 层处理
            }
        }

        #endregion

        #region DM 写库

        /// <summary>
        /// 保存上传记录到 DM (达梦) 数据库
        /// </summary>
        private async Task<UploadDbResult> SaveUploadToDmAsync(UploadDbRequest req)
        {
            // 1. 获取连接字符串和 Schema
            string connStr = GetActiveConnectionString("DM"); // 获取达梦连接串
            string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant(); // 获取 Schema 名称并转大写

            using var conn = new DmConnection(connStr);       // 创建达梦连接对象
            await conn.OpenAsync();                           // 异步打开连接
            _logger.LogInformation("[DM] 数据库连接已打开。");

            using var tx = conn.BeginTransaction();           // 开启事务

            try
            {
                // ✅ 关键调试日志
                _logger.LogInformation($"[DM Debug] Preparing to insert. CategoryId={req.CategoryId}, FileName={req.FileName}");

                // 2. 定义插入 Storage 表的 SQL (使用占位符 {0} 动态替换 Schema)
                const string insertStorageSqlTemplate = @"
                                                 INSERT INTO {0}.CAD_FILE_STORAGE
                                                 (
                                                     category_id, category_type, file_name, file_stored_name, display_name,
                                                     file_type, file_hash, block_name, layer_name, color_index, scale,
                                                     file_path, preview_image_name, preview_image_path, file_size, is_preview,
                                                     version, description, is_active, created_by, is_public, created_at, updated_at
                                                 )
                                                 VALUES
                                                 (
                                                     :CategoryId, :CategoryType, :FileName, :FileStoredName, :DisplayName,
                                                     :FileType, :FileHash, :BlockName, :LayerName, :ColorIndex, :Scale,
                                                     :FilePath, :PreviewImageName, :PreviewImagePath, :FileSize, :IsPreview,
                                                     1, :Description, 1, :CreatedBy, 1, :NowTime, :NowTime
                                                 )";

                string realInsertStorageSql = string.Format(insertStorageSqlTemplate, schema); // 生成最终 SQL
                DateTime now = DateTime.Now; // 获取当前时间，用于创建和更新时间字段

                // 3. 执行插入 (达梦数据库通常不直接在 Insert 时返回 ID，需后续查询)
                await conn.ExecuteAsync(
                    realInsertStorageSql,
                    new
                    {
                        CategoryId = req.CategoryId,            // 显式映射分类 ID
                        CategoryType = req.CategoryType,        // 分类类型
                        FileName = req.FileName,                // 文件名
                        FileStoredName = req.FileStoredName,    // 文件存储名
                        DisplayName = req.DisplayName,          // 文件显示名
                        FileType = req.FileType,                // 文件类型
                        FileHash = req.FileHash,                // 文件哈希值
                        BlockName = req.BlockName,              // 块名称
                        LayerName = req.LayerName,              // 图层名称
                        ColorIndex = req.ColorIndex,            // 颜色索引
                        Scale = req.Scale,                      // 缩放比例
                        FilePath = req.FilePath,                // 文件路径
                        PreviewImageName = req.PreviewImageName,// 预览图片名称
                        PreviewImagePath = req.PreviewImagePath,// 预览图片路径
                        FileSize = req.FileSize,                // 文件大小
                        IsPreview = string.IsNullOrWhiteSpace(req.PreviewImagePath) ? 0 : 1,// 是否预览图片
                        Description = req.Description,          // 描述
                        CreatedBy = req.CreatedBy,              // 创建者
                        NowTime = now                           // 当前时间
                    },
                    tx
                );
                _logger.LogInformation($"[DM] Storage 记录插入执行完成。");

                // 4. 回查 Storage ID (通过唯一文件名 file_stored_name 查询)
                string storageIdCol = await ResolveDmIdColumnNameAsync(conn, tx, schema, "CAD_FILE_STORAGE", new[] { "ID", "FILE_ID", "STORAGE_ID" });
                string selectStorageIdSql = string.Format(@"
                                                            SELECT {0}
                                                            FROM {1}.CAD_FILE_STORAGE
                                                            WHERE file_stored_name = :FileStoredName
                                                            ORDER BY created_at DESC
                                                            FETCH FIRST 1 ROWS ONLY", storageIdCol, schema);

                int storageId = await conn.ExecuteScalarAsync<int>(selectStorageIdSql, new { FileStoredName = req.FileStoredName }, tx);

                if (storageId <= 0)
                {
                    throw new Exception("DM 回查 storageId 失败，可能插入未成功或文件名冲突。");
                }
                _logger.LogInformation($"[DM] 回查 StorageId 成功: {storageId}");

                // 5. 插入属性 JSON 表
                string insertAttrSql = string.Format(@"
                                                     INSERT INTO {0}.CAD_BLOCK_ATTRIBUTES_JSON
                                                     (file_id, config_name, attributes_json, created_at, updated_at)
                                                     VALUES
                                                     (:FileId, :ConfigName, :AttributesJson, :NowTime, :NowTime)", schema);

                await conn.ExecuteAsync(
                    insertAttrSql,
                    new
                    {
                        FileId = storageId,
                        ConfigName = "default",
                        AttributesJson = req.AttributesJson,
                        NowTime = now
                    },
                    tx
                );
                _logger.LogInformation($"[DM] Attribute 记录插入执行完成。");

                // 6. 回查 Attr ID
                string attrIdCol = await ResolveDmIdColumnNameAsync(conn, tx, schema, "CAD_BLOCK_ATTRIBUTES_JSON", new[] { "ATTR_ID", "ID", "ATTRIBUTE_ID" });
                string selectAttrIdSql = string.Format(@"
                                          SELECT {0}
                                         FROM {1}.CAD_BLOCK_ATTRIBUTES_JSON
                                         WHERE file_id = :FileId
                                         ORDER BY created_at DESC
                                         FETCH FIRST 1 ROWS ONLY", attrIdCol, schema);

                int attrId = await conn.ExecuteScalarAsync<int>(selectAttrIdSql, new { FileId = storageId }, tx);

                if (attrId <= 0)
                {
                    throw new Exception("DM 回查 attrId 失败。");
                }
                _logger.LogInformation($"[DM] 回查 AttrId 成功: {attrId}");

                // 7. 更新 Storage 表，关联 Attribute ID
                string updateSql = string.Format(@"
                                                    UPDATE {0}.CAD_FILE_STORAGE
                                                    SET file_attribute_id = :AttrId, updated_at = :NowTime
                                                    WHERE {1} = :StorageId", schema, storageIdCol);

                await conn.ExecuteAsync(updateSql, new { AttrId = attrId, NowTime = now, StorageId = storageId }, tx);
                _logger.LogInformation($"[DM] Storage 表关联 AttributeId 更新成功。");

                // 8. 提交事务
                tx.Commit();
                _logger.LogInformation($"[DM] 事务提交成功。");

                return new UploadDbResult(storageId, attrId);
            }
            catch (Exception ex)
            {
                tx.Rollback(); // 异常回滚
                _logger.LogError(ex, "[DM Error] 事务回滚，保存记录失败。");
                throw;
            }
        }

        /// <summary>
        /// 解决 DM 数据库列名同步问题，动态获取主键列名
        /// </summary>
        private async Task<string> ResolveDmIdColumnNameAsync(
            DmConnection conn,
            IDbTransaction tx,
            string owner,
            string tableName,
            string[] preferred)
        {
            // 查询系统表获取指定表的所有列名
            const string sql = @"
                               SELECT COLUMN_NAME
                               FROM ALL_TAB_COLUMNS
                               WHERE OWNER = :Owner
                                 AND TABLE_NAME = :TableName";

            var cols = (await conn.QueryAsync<string>(sql, new { Owner = owner, TableName = tableName }, tx)).ToList();
            if (cols.Count == 0) throw new Exception("DM 列信息读取失败: " + owner + "." + tableName);

            // 优先匹配常用的主键名称
            foreach (var p in preferred)
            {
                var hit = cols.FirstOrDefault(c => string.Equals(c, p, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(hit)) return hit;
            }

            // 如果没有匹配到常用名，返回第一列作为主键（兜底策略）
            return cols[0];
        }

        #endregion

        #region 查询与路径解析（双库）

        private async Task<StorageRow?> GetStorageRowAsync(int storageId)
        {
            string dbType = GetDatabaseType(); // 获取数据库类型
            if (dbType == "DM")
            {
                // 达梦查询逻辑
                string connStr = GetActiveConnectionString("DM");
                string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant();

                string sql = string.Format(@"
                             SELECT
                                 id AS Id,
                                 category_id AS CategoryId,
                                 category_type AS CategoryType,
                                 file_name AS FileName,
                                 file_stored_name AS FileStoredName,
                                 file_path AS FilePath,
                                 preview_image_name AS PreviewImageName,
                                 preview_image_path AS PreviewImagePath
                             FROM {0}.CAD_FILE_STORAGE
                             WHERE id = :Id
                             FETCH FIRST 1 ROWS ONLY", schema);

                using var conn = new DmConnection(connStr);
                return await conn.QueryFirstOrDefaultAsync<StorageRow>(sql, new { Id = storageId });
            }
            else
            {
                // MySQL 查询逻辑
                string connStr = GetActiveConnectionString("MySQL");
                const string sql = @"
                              SELECT
                                  id AS Id,
                                  category_id AS CategoryId,
                                  category_type AS CategoryType,
                                  file_name AS FileName,
                                  file_stored_name AS FileStoredName,
                                  file_path AS FilePath,
                                  preview_image_name AS PreviewImageName,
                                  preview_image_path AS PreviewImagePath
                              FROM cad_file_storage
                              WHERE id = @Id
                              LIMIT 1";

                using var conn = new MySqlConnection(connStr);
                return await conn.QueryFirstOrDefaultAsync<StorageRow>(sql, new { Id = storageId });
            }
        }

        private string ResolvePhysicalPath(StorageRow row)
        {
            // 如果数据库中存储了绝对路径且文件存在，直接使用
            if (!string.IsNullOrWhiteSpace(row.FilePath) && System.IO.File.Exists(row.FilePath))
                return row.FilePath;

            // 否则根据配置根路径重新拼接
            string root = GetStorageRoot();
            if (string.IsNullOrWhiteSpace(root))
                return row.FilePath ?? string.Empty;

            string fileName = !string.IsNullOrWhiteSpace(row.FileStoredName) ? row.FileStoredName : row.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
                return row.FilePath ?? string.Empty;

            return Path.Combine(root, row.CategoryType ?? "sub", row.CategoryId.ToString(), fileName);
        }

        private string ResolvePreviewPath(StorageRow row)
        {
            // 优先使用数据库中存储的预览图绝对路径
            if (!string.IsNullOrWhiteSpace(row.PreviewImagePath) && System.IO.File.Exists(row.PreviewImagePath))
                return row.PreviewImagePath;

            // 如果只有预览图文件名，则在主文件目录下查找
            if (!string.IsNullOrWhiteSpace(row.PreviewImageName))
            {
                string graphicPath = ResolvePhysicalPath(row);
                string? dir = Path.GetDirectoryName(graphicPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    string candidate = Path.Combine(dir, row.PreviewImageName);
                    if (System.IO.File.Exists(candidate))
                        return candidate;
                }
            }

            return row.PreviewImagePath ?? string.Empty;
        }

        #endregion

        #region 通用工具

        private string GetDatabaseType()
        {
            // 从配置中读取数据库类型，默认为 DM
            string t = (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant();
            return t == "MYSQL" ? "MySQL" : "DM";
        }

        private string GetActiveConnectionString(string dbType)
        {
            // 优先读取 Database:ConnectionString 通用配置
            string conn = (_configuration["Database:ConnectionString"] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(conn))
                return conn;

            // 兜底按类型读取特定的 ConnectionStrings 配置
            string key = dbType == "MySQL" ? "MySQL" : "DM";
            conn = (_configuration.GetConnectionString(key) ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(conn))
                throw new InvalidOperationException("缺少连接串配置，请检查 Database:ConnectionString 或 ConnectionStrings:" + key);

            return conn;
        }

        private string GetStorageRoot()
        {
            // 读取存储根路径配置，支持多种配置键名
            string root = (_configuration["Storage:Root"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(root))
                root = (_configuration["UploadStorage:RootPath"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("缺少存储根路径配置（Storage:Root / UploadStorage:RootPath）。");
            return root;
        }

        private static Dictionary<string, string> ParseAttributes(string? json)
        {
            // 解析 JSON 字符串为字典，忽略大小写
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json.Trim());
                return dict ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // 解析失败返回空字典，避免崩溃
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string? GetAttr(Dictionary<string, string> attrs, string key)
        {
            // 安全地从字典中获取属性值
            if (attrs == null) return null;
            if (attrs.TryGetValue(key, out var value)) return value;
            return null;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            // 返回第一个非空且非空白字符串
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return string.Empty;
        }

        private static int FirstValidInt(string? v1, string? v2, string? v3, int fallback)
        {
            // 尝试解析多个字符串为整数，返回第一个有效的正整数，否则返回默认值
            if (int.TryParse((v1 ?? string.Empty).Trim(), out var i1) && i1 > 0) return i1;
            if (int.TryParse((v2 ?? string.Empty).Trim(), out var i2) && i2 > 0) return i2;
            if (int.TryParse((v3 ?? string.Empty).Trim(), out var i3) && i3 > 0) return i3;
            return fallback;
        }

        private static double FirstValidDouble(string? v1, string? v2, string? v3, double fallback)
        {
            // 尝试解析多个字符串为双精度浮点数，返回第一个有效的正数，否则返回默认值
            if (double.TryParse((v1 ?? string.Empty).Trim(), out var d1) && d1 > 0) return d1;
            if (double.TryParse((v2 ?? string.Empty).Trim(), out var d2) && d2 > 0) return d2;
            if (double.TryParse((v3 ?? string.Empty).Trim(), out var d3) && d3 > 0) return d3;
            return fallback;
        }

        private static async Task<string> ComputeSha256Async(string filePath)
        {
            // 计算文件的 SHA256 哈希值
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static void TryDeleteFile(string? path)
        {
            // 尝试删除文件，如果失败则忽略，避免影响主流程
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch { }
        }

        /// <summary>
        /// 获取内容类型
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static string GetContentType(string path)
        {
            string ext = Path.GetExtension(path);//通过路径拿到后缀
            ext = string.IsNullOrWhiteSpace(ext) ? string.Empty : ext.ToLowerInvariant();

            switch (ext)
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".bmp": return "image/bmp";
                case ".gif": return "image/gif";
                case ".dwg": return "application/octet-stream";
                default: return "application/octet-stream";
            }
        }

        #endregion

        #region 内部模型
        /// <summary>
        /// 储存行
        /// </summary>
        private sealed class StorageRow
        {
            public int Id { get; set; }
            public int CategoryId { get; set; }
            public string? CategoryType { get; set; }
            public string? FileName { get; set; }
            public string? FileStoredName { get; set; }
            public string? FilePath { get; set; }
            public string? PreviewImageName { get; set; }
            public string? PreviewImagePath { get; set; }
        }
        /// <summary>
        /// 上传数据库请求
        /// </summary>
        private sealed class UploadDbRequest
        {
            /// <summary>
            /// 分类id
            /// </summary>
            public int CategoryId { get; set; }
            /// <summary>
            /// 分类类型
            /// </summary>
            public string CategoryType { get; set; } = string.Empty;
            /// <summary>
            /// 文件名
            /// </summary>
            public string FileName { get; set; } = string.Empty;
            /// <summary>
            /// 文件储存名
            /// </summary>
            public string FileStoredName { get; set; } = string.Empty;
            /// <summary>
            /// 显示名
            /// </summary>
            public string DisplayName { get; set; } = string.Empty;
            /// <summary>
            /// 文件类型
            /// </summary>
            public string FileType { get; set; } = string.Empty;
            public string FileHash { get; set; } = string.Empty;
            public string BlockName { get; set; } = string.Empty;
            public string LayerName { get; set; } = string.Empty;
            public int ColorIndex { get; set; }
            public double Scale { get; set; } = 1.0;
            public string FilePath { get; set; } = string.Empty;
            public string? PreviewImageName { get; set; }
            public string? PreviewImagePath { get; set; }
            public long FileSize { get; set; }
            public string Description { get; set; } = string.Empty;
            public string CreatedBy { get; set; } = string.Empty;
            public string AttributesJson { get; set; } = "{}";
        }
        /// <summary>
        /// 上传数据结果
        /// </summary>
        private sealed class UploadDbResult
        {
            /// <summary>
            /// 上传数据结果
            /// </summary>
            /// <param name="storageId"></param>
            /// <param name="attrId"></param>
            public UploadDbResult(int storageId, int attrId)
            {
                StorageId = storageId;// 储存id
                AttrId = attrId;//属性id
            }
            /// <summary>
            /// 储存id
            /// </summary>
            public int StorageId { get; }
            /// <summary>
            /// 属性id
            /// </summary>
            public int AttrId { get; }
        }

        #endregion
    }
}