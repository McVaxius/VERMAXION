using System.Collections.Generic;
using System.Linq;

namespace VERMAXION;

public sealed class Configuration
{
    public List<string> PostProcessTaskOrder { get; set; } = VERMAXION.PostProcessTaskOrder.DefaultOrder.ToList();
    public Dictionary<string, PostProcessTaskPhase> PostProcessTaskPlacement { get; set; } = VERMAXION.PostProcessTaskOrder.CreateDefaultPlacement();
}
