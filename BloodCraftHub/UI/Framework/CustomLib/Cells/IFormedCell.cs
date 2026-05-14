using System;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Widgets.ScrollView;

namespace BloodCraftHub.UI.Framework.CustomLib.Cells;

public interface IFormedCell : ICell
{
    public int CurrentDataIndex { get; set; }
    public Action<int> OnClick { get; set; }
}