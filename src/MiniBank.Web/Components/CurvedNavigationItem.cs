using Microsoft.AspNetCore.Components;

namespace MiniBank.Web.Components
{
    public class CurvedNavigationItem
    {
        public RenderFragment Icon { get; set; } = default!;
        public string Label { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public object? Data { get; set; }
    }
}
