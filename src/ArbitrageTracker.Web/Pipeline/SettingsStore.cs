using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Sizing;

namespace ArbitrageTracker.Web.Pipeline;

public sealed class SettingsStore
{
    private readonly object _gate = new();
    public DetectionSettings Detection { get; private set; } = DetectionSettings.Default;
    public SizingSettings Sizing { get; private set; } = new();

    public event Action? Changed;

    public void Update(DetectionSettings detection, SizingSettings sizing)
    {
        lock (_gate)
        {
            Detection = detection;
            Sizing = sizing;
        }
        Changed?.Invoke();
    }
}
