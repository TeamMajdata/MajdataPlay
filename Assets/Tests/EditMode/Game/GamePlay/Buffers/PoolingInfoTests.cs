using MajdataPlay.Game.Notes;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Touch;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Buffers
{
    /// <summary>Checks the defaults and stored values of the note pooling and queue infos.</summary>
    public class PoolingInfoTests
    {
        /// <summary>Checks the default queue infos and that each access creates a new instance.</summary>
        [Test]
        public void QueueInfoDefaults_PointAtTheFirstKeyAndTheCentre()
        {
            Assert.That(TapQueueInfo.Default.Index, Is.EqualTo(0));
            Assert.That(TapQueueInfo.Default.KeyIndex, Is.EqualTo(1));
            Assert.That(TapQueueInfo.Default, Is.Not.SameAs(TapQueueInfo.Default));
            Assert.That(TouchQueueInfo.Default.Index, Is.EqualTo(0));
            Assert.That(TouchQueueInfo.Default.SensorPos, Is.EqualTo(SensorArea.C));
        }

        /// <summary>Checks the defaults of the tap and hold infos and the values stored by an initializer.</summary>
        [Test]
        public void TapAndHoldInfos_StoreTheirValues()
        {
            var binding = new EachLineBinding();
            var tap = new TapPoolingInfo
            {
                StartPos = 3,
                AppearTiming = 0.5f,
                Timing = 1.5f,
                NoteSortOrder = 7,
                Speed = 8f,
                IsEach = true,
                IsBreak = true,
                IsMine = true,
                IsEX = true,
                IsStar = true,
                IsDouble = true,
                RotateSpeed = -90f,
                QueueInfo = new TapQueueInfo { Index = 4, KeyIndex = 3 },
                EachLineBinding = binding,
            };
            var hold = new HoldPoolingInfo { LastFor = 2f };

            Assert.That((tap.StartPos, tap.AppearTiming, tap.Timing, tap.NoteSortOrder, tap.Speed), Is.EqualTo((3, 0.5f, 1.5f, 7, 8f)));
            Assert.That(tap.IsEach && tap.IsBreak && tap.IsMine && tap.IsEX && tap.IsStar && tap.IsDouble, Is.True);
            Assert.That(tap.RotateSpeed, Is.EqualTo(-90f));
            Assert.That(tap.QueueInfo.Index, Is.EqualTo(4));
            Assert.That(tap.EachLineBinding, Is.SameAs(binding));
            Assert.That(tap.Instance, Is.Null);
            Assert.That(hold.LastFor, Is.EqualTo(2f));
            Assert.That(hold.QueueInfo.KeyIndex, Is.EqualTo(1));
            Assert.That(hold.EachLineBinding, Is.Null);
        }

        /// <summary>Checks the touch and touch hold infos, including their group references.</summary>
        [Test]
        public void TouchInfos_StoreTheirValuesAndGroups()
        {
            var group = new TouchGroup();
            var holdGroup = new TouchHoldGroup();
            var touch = new TouchPoolingInfo { AreaPos = 'B', IsFirework = true, SensorPos = SensorArea.B4 };
            var touchHold = new TouchHoldPoolingInfo { AreaPos = 'C', LastFor = 1f, SensorPos = SensorArea.C };

            touch.GroupInfo = group;
            touchHold.GroupInfo = group;
            touchHold.TouchHoldGroupInfo = holdGroup;

            Assert.That((touch.AreaPos, touch.IsFirework, touch.SensorPos), Is.EqualTo(('B', true, SensorArea.B4)));
            Assert.That(touch.QueueInfo.SensorPos, Is.EqualTo(SensorArea.C));
            Assert.That(touch.GroupInfo, Is.SameAs(group));
            Assert.That((touchHold.AreaPos, touchHold.LastFor, touchHold.SensorPos, touchHold.IsFirework), Is.EqualTo(('C', 1f, SensorArea.C, false)));
            Assert.That(touchHold.GroupInfo, Is.SameAs(group));
            Assert.That(touchHold.TouchHoldGroupInfo, Is.SameAs(holdGroup));
        }

        /// <summary>Checks the each line info, which links two members through a distance provider.</summary>
        [Test]
        public void EachLineInfo_LinksItsMembers()
        {
            var a = new TapPoolingInfo { StartPos = 1 };
            var b = new HoldPoolingInfo { StartPos = 3 };
            var binding = new EachLineBinding();
            var info = new EachLinePoolingInfo { MemberA = a, MemberB = b, CurvLength = 2, DistanceProvider = binding };

            Assert.That(info.MemberA, Is.SameAs(a));
            Assert.That(info.MemberB, Is.SameAs(b));
            Assert.That(info.CurvLength, Is.EqualTo(2));
            Assert.That(info.DistanceProvider, Is.SameAs(binding));
        }

        /// <summary>Checks the slide queue info.</summary>
        [Test]
        public void SlideQueueInfo_StoresItsTiming()
        {
            var info = new SlideQueueInfo { Index = 5, AppearTiming = 2.5f };

            Assert.That(info.Index, Is.EqualTo(5));
            Assert.That(info.AppearTiming, Is.EqualTo(2.5f));
            Assert.That(info.SlideObject, Is.Null);
        }
    }
}
