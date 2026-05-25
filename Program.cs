using GB_NewCadPlus_IV.UploadApi.Services;
using GB_NewCadPlus_IV.UploadApi.Filters;
using GB_NewCadPlus_IV_Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// 注册上传写库服务（达梦实现）
builder.Services.AddScoped<GraphicUploadDmService>();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ========== 新增：操作日志服务（按小时滚动）==========
// 日志存放路径：可以从配置读取，也可以硬编码
string logDir = builder.Configuration.GetValue<string>("Logging:OperationLogPath")
                ?? Path.Combine(AppContext.BaseDirectory, "OperationLogs");

// 注册为单例，整个应用生命周期内使用同一个实例
builder.Services.AddSingleton(new HourlyFileLogger(logDir, "GraphicsOperation"));

// 注册 OperationLogFilter（需要注入 HourlyFileLogger）
builder.Services.AddScoped<OperationLogFilter>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
//app.UseSwagger();
//app.UseSwaggerUI();
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
// 每 6 小时清理一次超过 30 天的日志
var logger = app.Services.GetRequiredService<HourlyFileLogger>();
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromHours(6));
        logger.CleanOldLogs(30);
    }
});// 每 6 小时清理一次超过 30 天的日志
