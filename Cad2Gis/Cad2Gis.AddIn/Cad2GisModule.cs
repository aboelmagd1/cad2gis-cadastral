using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;

namespace Cad2Gis.AddIn;

public class Cad2GisModule : Module
{
    private static Cad2GisModule? _this;

    public static Cad2GisModule Current => _this ??= (Cad2GisModule)FrameworkApplication.FindModule("Cad2Gis_Module");

    #region Overrides
    protected override bool Initialize()
    {
        return base.Initialize();
    }

    protected override bool CanUnload()
    {
        return true;
    }
    #endregion
}
