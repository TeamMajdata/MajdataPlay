#nullable enable
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajSimai;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Storage = MajdataPlay.IO.Storage.FileSystem;

namespace MajdataPlay.Tests
{
    /// <summary>Adds real constructor, central resource-table and original-backend metadata assertions.</summary>
    public static partial class SongStorageUnityValidation
    {
        /// <summary>Checks that public construction cannot silently re-resolve a directory from a string.</summary>
        /// <exception cref="InvalidOperationException">A required directory constructor is missing or a legacy constructor remains.</exception>
        private static void ValidateConstructorContracts()
        {
            var localConstructors = typeof(SongDetail).GetConstructors();
            Require(localConstructors.Length == 1, "SongDetail must expose exactly one public constructor.");
            Require(localConstructors[0].GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(new Type[] { typeof(StorageDirectory), typeof(SimaiMetadata) }),
                "SongDetail must accept StorageDirectory and metadata, not a string directory.");
            var onlineConstructors = typeof(OnlineSongDetail).GetConstructors();
            Require(onlineConstructors.Length == 1, "OnlineSongDetail must expose exactly one public constructor.");
            Require(onlineConstructors[0].GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(new Type[] { typeof(StorageDirectory), typeof(ApiEndpoint), typeof(MajnetSongDetail) }),
                "OnlineSongDetail must accept the caller-created StorageDirectory, endpoint and remote metadata.");
            ExpectException<ArgumentNullException>(() => new SongDetail(null!, default));
            ExpectException<ArgumentNullException>(() => new OnlineSongDetail(null!, new ApiEndpoint(), CreateRemoteMetadata(30000)));
            Debug.Log("SONG_STORAGE_CONSTRUCTORS_PASSED: real local/online directory constructors only; staged production callers compile without legacy overloads.");
        }

