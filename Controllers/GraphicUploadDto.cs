using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace GB_NewCadPlus_IV_Server.Models.Dtos
{
    public class GraphicUploadDto
    {
        // 基础信息
        public int CategoryId { get; set; }

        // 移除 [Required]，允许前端不传，后端给默认值
        public string? CategoryType { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? CreatedBy { get; set; }
        public string? AttributesJson { get; set; }

        // 文件本身
        public IFormFile? DwgFile { get; set; }
    }
}