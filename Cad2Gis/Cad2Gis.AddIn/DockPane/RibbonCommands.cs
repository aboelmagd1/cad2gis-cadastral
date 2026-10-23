using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;

namespace Cad2Gis.AddIn.DockPane;

public class ShowCad2GisDockpaneButton : Button
{
    protected override void OnClick()
    {
        Cad2GisDockpaneViewModel.Show();
    }

    protected override void OnUpdate()
    {
        Enabled = true;
    }
}

public class ExecuteStep1Button : Button
{
    protected override void OnClick()
    {
        var vm = Cad2GisDockpaneViewModel.Instance;
        Cad2GisDockpaneViewModel.Show();
        if (vm != null)
        {
            vm.SelectedTabIndex = 0;
            vm.ScanCadLayersCommand.Execute(null);
        }
    }

    protected override void OnUpdate()
    {
        Enabled = true;
    }
}

public class ExecuteStep2Button : Button
{
    protected override void OnClick()
    {
        var vm = Cad2GisDockpaneViewModel.Instance;
        Cad2GisDockpaneViewModel.Show();
        if (vm != null)
        {
            vm.SelectedTabIndex = 1;
            vm.BuildParcelsCommand.Execute(null);
        }
    }

    protected override void OnUpdate()
    {
        Enabled = true;
    }
}

public class ExecuteStep3Button : Button
{
    protected override void OnClick()
    {
        var vm = Cad2GisDockpaneViewModel.Instance;
        Cad2GisDockpaneViewModel.Show();
        if (vm != null)
        {
            vm.SelectedTabIndex = 2;
            vm.PopulateSchemaCommand.Execute(null);
        }
    }

    protected override void OnUpdate()
    {
        Enabled = true;
    }
}

public class ExecuteStep4Button : Button
{
    protected override void OnClick()
    {
        var vm = Cad2GisDockpaneViewModel.Instance;
        Cad2GisDockpaneViewModel.Show();
        if (vm != null)
        {
            vm.SelectedTabIndex = 3;
            vm.BuildBordersCommand.Execute(null);
        }
    }

    protected override void OnUpdate()
    {
        Enabled = true;
    }
}

public class ExecuteStep5Button : Button
{
    protected override void OnClick()
    {
        var vm = Cad2GisDockpaneViewModel.Instance;
        Cad2GisDockpaneViewModel.Show();
        if (vm != null)
        {
            vm.SelectedTabIndex = 4;
            vm.ExportAndQaCommand.Execute(null);
        }
    }

    protected override void OnUpdate()
    {
        Enabled = true;
    }
}

public class ExecuteAllPipelineButton : Button
{
    protected override void OnClick()
    {
        var vm = Cad2GisDockpaneViewModel.Instance;
        Cad2GisDockpaneViewModel.Show();
        if (vm != null)
        {
            vm.RunAllPipelineCommand.Execute(null);
        }
    }

    protected override void OnUpdate()
    {
        Enabled = true;
    }
}
