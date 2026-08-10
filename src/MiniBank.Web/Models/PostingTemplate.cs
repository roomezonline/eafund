namespace MiniBank.Web.Models
{
    public class PostingTemplate
    {
        public int PostingTemplateId { get; set; }
        public string TemplateName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public byte? CategoryCode { get; set; }
    }
}
