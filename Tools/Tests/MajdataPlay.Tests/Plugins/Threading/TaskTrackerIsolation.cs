using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MajdataPlay.Threading;
using UnityEngine.SceneManagement;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Threading
{
    /// <summary>
    /// Saves the static state of <see cref="TaskTracker"/> (history, running registrations and switches) when created and
    /// restores it when disposed, so tests may register, clear and reinitialize freely.
    /// </summary>
    public sealed class TaskTrackerIsolation : IDisposable
    {
        /// <summary>The saved history entries.</summary>
        private readonly List<DictionaryEntry> _entries;

        /// <summary>The saved running registrations.</summary>
        private readonly List<DictionaryEntry> _activeEntries;

        /// <summary>The saved tracking switch.</summary>
        private readonly bool _enableTracking;

        /// <summary>The saved stack trace switch.</summary>
        private readonly bool _enableStackTrace;

        /// <summary>The saved cached scene name.</summary>
        private readonly string _activeScene;

        /// <summary>Saves the current state.</summary>
        public TaskTrackerIsolation()
        {
            lock (Gate)
            {
                _entries = Copy(Entries);
                _activeEntries = Copy(ActiveEntries);
            }
            _enableTracking = TaskTracker.EnableTracking;
            _enableStackTrace = TaskTracker.EnableStackTrace;
            _activeScene = PrivateMember.GetStaticField<string>(typeof(TaskTracker), "_activeScene");
        }

        /// <summary>Gets the lock that guards the tracker's dictionaries.</summary>
        public static object Gate => PrivateMember.GetStaticField<object>(typeof(TaskTracker), "Gate");

        /// <summary>Gets the history dictionary.</summary>
        public static IDictionary Entries => PrivateMember.GetStaticField<IDictionary>(typeof(TaskTracker), "_entries");

        /// <summary>Gets the running registrations dictionary.</summary>
        public static IDictionary ActiveEntries => PrivateMember.GetStaticField<IDictionary>(typeof(TaskTracker), "_activeEntries");

        /// <summary>Gets the scene description the tracker records for the current active scene.</summary>
        /// <returns>The scene path, an unsaved scene marker or the no-scene marker.</returns>
        public static string DescribeActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return "No active scene";
            }
            return string.IsNullOrEmpty(scene.path) ? scene.name + " (unsaved)" : scene.path;
        }

        /// <summary>Finds the snapshots registered under a name.</summary>
        /// <param name="name">The registration name.</param>
        /// <returns>The matching snapshots.</returns>
        public static TaskTrackerSnapshot[] Find(string name)
        {
            return TaskTracker.GetSnapshots().Where(x => x.Name == name).ToArray();
        }

        /// <summary>Finds the single snapshot registered under a name.</summary>
        /// <param name="name">The registration name.</param>
        /// <returns>The snapshot.</returns>
        /// <exception cref="InvalidOperationException">No or several snapshots have that name.</exception>
        public static TaskTrackerSnapshot Single(string name)
        {
            return Find(name).Single();
        }

        /// <summary>Creates a unique registration or scene name.</summary>
        /// <param name="prefix">A readable prefix.</param>
        /// <returns>The unique name.</returns>
        public static string UniqueName(string prefix)
        {
            return prefix + "-" + Guid.NewGuid().ToString("N");
        }

        /// <summary>Restores the saved state; running registrations of tasks that completed meanwhile are not restored.</summary>
        public void Dispose()
        {
            lock (Gate)
            {
                Restore(Entries, _entries, false);
                Restore(ActiveEntries, _activeEntries, true);
            }
            TaskTracker.EnableTracking = _enableTracking;
            TaskTracker.EnableStackTrace = _enableStackTrace;
            PrivateMember.SetStaticField(typeof(TaskTracker), "_activeScene", _activeScene);
        }

        /// <summary>Copies the pairs of a dictionary.</summary>
        /// <param name="source">The dictionary to copy.</param>
        /// <returns>The copied pairs.</returns>
        private static List<DictionaryEntry> Copy(IDictionary source)
        {
            var pairs = new List<DictionaryEntry>(source.Count);
            foreach (DictionaryEntry pair in source)
            {
                pairs.Add(pair);
            }
            return pairs;
        }

        /// <summary>Replaces the content of a dictionary with saved pairs.</summary>
        /// <param name="target">The dictionary to restore.</param>
        /// <param name="pairs">The saved pairs.</param>
        /// <param name="skipCompleted">Whether pairs whose task has completed are dropped.</param>
        private static void Restore(IDictionary target, List<DictionaryEntry> pairs, bool skipCompleted)
        {
            target.Clear();
            foreach (var pair in pairs)
            {
                if (skipCompleted && ((Task)pair.Key).IsCompleted)
                {
                    continue;
                }
                target.Add(pair.Key, pair.Value);
            }
        }
    }
}
