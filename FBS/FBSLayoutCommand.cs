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
        public ExternalEvent ShowIssuesEvent { get; set; }
        public ShowIssuesHandler ShowIssuesHandler { get; set; }
        public List<WallInfo> _selectedWalls;
        public List<LayoutVariant> _variants;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //UIDocument uidoc = commandData.Application.ActiveUIDocument;
            //Document doc = uidoc.Document;
            //IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element, new WallSelectionFilter_(), "Select foundation walls");
            //List<Line> wallInfos = new List<Line>();
            //foreach (Reference r in refs)
            //{
            //    Wall wall = doc.GetElement(r) as Wall;
            //    if (wall == null) continue;
            //    // Gather wall geometry and parameters
            //    LocationCurve locCurve = wall.Location as LocationCurve;
            //    XYZ start = locCurve.Curve.GetEndPoint(0);
            //    XYZ end = locCurve.Curve.GetEndPoint(1);
            //    wallInfos.Add(locCurve.Curve as Line);
            //}
            //CreateModelLines(doc, wallInfos);
            //LayoutWindow window = new LayoutWindow(commandData.Application);
            //window.Show();
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