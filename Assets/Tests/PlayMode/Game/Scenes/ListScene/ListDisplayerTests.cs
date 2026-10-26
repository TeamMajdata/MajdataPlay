using System.Collections;
using MajdataPlay.Net;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.List;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ListScene
{
    /// <summary>Covers the score, user and favorite displayers of the song list.</summary>
    public class ListDisplayerTests : ListSceneTestBase
    {
        /// <summary>Creates a fake song whose score record for a level is configured.</summary>
        /// <param name="hash">The song hash.</param>
        /// <param name="level">The chart level.</param>
        /// <param name="playCount">The play count.</param>
        /// <param name="dx">The DX achievement.</param>
        /// <param name="classic">The classic achievement.</param>
        /// <param name="combo">The combo state.</param>
        /// <returns>The song.</returns>
        private static FakeSongDetail ScoredSong(string hash, ChartLevel level, long playCount, double dx, double classic = 0, ComboState combo = ComboState.None)
        {
            var song = new FakeSongDetail(hash, "Scored", "1", "2", "3", "4", "5", "6", "7");
            var score = ScoreManager.GetScore(song, level);
            score.PlayCount = playCount;
            score.Acc = new Accurate { DX = dx, Classic = classic };
            score.ComboState = combo;
            return song;
        }

        /// <summary>Checks that an unplayed chart hides the score.</summary>
        [Test]
        public void ScoreDisplayer_Unplayed_HidesScore()
        {
            var displayer = CreateScoreDisplayer(Canvas);

            displayer.SetScore(ScoredSong("majtest-score-unplayed", ChartLevel.Master, 0, 0), ChartLevel.Master);

            Assert.That(PrivateMember.GetField<GameObject>(displayer, "_APbg").activeSelf, Is.False);
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(displayer, "_archieveRate").enabled, Is.False);
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(displayer, "_rank").text, Is.Empty);
        }

        /// <summary>Checks the rank thresholds and combo marks of played charts in DX mode.</summary>
        [Test]
        public void ScoreDisplayer_Played_ShowsRankAndComboMark()
        {
            var displayer = CreateScoreDisplayer(Canvas);
            var rank = PrivateMember.GetField<TextMeshProUGUI>(displayer, "_rank");
            var mark = PrivateMember.GetField<TextMeshProUGUI>(displayer, "_clearMark");
            var background = PrivateMember.GetField<GameObject>(displayer, "_APbg");
            var cases = new (double Dx, ComboState Combo, string Rank, string Mark, bool HasMark)[]
            {
                (100.5, ComboState.APPlus, "SSS+", "AP+", true),
                (100.2, ComboState.AP, "SSS", "AP", true),
                (99.6, ComboState.FCPlus, "SS+", "FC+", true),
                (99.1, ComboState.FC, "SS", "FC", true),
                (98.5, ComboState.None, "S+", string.Empty, false),
                (97.0, ComboState.None, "S", string.Empty, false),
                (90.0, ComboState.None, string.Empty, string.Empty, false),
            };
            for (var i = 0; i < cases.Length; i++)
            {
                var (dx, combo, expectedRank, expectedMark, hasMark) = cases[i];
                mark.text = string.Empty;

                displayer.SetScore(ScoredSong($"majtest-score-{i}", ChartLevel.Expert, 1, dx, combo: combo), ChartLevel.Expert);

                Assert.That(rank.text, Is.EqualTo(expectedRank), $"DX {dx}");
                Assert.That(background.activeSelf, Is.EqualTo(hasMark), $"DX {dx}");
                Assert.That(mark.text, Is.EqualTo(expectedMark), $"DX {dx}");
            }
        }

        /// <summary>Checks the achievement precision of the classic and DX judge modes.</summary>
        [Test]
        public void ScoreDisplayer_FormatsAchievementPerJudgeMode()
        {
            var displayer = CreateScoreDisplayer(Canvas);
            var rate = PrivateMember.GetField<TextMeshProUGUI>(displayer, "_archieveRate");

            MajEnv.Settings.Judge.Mode = JudgeModeOption.Classic;
            displayer.SetScore(ScoredSong("majtest-score-classic", ChartLevel.Basic, 2, 99.12345, 98.5), ChartLevel.Basic);
            Assert.That(rate.text, Is.EqualTo($"{98.5:F2}%"));
            Assert.That(rate.enabled, Is.True);

            MajEnv.Settings.Judge.Mode = JudgeModeOption.Modern;
            displayer.SetScore(ScoredSong("majtest-score-dx", ChartLevel.Basic, 2, 99.12345, 98.5), ChartLevel.Basic);
            Assert.That(rate.text, Is.EqualTo($"{99.12345:F4}%"));
        }

        /// <summary>Creates an active user info displayer.</summary>
        /// <returns>The displayer.</returns>
        private UserInfoDisplayer CreateUserInfo()
        {
            var displayer = AddInactiveUi<UserInfoDisplayer>("User", Canvas);
            PrivateMember.SetField(displayer, "_usernameDisplayer", CreateText("Name", displayer.transform));
            PrivateMember.SetField(displayer, "_userAvatarDisplayer", CreateImage("Avatar", displayer.transform));
            PrivateMember.SetField(displayer, "_defaultAvatar", CreateSprite("DefaultAvatar"));
            PrivateMember.SetField(displayer, "_loadingIcon", CreateRect("Loading", displayer.transform));
            var error = CreateRect("Error", displayer.transform);
            error.SetActive(false);
            PrivateMember.SetField(displayer, "_errorIcon", error);
            Activate(displayer);
            return displayer;
        }

        /// <summary>Checks that no endpoint hides the user panel.</summary>
        [Test]
        public void UserInfo_NoEndpoint_Hides()
        {
            var displayer = CreateUserInfo();

            displayer.DisplayUserInfo(null);

            Assert.That(displayer.gameObject.activeSelf, Is.False);
        }

        /// <summary>Checks that a logged-out endpoint shows a guest with the default avatar and stops refreshing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator UserInfo_LoggedOut_ShowsGuest()
        {
            var displayer = CreateUserInfo();
            var endpoint = CreateEndpoint();
            endpoint.RuntimeConfig.AuthMethod = NetAuthMethodOption.Plain;

            displayer.DisplayUserInfo(endpoint);
            var name = PrivateMember.GetField<TextMeshProUGUI>(displayer, "_usernameDisplayer");
            Assert.That(name.text, Is.EqualTo("Guest"));
            Assert.That(PrivateMember.GetField<Image>(displayer, "_userAvatarDisplayer").sprite, Is.SameAs(PrivateMember.GetField<Sprite>(displayer, "_defaultAvatar")));

            endpoint.RuntimeConfig.IsLoggedIn = true;
            endpoint.RuntimeConfig.Username = "late";
            yield return null;

            Assert.That(name.text, Is.EqualTo("Guest"));
        }

        /// <summary>Checks that a logged-in user shows the nickname, falls back to the username and flags a missing avatar.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator UserInfo_LoggedIn_ShowsNameAndAvatar()
        {
            var displayer = CreateUserInfo();
            var endpoint = CreateEndpoint();
            endpoint.RuntimeConfig.AuthMethod = NetAuthMethodOption.Plain;
            endpoint.RuntimeConfig.IsLoggedIn = true;
            endpoint.RuntimeConfig.Username = "user";
            var name = PrivateMember.GetField<TextMeshProUGUI>(displayer, "_usernameDisplayer");
            var avatar = PrivateMember.GetField<Image>(displayer, "_userAvatarDisplayer");

            displayer.DisplayUserInfo(endpoint);
            Assert.That(name.text, Is.EqualTo("user"));
            Assert.That(PrivateMember.GetField<GameObject>(displayer, "_errorIcon").activeSelf, Is.True);

            var sprite = CreateSprite("Avatar");
            endpoint.RuntimeConfig.Avatar = sprite;
            endpoint.RuntimeConfig.Nickname = "Nick";
            yield return null;

            Assert.That(name.text, Is.EqualTo("Nick"));
            Assert.That(avatar.sprite, Is.SameAs(sprite));
        }

        /// <summary>Checks that the panel follows the server of an online song and hides for local songs.</summary>
        [Test]
        public void UserInfo_DisplayFromSong_UsesOnlineServer()
        {
            var displayer = CreateUserInfo();
            var endpoint = CreateEndpoint();

            displayer.DisplayFromSong(CreateOnlineSong("user-song", endpoint));
            Assert.That(displayer.gameObject.activeSelf, Is.True);
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(displayer, "_usernameDisplayer").text, Is.EqualTo("Guest"));

            displayer.DisplayFromSong(new FakeSongDetail("majtest-user-local"));
            Assert.That(displayer.gameObject.activeSelf, Is.False);
        }

        /// <summary>Creates an active favorite toggle and removes the test song from favorites after the test.</summary>
        /// <param name="song">The song used by the test.</param>
        /// <returns>The toggle.</returns>
        private FavoriteAdder CreateAdder(ISongDetail song)
        {
            AddCleanup(() =>
            {
                while (SongStorage.IsInMyFavorites(song))
                {
                    SongStorage.RemoveFromMyFavorites(song);
                }
            });
            return CreateFavoriteAdder(Canvas);
        }

        /// <summary>Checks that pressing toggles the favorite state and its heart sprite.</summary>
        [Test]
        public void FavoriteAdder_PressTogglesFavorite()
        {
            var song = new FakeSongDetail("majtest-favorite-toggle");
            var adder = CreateAdder(song);
            var image = adder.GetComponent<Image>();

            adder.SetSong(song);
            Assert.That(image.sprite, Is.SameAs(adder.HeartAdd));
            Assert.That(image.enabled, Is.True);

            adder.FavoratePressed();
            Assert.That(SongStorage.IsInMyFavorites(song), Is.True);
            Assert.That(image.sprite, Is.SameAs(adder.HeartRemove));

            adder.FavoratePressed();
            Assert.That(SongStorage.IsInMyFavorites(song), Is.False);
            Assert.That(image.sprite, Is.SameAs(adder.HeartAdd));
        }

        /// <summary>Checks that a hidden toggle ignores presses.</summary>
        [Test]
        public void FavoriteAdder_Hide_IgnoresPresses()
        {
            var song = new FakeSongDetail("majtest-favorite-hidden");
            var adder = CreateAdder(song);
            adder.SetSong(song);

            adder.Hide();
            adder.FavoratePressed();

            Assert.That(adder.GetComponent<Image>().enabled, Is.False);
            Assert.That(SongStorage.IsInMyFavorites(song), Is.False);
        }

        /// <summary>Checks that holding the toggle for the configured time adds, then removes, the favorite.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator FavoriteAdder_HoldAddsThenRemoves()
        {
            var song = new FakeSongDetail("majtest-favorite-hold");
            var adder = CreateAdder(song);
            adder.PressToAddTime = 0.25f;
            adder.PressToRemoveTime = 0.25f;
            adder.SetSong(song);
            SetDeltaTime(0.1f);

            adder.SetState(true);
            yield return WaitUntil(() => SongStorage.IsInMyFavorites(song), 2f);
            Assert.That(SongStorage.IsInMyFavorites(song), Is.True);
            Assert.That(adder.State, Is.False);

            adder.SetState(true);
            yield return WaitUntil(() => !SongStorage.IsInMyFavorites(song), 2f);
            Assert.That(SongStorage.IsInMyFavorites(song), Is.False);
        }

        /// <summary>Checks that releasing before the hold time decays the timer without toggling.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator FavoriteAdder_ShortHoldDoesNothing()
        {
            var song = new FakeSongDetail("majtest-favorite-short");
            var adder = CreateAdder(song);
            adder.PressToAddTime = 10f;
            adder.SetSong(song);
            SetDeltaTime(0.1f);

            adder.SetState(true);
            yield return null;
            adder.SetState(false);
            yield return null;
            yield return null;

            Assert.That(SongStorage.IsInMyFavorites(song), Is.False);
            Assert.That(PrivateMember.GetField<float>(adder, "_pressTimer"), Is.EqualTo(0f));
        }
    }
}