        /// <summary>Exercises every configured resource name, case variants, required resources and file-only matching.</summary>
        /// <returns>A task completing after real construction, scanning and backend metadata reads.</returns>
        /// <exception cref="InvalidOperationException">A central resource table or production matching behavior differs.</exception>
        private static async Task ValidateResourceTablesAsync()
        {
            var tracks = SongStorage.SupportedTrackFileNames.ToArray();
            var videos = SongStorage.SupportedVideoFileNames.ToArray();
            var covers = SongStorage.SupportedCoverFileNames.ToArray();
            Require(SongStorage.ChartFileName == "maidata.txt", "The shared metadata filename changed.");
            Require(tracks.SequenceEqual(new string[] { "track.mp3", "track.opus", "track.ogg", "track.aac", "track.wav" }),
                "Track names must use the production dot-prefixed audio extensions.");
            var expectedVideos = new string[] { "bg", "pv", "mv" }
                .SelectMany(prefix => new string[] { ".webm", ".mkv", ".mp4", ".mov" }.Select(extension => prefix + extension));
            Require(videos.SequenceEqual(expectedVideos), "The central video table must retain bg/pv/mv for every supported extension.");
            Require(covers.SequenceEqual(new string[] { "bg.png", "bg.jpg" }), "The central cover table must contain exactly bg.png and bg.jpg.");

            // The global provider deliberately does not own any of these locations. Reopening a URI is a regression.
            Storage.RegisterContentProvider(new ValidationFileSystem());
            var backend = new ValidationFileSystem();
            var fixtures = new List<ResourceFixture>();
            var number = 30000;
            foreach (var track in tracks)
            {
                fixtures.Add(SeedResourceSong(backend, backend.Root, number++, track, videos[0], covers[0]));
            }
            foreach (var video in videos)
            {
                fixtures.Add(SeedResourceSong(backend, backend.Root, number++, tracks[0], video, covers[0]));
            }
            foreach (var cover in covers)
            {
                fixtures.Add(SeedResourceSong(backend, backend.Root, number++, tracks[0], videos[0], cover));
            }
            var optionalMissing = SeedResourceSong(backend, backend.Root, number++, tracks[0], null, null);
            backend.AddFile(optionalMissing.Directory, "bg.jpeg");
            backend.AddFile(optionalMissing.Directory, "cover.jpg");
            backend.AddFile(optionalMissing.Directory, "trailer.mp4");
            backend.AddFile(optionalMissing.Directory, "mv.avi");
            fixtures.Add(optionalMissing);
            foreach (var fixture in fixtures)
            {
                await ValidateResourceSongAsync(fixture).ConfigureAwait(false);
            }

            using var metadataStream = new MemoryStream(Encoding.UTF8.GetBytes(CreateChartContent(number)), false);
            var metadata = await SimaiParser.ParseMetadataAsync(metadataStream).ConfigureAwait(false);
            var missingMetadata = backend.AddDirectory(backend.Root, "MissingMetadata");
            backend.AddFile(missingMetadata, "TRACK.MP3");
            var metadataDirectory = backend.AddDirectory(backend.Root, "MetadataDirectory");
            backend.AddDirectory(metadataDirectory, "maidata.txt");
            backend.AddFile(metadataDirectory, "TRACK.MP3");
            var wrongMetadataCase = backend.AddDirectory(backend.Root, "WrongMetadataCase");
            backend.AddFile(wrongMetadataCase, "MAIDATA.TXT", CreateChartContent(number++));
            backend.AddFile(wrongMetadataCase, "TRACK.MP3");
            var missingTrack = backend.AddDirectory(backend.Root, "MissingTrack");
            backend.AddFile(missingTrack, "maidata.txt", CreateChartContent(number++));
            var trackDirectory = backend.AddDirectory(backend.Root, "TrackDirectory");
            backend.AddFile(trackDirectory, "maidata.txt", CreateChartContent(number++));
            foreach (var track in tracks)
            {
                backend.AddDirectory(trackDirectory, track.ToUpperInvariant());
            }
            var unsupportedTrack = backend.AddDirectory(backend.Root, "UnsupportedTrack");
            backend.AddFile(unsupportedTrack, "maidata.txt", CreateChartContent(number++));
            backend.AddFile(unsupportedTrack, "track.flac");
            backend.AddFile(unsupportedTrack, "audio.mp3");
            foreach (var directory in new StorageDirectory[]
            {
                missingMetadata, metadataDirectory, wrongMetadataCase, missingTrack, trackDirectory, unsupportedTrack
            })
            {
                ExpectException<FileNotFoundException>(() => new SongDetail(directory, metadata));
                await ExpectExceptionAsync<FileNotFoundException>(() => SongDetail.ParseAsync(directory)).ConfigureAwait(false);
            }
            Require(wrongMetadataCase.FindFile("maidata.txt") is null,
                "The strict fixture backend unexpectedly accepted uppercase maidata; its rules must remain independent of resource matching.");

            using (var scanner = new SongCollectionScanner())
            {
                var scanned = await scanner.ScanAsync(backend.Root).ConfigureAwait(false);
                s_fixtureCharts.AddRange(scanned);
                Require(scanned.Length == fixtures.Count, "Real scanner accepted a resource directory or dropped a supported case-variant resource.");
                Require(scanner.DiscoveredChartCount == fixtures.Count && scanner.ProcessedChartCount == fixtures.Count && scanner.LoadedChartCount == fixtures.Count,
                    "Resource-matrix scanner counters do not match the valid fixture candidates.");
                var byTitle = fixtures.ToDictionary(fixture => fixture.Title);
                foreach (var song in scanned.Cast<SongDetail>())
                {
                    await AssertResourcePathsAsync(song, byTitle[song.Title]).ConfigureAwait(false);
                }
            }
            Require(backend.MutationAttemptCount == 0, "Local construction/scanning attempted to mutate the original backend.");
            Debug.Log("SONG_STORAGE_RESOURCE_TABLES_PASSED: every track/video/cover name, uppercase files, optional absence, required absence, backend maidata rules and file-only real scanning.");
            await ValidateOriginalBackendReadsAsync().ConfigureAwait(false);
            await ValidateCentralTableOverrideAsync().ConfigureAwait(false);
        }

        /// <summary>Constructs the real local song from actual upstream metadata and checks its retained file handle.</summary>
        /// <param name="fixture">The resource fixture with genuine metadata bytes.</param>
        /// <returns>A task completing after constructor and path-selection assertions.</returns>
        /// <exception cref="InvalidOperationException">Production resolves an incorrect resource or backend.</exception>
        private static async Task ValidateResourceSongAsync(ResourceFixture fixture)
        {
            using var stream = fixture.Maidata.OpenRead();
            var metadata = await SimaiParser.ParseMetadataAsync(stream).ConfigureAwait(false);
            var song = new SongDetail(fixture.Directory, metadata);
            s_fixtureCharts.Add(song);
            Require(song.Title == fixture.Title && song.Hash == ComputeFixtureHash(CreateChartContent(fixture.Number)),
                "The directory-only constructor did not retain the real MajSimai metadata.");
            var metadataFile = GetPrivateField<StorageFile>(song, "_maidataFile");
            Require(ReferenceEquals(metadataFile.FileSystem, fixture.Directory.FileSystem) && metadataFile.Location == fixture.Maidata.Location,
                "SongDetail lost the authoritative metadata file or its original backend.");
            await AssertResourcePathsAsync(song, fixture).ConfigureAwait(false);
        }

