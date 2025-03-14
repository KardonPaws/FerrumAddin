using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI.Selection;
using System;
using System.Net;
using FerrumAddin.GrillageCreator;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows;
using FerrumAddin.LinkedFiles;

namespace FerrumAddin
{
    [Transaction(TransactionMode.Manual)]
    public class LinkedFilesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            LinkedFilesWindow window = new LinkedFilesWindow(commandData.Application.ActiveUIDocument);
            window.ShowDialog();

            return Result.Succeeded;
        }
    }
}