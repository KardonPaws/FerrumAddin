using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FerrumAddin
{

    [Transaction(TransactionMode.Manual)]
    public class ConfiguratorShow : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Configurator cfg = new Configurator();
            cfg.ShowDialog();
            return Result.Succeeded;
        }
    }
}
