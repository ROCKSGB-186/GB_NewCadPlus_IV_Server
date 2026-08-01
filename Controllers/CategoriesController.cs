using GB_NewCadPlus_IV.UploadApi.Filters;
using GB_NewCadPlus_IV.UploadApi.Models;
using GB_NewCadPlus_IV.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_NewCadPlus_IV.UploadApi.Controllers;

/// <summary>
/// 分类查询和写入接口。
/// 分类数据库由服务器统一访问，客户端不再直接连接 MySQL 或达梦。
/// </summary>
[ApiController]
[Route("api/categories")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class CategoriesController : ControllerBase
{
    private readonly CategoryQueryService _categoryQueryService;
    private readonly CategoryCommandService _categoryCommandService;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(
        CategoryQueryService categoryQueryService,
        CategoryCommandService categoryCommandService,
        ILogger<CategoriesController> logger)
    {
        _categoryQueryService = categoryQueryService ?? throw new ArgumentNullException(nameof(categoryQueryService));
        _categoryCommandService = categoryCommandService ?? throw new ArgumentNullException(nameof(categoryCommandService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 获取全部主分类和子分类，供客户端构建分类树。
    /// 请求：GET /api/categories/tree
    /// </summary>
    [HttpGet("tree")]
    [ProducesResponseType(typeof(CategoryTreeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CategoryTreeResponse>> GetTreeAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 服务端统一查询数据库，客户端只接收 DTO，不接触数据库连接。
            CategoryTreeResponse response = await _categoryQueryService
                .GetTreeAsync(cancellationToken)
                .ConfigureAwait(false);

            return Ok(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 客户端主动取消请求时，不把取消当成服务器异常。
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "分类树接口执行失败。");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "分类查询失败，请查看服务器日志。"
            });
        }
    }

    /// <summary>
    /// 删除没有子分类的主分类。
    /// 请求：DELETE /api/categories/{categoryId}
    /// </summary>
    [HttpDelete("{categoryId:int}")]
    [ProducesResponseType(typeof(CategoryDeleteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CategoryDeleteResponse>> DeleteCategoryAsync(
        int categoryId,
        CancellationToken cancellationToken)
    {
        try
        {
            // 服务端统一删除主分类，客户端不直接访问分类数据库。
            CategoryDeleteResponse response = await _categoryCommandService
                .DeleteCategoryAsync(categoryId, cancellationToken)
                .ConfigureAwait(false);

            return Ok(response);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // 主分类 ID 不符合范围时返回 400。
            return BadRequest(new CategoryDeleteResponse
            {
                Success = false,
                Message = ex.Message,
                DeletedId = categoryId
            });
        }
        catch (InvalidOperationException ex)
        {
            // 存在子分类时返回 400，避免误删和孤儿数据。
            return BadRequest(new CategoryDeleteResponse
            {
                Success = false,
                Message = ex.Message,
                DeletedId = categoryId
            });
        }
        catch (KeyNotFoundException ex)
        {
            // 目标主分类不存在时返回 404。
            return NotFound(new CategoryDeleteResponse
            {
                Success = false,
                Message = ex.Message,
                DeletedId = categoryId
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 客户端取消请求时不记录为服务器异常。
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            // 记录服务器异常，但不泄露数据库连接信息。
            _logger.LogError(ex, "删除主分类接口执行失败。CategoryId={CategoryId}", categoryId);
            return StatusCode(StatusCodes.Status500InternalServerError, new CategoryDeleteResponse
            {
                Success = false,
                Message = "主分类删除失败，请查看服务器日志。",
                DeletedId = categoryId
            });
        }
    }

    /// <summary>
    /// 更新主分类或子分类。
    /// 请求：PUT /api/categories/{categoryId}
    /// </summary>
    [HttpPut("{categoryId:int}")]
    [ProducesResponseType(typeof(CategoryUpdateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CategoryUpdateResponse>> UpdateCategoryAsync(
        int categoryId,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 服务端统一更新分类，客户端不直接连接数据库。
            CategoryUpdateResponse response = await _categoryCommandService
                .UpdateCategoryAsync(categoryId, request, cancellationToken)
                .ConfigureAwait(false);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            // 参数不合法时返回 400。
            return BadRequest(new CategoryUpdateResponse
            {
                Success = false,
                Message = ex.Message,
                UpdatedId = categoryId
            });
        }
        catch (KeyNotFoundException ex)
        {
            // 目标分类不存在时返回 404。
            return NotFound(new CategoryUpdateResponse
            {
                Success = false,
                Message = ex.Message,
                UpdatedId = categoryId
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 客户端取消请求时不记录为服务器错误。
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            // 记录服务器异常，但不泄露数据库连接信息。
            _logger.LogError(ex, "更新分类接口执行失败。CategoryId={CategoryId}", categoryId);
            return StatusCode(StatusCodes.Status500InternalServerError, new CategoryUpdateResponse
            {
                Success = false,
                Message = "分类更新失败，请查看服务器日志。",
                UpdatedId = categoryId
            });
        }
    }

    /// <summary>
    /// 新增主分类。
    /// 请求：POST /api/categories
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CategoryMutationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CategoryMutationResponse>> AddCategoryAsync(
        [FromBody] AddCategoryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 服务端统一执行分类写入，客户端不直接访问数据库。
            CategoryMutationResponse response = await _categoryCommandService
                .AddCategoryAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            // 请求参数不符合业务规则时返回 400，便于客户端显示明确提示。
            return BadRequest(new CategoryMutationResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 客户端主动取消请求时，不把取消记录为服务器异常。
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            // 数据库异常记录到服务器日志，但不向客户端泄露连接信息。
            _logger.LogError(ex, "新增主分类接口执行失败。");
            return StatusCode(StatusCodes.Status500InternalServerError, new CategoryMutationResponse
            {
                Success = false,
                Message = "主分类新增失败，请查看服务器日志。"
            });
        }
    }

    /// <summary>
    /// 新增子分类。
    /// 请求：POST /api/categories/{parentId}/subcategories
    /// </summary>
    [HttpPost("{parentId:int}/subcategories")]
    [ProducesResponseType(typeof(SubcategoryMutationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SubcategoryMutationResponse>> AddSubcategoryAsync(
        int parentId,
        [FromBody] AddSubcategoryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 服务端统一新增子分类和更新父级列表，客户端不直接访问数据库。
            SubcategoryMutationResponse response = await _categoryCommandService
                .AddSubcategoryAsync(parentId, request, cancellationToken)
                .ConfigureAwait(false);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            // 参数不合法时返回 400，并把业务提示传给客户端。
            return BadRequest(new SubcategoryMutationResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 客户端取消请求时不记录为服务器错误。
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            // 记录异常但不向客户端泄露数据库连接信息。
            _logger.LogError(ex, "新增子分类接口执行失败。ParentId={ParentId}", parentId);
            return StatusCode(StatusCodes.Status500InternalServerError, new SubcategoryMutationResponse
            {
                Success = false,
                Message = "子分类新增失败，请查看服务器日志。"
            });
        }
    }

    /// <summary>
    /// 删除子分类。
    /// 请求：DELETE /api/categories/subcategories/{subcategoryId}
    /// </summary>
    [HttpDelete("subcategories/{subcategoryId:int}")]
    [ProducesResponseType(typeof(CategoryDeleteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CategoryDeleteResponse>> DeleteSubcategoryAsync(
        int subcategoryId,
        CancellationToken cancellationToken)
    {
        try
        {
            // 服务端统一删除子分类并清理父级列表，客户端不直接访问数据库。
            CategoryDeleteResponse response = await _categoryCommandService
                .DeleteSubcategoryAsync(subcategoryId, cancellationToken)
                .ConfigureAwait(false);

            return Ok(response);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // 子分类 ID 不符合约定时返回 400。
            return BadRequest(new CategoryDeleteResponse
            {
                Success = false,
                Message = ex.Message,
                DeletedId = subcategoryId
            });
        }
        catch (KeyNotFoundException ex)
        {
            // 目标记录不存在时返回 404。
            return NotFound(new CategoryDeleteResponse
            {
                Success = false,
                Message = ex.Message,
                DeletedId = subcategoryId
            });
        }
        catch (InvalidOperationException ex)
        {
            // 目标存在下级子分类时返回 400，提示客户端先处理子级。
            return BadRequest(new CategoryDeleteResponse
            {
                Success = false,
                Message = ex.Message,
                DeletedId = subcategoryId
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 客户端取消请求时不记录为服务器异常。
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            // 记录服务器异常，但不向客户端泄露数据库连接信息。
            _logger.LogError(ex, "删除子分类接口执行失败。SubcategoryId={SubcategoryId}", subcategoryId);
            return StatusCode(StatusCodes.Status500InternalServerError, new CategoryDeleteResponse
            {
                Success = false,
                Message = "子分类删除失败，请查看服务器日志。",
                DeletedId = subcategoryId
            });
        }
    }
}
