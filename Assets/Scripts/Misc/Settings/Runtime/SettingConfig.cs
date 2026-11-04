using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Scripting;

namespace MajdataPlay.Settings.Runtime;
[Preserve]
public class SettingConfig
{
    [JsonIgnore, Preserve]
    public string SelectedMenu { get; set; } = nameof(UserSetting.Game);
    [JsonIgnore, Preserve]
    public string SelectedOption { get; set; } = string.Empty;
    [JsonIgnore, Preserve]
    public bool IgnoreChartSettingPage { get; set; } = false;
}
