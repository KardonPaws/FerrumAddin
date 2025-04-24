using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;
using System.Windows;
using Autodesk.Revit.DB.Structure;
using System.Collections.Generic;
using System.Windows.Interop;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Windows.Controls;
using System.Windows.Forms;
using Autodesk.Revit.UI.Selection;
using System.Linq;

namespace FerrumAddin.FBS
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class FBSLayoutCommand : IExternalCommand
    {
        public ExternalEvent SelectWallsEvent { get; set; }
        public SelectWallsHandler SelectHandler { get; set; }
        public ExternalEvent PlaceLayoutEvent { get; set; }
        public PlaceLayoutHandler PlaceHandler { get; set; }
        public List<WallInfo> _selectedWalls;
        public List<LayoutVariant> _variants;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {

            SelectWallsHandler handler = new SelectWallsHandler();
            handler.SelectWalls(commandData.Application, this);
            _variants = LayoutGenerator.GenerateVariants(_selectedWalls, 1, 1);
            PlaceHandler = new PlaceLayoutHandler(this);
            PlaceHandler.VariantToPlace = _variants[0];
            PlaceLayoutEvent = ExternalEvent.Create(PlaceHandler);
            PlaceLayoutEvent.Raise();

            return Result.Succeeded;
                
        }        
    }

    public class WallSelectionFilter_ : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Wall;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}