        /// <summary>Checks selected paths without entering audio or cover decoding.</summary>
        /// <param name="song">The real local song instance.</param>
        /// <param name="fixture">The expected authoritative resource locations.</param>
        /// <returns>A task completing after the public video-path operation.</returns>
        /// <exception cref="InvalidOperationException">A directory or unsupported resource was selected.</exception>
        private static async Task AssertResourcePathsAsync(SongDetail song, ResourceFixture fixture)
        {
            Require(GetPrivateField<string>(song, "_trackPath") == fixture.TrackPath, "Local track matching did not select the expected file: " + fixture.Title);
            Require(GetPrivateField<string>(song, "_coverPath") == fixture.CoverPath, "Local cover matching did not select the expected file: " + fixture.Title);
            Require(await song.GetVideoPathAsync().ConfigureAwait(false) == fixture.VideoPath,
                "Local video matching did not select the expected file: " + fixture.Title);
        }

        /// <summary>Reads and reparses metadata through an unregistered original backend, including backend-specific maidata casing.</summary>
        /// <returns>A task completing after cached and forced metadata reads.</returns>
        /// <exception cref="InvalidOperationException">The backend was re-resolved or metadata was not reread.</exception>
        private static async Task ValidateOriginalBackendReadsAsync()
        {
            var backend = new ValidationFileSystem(nonSeekableReads: true);
            var fixture = SeedResourceSong(backend, backend.Root, 31000, "track.ogg", null, null);
            var song = await SongDetail.ParseAsync(fixture.Directory).ConfigureAwait(false);
            s_fixtureCharts.Add(song);
            Require(backend.GetReadCount(fixture.Maidata.Location) > 0, "Metadata parsing bypassed the original backend.");
            var chart = await song.GetMaidataAsync().ConfigureAwait(false);
            Require(chart.Title == fixture.Title && song.IsMaidataLoaded, "Original-backend full chart parsing failed.");
            var reads = backend.GetReadCount(fixture.Maidata.Location);
            Require(ReferenceEquals(chart, await song.GetMaidataAsync().ConfigureAwait(false)) && backend.GetReadCount(fixture.Maidata.Location) == reads,
                "The in-memory chart cache reread or replaced the unchanged chart.");
            backend.ReplaceContents(fixture.Maidata, CreateChartContent(31001));
            var updated = await song.GetMaidataAsync(ignoreCache: true).ConfigureAwait(false);
            Require(updated.Title == "Fixture-31001" && backend.GetReadCount(fixture.Maidata.Location) > reads,
                "Forced chart parsing did not reread the original backend's changed bytes.");
            Require(song.Hash == ComputeFixtureHash(CreateChartContent(31001)), "Changed original-backend metadata did not update the real chart hash.");
            Require(backend.MutationAttemptCount == 0, "Original-backend local metadata reads attempted mutation.");

            var insensitiveBackend = new ValidationFileSystem(ignoreNameCase: true);
            var directory = insensitiveBackend.AddDirectory(insensitiveBackend.Root, "BackendCaseRules");
            var maidata = insensitiveBackend.AddFile(directory, "MAIDATA.TXT", CreateChartContent(31002));
            insensitiveBackend.AddFile(directory, "TRACK.WAV");
            var caseSong = await SongDetail.ParseAsync(directory).ConfigureAwait(false);
            s_fixtureCharts.Add(caseSong);
            Require(caseSong.Title == "Fixture-31002" && insensitiveBackend.GetReadCount(maidata.Location) > 0,
                "SongDetail bypassed the backend's case-insensitive FindFile semantics.");
            using var scanner = new SongCollectionScanner();
            var scanned = await scanner.ScanAsync(insensitiveBackend.Root).ConfigureAwait(false);
            s_fixtureCharts.AddRange(scanned);
            Require(scanned.Length == 1 && scanned[0].Hash == caseSong.Hash, "Real scanner did not honor backend-specific maidata name rules.");
            Debug.Log("SONG_STORAGE_ORIGINAL_BACKEND_PASSED: real metadata/full parsing and forced changed-byte reads; strict/insensitive maidata FindFile rules remain backend-owned.");
        }

