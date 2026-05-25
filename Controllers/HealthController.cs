using Microsoft.AspNetCore.Mvc; // 引用 ASP.NET Core MVC 命名空间（控制器基类等）
using System.Runtime.InteropServices; // 引用运行时信息 API
using System.Reflection; // 引用反射以读取程序集属性
using System.Diagnostics; // 引用 FileVersionInfo 以获取产品版本信息

namespace GB_NewCadPlus_IV.UploadApi.Controllers
{
    /// <summary>
    /// 健康检查控制器
    /// - 提供基础运行信息，并包含当前服务版本号（从程序集 meta 数据读取）
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        /// <summary>
        /// GET api/health
        /// 返回当前服务的基本健康信息（包含版本号）
        /// </summary>
        /// <returns>JSON 包含状态、主机名、环境、版本及时间戳</returns>
        [HttpGet]
        public IActionResult Get()
        {
            // 尝试读取程序集的版本信息（优先 InformationalVersion，其次 ProductVersion，再次 AssemblyName.Version）
            string version = "Unknown"; // 默认版本占位符

            try
            {
                // 优先尝试从入口程序集（通常为宿主 exe）读取信息版本（可包含 SemVer + 元数据）
                var entryAsm = Assembly.GetEntryAssembly(); // 获取入口程序集（可能为 null）
                if (entryAsm != null)
                {
                    // 尝试读取 AssemblyInformationalVersionAttribute（常用于存放完整发版号如 1.0.0+sha）
                    var infoAttr = entryAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    if (!string.IsNullOrWhiteSpace(infoAttr?.InformationalVersion))
                    {
                        version = infoAttr.InformationalVersion; // 使用信息版本
                    }
                    else
                    {
                        // 如果没有信息版本，尝试使用程序集名称的版本号
                        var asmVer = entryAsm.GetName().Version;
                        if (asmVer != null)
                        {
                            version = asmVer.ToString();
                        }
                    }

                    // 额外尝试：如果仍为空，尝试从文件版本（ProductVersion）读取更友好的版本信息
                    if (version == "Unknown" || string.IsNullOrWhiteSpace(version))
                    {
                        try
                        {
                            var loc = entryAsm.Location; // 程序集文件路径（可能为空，例如某些托管环境）
                            if (!string.IsNullOrWhiteSpace(loc))
                            {
                                var fvi = FileVersionInfo.GetVersionInfo(loc);
                                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                                {
                                    version = fvi.ProductVersion; // 使用文件的产品版本
                                }
                            }
                        }
                        catch { /* 忽略读取文件版本的异常，保持健壮性 */ }
                    }
                }
                else
                {
                    // 当 EntryAssembly 为空时，尝试使用当前执行程序集作为回退
                    var execAsm = Assembly.GetExecutingAssembly();
                    var infoAttr2 = execAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    if (!string.IsNullOrWhiteSpace(infoAttr2?.InformationalVersion))
                    {
                        version = infoAttr2.InformationalVersion;
                    }
                    else
                    {
                        var asmVer2 = execAsm.GetName().Version;
                        if (asmVer2 != null) version = asmVer2.ToString();
                    }
                }
            }
            catch
            {
                // 读取版本失败时保留默认值 "Unknown"；不要抛出异常影响健康检查本身
            }

            // 构造返回对象：包含最小而有用的信息，便于远端排查（不包含敏感配置）
            var info = new
            {
                status = "OK", // 标识服务运行状态
                host = Environment.MachineName, // 主机名
                runtime = RuntimeInformation.FrameworkDescription, // 运行时框架信息 (.NET 版本)
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "NotSet", // 运行环境
                utcNow = DateTime.UtcNow, // 当前 UTC 时间
                version = version // 新增字段：服务版本号
            };

            // 返回 200 和 JSON 数据，便于 curl / 浏览器 / 监控系统快速检查
            return Ok(info);
        }

        /// <summary>
        /// 可选简单端点：GET api/health/ping 返回纯文本 "pong"
        /// 便于一些监控系统做最轻量的可用性检测
        /// </summary>
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Content("pong", "text/plain");
        }
    }
}