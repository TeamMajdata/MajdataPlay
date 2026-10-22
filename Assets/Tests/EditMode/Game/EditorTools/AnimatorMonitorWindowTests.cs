using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Editor.Windows;
using MajdataPlay.Runtime.Monitors;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>Covers the animator monitor window's row model without drawing it.</summary>
    public class AnimatorMonitorWindowTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>The window under test.</summary>
        private AnimatorMonitorWindow _window = null!;

        /// <summary>Creates the window and the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _window = ScriptableObject.CreateInstance<AnimatorMonitorWindow>();
        }

        /// <summary>Destroys the window and the test's objects.</summary>
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_window);
            _objects.Dispose();
        }

        /// <summary>Gets the animator names of the current rows.</summary>
        /// <returns>The names in row order.</returns>
        private List<string> RowNames()
        {
            var rows = PrivateMember.GetField<IList>(_window, "_rows");
            var names = new List<string>();
            foreach (var row in rows)
            {
                var animator = (Animator)row.GetType().GetField("Animator")!.GetValue(row);
                names.Add(animator.name);
            }
            return names;
        }

        /// <summary>Adds animators to the window's tracked list.</summary>
        /// <param name="animators">The animators.</param>
        private void Track(params Animator[] animators)
        {
            PrivateMember.GetField<List<Animator>>(_window, "_animators").AddRange(animators);
        }

        /// <summary>Checks that active animators become rows sorted by name and receive a monitor component.</summary>
        [Test]
        public void Refresh_ListsActiveAnimatorsSortedByName()
        {
            var second = _objects.Add<Animator>("Zeta");
            var first = _objects.Add<Animator>("Alpha");
            var hidden = _objects.Add<Animator>("Hidden");
            hidden.gameObject.SetActive(false);
            Track(second, first, hidden);

            PrivateMember.Invoke(_window, "Refresh");

            Assert.That(RowNames(), Is.EqualTo(new[] { "Alpha", "Zeta" }));
            Assert.That(first.GetComponent<AnimatorMonitor>(), Is.Not.Null);
        }

        /// <summary>Checks that inactive animators are listed when the active filter is off and monitors are reused.</summary>
        [Test]
        public void Refresh_IncludesInactiveWhenFilterIsOff()
        {
            var hidden = _objects.Add<Animator>("Hidden");
            hidden.gameObject.SetActive(false);
            Track(hidden);
            PrivateMember.SetField(_window, "_onlyActive", false);

            PrivateMember.Invoke(_window, "Refresh");
            PrivateMember.Invoke(_window, "Refresh");

            Assert.That(RowNames(), Is.EqualTo(new[] { "Hidden" }));
            Assert.That(hidden.GetComponents<AnimatorMonitor>().Length, Is.EqualTo(1));
        }

        /// <summary>Checks that the updated-only filter hides animators that did not update this frame.</summary>
        [Test]
        public void Refresh_UpdatedOnlyHidesIdleAnimators()
        {
            Track(_objects.Add<Animator>("Idle"));
            PrivateMember.SetField(_window, "_onlyUpdated", true);

            PrivateMember.Invoke(_window, "Refresh");

            Assert.That(RowNames(), Is.Empty);
        }

        /// <summary>Checks that refreshing the object list finds scene animators, including inactive ones, unless disabled.</summary>
        [Test]
        public void RefreshObjectList_FindsSceneAnimators()
        {
            var animator = _objects.Add<Animator>("Found");
            animator.gameObject.SetActive(false);

            PrivateMember.SetField(_window, "_autoRefreshObjects", false);
            PrivateMember.Invoke(_window, "RefreshObjectList");
            Assert.That(PrivateMember.GetField<List<Animator>>(_window, "_animators"), Has.No.Member(animator));

            PrivateMember.SetField(_window, "_autoRefreshObjects", true);
            PrivateMember.Invoke(_window, "RefreshObjectList");
            Assert.That(PrivateMember.GetField<List<Animator>>(_window, "_animators"), Does.Contain(animator));
        }

        /// <summary>Checks that destroyed animators are dropped from the monitor cache on the next object refresh.</summary>
        [Test]
        public void RefreshObjectList_DropsDestroyedAnimators()
        {
            var animator = _objects.Add<Animator>("Destroyed");
            Track(animator);
            PrivateMember.Invoke(_window, "Refresh");
            var cache = PrivateMember.GetField<IDictionary>(_window, "_cachedMinitors");
            var count = cache.Count;

            Object.DestroyImmediate(animator.gameObject);
            PrivateMember.Invoke(_window, "RefreshObjectList");

            Assert.That(cache.Count, Is.EqualTo(count - 1));
        }

        /// <summary>Checks that the editor update does nothing outside play mode.</summary>
        [Test]
        public void EditorUpdate_IgnoredOutsidePlayMode()
        {
            Track(_objects.Add<Animator>("Idle"));

            PrivateMember.Invoke(_window, "EditorUpdate");

            Assert.That(RowNames(), Is.Empty);
            Assert.That(PrivateMember.GetField<double>(_window, "_lastRefreshTime"), Is.EqualTo(0d));
        }

        /// <summary>Checks the culling mode labels.</summary>
        [Test]
        public void GetCullingMode_NamesEveryMode()
        {
            var animator = _objects.Add<Animator>("Culling");
            var labels = new List<string>();
            foreach (var mode in new[] { AnimatorCullingMode.AlwaysAnimate, AnimatorCullingMode.CullUpdateTransforms, AnimatorCullingMode.CullCompletely })
            {
                animator.cullingMode = mode;
                labels.Add(PrivateMember.InvokeStatic<string>(typeof(AnimatorMonitorWindow), "GetCullingMode", animator));
            }

            Assert.That(labels, Is.EqualTo(new[] { "Always Update", "Cull Update Transforms", "Cull Completely" }));
        }

        /// <summary>Checks that disabling the window releases its profiler recorder.</summary>
        [Test]
        public void OnDisable_ReleasesRecorder()
        {
            PrivateMember.Invoke(_window, "OnDisable");

            Assert.That(PrivateMember.GetField<object?>(_window, "_processAnimationsRecorder"), Is.Null);
        }
    }
}
