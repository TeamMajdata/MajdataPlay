using System;
using System.Reflection;
using MajdataPlay.i18n;
using NUnit.Framework;
using Unity.IL2CPP.CompilerServices;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Covers the marker and option attributes of the Base, i18n and IL2CPP helper code.</summary>
    public class AttributeTests
    {
        /// <summary>Checks the player loop timing selected by each callback attribute.</summary>
        [Test]
        public void PlayerLoopCallbackAttributes_SelectTiming()
        {
            Assert.That(new PlayerLoopCallbackAttribute().Timing, Is.EqualTo(PlayerLoopCallbackAttribute.LoopTiming.Update));
            Assert.That(new OnPreUpdateAttribute().Timing, Is.EqualTo(PlayerLoopCallbackAttribute.LoopTiming.PreUpdate));
            Assert.That(new OnUpdateAttribute().Timing, Is.EqualTo(PlayerLoopCallbackAttribute.LoopTiming.Update));
            Assert.That(new OnLateUpdateAttribute().Timing, Is.EqualTo(PlayerLoopCallbackAttribute.LoopTiming.LateUpdate));
            Assert.That(new OnFixedUpdateAttribute().Timing, Is.EqualTo(PlayerLoopCallbackAttribute.LoopTiming.FixedUpdate));
            Assert.That(new PlayerLoopCallbackAttribute { Timing = PlayerLoopCallbackAttribute.LoopTiming.LateUpdate }.Timing,
                        Is.EqualTo(PlayerLoopCallbackAttribute.LoopTiming.LateUpdate));
        }

        /// <summary>Checks that the persistent-object marker can only be applied once to classes.</summary>
        [Test]
        public void DontDestroyOnLoadAttribute_TargetsClassesOnce()
        {
            var usage = typeof(DontDestroyOnLoadAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

            Assert.That(usage.ValidOn, Is.EqualTo(AttributeTargets.Class));
            Assert.That(usage.AllowMultiple, Is.False);
            Assert.That(usage.Inherited, Is.False);
        }

        /// <summary>Checks that the IL2CPP option attribute stores the option and its value.</summary>
        [Test]
        public void Il2CppSetOptionAttribute_StoresOption()
        {
            var attribute = new Il2CppSetOptionAttribute(Option.ArrayBoundsChecks, false);

            Assert.That(attribute.Option, Is.EqualTo(Option.ArrayBoundsChecks));
            Assert.That(attribute.Value, Is.EqualTo(false));
        }

        /// <summary>Checks that the font locale attribute treats a missing locale as the default font.</summary>
        [Test]
        public void FontLcidAttribute_NullBecomesDefault()
        {
            Assert.That(new FontLCIDAttribute("zh-CN").LCID, Is.EqualTo("zh-CN"));
            Assert.That(new FontLCIDAttribute(null!).LCID, Is.Empty);
        }

        /// <summary>Checks that the editor read-only marker is a property drawer attribute for fields.</summary>
        [Test]
        public void ReadOnlyFieldAttribute_IsFieldPropertyAttribute()
        {
            var usage = typeof(MajdataPlay.Editor.ReadOnlyFieldAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

            Assert.That(new MajdataPlay.Editor.ReadOnlyFieldAttribute(), Is.InstanceOf<UnityEngine.PropertyAttribute>());
            Assert.That(usage.ValidOn, Is.EqualTo(AttributeTargets.Field));
        }

        /// <summary>Checks that the translation opt-out marker can be created.</summary>
        [Test]
        public void NoTranslateAttribute_CanBeCreated()
        {
            var usage = typeof(NoTranslateAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

            Assert.That(new NoTranslateAttribute(), Is.Not.Null);
            Assert.That(usage.AllowMultiple, Is.False);
        }
    }
}
