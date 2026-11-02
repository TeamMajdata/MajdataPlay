#nullable enable
using MajdataPlay.Net.Curl.Diagnostics;
using System;
using UnityEditor;
using UnityEngine;

namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// An Editor window for debugging the libcurl HTTP handler (<see cref="Curl.CurlHttpMessageHandler"/>). Its Network
    /// tab lists running and finished requests like a browser's network panel; its Info tab shows libcurl itself, the
    /// request counters and the connection pools.
    /// </summary>
    /// <remarks>
    /// Request recording through <see cref="CurlNetworkMonitor"/> is on while the window is open and the Network tab's
    /// Record toggle is on. The history lives in memory, so a domain reload (entering Play Mode with domain reload
    /// enabled) clears it.
    /// </remarks>
    public sealed class CurlDebuggerWindow : EditorWindow
    {
        /// <summary>
        /// The window title.
        /// </summary>
        private const string WindowTitle = "Curl Debugger";

        /// <summary>
        /// The time between checks for new data, in seconds.
        /// </summary>
        private const double RefreshInterval = 0.25;

        /// <summary>
        /// The tab names, in <see cref="CurlDebuggerTab"/> order.
        /// </summary>
        private static readonly string[] s_tabNames = { "Network", "Info" };

        /// <summary>
        /// The selected tab.
        /// </summary>
        [SerializeField]
        private CurlDebuggerTab _tab;

        /// <summary>
        /// The Network tab.
        /// </summary>
        [SerializeField]
        private CurlNetworkPanel _network = new();

        /// <summary>
        /// The Info tab.
        /// </summary>
        [SerializeField]
        private CurlInfoPanel _info = new();

        /// <summary>
        /// The editor time of the next check for new data.
        /// </summary>
        private double _nextRefresh;

        /// <summary>
        /// Gets the selected tab.
        /// </summary>
        internal CurlDebuggerTab Tab
        {
            get => _tab;
        }

        /// <summary>
        /// Opens the window.
        /// </summary>
        [MenuItem("Window/Curl Debugger")]
        public static void OpenWindow()
        {
            GetWindow<CurlDebuggerWindow>(WindowTitle).Show();
        }

        /// <summary>
        /// Shows a tab and loads its data at once.
        /// </summary>
        /// <param name="tab">The tab to show.</param>
        internal void SelectTab(CurlDebuggerTab tab)
        {
            _tab = tab;
            RefreshSelectedTab();
            Repaint();
        }

        /// <summary>
        /// Enables the panels, which applies the recording switch and loads the data.
        /// </summary>
        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(640, 320);
            _network.Enable(Repaint);
            _info.Refresh();
        }

        /// <summary>
        /// Disables the panels.
        /// </summary>
        private void OnDisable()
        {
            _network.Disable();
        }

        /// <summary>
        /// Stops recording once the window is closed.
        /// </summary>
        private void OnDestroy()
        {
            CurlNetworkMonitor.IsEnabled = false;
        }

        /// <summary>
        /// Refreshes the visible tab and repaints it when it changed or shows live durations.
        /// </summary>
        private void Update()
        {
            if (EditorApplication.timeSinceStartup < _nextRefresh)
            {
                return;
            }
            _nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;
            if (_tab == CurlDebuggerTab.Info)
            {
                _info.Refresh();
                Repaint();
            }
            else if (_network.Update())
            {
                Repaint();
            }
        }

        /// <summary>
        /// Draws the tab strip and the selected tab.
        /// </summary>
        private void OnGUI()
        {
            var tabStripHeight = EditorStyles.toolbar.fixedHeight > 0 ? EditorStyles.toolbar.fixedHeight : 21f;
            GUILayout.BeginArea(new Rect(0, 0, position.width, tabStripHeight));
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var tab = (CurlDebuggerTab)GUILayout.Toolbar((int)_tab, s_tabNames, EditorStyles.toolbarButton, GUILayout.Width(160));
                if (tab != _tab)
                {
                    _tab = tab;
                    RefreshSelectedTab();
                }
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndArea();
            var content = new Rect(0, tabStripHeight, position.width, Math.Max(0, position.height - tabStripHeight));
            if (_tab == CurlDebuggerTab.Info)
            {
                _info.OnGUI(content);
            }
            else
            {
                _network.OnGUI(content);
            }
        }

        /// <summary>
        /// Loads the latest data of the selected tab.
        /// </summary>
        private void RefreshSelectedTab()
        {
            if (_tab == CurlDebuggerTab.Info)
            {
                _info.Refresh();
            }
            else
            {
                _network.Refresh();
            }
        }
    }
}
