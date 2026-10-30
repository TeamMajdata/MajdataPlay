using System;
using System.Collections;
using LitMotion;
using MajdataPlay.IO;
using MajdataPlay.Net;
using MajdataPlay.Scenes.Login;
using MajdataPlay.Scenes.Result.Components;
using MajdataPlay.Scenes.Result.Components.Indicators;
using MajdataPlay.Scenes.Title;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.MiscScenes
{
    /// <summary>Covers the title and login entry scenes and the result screen's score sender without network.</summary>
    public class EntrySceneTests : SceneTestBase
    {
        /// <summary>The canvas holding the scene objects.</summary>
        private Transform _canvas = null!;

        /// <summary>Creates the canvas and the services the scenes use.</summary>
        [SetUp]
        public void SetUp()
        {
            _canvas = CreateCanvas();
            CreateAudioManager("MajdataPlay.wav", "bgm_title.mp3");
            CreateSceneSwitcherStub();
        }

        /// <summary>Creates an inactive title manager with its animated graphics.</summary>
        /// <returns>The manager.</returns>
        private TitleManager CreateTitle()
        {
            var title = AddInactiveUi<TitleManager>("Title", _canvas);
            var xxlb = CreateImage("Xxlb", title.transform);
            xxlb.rectTransform.anchoredPosition = new Vector2(100, 0);
            var logo = CreateImage("Logo", title.transform);
            logo.rectTransform.anchoredPosition = new Vector2(-50, 0);
            var echo = CreateRect("Echo", title.transform);
            ((RectTransform)echo.transform).sizeDelta = new Vector2(400, 50);
            PrivateMember.SetField(title, "_xxlbRect", xxlb.rectTransform);
            PrivateMember.SetField(title, "_xxlbImage", xxlb);
            PrivateMember.SetField(title, "_majdataPlayRect", logo.rectTransform);
            PrivateMember.SetField(title, "_majdataPlayImage", logo);
            PrivateMember.SetField(title, "_ribbonImage", CreateImage("Ribbon", title.transform));
            PrivateMember.SetField(title, "_echoBackground", echo.transform);
            PrivateMember.SetField(title, "_echoText", CreateText("EchoText", title.transform));
            PrivateMember.SetField(title, "_loadingIndicator", CreateRect("Loading", title.transform).transform);
            return title;
        }

        /// <summary>Checks that Awake moves the graphics to their entrance start state.</summary>
        [Test]
        public void Title_Awake_PreparesEntrance()
        {
            var title = CreateTitle();

            Activate(title);

            var xxlb = PrivateMember.GetField<RectTransform>(title, "_xxlbRect");
            Assert.That(xxlb.anchoredPosition.x, Is.EqualTo(100f - 540f).Within(1e-3f));
            Assert.That(PrivateMember.GetField<Image>(title, "_xxlbImage").color.a, Is.EqualTo(0f));
            Assert.That(PrivateMember.GetField<Image>(title, "_ribbonImage").fillAmount, Is.EqualTo(0f));
            Assert.That(PrivateMember.GetField<RectTransform>(title, "_echoBackground").sizeDelta.x, Is.EqualTo(0f));
            Assert.That(PrivateMember.GetField<RectTransform>(title, "_loadingIndicator").gameObject.activeSelf, Is.False);
        }

        /// <summary>Checks that the entrance animation ends at the target layout and starts the loading spinner.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Title_Entrance_EndsAtTargetsAndStartsLoading()
        {
            var title = CreateTitle();
            Activate(title);
            yield return null;
            yield return null;

            Assert.That(Sfx("bgm_title.mp3").PlayOneShotCount, Is.EqualTo(1));
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(title, "_echoText").text, Is.EqualTo("MAJTEXT_LOADING_SCORE_STORAGE..."));
            PrivateMember.GetField<CompositeMotionHandle>(title, "_titleEntranceMotions").Complete();

            Assert.That(PrivateMember.GetField<RectTransform>(title, "_xxlbRect").anchoredPosition.x, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(PrivateMember.GetField<RectTransform>(title, "_majdataPlayRect").anchoredPosition.x, Is.EqualTo(-50f).Within(1e-3f));
            Assert.That(PrivateMember.GetField<Image>(title, "_ribbonImage").fillAmount, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(PrivateMember.GetField<RectTransform>(title, "_echoBackground").sizeDelta.x, Is.EqualTo(400f).Within(1e-3f));
            Assert.That(PrivateMember.GetField<RectTransform>(title, "_loadingIndicator").gameObject.activeSelf, Is.True);

            PrivateMember.Invoke(title, "FinishLoadingAnimation");
            Assert.That(PrivateMember.GetField<RectTransform>(title, "_loadingIndicator").gameObject.activeSelf, Is.False);
            PrivateMember.Invoke(title, "StartLoadingAnimation");
            Assert.That(PrivateMember.GetField<RectTransform>(title, "_loadingIndicator").gameObject.activeSelf, Is.False);
            UnityEngine.Object.DestroyImmediate(title.gameObject);
        }

        /// <summary>Checks that the title mascot floats down from its start position and stops when destroyed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator XxlbBlink_FloatsUntilDestroyed()
        {
            var blink = AddInactiveUi<XxlbBlink>("Blink", _canvas);
            blink.Image = CreateImage("Mascot", blink.transform);
            blink.XxlbDefault = CreateSprite("Default");
            blink.XxlbBlinksHalf = CreateSprite("Half");
            blink.XxlbBlinks = CreateSprite("Closed");
            blink.XxlbWink = CreateSprite("Wink");
            var rect = (RectTransform)blink.transform;
            rect.anchoredPosition = new Vector2(0, 100);
            Activate(blink);

            yield return new WaitForSecondsRealtime(0.3f);

            Assert.That(rect.anchoredPosition.y, Is.LessThan(100f));
            Assert.That(rect.anchoredPosition.y, Is.GreaterThanOrEqualTo(80f));
            UnityEngine.Object.DestroyImmediate(blink.gameObject);
            yield return null;
        }

        /// <summary>Checks the view-mode WebSocket message types carry their values.</summary>
        [Test]
        public void ViewMessages_CarryValues()
        {
            var request = new MajdataPlay.Scenes.View.Types.MajWsRequestBase { requestType = MajdataPlay.Scenes.View.Types.MajWsRequestType.Play, requestData = 5 };
            var load = new MajdataPlay.Scenes.View.Types.MajWsRequestLoad { TrackPath = "t", ImagePath = "i", VideoPath = "v" };
            var binary = new MajdataPlay.Scenes.View.Types.MajWsRequestLoadBinary { Track = new byte[1], Image = new byte[2], Video = new byte[3] };
            var play = new MajdataPlay.Scenes.View.Types.MajWsRequestPlay { StartAt = 1.5, SimaiFumen = "1,E", Offset = 0.25, Speed = 2f };

            Assert.That(request.requestType, Is.EqualTo(MajdataPlay.Scenes.View.Types.MajWsRequestType.Play));
            Assert.That(request.requestData, Is.EqualTo(5));
            Assert.That(load.TrackPath + load.ImagePath + load.VideoPath, Is.EqualTo("tiv"));
            Assert.That(binary.Track.Length + binary.Image.Length + binary.Video.Length, Is.EqualTo(6));
            Assert.That(play.StartAt + play.Offset + play.Speed, Is.EqualTo(3.75d).Within(1e-6));
            Assert.That(play.SimaiFumen, Is.EqualTo("1,E"));
        }

        /// <summary>Creates a key event.</summary>
        /// <param name="isButton">Whether a button produced the event.</param>
        /// <param name="isDown">Whether it is a press.</param>
        /// <param name="zone">The button.</param>
        /// <param name="area">The sensor.</param>
        /// <returns>The event.</returns>
        private static InputEventArgs Event(bool isButton, bool isDown, ButtonZone zone = ButtonZone.A1, SensorArea area = SensorArea.A1)
        {
            return new InputEventArgs
            {
                IsButton = isButton,
                BZone = zone,
                SArea = area,
                OldStatus = isDown ? SwitchStatus.Off : SwitchStatus.On,
                Status = isDown ? SwitchStatus.On : SwitchStatus.Off,
            };
        }

        /// <summary>Checks that presses, the test button without the ready flag and unrelated sensors do not leave the title.</summary>
        [Test]
        public void Title_AreaClick_IgnoresUnrelatedInput()
        {
            var title = CreateTitle();

            PrivateMember.Invoke(title, "OnAreaClick", null, Event(true, true));
            PrivateMember.Invoke(title, "OnAreaClick", null, Event(true, false, ButtonZone.Test));
            PrivateMember.Invoke(title, "OnAreaClick", null, Event(false, false, area: SensorArea.A8));
            PrivateMember.Invoke(title, "OnAreaClick", null, Event(false, false, area: SensorArea.B1));

            Assert.That(Sfx("bgm_title.mp3").StopCount, Is.EqualTo(0));
        }

        /// <summary>Checks that a released button leaves for the list, or for login when online.</summary>
        [Test]
        public void Title_AreaClick_ButtonLeavesTitle()
        {
            var title = CreateTitle();
            ExpectSceneSwitch();
            PrivateMember.Invoke(title, "OnAreaClick", null, Event(true, false, ButtonZone.A4));
            Assert.That(Sfx("bgm_title.mp3").StopCount, Is.EqualTo(1));

            MajEnv.Settings.Online.Enable = true;
            ExpectSceneSwitch();
            PrivateMember.Invoke(title, "OnAreaClick", null, Event(false, false, area: SensorArea.E4));
            Assert.That(Sfx("MajdataPlay.wav").StopCount, Is.EqualTo(2));
        }

        /// <summary>Checks that the test button enters test mode once the title is ready.</summary>
        [Test]
        public void Title_AreaClick_TestButtonEntersTestMode()
        {
            var title = CreateTitle();
            PrivateMember.SetField(title, "_flag", true);
            ExpectSceneSwitch();

            PrivateMember.Invoke(title, "OnAreaClick", null, Event(true, false, ButtonZone.Test));

            Assert.That(PrivateMember.GetField<bool>(title, "_flag"), Is.False);
        }

        /// <summary>Creates an inactive login manager with its controls.</summary>
        /// <returns>The manager.</returns>
        private LoginManager CreateLogin()
        {
            var login = AddInactiveUi<LoginManager>("Login", _canvas);
            var qr = CreateRect("QR", login.transform);
            qr.AddComponent<RawImage>();
            PrivateMember.SetField(login, "_qrCodeComponent", qr);
            PrivateMember.SetField(login, "_usernameInput", CreateInputField("User", login.transform));
            PrivateMember.SetField(login, "_passwordInput", CreateInputField("Password", login.transform));
            PrivateMember.SetField(login, "_usernameClearButton", CreateRect("UserClear", login.transform));
            PrivateMember.SetField(login, "_passwordClearButton", CreateRect("PasswordClear", login.transform));
            PrivateMember.SetField(login, "_hintText", CreateText("Hint", login.transform));
            PrivateMember.SetField(login, "_loading", CreateRect("Loading", login.transform));
            PrivateMember.SetField(login, "_errorColor", Color.red);
            PrivateMember.SetField(login, "_succeedColor", Color.green);
            var endpoints = MajEnv.ApiEndpoints;
            AddCleanup(() => PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.ApiEndpoints), endpoints));
            return login;
        }

        /// <summary>Checks that login without endpoints skips to the list.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Login_WithoutEndpoints_SkipsToList()
        {
            var login = CreateLogin();
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.ApiEndpoints), Array.Empty<ApiEndpoint>());
            ExpectSceneSwitch();

            Activate(login);
            yield return null;

            Assert.That(PrivateMember.GetField<ApiEndpoint[]>(login, "_enabledEndpoints"), Is.Empty);
        }

        /// <summary>Checks the hint colors and the clear buttons that follow the inputs.</summary>
        [Test]
        public void Login_HintAndClearButtons()
        {
            var login = CreateLogin();
            var hint = PrivateMember.GetField<TextMeshProUGUI>(login, "_hintText");

            PrivateMember.Invoke(login, "Hint", "Failed", true);
            Assert.That(hint.text, Is.EqualTo("Failed"));
            Assert.That(hint.color, Is.EqualTo(Color.red));
            PrivateMember.Invoke(login, "Hint", "Done", false);
            Assert.That(hint.color, Is.EqualTo(Color.green));
            PrivateMember.Invoke(login, "Hint", string.Empty, true);
            Assert.That(hint.text, Is.Empty);
            Assert.That(hint.color, Is.EqualTo(Color.green));

            PrivateMember.GetField<TMP_InputField>(login, "_usernameInput").text = "name";
            PrivateMember.Invoke(login, "UpdateClearButtons");
            Assert.That(PrivateMember.GetField<GameObject>(login, "_usernameClearButton").activeSelf, Is.True);
            Assert.That(PrivateMember.GetField<GameObject>(login, "_passwordClearButton").activeSelf, Is.False);
        }

        /// <summary>Checks that the clear sensors empty the inputs once the login form is ready.</summary>
        [Test]
        public void Login_ClearSensorsEmptyInputs()
        {
            var login = CreateLogin();
            var user = PrivateMember.GetField<TMP_InputField>(login, "_usernameInput");
            var password = PrivateMember.GetField<TMP_InputField>(login, "_passwordInput");
            user.text = "name";
            password.text = "secret";

            UnityMessages.Send(login, "Update");
            Assert.That(user.text, Is.EqualTo("name"));

            PrivateMember.SetField(login, "_isReady", true);
            InputSimulator.SetSensor(SensorArea.D3, true);
            InputSimulator.SetSensor(SensorArea.A3, true);
            InputSimulator.SetSensor(SensorArea.C, true);
            InputSimulator.SetSensor(SensorArea.B4, true);
            InputSimulator.Step();
            UnityMessages.Send(login, "Update");

            Assert.That(user.text, Is.Empty);
            Assert.That(password.text, Is.Empty);
        }

        /// <summary>Checks that login never selects inputs that cannot be edited.</summary>
        /// <param name="interactable">Whether the input accepts interaction.</param>
        /// <param name="readOnly">Whether the input prevents text edits.</param>
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void Login_FocusIgnoresUneditableInputs(bool interactable, bool readOnly)
        {
            var eventSystem = Objects.Add<EventSystem>();
            EventSystem.current = eventSystem;
            var input = CreateInputField("User", _canvas);
            input.interactable = interactable;
            input.readOnly = readOnly;

            PrivateMember.InvokeStatic(typeof(LoginManager), "Focus", input);

            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
            Assert.That(input.isFocused, Is.False);
        }

        /// <summary>Checks that editable inputs receive focus and an existing caret and selection survive refocusing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Login_FocusPreservesExistingSelection()
        {
            var eventSystem = Objects.Add<EventSystem>();
            EventSystem.current = eventSystem;
            var input = CreateInputField("User", _canvas);
            input.text = "player";
            yield return null;

            PrivateMember.InvokeStatic(typeof(LoginManager), "Focus", input);
            yield return null;

            Assert.That(eventSystem.currentSelectedGameObject, Is.EqualTo(input.gameObject));
            Assert.That(input.isFocused, Is.True);
            input.selectionAnchorPosition = 1;
            input.selectionFocusPosition = 4;

            PrivateMember.InvokeStatic(typeof(LoginManager), "Focus", input);
            yield return null;

            Assert.That(input.isFocused, Is.True);
            Assert.That(input.selectionAnchorPosition, Is.EqualTo(1));
            Assert.That(input.selectionFocusPosition, Is.EqualTo(4));
            Assert.That(input.caretPosition, Is.EqualTo(4));
        }

        /// <summary>Creates an online score sender with its upload indicator.</summary>
        /// <returns>The sender.</returns>
        private OnlineInteractionSender CreateSender()
        {
            var indicator = AddInactiveUi<ScoreUploadIndicator>("Indicator", _canvas);
            PrivateMember.SetField(indicator, "_uploadingIconDisplayer", Objects.Add<SVGImage>("Up", indicator.transform));
            PrivateMember.SetField(indicator, "_successIconDisplayer", Objects.Add<SVGImage>("Ok", indicator.transform));
            PrivateMember.SetField(indicator, "_errorIconDisplayer", Objects.Add<SVGImage>("Err", indicator.transform));
            PrivateMember.SetField(indicator, "_textDisplayer", CreateText("Text", indicator.transform));
            Activate(indicator);
            var sender = AddInactiveUi<OnlineInteractionSender>("Sender", _canvas);
            PrivateMember.SetField(sender, "_scoreUploadIndicator", indicator);
            Activate(sender);
            return sender;
        }

        /// <summary>Creates an online song for a guest or a logged-in endpoint.</summary>
        /// <param name="directory">The temporary directory holding the song's object store.</param>
        /// <param name="authMethod">The endpoint's authentication method.</param>
        /// <returns>The song.</returns>
        private static OnlineSongDetail OnlineSong(TemporaryDirectory directory, NetAuthMethodOption authMethod)
        {
            var endpoint = new ApiEndpoint { Name = "Test", Url = new Uri("http://majdataplay.test/api/") };
            endpoint.RuntimeConfig.AuthMethod = authMethod;
            var detail = new MajnetSongDetail
            {
                Id = "1",
                Title = "Online",
                Artist = "Artist",
                Designer = "Designer",
                Description = string.Empty,
                Levels = new[] { "1" },
                Uploader = "Uploader",
                Hash = "majtest-interaction",
            };
            var cache = new OnlineChartCache(
                new ObjectStore(directory.Directory.CreateDirectory("Objects"), directory.Directory.CreateDirectory("Partials")),
                false);
            return new OnlineSongDetail(endpoint, detail, cache.CreateTransientRecord(endpoint, detail));
        }

        /// <summary>Checks that local songs and guests never initialize score uploads or post scores.</summary>
        [Test]
        public void Sender_LocalOrGuestDoesNotPostScores()
        {
            using var directory = new TemporaryDirectory();
            var local = CreateSender();
            local.Init(new FakeSongDetail("majtest-interaction-local"), new MaiScore());
            var localPost = local.SendScoreAsync();
            Assert.That(localPost.IsCompleted, Is.True);
            Assert.That(localPost.IsFaulted, Is.False);
            Assert.That(PrivateMember.GetField<bool>(local, "_isLocalOrGuest"), Is.True);
            Assert.That(PrivateMember.GetField<bool>(local, "_isInited"), Is.False);

            var guest = CreateSender();
            guest.Init(OnlineSong(directory, NetAuthMethodOption.None), new MaiScore());
            var guestPost = guest.SendScoreAsync();
            Assert.That(guestPost.IsCompleted, Is.True);
            Assert.That(guestPost.IsFaulted, Is.False);
            Assert.That(PrivateMember.GetField<bool>(guest, "_isLocalOrGuest"), Is.True);
            Assert.That(PrivateMember.GetField<bool>(guest, "_isInited"), Is.False);
        }

        /// <summary>Checks that logged-in songs initialize score uploads and a posted score is never sent again.</summary>
        [Test]
        public void Sender_LoggedInInitializesAndSkipsPostedScore()
        {
            using var directory = new TemporaryDirectory();
            var sender = CreateSender();
            var song = OnlineSong(directory, NetAuthMethodOption.Plain);
            var score = new MaiScore();
            sender.Init(song, score);
            Assert.That(PrivateMember.GetField<bool>(sender, "_isInited"), Is.True);
            Assert.That(PrivateMember.GetField<bool>(sender, "_isLocalOrGuest"), Is.False);
            Assert.That(PrivateMember.GetField<OnlineSongDetail>(sender, "_onlineDetail"), Is.SameAs(song));
            Assert.That(PrivateMember.GetField<MaiScore>(sender, "_score"), Is.SameAs(score));

            PrivateMember.SetField(sender, "_isScorePosted", true);
            var post = sender.SendScoreAsync();

            Assert.That(post.IsCompleted, Is.True);
            Assert.That(post.IsFaulted, Is.False);
            Assert.That(PrivateMember.GetField<bool>(sender, "_isScorePosted"), Is.True);
        }
    }
}
