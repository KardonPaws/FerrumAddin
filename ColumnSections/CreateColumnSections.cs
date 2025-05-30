using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Windows;

namespace FerrumAddinDev.ColumnSections
{
    [Transaction(TransactionMode.Manual)]
    public class CreateColumnSections : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet es)
        {
            var uiDoc = data.Application.ActiveUIDocument;
            var doc = uiDoc.Document;
            
            // Получаем шаблон вида для разреза
            var sectionTemplate = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(v => v.IsTemplate
                         && v.ViewType == ViewType.Section
                         && v.Name.Equals("ZH_КЖ_К_Арм_01"));
            if (sectionTemplate == null)
            {
                MessageBox.Show("Не найден шаблон вида ZH_КЖ_К_Арм_01", "Ошибка");
                return Result.Failed;
            }
            ElementId templateId = sectionTemplate.Id;
            // Собираем колонны
            var columns = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .ToList();

            // Группируем по "Марка"
            var byMark = columns
                .GroupBy(fi => fi.LookupParameter("Марка")?.AsString() ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Получаем ViewFamilyType для сечений
            var sectionType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .First(v => v.ViewFamily == ViewFamily.Section);

            if (sectionType == null)
            {
                MessageBox.Show("Не найдено семейство разрезов", "Ошибка");
                return Result.Failed;
            }

            // Получение шаблонов спецификаций
            string[] suffixes = new string[] { "", " ВД", " ВРС", " Материал" };
            List<ViewSchedule> scheduleTemplates = new List<ViewSchedule>();
            foreach (var sfx in suffixes)
            {
                // Исходное имя шаблона
                string originalName = "!Армирование пилонов" + sfx;
                // Ищем его в документе
                ViewSchedule origSchedule = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(vs => vs.Name.Equals(originalName));
                scheduleTemplates.Add(origSchedule);
            }
            if (scheduleTemplates.Count == 0)
            {
                MessageBox.Show("Не найдены шаблоны спецификаций", "Ошибка");
                return Result.Failed;
            }

            // Получаем символ штампа 
            var tbSymbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Where(x => x.Name == "КЖ")
                .Cast<FamilySymbol>()
                .FirstOrDefault();
            if (tbSymbol == null)
            {
                TaskDialog.Show("Ошибка", "Не найдено семейство штампа");
                return Result.Failed;
            }

            // Определяем следующий номер листа
            var existingNumbers = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(s =>
                {
                    int n;
                    return int.TryParse(s.SheetNumber, out n) ? n : 0;
                });
            int nextSheetNumber = existingNumbers.Any() ? existingNumbers.Max() + 1 : 1;

            List<XYZ> schedulePoints = new List<XYZ>()
            {
                new XYZ(8.682607785, 6.039084094, 0.000000000),
                new XYZ(8.682607785, 5.916110255, 0.000000000),
                new XYZ(8.922187027, 5.916110255, 0.000000000),
                new XYZ(8.682607785, 5.966082442, 0.000000000)
            };

            var vpTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(ElementType))
            .Cast<ElementType>().Where(x => x.FamilyName == "Видовой экран")
            .ToList();

            var vpTypeRazrez = vpTypes
                .FirstOrDefault(vt => vt.Name.Equals("Разрез_Номер вида"));
            var vpTypeZagolovok = vpTypes
                .FirstOrDefault(vt => vt.Name.Equals("Заголовок на листе"));

            if (vpTypeRazrez == null || vpTypeZagolovok == null)
            {
                TaskDialog.Show("Ошибка", "Не найдены типы viewport: 'Разрез_Номер вида' и/или 'Заголовок на листе'");
                return Result.Failed;
            }

            ElementId razrezTypeId = vpTypeRazrez.Id;
            ElementId zagolovokTypeId = vpTypeZagolovok.Id;

            double offset = 150 / 304.8;
            string razd = doc.ActiveView.LookupParameter("ADSK_Штамп_Раздел проекта")?.AsString() ?? string.Empty;
            using (var t = new Transaction(doc, "Сечения по пилонам"))
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
                    section1.Scale = 25;
                    try
                    {
                        section1.Name = razd + " Пилон " + marka;
                    }
                    catch
                    {
                        t.RollBack();
                        continue;
                    }
                    section1.LookupParameter("ADSK_Штамп_Раздел проекта").Set(razd);
                    section1.ViewTemplateId = templateId;

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
                    section2.Scale = 25;
                    section2.Name = razd + " Пилон " + marka + " Р1";
                    section2.LookupParameter("ADSK_Штамп_Раздел проекта").Set(razd);
                    section2.ViewTemplateId = templateId;

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
                    section3.Scale = 25;
                    section3.Name = razd + " Пилон " + marka + " Р";
                    section3.LookupParameter("ADSK_Штамп_Раздел проекта").Set(razd);
                    section3.ViewTemplateId = templateId;

                    List<ViewSchedule> createdSchedules = new List<ViewSchedule>();
                    for (int i = 0; i < scheduleTemplates.Count(); i++)
                    {
                        ViewSchedule newSchedule = doc.GetElement(scheduleTemplates[i].Duplicate(ViewDuplicateOption.Duplicate)) as ViewSchedule;
                        createdSchedules.Add(newSchedule);
                        // Переименовываем
                        newSchedule.Name = $"ZH_{razd}_Арм_{marka}" + suffixes[i];

                        // Меняем значение первого фильтра на текущую марку
                        ScheduleDefinition schedDef = newSchedule.Definition;
                        if (schedDef.GetFilterCount() > 0)
                        {
                            ScheduleFilter firstFilter = schedDef.GetFilter(0);
                            firstFilter.SetValue(@marka);
                            schedDef.SetFilter(0, firstFilter);
                        }
                    }

                    ViewSheet sheet = ViewSheet.Create(doc, tbSymbol.Id);

                    // Заполняем параметры листа
                    sheet.LookupParameter("ADSK_Штамп_Раздел проекта").Set(razd);
                    sheet.SheetNumber = nextSheetNumber.ToString();
                    sheet.Name = $"Пилон {marka}";
                    nextSheetNumber++;

                    var tbInstance = new FilteredElementCollector(doc, sheet.Id)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>()
                        .FirstOrDefault(fi_ => fi_.OwnerViewId == sheet.Id);

                    if (tbInstance != null)
                    {
                        // Получаем текущее местоположение
                        var locPoint = tbInstance.Location as LocationPoint;
                        if (locPoint != null)
                        {
                            XYZ currentPos = locPoint.Point;
                            XYZ targetPos = new XYZ(9.305967365, 5.081078844, 0.0);
                            XYZ translation = targetPos - currentPos;

                            // Сдвигаем элемент
                            ElementTransformUtils.MoveElement(doc, tbInstance.Id, translation);
                        }
                        tbInstance.LookupParameter("А").Set(3);
                    }


                    // Размещаем 3 разреза на листе
                    Viewport v1 = Viewport.Create(doc, sheet.Id, section1.Id, new XYZ(8.165574789, 5.646644124, -0.059228049));
                    v1.ChangeTypeId(razrezTypeId);
                    v1.LabelOffset = new XYZ(0.166867110, 0.696294923, 0.000000000);
                    Viewport v2 = Viewport.Create(doc, sheet.Id, section2.Id, new XYZ(8.437272730, 5.643270480, -0.064714496));
                    v2.ChangeTypeId(zagolovokTypeId);
                    v2.LabelOffset = new XYZ(0.118537803, 0.630344372, 0.000000000);
                    Viewport v3 = Viewport.Create(doc, sheet.Id, section3.Id, new XYZ(8.280574212, 5.181068513, -0.426660051));
                    v3.ChangeTypeId(razrezTypeId);
                    v3.LabelOffset = new XYZ(0.098949906, 0.151181997, 0.000000000);

                    // Размещаем 4 спецификации
                    for (int i = 0; i < createdSchedules.Count; i++)
                    {
                        double x = 1.0 + i * 6.0;
                        ScheduleSheetInstance.Create(doc, sheet.Id, createdSchedules[i].Id, schedulePoints[i]);
                    }
                }

                t.Commit();
            }

            return Result.Succeeded;
        }
    }
}
