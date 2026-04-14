using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FerrumAddinDev.ColumnSections
{
    [Transaction(TransactionMode.Manual)]
    public class CreateColumnSections : IExternalCommand
    {
        private static ViewSection FindSectionByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSection))
                .Cast<ViewSection>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static ViewSchedule FindScheduleByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static ViewSheet FindSheetByName(Document doc, string sheetName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .FirstOrDefault(s => s.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
        }

        private static Viewport FindViewportOnSheet(Document doc, ElementId sheetId, ElementId viewId)
        {
            return new FilteredElementCollector(doc, sheetId)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .FirstOrDefault(vp => vp.ViewId == viewId);
        }

        private static bool IsViewPlacedOnAnySheet(Document doc, ElementId viewId)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Any(vp => vp.ViewId == viewId);
        }

        private static ScheduleSheetInstance FindScheduleInstanceOnSheet(Document doc, ElementId sheetId, ElementId scheduleId)
        {
            return new FilteredElementCollector(doc, sheetId)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>()
                .FirstOrDefault(x => x.ScheduleId == scheduleId);
        }

        private static ViewSection GetOrCreateSection(
            Document doc,
            string viewName,
            ElementId sectionTypeId,
            BoundingBoxXYZ boundingBox,
            ElementId templateId,
            string razd)
        {
            var existing = FindSectionByName(doc, viewName);
            if (existing != null)
            {
                if (existing.Scale != 25)
                    existing.Scale = 25;

                if (existing.ViewTemplateId != templateId)
                    existing.ViewTemplateId = templateId;

                var existingParam = existing.LookupParameter("ADSK_Штамп_Раздел проекта");
                if (existingParam != null && !existingParam.IsReadOnly)
                    existingParam.Set(razd);

                return existing;
            }

            ViewSection section = ViewSection.CreateSection(doc, sectionTypeId, boundingBox);
            section.Scale = 25;
            section.Name = viewName;

            var p = section.LookupParameter("ADSK_Штамп_Раздел проекта");
            if (p != null && !p.IsReadOnly)
                p.Set(razd);

            section.ViewTemplateId = templateId;
            return section;
        }

        private static ViewSchedule GetOrCreateSchedule(
            Document doc,
            ViewSchedule template,
            string scheduleName,
            string marka)
        {
            var existing = FindScheduleByName(doc, scheduleName);
            if (existing != null)
                return existing;

            ViewSchedule newSchedule = doc.GetElement(template.Duplicate(ViewDuplicateOption.Duplicate)) as ViewSchedule;
            newSchedule.Name = scheduleName;

            ScheduleDefinition schedDef = newSchedule.Definition;
            if (schedDef.GetFilterCount() > 0)
            {
                ScheduleFilter firstFilter = schedDef.GetFilter(0);
                firstFilter.SetValue(marka);
                schedDef.SetFilter(0, firstFilter);
            }

            return newSchedule;
        }

        private static ViewSheet GetOrCreateSheetByName(
            Document doc,
            FamilySymbol tbSymbol,
            string sheetName,
            string sheetNumber,
            string razd)
        {
            var existing = FindSheetByName(doc, sheetName);
            if (existing != null)
            {
                var existingParam = existing.LookupParameter("ADSK_Штамп_Раздел проекта");
                if (existingParam != null && !existingParam.IsReadOnly)
                    existingParam.Set(razd);

                return existing;
            }

            ViewSheet sheet = ViewSheet.Create(doc, tbSymbol.Id);
            sheet.SheetNumber = sheetNumber;
            sheet.Name = sheetName;

            var p = sheet.LookupParameter("ADSK_Штамп_Раздел проекта");
            if (p != null && !p.IsReadOnly)
                p.Set(razd);

            return sheet;
        }

        private static Viewport GetOrCreateViewport(
            Document doc,
            ElementId sheetId,
            ElementId viewId,
            XYZ point,
            ElementId viewportTypeId)
        {
            var existing = FindViewportOnSheet(doc, sheetId, viewId);
            if (existing != null)
            {
                if (existing.GetTypeId() != viewportTypeId)
                    existing.ChangeTypeId(viewportTypeId);
                return existing;
            }

            if (IsViewPlacedOnAnySheet(doc, viewId))
                return null;

            Viewport vp = Viewport.Create(doc, sheetId, viewId, point);
            vp.ChangeTypeId(viewportTypeId);
            return vp;
        }

        private static ScheduleSheetInstance GetOrCreateScheduleInstance(
            Document doc,
            ElementId sheetId,
            ElementId scheduleId,
            XYZ point)
        {
            var existing = FindScheduleInstanceOnSheet(doc, sheetId, scheduleId);
            if (existing != null)
                return existing;

            return ScheduleSheetInstance.Create(doc, sheetId, scheduleId, point);
        }

        private static void SetViewportLabelOffsetAsInOriginal(Viewport viewport, View view)
        {
            if (viewport == null || view == null)
                return;

            BoundingBoxXYZ viewBox = view.get_BoundingBox(null);
            Outline outline = viewport.GetBoxOutline();

            double p1 = (outline.MaximumPoint.X - outline.MinimumPoint.X) / 2;
            double p2 = (viewBox.Max.Y - viewBox.Min.Y) / 50 + (outline.MaximumPoint.Y - outline.MinimumPoint.Y) / 2;


                viewport.LabelOffset = new XYZ(p1, p2, 0.000000000);
        }

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet es)
        {
            var uiDoc = data.Application.ActiveUIDocument;
            var doc = uiDoc.Document;

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

            var columns = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .ToList();

            var byMark = columns
                .GroupBy(fi => fi.LookupParameter("Марка")?.AsString() ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.ToList());

            var sectionType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section);

            if (sectionType == null)
            {
                MessageBox.Show("Не найдено семейство разрезов", "Ошибка");
                return Result.Failed;
            }

            string[] suffixes = new string[] { "", " Материал", " ВД", " ВРС" };
            List<ViewSchedule> scheduleTemplates = new List<ViewSchedule>();
            foreach (var sfx in suffixes)
            {
                string originalName = "!Армирование пилонов" + sfx;
                ViewSchedule origSchedule = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(vs => vs.Name.Equals(originalName));
                scheduleTemplates.Add(origSchedule);
            }
            if (scheduleTemplates.Count == 0 || scheduleTemplates.Any(x => x == null))
            {
                MessageBox.Show("Не найдены шаблоны спецификаций", "Ошибка");
                return Result.Failed;
            }

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
                new XYZ(8.682607785, 5.966082442, 0.000000000),
                new XYZ(8.922187027, 5.916110255, 0.000000000)
            };

            var vpTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType))
                .Cast<ElementType>()
                .Where(x => x.FamilyName == "Видовой экран")
                .ToList();

            var vpTypeRazrez = vpTypes.FirstOrDefault(vt => vt.Name.Equals("Разрез_Номер вида"));
            var vpTypeZagolovok = vpTypes.FirstOrDefault(vt => vt.Name.Equals("Заголовок на листе"));

            if (vpTypeRazrez == null || vpTypeZagolovok == null)
            {
                TaskDialog.Show("Ошибка", "Не найдены типы viewport: 'Разрез_Номер вида' и/или 'Заголовок на листе'");
                return Result.Failed;
            }

            ElementId razrezTypeId = vpTypeRazrez.Id;
            ElementId zagolovokTypeId = vpTypeZagolovok.Id;

            double offset = 150 / 304.8;
            string razd = doc.ActiveView.LookupParameter("ADSK_Штамп_Раздел проекта")?.AsString() ?? string.Empty;

            using (TransactionGroup tg = new TransactionGroup(doc))
            {
                tg.Start("Сечения по пилонам");

                foreach (var kv in byMark)
                {
                    using (var t = new Transaction(doc, "Сечения по пилонам"))
                    {
                        t.Start();

                        string marka = kv.Key;
                        var instances = kv.Value;
                        var fi = instances.FirstOrDefault();
                        if (fi == null)
                        {
                            t.Commit();
                            continue;
                        }

                        var bb = fi.get_BoundingBox(null);
                        if (bb == null)
                        {
                            t.Commit();
                            continue;
                        }

                        double elementLenght = bb.Max.X - bb.Min.X;
                        double elementWidth = bb.Max.Y - bb.Min.Y;
                        double elementHeight = bb.Max.Z - bb.Min.Z;

                        XYZ direction = elementWidth > elementLenght ? XYZ.BasisX : XYZ.BasisY;
                        XYZ center = (bb.Max + bb.Min) / 2 - direction * 500 / 304.8;
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
                            boundingBox.Min = new XYZ(-elementWidth / 2 - offset, -elementHeight / 2 - offset, 0);
                            boundingBox.Max = new XYZ(elementWidth / 2 + offset, elementHeight / 2 + offset, elementLenght + 500 / 304.8);
                        }
                        else
                        {
                            boundingBox.Min = new XYZ(-elementLenght / 2 - offset, -elementHeight / 2 - offset, 0);
                            boundingBox.Max = new XYZ(elementLenght / 2 + offset, elementHeight / 2 + offset, elementWidth + 500 / 304.8);
                        }

                        string section1Name = razd + " Пилон " + marka;
                        ViewSection section1 = GetOrCreateSection(doc, section1Name, sectionType.Id, boundingBox, templateId, razd);

                        direction = elementWidth < elementLenght ? XYZ.BasisX : XYZ.BasisY;
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

                        string section2Name = razd + " Пилон " + marka + " Р1";
                        ViewSection section2 = GetOrCreateSection(doc, section2Name, sectionType.Id, boundingBox, templateId, razd);

                        direction = -XYZ.BasisZ;
                        center = (bb.Max + bb.Min) / 2;
                        upDirection = elementWidth > elementLenght ? XYZ.BasisX : XYZ.BasisY;
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

                        string section3Name = razd + " Пилон " + marka + " Р";
                        ViewSection section3 = GetOrCreateSection(doc, section3Name, sectionType.Id, boundingBox, templateId, razd);

                        List<ViewSchedule> createdSchedules = new List<ViewSchedule>();
                        for (int i = 0; i < scheduleTemplates.Count; i++)
                        {
                            string scheduleName = $"ZH_{razd}_Арм_{marka}" + suffixes[i];
                            ViewSchedule schedule = GetOrCreateSchedule(doc, scheduleTemplates[i], scheduleName, marka);
                            createdSchedules.Add(schedule);
                        }

                        string sheetName = $"Пилон {marka}";
                        if (string.IsNullOrEmpty(marka))
                        {
                            sheetName = "Пилон";
                        }
                        string sheetNumber = nextSheetNumber.ToString();
                        bool sheetAlreadyExists = FindSheetByName(doc, sheetName) != null;
                        ViewSheet sheet = GetOrCreateSheetByName(doc, tbSymbol, sheetName, sheetNumber, razd);
                        if (!sheetAlreadyExists)
                            nextSheetNumber++;

                        var tbInstance = new FilteredElementCollector(doc, sheet.Id)
                            .OfCategory(BuiltInCategory.OST_TitleBlocks)
                            .OfClass(typeof(FamilyInstance))
                            .Cast<FamilyInstance>()
                            .FirstOrDefault(fi_ => fi_.OwnerViewId == sheet.Id);

                        if (tbInstance != null)
                        {
                            var locPoint = tbInstance.Location as LocationPoint;
                            if (locPoint != null)
                            {
                                XYZ currentPos = locPoint.Point;
                                XYZ targetPos = new XYZ(9.305967365, 5.081078844, 0.0);

                                if (!currentPos.IsAlmostEqualTo(targetPos))
                                {
                                    XYZ translation = targetPos - currentPos;
                                    ElementTransformUtils.MoveElement(doc, tbInstance.Id, translation);
                                }
                            }

                            var aParam = tbInstance.LookupParameter("А");
                            if (aParam != null && !aParam.IsReadOnly && aParam.AsInteger() != 3)
                                aParam.Set(3);
                        }

                        Viewport v1 = GetOrCreateViewport(doc, sheet.Id, section1.Id,
                            new XYZ(8.165574789, 5.646644124, -0.059228049), razrezTypeId);
                        SetViewportLabelOffsetAsInOriginal(v1, section1);

                        Viewport v2 = GetOrCreateViewport(doc, sheet.Id, section2.Id,
                            new XYZ(8.437272730, 5.643270480, -0.064714496), zagolovokTypeId);
                        SetViewportLabelOffsetAsInOriginal(v2, section2);

                        Viewport v3 = GetOrCreateViewport(doc, sheet.Id, section3.Id,
                            new XYZ(8.280574212, 5.181068513, -0.426660051), razrezTypeId);
                        SetViewportLabelOffsetAsInOriginal(v3, section3);

                        for (int i = 0; i < createdSchedules.Count; i++)
                        {
                            GetOrCreateScheduleInstance(doc, sheet.Id, createdSchedules[i].Id, schedulePoints[i]);
                        }

                        t.Commit();
                    }
                }

                tg.Assimilate();
            }

            return Result.Succeeded;
        }
    }
}
