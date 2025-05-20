using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FerrumAddinDev.ColumnSections
{
    [Transaction(TransactionMode.Manual)]
    public class CreateColumnSections : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet es)
        {
            var uiDoc = data.Application.ActiveUIDocument;
            var doc = uiDoc.Document;

            // 1. Собираем колонны
            var columns = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .ToList();

            // 2. Группируем по "Марка"
            var byMark = columns
                .GroupBy(fi => fi.LookupParameter("Марка")?.AsString() ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 3. Получаем ViewFamilyType для сечений
            var sectionType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .First(v => v.ViewFamily == ViewFamily.Section);

            using (var t = new Transaction(doc, "Чертежи пилонов"))
            {
                t.Start();

                foreach (var kv in byMark)
                {
                    string marka = kv.Key;
                    var instances = kv.Value;
                    string razd = doc.ActiveView.LookupParameter("ADSK_Штамп_Раздел проекта")?.AsString() ?? string.Empty;

                    var groupBBox = new BoundingBoxXYZ();

                    var fi = instances.FirstOrDefault();
                    var bb = fi.get_BoundingBox(null);


                }

                t.Commit();
            }

            return Result.Succeeded;
        }
    }
}
