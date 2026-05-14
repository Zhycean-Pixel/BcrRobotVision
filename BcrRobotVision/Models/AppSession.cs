namespace BcrRobotVision.Models
{
    public enum RunMode
    {
        全部正面 = 1,
        全部反面 = 2
    }

    public static class AppSession
    {
        public static RunMode CurrentMode { get; set; } = RunMode.全部正面;
    }
}