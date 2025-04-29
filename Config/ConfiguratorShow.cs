using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FerrumAddinDev
{

    [Transaction(TransactionMode.Manual)]
    public class ConfiguratorShow : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            CloseEv = ExternalEvent.Create(new CloseDoc());
            Configurator cfg = new Configurator(commandData);
            cfg.ShowDialog();
            return Result.Succeeded;
        }
        public static ExternalEvent CloseEv;
    }
    public class CloseDoc : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
           
        }

        public string GetName()
        {
            return "xxx";
        }
    }
}
