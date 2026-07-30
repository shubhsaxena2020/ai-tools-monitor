using System.Reflection;
using AiToolsMonitor.Popup;

namespace AiToolsMonitor.Tests;

public class PopupTests
{
    [Fact]
    public void QuotaProgressBar_IsDecorativeAndCannotReceiveFocus()
    {
        Type progressBarType = typeof(StatusPopup).Assembly.GetType(
            "AiToolsMonitor.Popup.QuotaProgressBar",
            throwOnError: true)!;

        object progressBar = Activator.CreateInstance(progressBarType)!;
        MethodInfo getStyle = progressBarType.GetMethod(
            "GetStyle",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Type controlStylesType = getStyle.GetParameters().Single().ParameterType;
        object selectableStyle = Enum.Parse(controlStylesType, "Selectable");

        try
        {
            Assert.False((bool)progressBarType.GetProperty("TabStop")!.GetValue(progressBar)!);
            Assert.False((bool)getStyle.Invoke(progressBar, [selectableStyle])!);
        }
        finally
        {
            ((IDisposable)progressBar).Dispose();
        }
    }
}
