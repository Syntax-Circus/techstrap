using System.Diagnostics.Metrics;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>
/// A <see cref="MeterListener"/> on one <see cref="TechStrapMetrics"/> instance only: other hosts in the same test process create meters with the same name, so the listener
/// subscribes to an instrument only when it belongs to the meter it was given.
/// </summary>
internal sealed class MetricsProbe : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _sums = [];
    private readonly Dictionary<string, long> _lastObserved = [];
    private readonly List<string> _published = [];

    public MetricsProbe(TechStrapMetrics metrics)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == metrics.Meter)
            {
                lock (_gate)
                {
                    _published.Add($"{instrument.Name}:{instrument.GetType().Name}");
                }

                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => Record(instrument, value));
        _listener.SetMeasurementEventCallback<int>((instrument, value, _, _) => Record(instrument, value));
        _listener.Start();
    }

    public IReadOnlyList<string> Published
    {
        get
        {
            lock (_gate)
            {
                return [.. _published];
            }
        }
    }

    /// <summary>The total of a counter's increments so far.</summary>
    public long Sum(string instrument)
    {
        lock (_gate)
        {
            return _sums.GetValueOrDefault(instrument);
        }
    }

    /// <summary>Asks every observable instrument for its current value and returns the gauge's.</summary>
    public long Observe(string instrument)
    {
        _listener.RecordObservableInstruments();
        lock (_gate)
        {
            return _lastObserved.GetValueOrDefault(instrument, -1);
        }
    }

    private void Record(Instrument instrument, long value)
    {
        lock (_gate)
        {
            if (instrument is ObservableGauge<int>)
            {
                _lastObserved[instrument.Name] = value;
            }
            else
            {
                _sums[instrument.Name] = _sums.GetValueOrDefault(instrument.Name) + value;
            }
        }
    }

    public void Dispose() => _listener.Dispose();
}
