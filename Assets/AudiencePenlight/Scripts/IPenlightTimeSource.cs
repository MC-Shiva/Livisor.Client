using UnityEngine;

namespace Livisor.Live.Penlights
{
    /// <summary>
    /// Supplies show time without coupling the renderer to Timeline or networking.
    /// A Timeline-backed implementation can be injected later.
    /// </summary>
    public interface IPenlightTimeSource
    {
        double CurrentTime { get; }
    }

    public sealed class LocalPenlightTimeSource : IPenlightTimeSource
    {
        public double CurrentTime => Time.timeAsDouble;
    }
}