        /// <summary>Temporarily changes central table entries to detect hard-coded resource lists and culture-sensitive comparisons.</summary>
        /// <returns>A task completing after real local, scanner and online cache-lookup assertions.</returns>
        /// <exception cref="InvalidOperationException">Production ignores the central tables or uses culture-sensitive matching.</exception>
        private static async Task ValidateCentralTableOverrideAsync()
        {
            var track = SongStorage.SupportedTrackFileNames[0];
            var video = SongStorage.SupportedVideoFileNames[0];
            var cover = SongStorage.SupportedCoverFileNames[0];
            var culture = CultureInfo.CurrentCulture;
            try
            {
                SongStorage.SupportedTrackFileNames[0] = "fixture-track.resource";
                SongStorage.SupportedVideoFileNames[0] = "fixture-video.resource";
                SongStorage.SupportedCoverFileNames[0] = "fixture-cover.resource";
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                var backend = new ValidationFileSystem();
                var fixture = SeedResourceSong(backend, backend.Root, 32000,
                    SongStorage.SupportedTrackFileNames[0], SongStorage.SupportedVideoFileNames[0], SongStorage.SupportedCoverFileNames[0]);
                await ValidateResourceSongAsync(fixture).ConfigureAwait(false);
                using var scanner = new SongCollectionScanner();
                var scanned = await scanner.ScanAsync(backend.Root).ConfigureAwait(false);
                s_fixtureCharts.AddRange(scanned);
                Require(scanned.Length == 1, "The real scanner ignored an overridden central track filename.");
                await AssertResourcePathsAsync((SongDetail)scanned[0], fixture).ConfigureAwait(false);
                var onlineSong = new OnlineSongDetail(fixture.Directory, new ApiEndpoint(), CreateRemoteMetadata(fixture.Number));
                s_fixtureCharts.Add(onlineSong);
                AssertOnlineCachedResources(onlineSong, fixture);
                Require(await onlineSong.GetVideoPathAsync().ConfigureAwait(false) == fixture.VideoPath,
                    "Online video lookup ignored the central table or ordinal case rules.");
                Require(backend.MutationAttemptCount == 0, "Central-table cache discovery mutated the pre-created backend.");
            }
            finally
            {
                SongStorage.SupportedTrackFileNames[0] = track;
                SongStorage.SupportedVideoFileNames[0] = video;
                SongStorage.SupportedCoverFileNames[0] = cover;
                CultureInfo.CurrentCulture = culture;
            }
            Debug.Log("SONG_STORAGE_CENTRAL_MATCHING_PASSED: temporary table sentinels used by real local/online lookup and scanner under tr-TR; original tables restored.");
        }

        /// <summary>Seeds valid metadata and case-variant files after matching directory decoys.</summary>
        /// <param name="backend">The original backend owning all fixture entries.</param>
        /// <param name="parent">The fixture collection or cache parent directory.</param>
        /// <param name="number">The unique chart identity.</param>
        /// <param name="trackName">The supported track name whose uppercase file must be selected.</param>
        /// <param name="videoName">The supported video name, or null to omit optional video files.</param>
        /// <param name="coverName">The supported cover name, or null to omit optional cover files.</param>
        /// <returns>The expected original handles and locations without performing chart discovery.</returns>
        private static ResourceFixture SeedResourceSong(ValidationFileSystem backend, StorageDirectory parent, int number,
            string trackName, string? videoName, string? coverName)
        {
            var directory = backend.AddDirectory(parent, "Chart-" + number.ToString("D4"));
            var maidata = backend.AddFile(directory, SongStorage.ChartFileName, CreateChartContent(number));
            foreach (var name in SongStorage.SupportedTrackFileNames.Concat(SongStorage.SupportedVideoFileNames).Concat(SongStorage.SupportedCoverFileNames))
            {
                backend.AddDirectory(directory, name);
            }
            var track = backend.AddFile(directory, trackName.ToUpperInvariant());
            var video = videoName is null ? null : backend.AddFile(directory, videoName.ToUpperInvariant());
            var cover = coverName is null ? null : backend.AddFile(directory, coverName.ToUpperInvariant());
            return new ResourceFixture(directory, maidata, number, track.Location, video?.Location ?? string.Empty, cover?.Location ?? string.Empty);
        }

