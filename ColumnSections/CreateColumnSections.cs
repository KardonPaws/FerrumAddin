using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
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

            double offset = 150 / 304.8;
            string razd = doc.ActiveView.LookupParameter("ADSK_Штамп_Раздел проекта")?.AsString() ?? string.Empty;
            using (var t = new Transaction(doc, "Чертежи пилонов"))
            {
                t.Start();

                foreach (var kv in byMark)
                {
                    string marka = kv.Key;
                    var instances = kv.Value;
                    var firstElement = instances.FirstOrDefault();

                    var groupBBox = new BoundingBoxXYZ();

                    var fi = instances.FirstOrDefault();
                    var bb = fi.get_BoundingBox(null);

                    // Размеры элемента
                    double elementLenght = bb.Max.X - bb.Min.X;
                    double elementWidth = bb.Max.Y - bb.Min.Y;
                    double elementHeight = bb.Max.Z - bb.Min.Z;
                    
                    XYZ direction = XYZ.Zero;
                    if (elementWidth > elementLenght)
                    {
                        direction = XYZ.BasisX;
                    }
                    else
                    {
                        direction = XYZ.BasisY;
                    }
                    XYZ center = (bb.Max + bb.Min) / 2 - direction*500/304.8;


                    XYZ upDirection = XYZ.BasisZ;
                    
                    XYZ crossDirection = direction.CrossProduct(upDirection).Negate();


                    Transform trans = Transform.Identity;
                    trans.Origin = center;
                    trans.BasisX = crossDirection;
                    trans.BasisY = upDirection;
                    trans.BasisZ = direction;

                    BoundingBoxXYZ boundingBox = new BoundingBoxXYZ();
                    boundingBox.Transform = trans;

                    if (elementWidth > elementLenght)
                    {
                        boundingBox.Min = new XYZ(-elementWidth / 2 - offset , - elementHeight / 2 - offset, 0); 
                        boundingBox.Max = new XYZ(elementWidth / 2 + offset, elementHeight / 2 + offset, elementLenght + 500/304.8);   
                    }
                    else
                    {
                        boundingBox.Min = new XYZ(-elementLenght / 2 - offset, -elementHeight / 2 - offset, 0);
                        boundingBox.Max = new XYZ(elementLenght / 2 + offset, elementHeight / 2 + offset, elementWidth + 500/304.8);
                    }

                    // Создание разреза
                    ViewSection section1 = ViewSection.CreateSection(doc, sectionType.Id, boundingBox);
                    section1.Name = razd + " Пилон " + marka;
                    section1.LookupParameter("ADSK_Штамп_Раздел проекта").SetValueString(razd);

                    direction = XYZ.Zero;
                    if (elementWidth < elementLenght)
                    {
                        direction = XYZ.BasisX;
                    }
                    else
                    {
                        direction = XYZ.BasisY;
                    }
                    center = (bb.Max + bb.Min) / 2;


                    upDirection = XYZ.BasisZ;

                    crossDirection = direction.CrossProduct(upDirection).Negate();


                    trans = Transform.Identity;
                    trans.Origin = center;
                    trans.BasisX = crossDirection;
                    trans.BasisY = upDirection;
                    trans.BasisZ = direction;

                    boundingBox = new BoundingBoxXYZ();
                    boundingBox.Transform = trans;

                    if (elementWidth < elementLenght)
                    {
                        boundingBox.Min = new XYZ(-elementWidth / 2 - offset, -elementHeight / 2 - offset, 0);
                        boundingBox.Max = new XYZ(elementWidth / 2 + offset, elementHeight / 2 + offset, elementLenght / 2 + 100 / 304.8);
                    }
                    else
                    {
                        boundingBox.Min = new XYZ(-elementLenght / 2 - offset, -elementHeight / 2 - offset, 0);
                        boundingBox.Max = new XYZ(elementLenght / 2 + offset, elementHeight / 2 + offset, elementWidth / 2 + 100 / 304.8);
                    }

                    // Создание разреза
                    ViewSection section2 = ViewSection.CreateSection(doc, sectionType.Id, boundingBox);
                    section2.Name = razd + " Пилон " + marka + " Р1";
                    section2.LookupParameter("ADSK_Штамп_Раздел проекта").SetValueString(razd);

                    direction = -XYZ.BasisZ;
                    center = (bb.Max + bb.Min) / 2;

                    upDirection = XYZ.Zero;
                    if (elementWidth > elementLenght)
                    {
                        upDirection = XYZ.BasisX;
                    }
                    else
                    {
                        upDirection = XYZ.BasisY;
                    }

                    crossDirection = direction.CrossProduct(upDirection).Negate();


                    trans = Transform.Identity;
                    trans.Origin = center;
                    trans.BasisX = crossDirection;
                    trans.BasisY = upDirection;
                    trans.BasisZ = direction;

                    boundingBox = new BoundingBoxXYZ();
                    boundingBox.Transform = trans;

                    if (elementWidth < elementLenght)
                    {
                        boundingBox.Min = new XYZ(-elementLenght / 2 - offset, -elementWidth / 2 - offset, 0);
                        boundingBox.Max = new XYZ(elementLenght / 2 + offset, elementWidth / 2 + offset, elementHeight / 2 - 100 / 304.8);
                    }
                    else
                    {
                        boundingBox.Min = new XYZ(-elementWidth / 2 - offset, -elementLenght / 2 - offset, 0);
                        boundingBox.Max = new XYZ(elementWidth / 2 + offset, elementLenght / 2 + offset, elementHeight / 2 - 100 / 304.8);
                    }

                    // Создание разреза
                    ViewSection section3 = ViewSection.CreateSection(doc, sectionType.Id, boundingBox);
                    section3.Name = razd + " Пилон " + marka + " Р";
                    section3.LookupParameter("ADSK_Штамп_Раздел проекта").SetValueString(razd);
                }

                t.Commit();
            }

            return Result.Succeeded;
        }
    }
}
