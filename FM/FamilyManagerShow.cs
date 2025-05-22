using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace FerrumAddinDev
{

    [Transaction(TransactionMode.Manual)]
    public class FamilyManagerShow : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // dockable window id
                DockablePaneId id = new DockablePaneId(new Guid("{3496B5BA-F8C4-403D-AF7E-B95D25F15CED}"));
                DockablePane dockableWindow = commandData.Application.GetDockablePane(id);
                dockableWindow.Show();
            }
            catch (Exception ex)
            {
                // show error info dialog
                MessageBox.Show(ex.Message, "Info Message");
            }
            // return result
            return Result.Succeeded;
        }
    }
}
