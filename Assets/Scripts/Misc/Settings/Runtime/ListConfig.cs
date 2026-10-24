using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Scripting;

namespace MajdataPlay.Settings.Runtime;
[Preserve]
public class ListConfig
{
    [Preserve]
    public Guid SelectedDirGuid { get; set; } = Guid.Empty;
    [Preserve]
    public string SelectedSongHash { get; set; } = string.Empty;
    [Preserve]
    public ChartLevel SelectedDiff { get; set; } = ChartLevel.Easy;
    [JsonIgnore, Preserve]
    public SongOrder OrderBy { get; } = new();
}
