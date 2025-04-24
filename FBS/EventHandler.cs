using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FerrumAddin.FBS
{
    // Selection filter to allow only wall elements
    public class WallSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Wall;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    public class SelectWallsHandler
    {
        private FBSLayoutCommand _window;
        public void SelectWalls(UIApplication app, FBSLayoutCommand win)
        {
            _window = win;
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element, new WallSelectionFilter(), "Select foundation walls");
                if (refs == null) return;
                List<WallInfo> wallInfos = new List<WallInfo>();
                foreach (Reference r in refs)
                {
                    Wall wall = doc.GetElement(r) as Wall;
                    if (wall == null) continue;
                    // Gather wall geometry and parameters
                    LocationCurve locCurve = wall.Location as LocationCurve;
                    XYZ start = locCurve.Curve.GetEndPoint(0);
                    XYZ end = locCurve.Curve.GetEndPoint(1);
                    double length = locCurve.Curve.Length * 304.8;   // feet to mm
                    double thickness = wall.Width * 304.8;          // feet to mm
                    // Wall height and base elevation
                    double baseElev = wall.get_BoundingBox(null).Min.Z;
                    double height;
                    Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    if (heightParam != null && heightParam.HasValue)
                    {
                        height = heightParam.AsDouble() * 304.8;
                    }
                    else
                    {
                        height = (wall.get_BoundingBox(null).Max.Z - wall.get_BoundingBox(null).Min.Z) * 304.8;
                    }
                    // Find openings (doors/windows) hosted in this wall
                    List<OpeningInfo> openings = new List<OpeningInfo>();
                    IEnumerable<FamilyInstance> hostedInserts = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .OfType<FamilyInstance>()
                        .Where(fi => fi.Category.Id.Value == (int)BuiltInCategory.OST_Doors
                                 || fi.Category.Id.Value == (int)BuiltInCategory.OST_Windows)
                        .Where(fi => fi.Host.Id == wall.Id);
                    foreach (FamilyInstance fi in hostedInserts)
                    {
                        // Determine opening location along wall
                        XYZ insPoint;
                        if (fi.Location is LocationPoint lp)
                        {
                            insPoint = lp.Point;
                        }
                        else
                        {
                            BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                            insPoint = (bb.Min + bb.Max) / 2;
                        }
                        XYZ wallDir = (end - start).Normalize();
                        XYZ vec = insPoint - start;
                        double distAlongWall = vec.DotProduct(wallDir);  // in feet
                        double openingCenterMm = distAlongWall * 304.8;
                        // Opening width (if available via parameter)
                        double openWidthFt = 0;
                        Parameter widthParam = fi.get_Parameter(BuiltInParameter.WINDOW_WIDTH) ?? fi.get_Parameter(BuiltInParameter.DOOR_WIDTH);
                        if (widthParam != null && widthParam.HasValue)
                        {
                            openWidthFt = widthParam.AsDouble();
                        }
                        // Compute opening span in mm
                        double openWidthMm = openWidthFt * 304.8;
                        double openStart = openingCenterMm - openWidthMm / 2;
                        double openEnd = openingCenterMm + openWidthMm / 2;
                        openings.Add(new OpeningInfo { Start = openStart, End = openEnd });
                    }
                    openings = openings.OrderBy(op => op.Start).ToList();
                    WallInfo info = new WallInfo
                    {
                        Id = wall.Id,
                        StartPoint = start,
                        EndPoint = end,
                        Direction = (end - start).Normalize(),
                        Length = length,
                        Thickness = thickness,
                        Height = Math.Round(height),
                        BaseElevation = baseElev,
                        Openings = openings,
                        line = locCurve.Curve as Line
                    };
                    int rowCount = (int)Math.Round(info.Height / 600.0);
                    double baseElevMm = Math.Round(info.BaseElevation * 304.8);
                    for (int row = 1; row <= rowCount; row++)
                    {
                        // первый ряд на BaseElevation, последующие — через 600 мм
                        double zMm = baseElevMm + (row - 1) * 600.0;
                        info.coordZList.Add(zMm);
                    }
                    wallInfos.Add(info);
                }
                // Return selected walls info to the main window
                _window._selectedWalls = wallInfos;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User cancelled selection
            }
        }
        public string GetName() => "SelectWallsHandler";
    }

    public class PlaceLayoutHandler : IExternalEventHandler
    {
        private FBSLayoutCommand _window;
        public LayoutVariant VariantToPlace { get; set; }
        public PlaceLayoutHandler(FBSLayoutCommand window)
        {
            _window = window;
        }
        public void Execute(UIApplication app)
        {
            if (VariantToPlace == null) return;
            Document doc = app.ActiveUIDocument.Document;
            BlockPlacer.PlaceVariant(VariantToPlace, doc);
        }
        public string GetName() => "PlaceLayoutHandler";
    }

    public class ShowIssuesHandler : IExternalEventHandler
    {
        public LayoutVariant VariantToShow { get; set; }
        public void Execute(UIApplication app)
        {
            if (VariantToShow == null || !VariantToShow.IsPlaced) return;
            UIDocument uidoc = app.ActiveUIDocument;
            // Highlight all blocks of the variant (for simplicity, since specific issue blocks are not flagged separately)
            HashSet<ElementId> issueIds = new HashSet<ElementId>();
            foreach (BlockPlacement block in VariantToShow.Blocks)
            {
                if (block.PlacedElementId != ElementId.InvalidElementId)
                {
                    issueIds.Add(block.PlacedElementId);
                }
            }
            uidoc.Selection.SetElementIds(issueIds.ToList());
        }
        public string GetName() => "ShowIssuesHandler";
    }
}