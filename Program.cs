using GB_NewCadPlus_IV.UploadApi.Services;
using GB_NewCadPlus_IV.UploadApi.Filters;
using GB_NewCadPlus_IV_Server.Services;
using Microsoft.AspNetCore.Diagnostics;
using System.Text.Json;


 // 1. 创建 Web 应用: 初始化 ASP.NET Core 应用构建器，它会读取配置文件（如 appsettings.json）、环境变量等。
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// 2. 注册控制器服务: 将 MVC 控制器添加到依赖注入容器中(支持 REST API），使其能够处理 HTTP 请求。
builder.Services.AddControllers();


// 3. 注册业务依赖（依赖注入）将 GraphicUploadDmService 注册为 Scoped 生命周期的服务（每个 HTTP 请求创建一个实例）。
// 从命名看，它应该是一个使用达梦数据库实现的上传写库服务，用于处理图形数据持久化。
builder.Services.AddScoped<GraphicUploadDmService>();

// 注册分类查询服务：分类数据库访问只允许发生在服务器端，客户端通过 HTTP 接口读取分类数据。
builder.Services.AddScoped<CategoryQueryService>();

// 注册分类写入服务：主分类新增操作只允许由服务器访问数据库。
builder.Services.AddScoped<CategoryCommandService>();

// 部门查询服务：客户端通过 HTTP 获取部门列表，数据库只由服务器访问。
builder.Services.AddScoped<DepartmentQueryService>();

// 用户认证、注册及用户/部门写操作统一在服务器执行。
builder.Services.AddScoped<AuthUserDepartmentService>();

// 图形文件查询服务：文件元数据统一由服务器读取。
builder.Services.AddScoped<GraphicQueryService>();

// 注册规范查询服务；规范数据库仍由服务器统一访问，客户端只通过 HTTP 查询。
builder.Services.AddScoped<StandardQueryService>();

// 注册专业规范解析器目录。现有法兰导入仍走兼容入口，后续专业解析器按 FamilyCode 接入。
builder.Services.AddSingleton<IStandardImportParser, FlangeStandardImportParser>();
builder.Services.AddSingleton<IStandardImportParser, PipeStandardImportParser>();
builder.Services.AddSingleton<StandardImportParserRegistry>();

// 注册管道通用字段目录服务；第一阶段提供字段定义、默认值和进口/出口图面样式。
builder.Services.AddScoped<PipelineCatalogService>();

// 注册管道 GB 设计规范匹配服务；当前从 PipelineStandards:Records 配置读取真实记录。
builder.Services.AddScoped<PipelineDesignStandardService>();

// 注册规范资料管理查询服务，目录和版本元数据统一由服务器读取。
// 该查询服务同时被单例导入预览批次使用，因此注册为单例；服务本身只依赖配置和日志，不持有请求级状态。
builder.Services.AddSingleton<StandardManagementQueryService>();

// 注册模板查询和动态 Excel 预览服务；只读预览不影响既有法兰导入写库流程。
builder.Services.AddScoped<StandardTemplateQueryService>();
builder.Services.AddScoped<DynamicStandardPreviewService>();
builder.Services.AddScoped<DynamicStandardImportService>();

// 注册规范管理写入服务，统一处理版本、附件和状态变更。
builder.Services.AddScoped<StandardManagementCommandService>();

// 注册规范附件存储，数据库仅保存文件元数据和相对路径。
builder.Services.AddSingleton<IStandardFileStorage, LocalStandardFileStorage>();

// 导入预览批次需要在预览和确认请求之间短暂保留，因此使用单例内存缓存。
builder.Services.AddSingleton<StandardImportService>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle

//AddEndpointsApiExplorer() + AddSwaggerGen()：启用 Swagger / OpenAPI 文档生成，方便测试和查看接口。
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();



// 4. 配置日志服务：创建 HourlyFileLogger 实例，并注册为单例服务。日志文件路径从配置中读取，如果没有配置则使用默认路径。
//日志目录：优先从配置 Logging:OperationLogPath 读取，否则默认放到程序运行目录下的 OperationLogs 文件夹。
string logDir = builder.Configuration.GetValue<string>("Logging:OperationLogPath")
                ?? Path.Combine(AppContext.BaseDirectory, "OperationLogs");

// 日志实现：HourlyFileLogger 是一个自定义的日志记录器，按小时分割日志文件，文件名包含 GraphicsOperation 前缀。它被注册为 Singleton（全局唯一实例）。
builder.Services.AddSingleton(new HourlyFileLogger(logDir, "GraphicsOperation"));

// 注册日志接口抽象：将 IFileLogService 接口映射到 HourlyFileLogger 实现，使得其他服务可以通过接口依赖注入使用日志功能。
builder.Services.AddSingleton<IFileLogService, HourlyFileLogger>();

// 日志过滤器：OperationLogFilter 是一个 ASP.NET Core 动作过滤器（通常用于自动记录每个 API 请求/响应）。它需要依赖 HourlyFileLogger（通过构造函数注入）。
builder.Services.AddScoped<OperationLogFilter>();



// 5. 构建应用: 创建 Web 应用实例，准备处理 HTTP 请求。根据上面注册的配置构建出可运行的应用对象。
var app = builder.Build();

// 统一异常处理：避免向客户端泄露数据库连接异常和服务器堆栈，详细异常由服务端日志记录。
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalExceptionHandler");

        if (exceptionFeature?.Error != null)
            logger.LogError(exceptionFeature.Error, "未处理的 API 异常。Path={Path}", context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            success = false,
            message = "服务器内部错误，请联系管理员。"
        }));
    });
});

// 6. 配置开发环境中间件: 定义 HTTP 请求处理流程。根据环境条件启用 Swagger UI，强制使用 HTTPS，启用授权中间件，并映射控制器路由。
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
} // 仅在开发环境启用 Swagger 页面和 JSON 端点，生产环境自动关闭。

// 7. 标准 HTTP 中间件配置：
// 当前客户端默认通过 HTTP 访问 API；仅在明确配置时启用 HTTPS 重定向，
// 避免 HTTP 部署环境把请求重定向到不存在的 HTTPS 端口。
if (builder.Configuration.GetValue<bool>("Server:UseHttpsRedirection"))
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();    // 启用授权中间件（若需要身份验证）

app.MapControllers();      // 映射控制器路由


// 8. 后台日志清理任务: 获取 HourlyFileLogger 实例，并启动一个后台任务，每 6 小时执行一次日志清理操作，删除超过 30 天的日志文件。
var logger = app.Services.GetRequiredService<HourlyFileLogger>();
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromHours(6));
        logger.CleanOldLogs(30);        //每隔 6 小时 调用一次 CleanOldLogs(30)，删除 超过 30 天 的日志文件。
    }
});

// 9. 启动应用: 启动 ASP.NET Core 应用，开始监听 HTTP 请求。此时应用已经准备好处理来自客户端的请求了。
app.Run();
