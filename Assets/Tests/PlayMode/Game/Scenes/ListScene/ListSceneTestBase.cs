using System;
using System.Collections;
using System.Collections.Generic;
using LitMotion;
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Net.Models;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.List;
using MajdataPlay.Scenes.List.Models;
using MajdataPlay.Scenes.Setting;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using MajSimai;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ListScene
{
    /// <summary>
    /// Builds the song list scene's object graph for tests: level rings, cover displayers, the center cover with all of
    /// its child displayers, and the cover and collection list managers.
    /// </summary>
    public abstract class ListSceneTestBase : SceneTestBase
    {
        /// <summary>The temporary directory backing online songs, created on demand.</summary>
        private TemporaryDirectory? _onlineCache;

        /// <summary>The in-memory chart cache whose object store lives in <see cref="_onlineCache"/>, created on demand.</summary>
        private OnlineChartCache? _onlineChartCache;

        /// <summary>The center cover displayers created by the test.</summary>
        private readonly List<CenterCoverDisplayer> _centers = new();

        /// <summary>
        /// Completes the background cover fade of every center cover before the objects are destroyed; the fade is
        /// not bound to the displayer's lifetime.
        /// </summary>
        [TearDown]
        public void TearDownListScene()
        {
            foreach (var center in _centers)
            {
                if (center != null)
                {
                    PrivateMember.GetField<MotionHandle>(center, "_bgSongCoverAnim").TryComplete();
                }
            }
            _centers.Clear();
        }

        /// <summary>Gets the canvas holding the scene objects.</summary>
        protected Transform Canvas { get; private set; } = null!;

        /// <summary>Creates the canvas and the runtime database.</summary>
        [SetUp]
        public void SetUpListScene()
        {
            Canvas = CreateCanvas();
            CreateRuntimeDatabase();
            CreateGameManagerStub();
            CreateAudioManager("bgm_select.mp3");
            AddCleanup(() =>
            {
                _onlineChartCache = null;
                _onlineCache?.Dispose();
                _onlineCache = null;
            });
        }

        /// <summary>Creates a root with seven level displayers, each holding a text child named Text.</summary>
        /// <param name="parent">The parent.</param>
        /// <param name="count">The number of level displayers.</param>
        /// <returns>The root.</returns>
        protected GameObject CreateLevelRoot(Transform parent, int count = 7)
        {
            var root = CreateRect("Levels", parent);
            for (var i = 0; i < count; i++)
            {
                var level = CreateRect($"Level{i}", root.transform);
                level.transform.localPosition = new Vector3(i * 10, 0, 0);
                ((RectTransform)level.transform).pivot = new Vector2(0.5f, -0.35f);
                level.AddComponent<CanvasGroup>();
                CreateText("Text", level.transform);
            }
            return root;
        }

        /// <summary>Creates an inactive song cover displayer with background, cover, levels and loading indicator.</summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The displayer.</returns>
        internal SongCoverDisplayer CreateSongCoverDisplayer(Transform parent)
        {
            var displayer = AddInactiveUi<SongCoverDisplayer>("SongCover", parent);
            var background = CreateImage("BG", displayer.transform);
            background.rectTransform.sizeDelta = new Vector2(200, 200);
            background.rectTransform.anchoredPosition = new Vector2(0, 20);
            var cover = CreateImage("Cover", displayer.transform);
            cover.rectTransform.sizeDelta = new Vector2(160, 160);
            PrivateMember.SetField(displayer, "_coverDisplayer", cover);
            PrivateMember.SetField(displayer, "_levelDisplayerListRoot", CreateLevelRoot(displayer.transform));
            PrivateMember.SetField(displayer, "_loadingComponent", CreateRect("Loading", displayer.transform));
            PrivateMember.SetField(displayer, "_levelRingDisplayer", CreateImage("Ring", displayer.transform));
            var selected = CreateRect("SelectedLevel", displayer.transform);
            ((RectTransform)selected.transform).sizeDelta = new Vector2(75f, 75f);
            ((RectTransform)selected.transform).pivot = new Vector2(0.5f, -1.9f);
            selected.transform.localRotation = Quaternion.Euler(0f, 0f, 50f);
            PrivateMember.SetField(displayer, "_selectedLevelGroup", selected.AddComponent<CanvasGroup>());
            PrivateMember.SetField(displayer, "_selectedLevelTitle", CreateText("Title", selected.transform));
            PrivateMember.SetField(displayer, "_selectedLevelText", CreateText("Text", selected.transform));
            PrivateMember.SetField(displayer, "_selectedLevelColor", CreateImage("Background", selected.transform));
            return displayer;
        }

        /// <summary>Creates an inactive thumbnail displayer.</summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The displayer.</returns>
        protected ThumbnailDisplayer CreateThumbnailDisplayer(Transform parent)
        {
            var displayer = AddInactiveUi<ThumbnailDisplayer>("Thumbnail", parent);
            PrivateMember.SetField(displayer, "_coverDisplayer", CreateImage("Cover", displayer.transform));
            PrivateMember.SetField(displayer, "_loadingComponent", CreateRect("Loading", displayer.transform));
            return displayer;
        }

        /// <summary>Creates an active score displayer.</summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The displayer.</returns>
        protected MaiScoreDisplayer CreateScoreDisplayer(Transform parent)
        {
            var displayer = AddInactiveUi<MaiScoreDisplayer>("Score", parent);
            PrivateMember.SetField(displayer, "_archieveRate", CreateText("Rate", displayer.transform));
            PrivateMember.SetField(displayer, "_APbg", CreateRect("APBG", displayer.transform));
            PrivateMember.SetField(displayer, "_clearMark", CreateText("Clear", displayer.transform));
            PrivateMember.SetField(displayer, "_rank", CreateText("Rank", displayer.transform));
            Activate(displayer);
            return displayer;
        }

        /// <summary>Creates an active chart metadata displayer.</summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The displayer.</returns>
        protected ChartMetadataDisplayer CreateMetadataDisplayer(Transform parent)
        {
            var displayer = AddInactiveUi<ChartMetadataDisplayer>("Metadata", parent);
            foreach (var field in new[] { "_onlineIdDisplayer", "_charter", "_title", "_artist", "_bpmDisplayer", "_durationDisplayer" })
            {
                PrivateMember.SetField(displayer, field, CreateText(field, displayer.transform));
            }
            Activate(displayer);
            return displayer;
        }

        /// <summary>Creates an active radar displayer whose image uses the radar shader.</summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The displayer.</returns>
        protected MajRadarDisplayer CreateRadarDisplayer(Transform parent)
        {
            var displayer = AddInactiveUi<MajRadarDisplayer>("Radar", parent);
            var image = Objects.Add<RawImage>("RadarImage", displayer.transform);
            var shader = Shader.Find("Maj/UI/MajRadar");
            Assert.That(shader, Is.Not.Null, "The radar shader is missing.");
            image.material = Objects.Track(new Material(shader));
            PrivateMember.SetField(displayer, "_radarRawImage", image);
            PrivateMember.SetField(displayer, "_estiText", CreateText("Esti", displayer.transform));
            PrivateMember.SetField(displayer, "_radarFillColors", new[] { Color.red, Color.green, Color.blue, Color.cyan, Color.magenta, Color.yellow });
            PrivateMember.SetField(displayer, "_radarOutlineColors", new[] { Color.white, Color.gray, Color.black, Color.red, Color.green, Color.blue });
            Activate(displayer);
            return displayer;
        }

        /// <summary>Creates an active online statistics displayer.</summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The displayer.</returns>
        protected OnlineInfoDisplayer CreateOnlineInfoDisplayer(Transform parent)
        {
            var displayer = AddInactiveUi<OnlineInfoDisplayer>("OnlineInfo", parent);
            foreach (var field in new[] { "_likeCountDisplayer", "_playCountDisplayer", "_commentCountDisplayer", "_commentTextDisplayer" })
            {
                PrivateMember.SetField(displayer, field, CreateText(field, displayer.transform));
            }
            PrivateMember.SetField(displayer, "_commentBox", CreateRect("CommentBox", displayer.transform));
            PrivateMember.SetField(displayer, "_thumbUpImage", CreateImage("ThumbUp", displayer.transform));
            Activate(displayer);
            return displayer;
        }

        /// <summary>Creates an active online ranking displayer with ranker rows.</summary>
        /// <param name="parent">The parent.</param>
        /// <param name="rows">The number of ranker rows.</param>
        /// <returns>The displayer.</returns>
        protected OnlineScoreRankDisplayer CreateRankDisplayer(Transform parent, int rows = 3)
        {
            var displayer = AddInactiveUi<OnlineScoreRankDisplayer>("Ranking", parent);
            var root = CreateRect("Rankers", displayer.transform);
            for (var i = 0; i < rows; i++)
            {
                var row = CreateRect($"Ranker{i}", root.transform);
                CreateImage("Avatar", row.transform);
                CreateText("Rank", row.transform);
                CreateText("Username", row.transform);
                CreateText("Accurate", row.transform);
            }
            var empty = CreateRect("Empty", displayer.transform);
            CreateText("EmptyText", empty.transform);
            PrivateMember.SetField(displayer, "_rankerDisplayerListRoot", root);
            PrivateMember.SetField(displayer, "_rankDisplayer", CreateText("SelfRank", displayer.transform));
            PrivateMember.SetField(displayer, "_loadingIndicator", CreateRect("Loading", displayer.transform));
            PrivateMember.SetField(displayer, "_emptyIndicator", empty);
            PrivateMember.SetField(displayer, "_rankerRankColors", new[] { Color.yellow, Color.gray, Color.red });
            PrivateMember.SetField(displayer, "_apColor", new Color(1f, 0.5f, 0f));
            PrivateMember.SetField(displayer, "_fcColor", new Color(0f, 0.5f, 1f));
            Activate(displayer);
            return displayer;
        }

        /// <summary>Creates an active favorite toggle with heart sprites.</summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The toggle.</returns>
        protected FavoriteAdder CreateFavoriteAdder(Transform parent)
        {
            var image = CreateImage("Favorite", parent);
            var adder = image.gameObject.AddComponent<FavoriteAdder>();
            adder.HeartAdd = CreateSprite("HeartAdd");
            adder.HeartRemove = CreateSprite("HeartRemove");
            return adder;
        }

        /// <summary>Creates an active center cover displayer with every child displayer it drives.</summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The center cover displayer.</returns>
        protected CenterCoverDisplayer CreateCenterCoverDisplayer(Transform parent)
        {
            var center = AddInactiveUi<CenterCoverDisplayer>("Center", parent);
            var favoriteRoot = CreateRect("Favorite", center.transform);
            var chartVisual = AddInactiveUi<ChartVisualDisplayer>("ChartVisual", center.transform);
            chartVisual.gameObject.AddComponent<RawImage>();
            Activate(chartVisual);
            PrivateMember.SetField(center, "_scoreDisplayer", CreateScoreDisplayer(center.transform));
            PrivateMember.SetField(center, "_metadataDisplayer", CreateMetadataDisplayer(center.transform));
            PrivateMember.SetField(center, "_chartAnalyzer", chartVisual);
            PrivateMember.SetField(center, "_majRadar", CreateRadarDisplayer(center.transform));
            PrivateMember.SetField(center, "_onlineInfoDisplayer", CreateOnlineInfoDisplayer(center.transform));
            PrivateMember.SetField(center, "_onlineScoreRankDisplayer", CreateRankDisplayer(center.transform));
            PrivateMember.SetField(center, "_previewPlayer", Objects.Add<PreviewSoundPlayer>("Preview", center.transform));
            PrivateMember.SetField(center, "_favoriteAdder", CreateFavoriteAdder(favoriteRoot.transform));
            PrivateMember.SetField(center, "_bgSongCoverDisplayer", CreateImage("BGCover", center.transform));
            Activate(center);
            _centers.Add(center);
            return center;
        }

        /// <summary>Creates the cover and collection list managers wired to each other and to a center cover.</summary>
        /// <param name="covers">The number of cover displayers.</param>
        /// <param name="thumbnails">The number of thumbnail displayers.</param>
        /// <param name="collectionDisplayers">The number of collection tabs.</param>
        /// <returns>The inactive managers; activate both with <see cref="ActivateManagers"/>.</returns>
        protected (CoverListManager Covers, CollectionListManager Collections, CenterCoverDisplayer Center) CreateListManagers(int covers = 7, int thumbnails = 13, int collectionDisplayers = 7)
        {
            var center = CreateCenterCoverDisplayer(Canvas);
            var coverManager = AddInactiveUi<CoverListManager>("CoverList", Canvas);
            var collectionManager = AddInactiveUi<CollectionListManager>("CollectionList", Canvas);

            var coverRoot = CreateRect("Covers", coverManager.transform);
            for (var i = 0; i < covers; i++)
            {
                CreateSongCoverDisplayer(coverRoot.transform).gameObject.SetActive(true);
            }
            var thumbnailRoot = CreateRect("Thumbnails", coverManager.transform);
            for (var i = 0; i < thumbnails; i++)
            {
                CreateThumbnailDisplayer(thumbnailRoot.transform).gameObject.SetActive(true);
            }
            var notice = CreateRect("EmptyNotice", coverManager.transform);
            CreateText("NoticeText", notice.transform);
            PrivateMember.SetField(coverManager, "_songCoverListRoot", coverRoot);
            PrivateMember.SetField(coverManager, "_thumbnailListRoot", thumbnailRoot);
            PrivateMember.SetField(coverManager, "_progressDisplayer", CreateText("Progress", coverManager.transform));
            PrivateMember.SetField(coverManager, "_emptyCollectionNotice", notice);
            PrivateMember.SetField(coverManager, "_centerCoverDisplayer", center);
            PrivateMember.SetField(coverManager, "_collectionListManager", collectionManager);

            var collectionRoot = CreateRect("Collections", collectionManager.transform);
            for (var i = 0; i < collectionDisplayers; i++)
            {
                CreateCollectionTab(collectionRoot.transform, $"Collection{i}");
            }
            PrivateMember.SetField(collectionManager, "_collectionListRoot", collectionRoot);
            PrivateMember.SetField(collectionManager, "_centerCoverDisplayer", center);
            PrivateMember.SetField(collectionManager, "_coverListManager", coverManager);
            return (coverManager, collectionManager, center);
        }

        /// <summary>Reads the pooled visual currently assigned to a song index without constructing a second owner.</summary>
        /// <param name="manager">The active cover list manager.</param>
        /// <param name="index">The song index whose binding is required.</param>
        /// <returns>The existing pooled song-cover displayer.</returns>
        internal static SongCoverDisplayer BoundCover(CoverListManager manager, int index)
        {
            var bindings = PrivateMember.GetField<IEnumerable>(manager, "_songCoverBindings");
            var current = 0;
            foreach (var binding in bindings)
            {
                if (current++ == index)
                {
                    var displayer = PrivateMember.GetProperty<SongCoverDisplayer?>(binding, "Displayer");
                    Assert.That(displayer, Is.Not.Null, "The requested song must have a real pool binding.");
                    return displayer!;
                }
            }
            Assert.Fail("The requested song index has no binding.");
            return null!;
        }

        /// <summary>Creates one real moving category with the same single-background presentation as the Setting menu.</summary>
        /// <param name="parent">The parent that owns the reusable category tabs.</param>
        /// <param name="name">The fixture object's stable name.</param>
        /// <returns>The initialized collection displayer with its presentation, text and icon references.</returns>
        protected CollectionDisplayer CreateCollectionTab(Transform parent, string name)
        {
            var displayer = AddInactiveUi<CollectionDisplayer>(name, parent);
            ((RectTransform)displayer.transform).sizeDelta = new Vector2(147.83f, 76.8f);
            var tab = displayer.gameObject.AddComponent<MenuTitleDisplayer>();
            var background = CreateImage("BG", displayer.transform);
            background.color = new Color(0.9058824f, 0.9176471f, 0.9176471f, 1f);
            var viewport = (RectTransform)CreateRect("TitleViewport", displayer.transform).transform;
            viewport.sizeDelta = new Vector2(111.83f, 40f);
            viewport.anchoredPosition = new Vector2(0f, -17f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var title = CreateText("Name", parent);
            title.fontSize = 22f;
            title.color = Color.gray;
            title.rectTransform.sizeDelta = viewport.sizeDelta;
            title.transform.SetParent(viewport, false);
            var icon = CreateImage("Icon", displayer.transform);
            icon.rectTransform.sizeDelta = new Vector2(24f, 24f);
            icon.rectTransform.anchoredPosition = new Vector2(0f, 16f);
            PrivateMember.SetField(tab, "_background", background);
            PrivateMember.SetField(tab, "_selectedBackgroundColor", new Color(159f / 255f, 176f / 255f, 188f / 255f, 1f));
            PrivateMember.SetField(tab, "_selectedWidth", 254.69f);
            PrivateMember.SetField(tab, "_titleDisplayer", title);
            PrivateMember.SetField(tab, "_titleViewport", viewport);
            PrivateMember.SetField(tab, "_unselectedTitleInset", 18f);
            PrivateMember.SetField(tab, "_selectedTextColor", Color.white);
            PrivateMember.SetField(tab, "_selectedFontSize", 32f);
            PrivateMember.SetField(tab, "_iconTransform", icon.rectTransform);
            PrivateMember.SetField(tab, "_selectedIconPosition", new Vector2(0f, 62f));
            PrivateMember.SetField(displayer, "_tabDisplayer", tab);
            PrivateMember.SetField(displayer, "_iconDisplayer", icon);
            PrivateMember.SetField(displayer, "_nameDisplayer", title);
            PrivateMember.SetField(displayer, "_onlineCollectionIcon", CreateSprite("Online"));
            PrivateMember.SetField(displayer, "_onlineCollectionIconColor", Color.cyan);
            PrivateMember.SetField(displayer, "_danCollectionIcon", CreateSprite("Dan"));
            PrivateMember.SetField(displayer, "_danCollectionIconColor", Color.magenta);
            PrivateMember.SetField(displayer, "_folderCollectionIcon", CreateSprite("Folder"));
            PrivateMember.SetField(displayer, "_folderCollectionIconColor", Color.yellow);
            PrivateMember.SetField(displayer, "_favoriteCollectionIcon", CreateSprite("Favorite"));
            PrivateMember.SetField(displayer, "_favoriteCollectionIconColor", Color.red);
            Activate(displayer);
            return displayer;
        }

        /// <summary>Activates the list managers so Awake runs now and Start on the next frame.</summary>
        /// <param name="covers">The cover list manager.</param>
        /// <param name="collections">The collection list manager.</param>
        protected static void ActivateManagers(CoverListManager covers, CollectionListManager collections)
        {
            Activate(collections);
            Activate(covers);
        }

        /// <summary>Creates a parsed chart with the given fumen.</summary>
        /// <param name="fumen">The simai fumen.</param>
        /// <returns>The chart.</returns>
        protected static SimaiChart Chart(string fumen)
        {
            return SimaiParser.ParseChart("13", "Designer", fumen.AsSpan());
        }

        /// <summary>Creates a maidata file whose chart at a level index is the given chart.</summary>
        /// <param name="level">The chart level.</param>
        /// <param name="chart">The chart.</param>
        /// <param name="commands">Optional commands.</param>
        /// <returns>The maidata file.</returns>
        protected static SimaiFile Maidata(ChartLevel level, SimaiChart chart, params SimaiCommand[] commands)
        {
            var charts = new SimaiChart[7];
            for (var i = 0; i < charts.Length; i++)
            {
                charts[i] = i == (int)level ? chart : SimaiChart.Empty;
            }
            return new SimaiFile("Title", "Artist", 0f, "hash", charts, commands);
        }

        /// <summary>Creates an online song whose files are kept in a temporary object store, with a local test endpoint.</summary>
        /// <param name="id">The song identifier.</param>
        /// <param name="endpoint">The endpoint, or a new local endpoint when null.</param>
        /// <returns>The online song.</returns>
        internal OnlineSongDetail CreateOnlineSong(string id, ApiEndpoint? endpoint = null)
        {
            _onlineCache ??= new TemporaryDirectory();
            _onlineChartCache ??= new OnlineChartCache(
                new ObjectStore(_onlineCache.Directory.CreateDirectory("Objects"), _onlineCache.Directory.CreateDirectory("Partials")),
                false);
            endpoint ??= CreateEndpoint();
            var detail = new MajnetSongDetail
            {
                Id = id,
                Title = "Online " + id,
                Artist = "Online Artist",
                Designer = "Designer",
                Description = string.Empty,
                Levels = new[] { "1", "3", "7", "10", "13", "14", string.Empty },
                Uploader = "Uploader",
                Timestamp = new DateTime(2024, 2, 2),
                Hash = "majtest-online-" + id,
            };
            return new OnlineSongDetail(endpoint, detail, _onlineChartCache.CreateTransientRecord(endpoint, detail));
        }

        /// <summary>Creates a local endpoint that is never contacted by the tests.</summary>
        /// <returns>The endpoint.</returns>
        protected static ApiEndpoint CreateEndpoint()
        {
            return new ApiEndpoint
            {
                Name = "Test",
                Url = new Uri("http://majdataplay.test/api/"),
            };
        }

        /// <summary>Sets the songs and collections published by the song storage.</summary>
        /// <param name="collections">The collections.</param>
        protected static void PublishCollections(params SongCollection[] collections)
        {
            PrivateMember.SetStaticProperty(typeof(SongStorage), nameof(SongStorage.Collections), collections);
        }
    }
}
