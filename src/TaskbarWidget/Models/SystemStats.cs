namespace TaskbarWidget.Models;

public sealed class SystemStats
{
    public double UploadBps { get; set; }
    public double DownloadBps { get; set; }
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
    public double MemoryUsedGb { get; set; }
    public double MemoryTotalGb { get; set; }
}
