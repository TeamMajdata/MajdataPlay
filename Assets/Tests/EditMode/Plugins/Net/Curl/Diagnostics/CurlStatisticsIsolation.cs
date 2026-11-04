#nullable enable
using System;
using System.Collections.Generic;
using MajdataPlay.Net.Curl.Diagnostics;

namespace MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics
{
    /// <summary>
    /// Saves the static state of <see cref="CurlStatistics"/> (the handler list and the tracking switch), empties the
    /// list for the test and restores both when disposed, so a Curl Debugger open during the run keeps its handlers.
    /// </summary>
    internal sealed class CurlStatisticsIsolation : IDisposable
    {
        /// <summary>The saved handler list, oldest first.</summary>
        private readonly CurlHandlerStatistics[] _handlers;

        /// <summary>The saved connection tracking switch.</summary>
        private readonly bool _isConnectionTrackingEnabled;

        /// <summary>Saves the current state, empties the handler list and turns connection tracking on.</summary>
        public CurlStatisticsIsolation()
        {
            _isConnectionTrackingEnabled = CurlStatistics.IsConnectionTrackingEnabled;
            lock (Gate)
            {
                _handlers = Handlers.ToArray();
                Handlers.Clear();
            }
            CurlStatistics.IsConnectionTrackingEnabled = true;
        }

        /// <summary>Gets the lock that guards the handler list.</summary>
        public static object Gate => PrivateMember.GetStaticField<object>(typeof(CurlStatistics), "s_sync");

        /// <summary>Gets the handler list.</summary>
        public static List<CurlHandlerStatistics> Handlers => PrivateMember.GetStaticField<List<CurlHandlerStatistics>>(typeof(CurlStatistics), "s_handlers");

        /// <summary>Restores the saved handler list and tracking switch, dropping the handlers the test registered.</summary>
        public void Dispose()
        {
            lock (Gate)
            {
                Handlers.Clear();
                Handlers.AddRange(_handlers);
            }
            CurlStatistics.IsConnectionTrackingEnabled = _isConnectionTrackingEnabled;
        }
    }
}
