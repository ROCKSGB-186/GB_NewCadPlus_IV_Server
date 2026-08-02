namespace GB_NewCadPlus_IV.UploadApi.Models;

public sealed class GraphicListResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<GraphicDto> Files { get; init; } = Array.Empty<GraphicDto>();
}

public sealed class GraphicDto
{
    public int Id { get; init; }
    public int CategoryId { get; init; }
    public string CategoryType { get; init; } = string.Empty;
    public string? FileAttributeId { get; init; }
    public string? FileName { get; init; }
    public string? FileStoredName { get; init; }
    public string? DisplayName { get; init; }
    public string? FileType { get; init; }
    public string? FileHash { get; init; }
    public string? BlockName { get; init; }
    public string? LayerName { get; init; }
    public int? ColorIndex { get; init; }
    public double? Scale { get; init; }
    public string? FilePath { get; init; }
    public string? PreviewImageName { get; init; }
    public string? PreviewImagePath { get; init; }
    public long? FileSize { get; init; }
    public int IsPreview { get; init; }
    public int Version { get; init; }
    public string? Description { get; init; }
    public int IsActive { get; init; }
    public string? CreatedBy { get; init; }
    public string? Title { get; init; }
    public string? Keywords { get; init; }
    public int IsPublic { get; init; }
    public string? UpdatedBy { get; init; }
    public DateTime? LastAccessedAt { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
