using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using FerrumAddin.Stats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FerrumAddin
{

    [Transaction(TransactionMode.Manual)]
    public class StatsShow : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            StatsWindow statsWindow = new StatsWindow();
            statsWindow.Show();
            return Result.Succeeded;
        }
    }
    
}
