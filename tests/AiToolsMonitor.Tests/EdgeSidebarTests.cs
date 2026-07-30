using AiToolsMonitor.Shell;

namespace AiToolsMonitor.Tests;

public class EdgeSidebarTests
{
    [Fact]
    public void EdgeSidebar_HasStableNativeWindowIdentity()
    {
        using var sidebar = new EdgeSidebarTab();

        Assert.Equal("AI Tools Monitor Edge Sidebar", sidebar.Text);
    }
}
