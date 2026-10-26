using System.Collections.Generic;

namespace Svg.Model {

  public record SvgParameters {
    public Dictionary<string, string>? Entities { get; init; }
    public string? Css { get; init; }

    public SvgParameters(Dictionary<string, string>? entities, string? css) {
      Entities = entities;
      Css = css;
    }
  }
}