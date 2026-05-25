using GB_NewCadPlus_IV.UploadApi.Services;
using GB_NewCadPlus_IV_Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LogLevel = GB_NewCadPlus_IV_Server.Services.LogLevel;

namespace GB_NewCadPlus_IV.UploadApi.Filters
{
    /// <summary>
    /// 操作日志过滤器：记录每次 API 请求的详细信息，便于分析客户端/服务器端问题。
    /// 记录内容：时间、客户端IP、请求方法/路径、参数摘要、响应状态码、耗时、异常信息。
    /// </summary>
    public class OperationLogFilter : IAsyncActionFilter
    {
        private readonly HourlyFileLogger _logger;

        public OperationLogFilter(HourlyFileLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var sw = Stopwatch.StartNew();
            var request = context.HttpContext.Request;

            // 1. 获取客户端信息
            string clientIp = GetClientIp(context.HttpContext);
            string method = request.Method;
            string path = request.Path;
            string queryString = request.QueryString.HasValue ? request.QueryString.Value! : string.Empty;

            // 2. 获取关键请求参数摘要（避免日志过大，只取前 500 字符）
            string? bodySummary = null;
            if (request.ContentLength > 0 && (method == "POST" || method == "PUT"))
            {
                try
                {
                    request.EnableBuffering();
                    using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                    string body = await reader.ReadToEndAsync();
                    request.Body.Position = 0;
                    bodySummary = body.Length > 500 ? body.Substring(0, 500) + "..." : body;
                }
                catch
                {
                    bodySummary = "(无法读取请求体)";
                }
            }

            // 3. 记录请求开始
            var sb = new StringBuilder();
            sb.AppendLine($"========== 请求开始 ==========");
            sb.AppendLine($"  客户端IP    : {clientIp}");
            sb.AppendLine($"  请求方式    : {method}");
            sb.AppendLine($"  请求路径    : {path}");
            if (!string.IsNullOrEmpty(queryString))
                sb.AppendLine($"  查询字符串  : {queryString}");
            if (bodySummary != null)
                sb.AppendLine($"  请求体摘要  : {bodySummary}");
            sb.AppendLine($"  请求时间    : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

            _logger.WriteLine(LogLevel.Info, sb.ToString());

            // 4. 执行实际动作
            Exception? occurredException = null;
            ActionExecutedContext? resultContext = null;

            try
            {
                resultContext = await next();
            }
            catch (Exception ex)
            {
                occurredException = ex;
            }

            sw.Stop();

            // 5. 记录响应结果
            var resultSb = new StringBuilder();
            resultSb.AppendLine($"========== 请求结束 ==========");
            resultSb.AppendLine($"  请求路径    : {path}");
            resultSb.AppendLine($"  耗时(ms)    : {sw.ElapsedMilliseconds}");

            if (occurredException != null)
            {
                // ---- 服务器端异常 ----
                resultSb.AppendLine($"  结果        : ❌ 服务器端异常");
                resultSb.AppendLine($"  异常类型    : {occurredException.GetType().FullName}");
                resultSb.AppendLine($"  异常消息    : {occurredException.Message}");
                resultSb.AppendLine($"  堆栈摘要    : {occurredException.StackTrace?[..Math.Min(occurredException.StackTrace?.Length ?? 0, 1000)]}");
                _logger.WriteLine(LogLevel.Error, resultSb.ToString());
            }
            else if (resultContext != null)
            {
                int statusCode = resultContext.HttpContext.Response.StatusCode;

                if (statusCode >= 200 && statusCode < 300)
                {
                    resultSb.AppendLine($"  结果        : ✅ 成功 (HTTP {statusCode})");
                    _logger.WriteLine(LogLevel.Info, resultSb.ToString());
                }
                else if (statusCode >= 400 && statusCode < 500)
                {
                    // 4xx = 客户端问题
                    resultSb.AppendLine($"  结果        : ⚠️ 客户端错误 (HTTP {statusCode})");
                    if (resultContext.Result != null)
                        resultSb.AppendLine($"  返回详情    : {resultContext.Result}");
                    _logger.WriteLine(LogLevel.Warning, resultSb.ToString());
                }
                else if (statusCode >= 500)
                {
                    // 5xx = 服务器端问题
                    resultSb.AppendLine($"  结果        : ❌ 服务器端错误 (HTTP {statusCode})");
                    if (resultContext.Exception != null)
                    {
                        resultSb.AppendLine($"  异常类型    : {resultContext.Exception.GetType().FullName}");
                        resultSb.AppendLine($"  异常消息    : {resultContext.Exception.Message}");
                    }
                    _logger.WriteLine(LogLevel.Error, resultSb.ToString());
                }
                else
                {
                    resultSb.AppendLine($"  结果        : HTTP {statusCode}");
                    _logger.WriteLine(LogLevel.Info, resultSb.ToString());
                }
            }

            // 如果是服务器端异常，重新抛出以让框架处理
            if (occurredException != null)
                throw occurredException;
        }

        /// <summary>
        /// 获取客户端真实 IP（考虑反向代理 / 负载均衡）
        /// </summary>
        private static string GetClientIp(HttpContext context)
        {
            // 优先从 X-Forwarded-For 头获取（适用于 Nginx/反向代理场景）
            string? forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',')[0].Trim();

            // 其次从 X-Real-IP 头获取
            string? realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(realIp))
                return realIp.Trim();

            // 最后使用直接连接的远程 IP
            return context.Connection.RemoteIpAddress?.ToString() ?? "未知";
        }
    }
}