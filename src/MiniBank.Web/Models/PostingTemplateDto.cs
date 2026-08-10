namespace MiniBank.Web.Models
{
    public class PostingTemplateDto
    {
        public int PostingTemplateId { get; set; }
        public string TemplateName { get; set; } = string.Empty;
        public byte? CategoryCode { get; set; }
        public bool IsActive { get; set; }
    }
}
