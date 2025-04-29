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

namespace FerrumAddinDev.FBS
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
            SelectWallsHandler handler = new SelectWallsHandler();
            handler.SelectWalls(commandData.Application, this);
            _variants = LayoutGenerator.GenerateVariants(_selectedWalls, 1, 1);
            PlaceHandler = new PlaceLayoutHandler(this);
            PlaceHandler.VariantToPlace = _variants[0];
            PlaceLayoutEvent = ExternalEvent.Create(PlaceHandler);
            PlaceLayoutEvent.Raise();

            return Result.Succeeded;
                
        }

        private void CreateModelLines(Document doc, List<Line> lines)
        {
            // Получаем плоскость для создания линий (например, плоскость уровня)
            Level level = doc.ActiveView.GenLevel;
            if (level == null)
            {
                TaskDialog.Show("Ошибка", "Не удалось получить уровень для создания линий.");
                return;
            }


            // Начинаем транзакцию
            using (Transaction trans = new Transaction(doc, "Создание линий модели"))
            {
                trans.Start();

                foreach (Line line in lines)
                {
                    Plane plane;
                    if (line.Direction.Z != 0)
                    {
                        plane = Plane.CreateByThreePoints(line.GetEndPoint(0), line.GetEndPoint(1), line.GetEndPoint(0) + 1 * XYZ.BasisX);
                    }
                    else
                    {
                        plane = Plane.CreateByThreePoints(line.GetEndPoint(0), line.GetEndPoint(1), line.GetEndPoint(0) + 1 * XYZ.BasisZ);
                    }
                    // Создаем модель линии
                    doc.Create.NewModelCurve(line, SketchPlane.Create(doc, plane));
                }

                trans.Commit();
            }
        }
    }

    public class WallSelectionFilter_ : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Wall;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}