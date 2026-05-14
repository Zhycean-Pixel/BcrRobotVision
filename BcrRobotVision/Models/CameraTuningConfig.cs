namespace BcrRobotVision.Models
{
    public class CameraTuningConfig
    {
        public uint Exposure { get; set; } = 3000;

        public ushort Frame { get; set; } = 1000;

        public uint GrabLineNumber { get; set; } = 320;

        public int TriggerModeIndex { get; set; } = 0;

        public int GrabModeIndex { get; set; } = 0;

        public int DataTypeIndex { get; set; } = 0;
    }
}