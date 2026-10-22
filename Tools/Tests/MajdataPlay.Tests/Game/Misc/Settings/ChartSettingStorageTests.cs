using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO.Storage;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using MajdataPlay.Utils;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>Covers loading, unit conversion and saving of per-chart settings in <see cref="ChartSettingStorage"/>.</summary>
    public class ChartSettingStorageTests
    {
        /// <summary>The settings file name used by the storage.</summary>
        private const string StorageFileName = "ChartSetting.db";

        /// <summary>The temporary directory acting as the storage root.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The entries present before the test.</summary>
        private KeyValuePair<string, ChartSetting>[] _savedEntries = Array.Empty<KeyValuePair<string, ChartSetting>>();

        /// <summary>The initialization flag before the test.</summary>
        private bool _savedIsInited;

        /// <summary>The storage directory before the test.</summary>
        private StorageDirectory? _savedDirectory;

        /// <summary>The offset unit before the test.</summary>
        private OffsetUnitOption _savedOffsetUnit;

        /// <summary>Gets the private chart settings dictionary.</summary>
        private static Dictionary<string, ChartSetting> Storage => PrivateMember.GetStaticField<Dictionary<string, ChartSetting>>(typeof(ChartSettingStorage), "_storage");

        /// <summary>Resets the storage and points it at a temporary directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
            _savedEntries = Storage.ToArray();
            _savedIsInited = PrivateMember.GetStaticField<bool>(typeof(ChartSettingStorage), "_isInited");
            _savedDirectory = PrivateMember.GetStaticField<StorageDirectory?>(typeof(ChartSettingStorage), "s_storageDirectory");
            _savedOffsetUnit = MajEnv.Settings.Debug.OffsetUnit;
            Storage.Clear();
            PrivateMember.SetStaticField(typeof(ChartSettingStorage), "_isInited", false);
            PrivateMember.SetStaticField(typeof(ChartSettingStorage), "s_storageDirectory", _temporary.Directory);
            MajEnv.Settings.Debug.OffsetUnit = OffsetUnitOption.Frame;
        }

        /// <summary>Restores the storage state and removes the save handler registered by initialization.</summary>
        [TearDown]
        public void TearDown()
        {
            var handler = (EventHandler<EventArgs?>)Delegate.CreateDelegate(typeof(EventHandler<EventArgs?>), GetOnSave());
            GameManager.OnSave -= handler;
            Storage.Clear();
            foreach (var pair in _savedEntries)
            {
                Storage[pair.Key] = pair.Value;
            }
            PrivateMember.SetStaticField(typeof(ChartSettingStorage), "_isInited", _savedIsInited);
            PrivateMember.SetStaticField(typeof(ChartSettingStorage), "s_storageDirectory", _savedDirectory);
            MajEnv.Settings.Debug.OffsetUnit = _savedOffsetUnit;
            _temporary.Dispose();
        }

        /// <summary>Gets the private save handler.</summary>
        /// <returns>The handler method.</returns>
        private static MethodInfo GetOnSave()
        {
            return typeof(ChartSettingStorage).GetMethod("OnSave", BindingFlags.Static | BindingFlags.NonPublic)!;
        }

        /// <summary>Invokes the private save handler.</summary>
        private static void Save()
        {
            GetOnSave().Invoke(null, new object?[] { null, null });
        }

        /// <summary>Checks that a missing settings file leaves the storage empty and marks it initialized.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_WithoutFile_StartsEmpty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ChartSettingStorage.InitAsync();
                await UniTask.SwitchToMainThread();

                Assert.That(PrivateMember.GetStaticField<bool>(typeof(ChartSettingStorage), "_isInited"), Is.True);
                Assert.That(Storage, Is.Empty);
            });
        }

        /// <summary>Checks that stored settings are loaded, converted to the current unit and deduplicated.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_LoadsAndConvertsSettings()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _temporary.WriteText(StorageFileName,
                    "[{\"Hash\":\"seconds\",\"Unit\":0,\"AudioOffset\":0.05,\"TrackVolumeOffset\":0.5}," +
                    "{\"Hash\":\"frames\",\"Unit\":1,\"AudioOffset\":3}," +
                    "{\"Hash\":\"\",\"Unit\":1,\"AudioOffset\":9}," +
                    "{\"Hash\":\"seconds\",\"Unit\":1,\"AudioOffset\":7}]");

                await ChartSettingStorage.InitAsync();
                await UniTask.SwitchToMainThread();

                Assert.That(Storage.Keys, Is.EquivalentTo(new[] { "seconds", "frames" }));
                var converted = ChartSettingStorage.GetSetting("seconds");
                Assert.That(converted.Unit, Is.EqualTo(OffsetUnitOption.Frame));
                Assert.That(converted.AudioOffset, Is.EqualTo(3f).Within(1e-4f));
                Assert.That(converted.TrackVolumeOffset, Is.EqualTo(0.5f));
                Assert.That(ChartSettingStorage.GetSetting("frames").AudioOffset, Is.EqualTo(3f));
            });
        }

        /// <summary>Checks that frame offsets are converted to seconds when seconds are configured.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_ConvertsFramesToSeconds()
        {
            return UniTask.ToCoroutine(async () =>
            {
                MajEnv.Settings.Debug.OffsetUnit = OffsetUnitOption.Second;
                _temporary.WriteText(StorageFileName, "[{\"Hash\":\"frames\",\"Unit\":1,\"AudioOffset\":6}]");

                await ChartSettingStorage.InitAsync();
                await UniTask.SwitchToMainThread();

                var setting = ChartSettingStorage.GetSetting("frames");
                Assert.That(setting.Unit, Is.EqualTo(OffsetUnitOption.Second));
                Assert.That(setting.AudioOffset, Is.EqualTo(0.1f).Within(1e-5f));
            });
        }

        /// <summary>Checks that an unreadable settings file is backed up and ignored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_CorruptFile_IsBackedUp()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _temporary.WriteText(StorageFileName, "{ corrupt");

                await ChartSettingStorage.InitAsync();
                await UniTask.SwitchToMainThread();

                var backups = _temporary.Directory.EnumerateEntries()
                                                  .Where(entry => entry.Name.StartsWith(StorageFileName + ".") && entry.Name.EndsWith(".bak"))
                                                  .ToArray();
                Assert.That(backups, Has.Length.EqualTo(1));
                Assert.That(Storage, Is.Empty);
                Assert.That(_temporary.Directory.FindFile(StorageFileName), Is.Not.Null);
            });
        }

        /// <summary>Checks that a second initialization does not reload the file.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_IsIdempotent()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ChartSettingStorage.InitAsync();
                _temporary.WriteText(StorageFileName, "[{\"Hash\":\"late\",\"Unit\":1,\"AudioOffset\":1}]");

                await ChartSettingStorage.InitAsync();
                await UniTask.SwitchToMainThread();

                Assert.That(Storage.ContainsKey("late"), Is.False);
            });
        }

        /// <summary>Checks that unknown charts get a new setting in the current unit, created once.</summary>
        [Test]
        public void GetSetting_CreatesDefaultsOnce()
        {
            var song = new FakeSongDetail("new-hash");

            var first = ChartSettingStorage.GetSetting(song);
            var second = ChartSettingStorage.GetSetting("new-hash");

            Assert.That(first, Is.SameAs(second));
            Assert.That(first.Hash, Is.EqualTo("new-hash"));
            Assert.That(first.Unit, Is.EqualTo(OffsetUnitOption.Frame));
            Assert.That(first.AudioOffset, Is.EqualTo(0f));
        }

        /// <summary>Checks the conversion of every stored offset between seconds and frames.</summary>
        [Test]
        public void ConvertUnit_ConvertsStoredOffsets()
        {
            var setting = ChartSettingStorage.GetSetting("convert");
            setting.AudioOffset = 6f;

            ChartSettingStorage.ConvertUnitToSecond();
            Assert.That(setting.Unit, Is.EqualTo(OffsetUnitOption.Second));
            Assert.That(setting.AudioOffset, Is.EqualTo(0.1f).Within(1e-5f));

            ChartSettingStorage.ConvertUnitToSecond();
            Assert.That(setting.AudioOffset, Is.EqualTo(0.1f).Within(1e-5f));

            ChartSettingStorage.ConvertUnitToFrame();
            Assert.That(setting.Unit, Is.EqualTo(OffsetUnitOption.Frame));
            Assert.That(setting.AudioOffset, Is.EqualTo(6f).Within(1e-4f));

            ChartSettingStorage.ConvertUnitToFrame();
            Assert.That(setting.AudioOffset, Is.EqualTo(6f).Within(1e-4f));
        }

        /// <summary>Checks that saving writes every setting once initialization has completed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnSave_WritesSettings()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ChartSettingStorage.InitAsync();
                await UniTask.SwitchToMainThread();
                ChartSettingStorage.GetSetting("saved").AudioOffset = 2f;

                Save();

                var file = _temporary.Directory.FindFile(StorageFileName);
                Assert.That(file, Is.Not.Null);
                var stored = Serializer.Json.Deserialize<ChartSetting[]>(file!.ReadAllText())!;
                Assert.That(stored.Single().Hash, Is.EqualTo("saved"));
                Assert.That(stored.Single().AudioOffset, Is.EqualTo(2f));

                Save();
                Assert.That(Serializer.Json.Deserialize<ChartSetting[]>(file.ReadAllText())!, Has.Length.EqualTo(1));
            });
        }

        /// <summary>Checks that saving before initialization writes nothing.</summary>
        [Test]
        public void OnSave_BeforeInit_WritesNothing()
        {
            ChartSettingStorage.GetSetting("unsaved");

            Save();

            Assert.That(_temporary.Directory.FindFile(StorageFileName), Is.Null);
        }
    }
}
