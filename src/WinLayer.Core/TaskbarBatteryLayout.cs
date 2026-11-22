using System.Drawing;

namespace WinLayer.Core;

public static class TaskbarBatteryLayout
{
    public static Rectangle FindBand(Rectangle taskbar, Rectangle notificationArea, int gap)
    {
        if (taskbar.Height <= 0 || taskbar.Width < 2L * taskbar.Height ||
            notificationArea.Width <= 0 || notificationArea.Height <= 0 || gap < 0 ||
            !taskbar.IntersectsWith(notificationArea) || notificationArea.Left <= taskbar.Left)
            return Rectangle.Empty;

        var right = Math.Min(taskbar.Right, notificationArea.Left) - gap;
        var capacity = Math.Min(right - taskbar.Left, taskbar.Width / 3);
        return capacity > 0 ? new(right - capacity, taskbar.Top, capacity, taskbar.Height) : Rectangle.Empty;
    }
}
