using MajdataPlay.Scenes.Game.Notes.Skins;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Skins
{
    /// <summary>Checks that the note skin views expose the sprites they are created with.</summary>
    public class SkinStructTests
    {
        /// <summary>Tracks the textures and sprites created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>The first sprite.</summary>
        private Sprite _a = null!;

        /// <summary>The second sprite.</summary>
        private Sprite _b = null!;

        /// <summary>Creates two distinct sprites.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            var texture = _objects.Track(new Texture2D(2, 2));
            _a = _objects.Track(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero));
            _b = _objects.Track(Sprite.Create(texture, new Rect(1, 1, 1, 1), Vector2.zero));
        }

        /// <summary>Destroys the sprites and textures.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks the tap, star and slide skin views.</summary>
        [Test]
        public void TapStarAndSlideSkins_ExposeTheirSprites()
        {
            var colors = new[] { Color.red };
            var sprites = new[] { _a, _b };
            var tap = new TapSkin { Normal = _a, Each = _b, Break = _a, Ex = _b, Mine = _a, BreakMine = _b, GuideLines = sprites, ExEffects = colors };
            var star = new StarSkin
            {
                Normal = _a, Double = _b, Each = _a, EachDouble = _b, Break = _a, BreakDouble = _b, Ex = _a, ExDouble = _b,
                Mine = _a, DoubleMine = _b, BreakMine = _a, BreakDoubleMine = _b, GuideLines = sprites, ExEffects = colors,
            };
            var slide = new SlideSkin { Star = star, Normal = _a, Each = _b, Break = _a, Mine = _b, BreakMine = _a };

            Assert.That((tap.Normal, tap.Each, tap.Break, tap.Ex, tap.Mine, tap.BreakMine), Is.EqualTo((_a, _b, _a, _b, _a, _b)));
            Assert.That(tap.GuideLines.Length, Is.EqualTo(2));
            Assert.That(tap.ExEffects[0], Is.EqualTo(Color.red));
            Assert.That((star.Normal, star.Double, star.Each, star.EachDouble), Is.EqualTo((_a, _b, _a, _b)));
            Assert.That((star.Break, star.BreakDouble, star.Ex, star.ExDouble), Is.EqualTo((_a, _b, _a, _b)));
            Assert.That((star.Mine, star.DoubleMine, star.BreakMine, star.BreakDoubleMine), Is.EqualTo((_a, _b, _a, _b)));
            Assert.That(star.GuideLines[1], Is.EqualTo(_b));
            Assert.That(star.ExEffects.Length, Is.EqualTo(1));
            Assert.That(slide.Star.Normal, Is.EqualTo(_a));
            Assert.That((slide.Normal, slide.Each, slide.Break, slide.Mine, slide.BreakMine), Is.EqualTo((_a, _b, _a, _b, _a)));
        }

        /// <summary>Checks the hold, wifi and each line skin views.</summary>
        [Test]
        public void HoldWifiAndEachLineSkins_ExposeTheirSprites()
        {
            var sprites = new[] { _a, _b, _a };
            var hold = new HoldSkin
            {
                Normal = _a, Normal_On = _b, Off = _a, Each = _b, Each_On = _a, Break = _b, Break_On = _a, Ex = _b,
                Mine = _a, Mine_On = _b, BreakMine = _a, BreakMine_On = _b, Ends = sprites, GuideLines = sprites, ExEffects = new[] { Color.blue },
            };
            var wifi = new WifiSkin { Star = new StarSkin { Normal = _b }, Normal = sprites, Each = sprites, Break = sprites, Mine = sprites, BreakMine = sprites };
            var eachLine = new EachLineSkin { EachGuideLines = sprites };

            Assert.That((hold.Normal, hold.Normal_On, hold.Off, hold.Each, hold.Each_On, hold.Break), Is.EqualTo((_a, _b, _a, _b, _a, _b)));
            Assert.That((hold.Break_On, hold.Ex, hold.Mine, hold.Mine_On, hold.BreakMine, hold.BreakMine_On), Is.EqualTo((_a, _b, _a, _b, _a, _b)));
            Assert.That(hold.Ends.Length + hold.GuideLines.Length, Is.EqualTo(6));
            Assert.That(hold.ExEffects[0], Is.EqualTo(Color.blue));
            Assert.That(wifi.Star.Normal, Is.EqualTo(_b));
            Assert.That(wifi.Normal.Length + wifi.Each.Length + wifi.Break.Length + wifi.Mine.Length + wifi.BreakMine.Length, Is.EqualTo(15));
            Assert.That(eachLine.EachGuideLines[2], Is.EqualTo(_a));
        }

        /// <summary>Checks the touch and touch hold skin views.</summary>
        [Test]
        public void TouchSkins_ExposeTheirSprites()
        {
            var fans = new[] { _a, _b };
            var touch = new TouchSkin
            {
                Normal = _a, Each = _b, Break = _a, Mine = _b, BreakMine = _a,
                Point_Normal = _b, Point_Each = _a, Point_Break = _b, Point_Mine = _a, Point_Break_Mine = _b,
                Border_Normal = fans, Border_Each = fans, Border_Break = fans, Border_Mine = fans, Border_Break_Mine = fans, JustBorder = _a,
            };
            var touchHold = new TouchHoldSkin
            {
                Fans = fans, Fans_Break = fans, Fans_Mine = fans, Fans_Break_Mine = fans,
                Boader = _a, Boader_Break = _b, Boader_Mine = _a, Boader_Break_Mine = _b,
                Point = _a, Point_Each = _b, Point_Break = _a, Point_Mine = _b, Point_Break_Mine = _a, Off = _b,
            };

            Assert.That((touch.Normal, touch.Each, touch.Break, touch.Mine, touch.BreakMine), Is.EqualTo((_a, _b, _a, _b, _a)));
            Assert.That((touch.Point_Normal, touch.Point_Each, touch.Point_Break, touch.Point_Mine, touch.Point_Break_Mine), Is.EqualTo((_b, _a, _b, _a, _b)));
            Assert.That(touch.Border_Normal.Length + touch.Border_Each.Length + touch.Border_Break.Length + touch.Border_Mine.Length + touch.Border_Break_Mine.Length, Is.EqualTo(10));
            Assert.That(touch.JustBorder, Is.EqualTo(_a));
            Assert.That(touchHold.Fans.Length + touchHold.Fans_Break.Length + touchHold.Fans_Mine.Length + touchHold.Fans_Break_Mine.Length, Is.EqualTo(8));
            Assert.That((touchHold.Boader, touchHold.Boader_Break, touchHold.Boader_Mine, touchHold.Boader_Break_Mine), Is.EqualTo((_a, _b, _a, _b)));
            Assert.That((touchHold.Point, touchHold.Point_Each, touchHold.Point_Break, touchHold.Point_Mine, touchHold.Point_Break_Mine, touchHold.Off), Is.EqualTo((_a, _b, _a, _b, _a, _b)));
        }

        /// <summary>Checks the judge text skin and its fast/late variants.</summary>
        [Test]
        public void JudgeTextSkin_ExposesTheSpecialTexts()
        {
            var special = new SpecialTextSkin { Fast = _a, Normal = _b, Late = null };
            var text = new JudgeTextSkin
            {
                CP_Shine = _a, P_Shine = _b, Break_2600_Shine = _a,
                CriticalPerfect = special, Break_2600 = special, Break_2550 = special, Break_2500 = special, Break_2000 = special,
                Break_1500 = special, Break_1250 = special, Break_1000 = special, Break_0 = _b, Perfect = special, Great = special,
                Good = special, Miss = _a, Fast = _b, Late = _a,
            };

            Assert.That(special.Fast, Is.EqualTo(_a));
            Assert.That(special.Normal, Is.EqualTo(_b));
            Assert.That(special.Late, Is.Null);
            Assert.That((text.CP_Shine, text.P_Shine, text.Break_2600_Shine, text.Break_0, text.Miss, text.Fast, text.Late), Is.EqualTo((_a, _b, _a, _b, _a, _b, _a)));
            Assert.That(text.CriticalPerfect.Normal, Is.EqualTo(_b));
            Assert.That(text.Break_2600.Fast, Is.EqualTo(_a));
            Assert.That(text.Break_2550.Normal, Is.EqualTo(_b));
            Assert.That(text.Break_2500.Normal, Is.EqualTo(_b));
            Assert.That(text.Break_2000.Normal, Is.EqualTo(_b));
            Assert.That(text.Break_1500.Normal, Is.EqualTo(_b));
            Assert.That(text.Break_1250.Normal, Is.EqualTo(_b));
            Assert.That(text.Break_1000.Normal, Is.EqualTo(_b));
            Assert.That(text.Perfect.Normal, Is.EqualTo(_b));
            Assert.That(text.Great.Normal, Is.EqualTo(_b));
            Assert.That(text.Good.Normal, Is.EqualTo(_b));
        }
    }
}