        /// <summary>Creates genuine Simai markup reused by local and online cached-metadata cases.</summary>
        /// <param name="number">The chart's deterministic fixture identity.</param>
        /// <returns>UTF-8-ready Simai text with title, artist, designer, difficulty and note data.</returns>
        private static string CreateChartContent(int number)
        {
            return "&title=Fixture-" + number.ToString("D4") + "\r\n&artist=Fixture Artist\r\n&des_5=Fixture Designer\r\n&lv_5=12+\r\n&inote_5=(120){4}1,2,3,4,E\r\n";
        }

        /// <summary>Computes the independent expected Simai identity from the original fixture bytes.</summary>
        /// <param name="content">The exact Simai text whose UTF-8 bytes are hashed.</param>
        /// <returns>The fixture MD5 encoded as Base64, matching the public song identity contract.</returns>
        private static string ComputeFixtureHash(string content)
        {
            using var md5 = MD5.Create();
            return Convert.ToBase64String(md5.ComputeHash(Encoding.UTF8.GetBytes(content)));
        }

        /// <summary>Reads a production field only to inspect selected handles/paths without media decoding.</summary>
        /// <typeparam name="T">The expected field type.</typeparam>
        /// <param name="instance">The real production song instance.</param>
        /// <param name="name">The tightly scoped field being inspected.</param>
        /// <returns>The real production field value.</returns>
        /// <exception cref="InvalidOperationException">The expected inspection seam is missing or has another type.</exception>
        private static T GetPrivateField<T>(object instance, string name)
        {
            var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field is not null, "The production inspection field is missing: " + name);
            var value = field!.GetValue(instance);
            Require(value is T, "The production inspection field has an unexpected type: " + name);
            return (T)value!;
        }

        /// <summary>Requires a synchronous operation to expose the expected production failure.</summary>
        /// <typeparam name="TException">The expected failure type.</typeparam>
        /// <param name="operation">The constructor or operation under test.</param>
        /// <exception cref="InvalidOperationException">The operation unexpectedly succeeds.</exception>
        private static void ExpectException<TException>(Action operation) where TException : Exception
        {
            try
            {
                operation();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException("Expected " + typeof(TException).Name + " was not observed.");
        }

        /// <summary>Requires an asynchronous operation to expose the expected production failure.</summary>
        /// <typeparam name="TException">The expected failure type.</typeparam>
        /// <param name="operation">The production operation under test.</param>
        /// <returns>A task completing after the expected failure is observed.</returns>
        /// <exception cref="InvalidOperationException">The operation unexpectedly succeeds.</exception>
        private static async Task ExpectExceptionAsync<TException>(Func<Task> operation) where TException : Exception
        {
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException("Expected " + typeof(TException).Name + " was not observed.");
        }

        /// <summary>Stores expected fixture resources without containing a substitute selection algorithm.</summary>
        private sealed class ResourceFixture
        {
            /// <summary>Gets the authoritative already-created chart directory.</summary>
            public StorageDirectory Directory { get; }
            /// <summary>Gets the genuine metadata file owned by the original backend.</summary>
            public StorageFile Maidata { get; }
            /// <summary>Gets the deterministic fixture chart number.</summary>
            public int Number { get; }
            /// <summary>Gets the independently expected parsed title.</summary>
            public string Title => "Fixture-" + Number.ToString("D4");
            /// <summary>Gets the expected track file location.</summary>
            public string TrackPath { get; }
            /// <summary>Gets the expected video file location, or an empty string when optional video is absent.</summary>
            public string VideoPath { get; }
            /// <summary>Gets the expected cover file location, or an empty string when optional cover is absent.</summary>
            public string CoverPath { get; }

            /// <summary>Captures fixture expectations supplied by seeding, never by chart scanning.</summary>
            /// <param name="directory">The chart directory handle.</param>
            /// <param name="maidata">The metadata file handle.</param>
            /// <param name="number">The fixture chart number.</param>
            /// <param name="trackPath">The seeded track file location.</param>
            /// <param name="videoPath">The seeded video file location or an empty string.</param>
            /// <param name="coverPath">The seeded cover file location or an empty string.</param>
            public ResourceFixture(StorageDirectory directory, StorageFile maidata, int number, string trackPath, string videoPath, string coverPath)
            {
                Directory = directory;
                Maidata = maidata;
                Number = number;
                TrackPath = trackPath;
                VideoPath = videoPath;
                CoverPath = coverPath;
            }
        }
    }
}
