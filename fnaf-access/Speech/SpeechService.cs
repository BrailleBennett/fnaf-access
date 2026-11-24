using Refractor;

namespace fnaf_access.Speech;

/// <summary>
/// Thread-safe speech and braille output service backed by Prism via Refractor.
/// Automatically selects the best available output backend (running screen reader first,
/// falling back to a system speech engine) and switches dynamically if availability changes.
/// </summary>
public sealed class SpeechService : IDisposable
{
    private readonly Lock _sync = new();
    private Prism? _prism;
    private Backend? _backend;
    private BackendFeatures _features;
    private bool _disposed;

    public string ActiveBackendName
    {
        get
        {
            lock (_sync)
            {
                return _backend?.Name ?? "None";
            }
        }
    }

    public BackendFeatures ActiveFeatures
    {
        get
        {
            lock (_sync)
            {
                return _features;
            }
        }
    }

    public bool IsScreenReader => (ActiveFeatures & BackendFeatures.IsScreenReader) != 0;
    public bool SupportsCombinedOutput => (ActiveFeatures & BackendFeatures.Output) != 0;

    public SpeechService()
    {
        _prism = new Prism(new PrismOptions
        {
            AvailabilityChanged = OnAvailabilityChanged
        });

        AcquireBestLocked();
    }

    private void OnAvailabilityChanged(BackendId id, string name, bool isAvailable)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            AcquireBestLocked();
        }
    }

    private void AcquireBestLocked()
    {
        if (_prism is null)
        {
            return;
        }

        try
        {
            Backend? previous = _backend;
            Backend next = _prism.CreateBest();
            BackendFeatures nextFeatures = next.GetFeatures();

            _backend = next;
            _features = nextFeatures;

            if (previous is not null)
            {
                try
                {
                    previous.Dispose();
                }
                catch
                {
                    // Ignore disposal errors from a backend that may have just exited.
                }
            }
        }
        catch (PrismException)
        {
            // Keep existing backend if re-acquisition fails transiently.
        }
    }

    /// <summary>
    /// Outputs text using speech and braille together when supported by the active backend,
    /// otherwise falls back to speech.
    /// </summary>
    public void Output(string text, bool interrupt = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_backend is null)
            {
                AcquireBestLocked();
                if (_backend is null)
                {
                    return;
                }
            }

            if (!TryOutputLocked(text, interrupt))
            {
                // Backend may have gone away (e.g., screen reader restarted); retry once.
                AcquireBestLocked();
                if (_backend is not null)
                {
                    TryOutputLocked(text, interrupt);
                }
            }
        }
    }

    private bool TryOutputLocked(string text, bool interrupt)
    {
        if (_backend is null)
        {
            return false;
        }

        try
        {
            if ((_features & BackendFeatures.Output) != 0)
            {
                _backend.Output(text, interrupt);
                return true;
            }

            if ((_features & BackendFeatures.Speak) != 0)
            {
                _backend.Speak(text, interrupt);
                return true;
            }

            if ((_features & BackendFeatures.Braille) != 0)
            {
                _backend.Braille(text);
                return true;
            }

            return false;
        }
        catch (PrismException ex) when (ex.Error == PrismError.NotImplemented)
        {
            try
            {
                _backend.Speak(text, interrupt);
                return true;
            }
            catch (PrismException)
            {
                return false;
            }
        }
        catch (PrismException)
        {
            return false;
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_disposed || _backend is null)
            {
                return;
            }

            if ((_features & BackendFeatures.Stop) == 0)
            {
                return;
            }

            try
            {
                _backend.Stop();
            }
            catch (PrismException)
            {
                // Ignore if backend cannot stop.
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_backend is not null)
            {
                try
                {
                    if ((_features & BackendFeatures.Stop) != 0)
                    {
                        _backend.Stop();
                    }
                }
                catch
                {
                    // Ignore stop failures during shutdown.
                }

                try
                {
                    _backend.Dispose();
                }
                catch
                {
                    // Ignore dispose failures during shutdown.
                }

                _backend = null;
            }

            _prism?.Dispose();
            _prism = null;
        }
    }
}
