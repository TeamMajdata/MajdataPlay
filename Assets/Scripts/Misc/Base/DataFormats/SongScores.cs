using System;

#nullable enable
namespace MajdataPlay
{
    /// <summary>Groups the seven difficulty records displayed for a song.</summary>
    internal readonly struct SongScores
    {
        /// <summary>Gets the easy chart's score.</summary>
        public required MaiScore Easy { get; init; }
        /// <summary>Gets the basic chart's score.</summary>
        public required MaiScore Basic { get; init; }
        /// <summary>Gets the advance chart's score.</summary>
        public required MaiScore Advance { get; init; }
        /// <summary>Gets the expert chart's score.</summary>
        public required MaiScore Expert { get; init; }
        /// <summary>Gets the master chart's score.</summary>
        public required MaiScore Master { get; init; }
        /// <summary>Gets the remaster chart's score.</summary>
        public required MaiScore ReMaster { get; init; }
        /// <summary>Gets the utage chart's score.</summary>
        public required MaiScore UTAGE { get; init; }

        /// <summary>Creates empty score records using each difficulty's own level hash.</summary>
        /// <param name="songDetail">The song whose difficulty hashes identify the scores.</param>
        /// <returns>Empty score records for the song's seven difficulties.</returns>
        /// <exception cref="ArgumentNullException">The song is null.</exception>
        public static SongScores Create(ISongDetail songDetail)
        {
            if (songDetail is null)
            {
                throw new ArgumentNullException(nameof(songDetail));
            }
            return new SongScores
            {
                Easy = new MaiScore { LevelHash = songDetail.GetLevelHash(ChartLevel.Easy) ?? string.Empty },
                Basic = new MaiScore { LevelHash = songDetail.GetLevelHash(ChartLevel.Basic) ?? string.Empty },
                Advance = new MaiScore { LevelHash = songDetail.GetLevelHash(ChartLevel.Advance) ?? string.Empty },
                Expert = new MaiScore { LevelHash = songDetail.GetLevelHash(ChartLevel.Expert) ?? string.Empty },
                Master = new MaiScore { LevelHash = songDetail.GetLevelHash(ChartLevel.Master) ?? string.Empty },
                ReMaster = new MaiScore { LevelHash = songDetail.GetLevelHash(ChartLevel.ReMaster) ?? string.Empty },
                UTAGE = new MaiScore { LevelHash = songDetail.GetLevelHash(ChartLevel.UTAGE) ?? string.Empty },
            };
        }
    }
}
