using GB_NewCadPlus_IV.UploadApi.Services;
using GB_NewCadPlus_IV.UploadApi.Filters;
using GB_NewCadPlus_IV_Server.Services;


 // 1. 创建 Web 应用: 初始化 ASP.NET Core 应用构建器，它会读取配置文件（如 appsettings.json）、环境变量等。
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// 2. 注册控制器服务: 将 MVC 控制器添加到依赖注入容器中(支持 REST API），使其能够处理 HTTP 请求。
builder.Services.AddControllers();


// 3. 注册业务依赖（依赖注入）将 GraphicUploadDmService 注册为 Scoped 生命周期的服务（每个 HTTP 请求创建一个实例）。
// 从命名看，它应该是一个使用达梦数据库实现的上传写库服务，用于处理图形数据持久化。
builder.Services.AddScoped<GraphicUploadDmService>();

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

// 6. 配置开发环境中间件: 定义 HTTP 请求处理流程。根据环境条件启用 Swagger UI，强制使用 HTTPS，启用授权中间件，并映射控制器路由。
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
} // 仅在开发环境启用 Swagger 页面和 JSON 端点，生产环境自动关闭。

// 7. 标准 HTTP 中间件配置：
app.UseHttpsRedirection(); // 强制重定向到 HTTPS

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
