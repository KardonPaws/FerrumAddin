using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FerrumAddinDev.FM;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using View = Autodesk.Revit.DB.View;

namespace FerrumAddinDev.LintelCreator_v3
{
    internal static class LintelServiceSettingsV3
    {
        public static bool SplitByZeroElevation { get; set; }
    }

    internal static class LintelServiceElementFilterV3
    {
        public static bool IsLintel(FamilyInstance instance)
        {
            if (instance == null
                || instance.SuperComponent != null
                || instance.Category?.Id.Value != (long)BuiltInCategory.OST_StructuralFraming)
                return false;

            string grouping = instance.LookupParameter("ADSK_Группирование")?.AsString();
            string keyNote = instance.Symbol?.LookupParameter("Ключевая пометка")?.AsString();
            string model = instance.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString();
            return string.Equals(grouping, "ПР", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(keyNote, "ПР", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(model, "Перемычки составные", StringComparison.OrdinalIgnoreCase);
        }

        public static double GetStoreyNumber(FamilyInstance instance)
        {
            Parameter parameter = instance?.LookupParameter("ZH_Этаж_Числовой");
            if (parameter == null || !parameter.HasValue) return 0;
            if (parameter.StorageType == StorageType.Double) return parameter.AsDouble();
            if (parameter.StorageType == StorageType.Integer) return parameter.AsInteger();
            string text = parameter.AsString() ?? parameter.AsValueString();
            return double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out double value)
                ? value
                : 0;
        }
    }

    public class PlaceSectionsV3 : IExternalEventHandler
    {
        private const string ScheduleAboveName = "Ведомость_Пр_выше 0,00";
        private const string ScheduleBelowName = "Ведомость_Пр_ниже 0,00";

        private sealed class PlacementReport
        {
            public string GroupName { get; set; }
            public string ExpectedScheduleName { get; set; }
            public string FoundScheduleName { get; set; }
            public int ScheduleInstanceCount { get; set; }
            public int TotalSections { get; set; }
            public List<string> Placed { get; } = new List<string>();
            public List<string> AlreadyOnCurrentSheet { get; } = new List<string>();
            public List<string> OnOtherSheets { get; } = new List<string>();
            public List<string> SkippedWithoutSchedule { get; } = new List<string>();
            public List<string> CannotBePlaced { get; } = new List<string>();
            public List<string> Errors { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();
            public List<string> RolledBack { get; } = new List<string>();

            public bool ScheduleFound => !string.IsNullOrEmpty(FoundScheduleName);

            public int SkippedCount => AlreadyOnCurrentSheet.Count
                                       + OnOtherSheets.Count
                                       + SkippedWithoutSchedule.Count
                                       + CannotBePlaced.Count
                                       + Errors.Count
                                       + RolledBack.Count;
        }

        public void Execute(UIApplication uiApp)
        {
            Document doc = uiApp.ActiveUIDocument.Document;

            ViewSheet activeSheet = doc.ActiveView as ViewSheet;
            if (activeSheet == null)
            {
                TaskDialog.Show(
                    "Размещение разрезов",
                    "Размещение не выполнено.\n\nОткройте лист, на котором должны быть размещены разрезы, и повторите команду.");
                return;
            }

            // Получение всех ScheduleSheetInstance на активном листе.
            List<ScheduleSheetInstance> scheduleInstances = new FilteredElementCollector(doc, activeSheet.Id)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>()
                .ToList();

            // Группировка ScheduleSheetInstance по имени ведомости. Некорректные экземпляры
            // без связанной спецификации не должны прерывать выполнение всей команды.
            Dictionary<string, List<ScheduleSheetInstance>> scheduleGroups = scheduleInstances
                .Select(s => new { Instance = s, Schedule = doc.GetElement(s.ScheduleId) })
                .Where(x => x.Schedule != null && !string.IsNullOrWhiteSpace(x.Schedule.Name))
                .GroupBy(x => x.Schedule.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Instance).OrderBy(s => s.SegmentIndex).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            // Получение всех разрезов из документа.
            List<ViewSection> sections = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSection))
                .Cast<ViewSection>()
                .ToList();

            // 12.02.26 - марки под углом + нумерация + разрезы.
            int ExtractNumber(string name)
            {
                Match match = Regex.Match(name ?? string.Empty, @"Пр-(\d+)");
                return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue;
            }

            List<ViewSection> sectionsAbove = sections
                .Where(s => s.Name.IndexOf("выше 0", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(s => ExtractNumber(s.Name))
                .ThenBy(s => s.Name)
                .ToList();

            List<ViewSection> sectionsBelow = sections
                .Where(s => s.Name.IndexOf("ниже 0", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(s => ExtractNumber(s.Name))
                .ThenBy(s => s.Name)
                .ToList();

            Dictionary<ElementId, List<Viewport>> viewportsByViewId = new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .GroupBy(v => v.ViewId)
                .ToDictionary(g => g.Key, g => g.ToList());

            ElementType viewportType = new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType))
                .Cast<ElementType>()
                .FirstOrDefault(x => string.Equals(x.FamilyName, "Видовой экран", StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(x.Name, "Без названия", StringComparison.OrdinalIgnoreCase));

            List<PlacementReport> reports = new List<PlacementReport>();
            string fatalError = null;

            try
            {
                using (Transaction trans = new Transaction(doc, "Размещение разрезов"))
                {
                    trans.Start();

                    reports.Add(PlaceSections(
                        doc,
                        activeSheet,
                        sectionsAbove,
                        scheduleGroups,
                        ScheduleAboveName,
                        "Разрезы выше 0",
                        viewportType?.Id,
                        viewportsByViewId));

                    reports.Add(PlaceSections(
                        doc,
                        activeSheet,
                        sectionsBelow,
                        scheduleGroups,
                        ScheduleBelowName,
                        "Разрезы ниже 0",
                        viewportType?.Id,
                        viewportsByViewId));

                    if (trans.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit не зафиксировал транзакцию размещения.");
                }
            }
            catch (Exception ex)
            {
                fatalError = ex.Message;
                foreach (PlacementReport report in reports)
                {
                    report.RolledBack.AddRange(report.Placed);
                    report.Placed.Clear();
                }
            }

            ShowPlacementReport(activeSheet, reports, fatalError);
        }

        private PlacementReport PlaceSections(
            Document doc,
            ViewSheet activeSheet,
            List<ViewSection> sections,
            Dictionary<string, List<ScheduleSheetInstance>> scheduleGroups,
            string scheduleName,
            string groupName,
            ElementId viewportTypeId,
            Dictionary<ElementId, List<Viewport>> viewportsByViewId)
        {
            PlacementReport report = new PlacementReport
            {
                GroupName = groupName,
                ExpectedScheduleName = scheduleName,
                TotalSections = sections.Count
            };

            List<KeyValuePair<string, List<ScheduleSheetInstance>>> matchingScheduleGroups = scheduleGroups
                .Where(x => x.Key.IndexOf(scheduleName, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(x => x.Key)
                .ToList();

            List<ScheduleSheetInstance> matchingScheduleInstances = null;
            if (matchingScheduleGroups.Count > 0)
            {
                KeyValuePair<string, List<ScheduleSheetInstance>> selectedGroup = matchingScheduleGroups.First();
                report.FoundScheduleName = selectedGroup.Key;
                report.ScheduleInstanceCount = selectedGroup.Value.Count;
                matchingScheduleInstances = selectedGroup.Value;

                if (matchingScheduleGroups.Count > 1)
                {
                    report.Warnings.Add(
                        "Найдено несколько подходящих спецификаций: "
                        + string.Join(", ", matchingScheduleGroups.Select(x => "«" + x.Key + "»"))
                        + ". Для привязки использована «" + selectedGroup.Key + "». ");
                }
            }

            XYZ basePoint = matchingScheduleInstances?.FirstOrDefault()?.Point;

            for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            {
                ViewSection section = sections[sectionIndex];
                List<Viewport> existingViewports;

                if (viewportsByViewId.TryGetValue(section.Id, out existingViewports)
                    && existingViewports.Count > 0)
                {
                    if (existingViewports.Any(v => v.SheetId.Equals(activeSheet.Id)))
                    {
                        report.AlreadyOnCurrentSheet.Add(section.Name);
                    }
                    else
                    {
                        List<string> sheetNames = existingViewports
                            .Select(v => GetSheetName(doc, v.SheetId))
                            .Distinct()
                            .OrderBy(x => x)
                            .ToList();
                        report.OnOtherSheets.Add(section.Name + " → " + string.Join(", ", sheetNames));
                    }

                    continue;
                }

                if (!report.ScheduleFound || basePoint == null)
                {
                    report.SkippedWithoutSchedule.Add(section.Name);
                    continue;
                }

                if (viewportTypeId == null || viewportTypeId.Equals(ElementId.InvalidElementId))
                {
                    report.CannotBePlaced.Add(section.Name + " — не найден тип видового экрана «Без названия».");
                    continue;
                }

                bool canAddToSheet;
                try
                {
                    canAddToSheet = Viewport.CanAddViewToSheet(doc, activeSheet.Id, section.Id);
                }
                catch (Exception ex)
                {
                    report.Errors.Add(section.Name + " — не удалось проверить возможность размещения: " + ex.Message);
                    continue;
                }

                if (!canAddToSheet)
                {
                    report.CannotBePlaced.Add(
                        section.Name + " — Revit не разрешает разместить этот вид на листе (проверьте шаблон вида и существующие размещения). ");
                    continue;
                }

                // Индекс учитывается даже для пропущенных видов: оставшиеся разрезы сохраняют
                // соответствие строкам спецификации и не сдвигаются на их место.
                double yOffset = sectionIndex * 0.166;
                XYZ placementPoint = new XYZ(basePoint.X + 0.16, basePoint.Y - 0.15 - yOffset, basePoint.Z);

                using (SubTransaction subTransaction = new SubTransaction(doc))
                {
                    try
                    {
                        subTransaction.Start();
                        Viewport viewport = Viewport.Create(doc, activeSheet.Id, section.Id, placementPoint);
                        viewport.ChangeTypeId(viewportTypeId);
                        if (subTransaction.Commit() == TransactionStatus.Committed)
                            report.Placed.Add(section.Name);
                        else
                            report.Errors.Add(section.Name + " — Revit отменил размещение вида.");
                    }
                    catch (Exception ex)
                    {
                        if (subTransaction.GetStatus() == TransactionStatus.Started)
                            subTransaction.RollBack();

                        report.Errors.Add(section.Name + " — ошибка размещения: " + ex.Message);
                    }
                }
            }

            return report;
        }

        private static string GetSheetName(Document doc, ElementId sheetId)
        {
            ViewSheet sheet = doc.GetElement(sheetId) as ViewSheet;
            if (sheet == null)
                return "лист с Id " + sheetId.Value;

            return "лист «" + sheet.SheetNumber + " — " + sheet.Name + "»";
        }

        private static void ShowPlacementReport(
            ViewSheet activeSheet,
            List<PlacementReport> reports,
            string fatalError)
        {
            int totalSections = reports.Sum(x => x.TotalSections);
            int placedCount = reports.Sum(x => x.Placed.Count);
            int skippedCount = reports.Sum(x => x.SkippedCount);
            int errorCount = reports.Sum(x => x.Errors.Count) + (fatalError == null ? 0 : 1);
            int missingScheduleCount = reports.Count(x => !x.ScheduleFound);

            StringBuilder mainContent = new StringBuilder();
            mainContent.AppendLine("Лист: " + activeSheet.SheetNumber + " — " + activeSheet.Name);
            mainContent.AppendLine("Найдено разрезов: " + totalSections);
            mainContent.AppendLine("Размещено: " + placedCount);
            mainContent.AppendLine("Пропущено: " + skippedCount);
            mainContent.AppendLine("Ошибок: " + errorCount);

            if (missingScheduleCount > 0)
            {
                mainContent.AppendLine();
                mainContent.AppendLine(
                    "Нет спецификаций для размещения: "
                    + string.Join(", ", reports.Where(x => !x.ScheduleFound).Select(x => "«" + x.ExpectedScheduleName + "»"))
                    + ".");
            }

            if (totalSections == 0 && fatalError == null)
            {
                mainContent.AppendLine();
                mainContent.AppendLine("В проекте не найдены разрезы с «выше 0» или «ниже 0» в имени.");
            }

            StringBuilder details = new StringBuilder();
            foreach (PlacementReport report in reports)
            {
                details.AppendLine(report.GroupName);
                details.AppendLine("Разрезов найдено: " + report.TotalSections);
                details.AppendLine(report.ScheduleFound
                    ? "Спецификация: «" + report.FoundScheduleName + "» (экземпляров/сегментов на листе: " + report.ScheduleInstanceCount + ")."
                    : "Спецификация: НЕ НАЙДЕНА. Ожидалась «" + report.ExpectedScheduleName + "».");

                AppendItems(details, "Размещено", report.Placed);
                AppendItems(details, "Уже было на текущем листе — пропущено", report.AlreadyOnCurrentSheet);
                AppendItems(details, "На других листах — пропущено", report.OnOtherSheets);
                AppendItems(details, "Без нужной спецификации — пропущено", report.SkippedWithoutSchedule);
                AppendItems(details, "Невозможно разместить — пропущено", report.CannotBePlaced);
                AppendItems(details, "Ошибки", report.Errors);
                AppendItems(details, "Предупреждения", report.Warnings);
                AppendItems(details, "Размещение отменено из-за общей ошибки", report.RolledBack);
                details.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(fatalError))
            {
                details.AppendLine("Общая ошибка транзакции");
                details.AppendLine("• " + fatalError);
            }

            bool hasWarnings = skippedCount > 0
                               || missingScheduleCount > 0
                               || errorCount > 0
                               || reports.Any(x => x.Warnings.Count > 0)
                               || (totalSections == 0 && fatalError == null);

            TaskDialog dialog = new TaskDialog("Размещение разрезов")
            {
                MainInstruction = fatalError != null
                    ? "Размещение не завершено: изменения отменены."
                    : hasWarnings
                        ? "Размещение завершено с уведомлениями."
                        : "Все разрезы успешно размещены.",
                MainContent = mainContent.ToString().TrimEnd(),
                ExpandedContent = details.ToString().TrimEnd(),
                MainIcon = hasWarnings
                    ? TaskDialogIcon.TaskDialogIconWarning
                    : TaskDialogIcon.TaskDialogIconInformation,
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.Show();
        }

        private static void AppendItems(StringBuilder builder, string title, List<string> items)
        {
            if (items.Count == 0)
                return;

            builder.AppendLine(title + " (" + items.Count + "):");
            foreach (string item in items)
                builder.AppendLine("• " + item);
        }

        public string GetName()
        {
            return "Размещение разрезов";
        }
    }

    public class LintelNumerateV3 : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            bool check = LintelServiceSettingsV3.SplitByZeroElevation;
            using (Transaction trans = new Transaction(doc, "Нумерация элементов"))
            {
                trans.Start();

                try
                {
                    // Сбор всех элементов категории OST_StructuralFraming
                    var framingElements = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(LintelServiceElementFilterV3.IsLintel)
                    .OrderBy(f => f.Symbol.Name) // Сортировка элементов по имени символа
                    .ToList();

                    // 24.02.26 - изменена сортировка
                    var alphaNum = new AlphanumComparatorFastString();

                    string GetTypeName(ElementId symbolId)
                    {
                        var sym = doc.GetElement(symbolId) as FamilySymbol;
                        return sym?.Name ?? string.Empty;
                    }

                    var groupedElements = framingElements
                        .GroupBy(el => el.Symbol.Id)
                        .OrderBy(g => GetTypeName(g.Key), alphaNum);                             

                    if (check)
                    {
                        int positionCounter1 = 1;
                        int positionCounter2 = 1;
                        foreach (var group in groupedElements)
                        {
                            // 16.02.26 - игнорирование стен гкл и фсд + изменения нумерации
                            bool v1 = false;
                            bool v2 = false;
                            foreach (var element in group)
                            {
                                //18.02.26 - изменения в перемычках
                                if (element.LookupParameter("ZH_Этаж_Числовой").AsDouble() > 0)
                                {
                                    string positionValue = $"Пр-{positionCounter1}";

                                   // Назначение значения параметру ADSK_Позиция
                                    var positionParam = element.LookupParameter("ADSK_Позиция");
                                    if (positionParam != null && positionParam.IsReadOnly == false)
                                    {
                                        positionParam.Set(positionValue);
                                    }
                                    v1 = true;
                                }
                                else
                                {
                                    string positionValue = $"Пр-{positionCounter2}";

                                    // Назначение значения параметру ADSK_Позиция
                                    var id = element.Id;
                                    var positionParam = element.LookupParameter("ADSK_Позиция");
                                    if (positionParam != null && positionParam.IsReadOnly == false)
                                    {
                                        positionParam.Set(positionValue);
                                    }
                                    v2 = true;
                                }
                            }
                            if (v1)
                            {
                                positionCounter1++;
                            }
                            if (v2)
                            {
                                positionCounter2++;
                            }
                        }
                    }
                    else
                    {
                        // Нумерация групп
                        int positionCounter = 1;
                        foreach (var group in groupedElements)
                        {
                            string positionValue = $"Пр-{positionCounter}";

                            foreach (var element in group)
                            {
                                // Назначение значения параметру ADSK_Позиция
                                var positionParam = element.LookupParameter("ADSK_Позиция");
                                if (positionParam != null && positionParam.IsReadOnly == false)
                                {
                                    positionParam.Set(positionValue);
                                }
                            }

                            positionCounter++;
                        }
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка");
                    trans.RollBack();
                }
            }
        }

        public string GetName()
        {
            return "Нумерация перемычек";
        }
    }

    public class NestedElementsNumberingV3 : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            // 24.02.26 - изменена сортировка
            bool check = LintelServiceSettingsV3.SplitByZeroElevation;
            var alphaNum = new AlphanumComparatorFastString();

            string GetTypeParamString(ElementId symbolId, string paramName)
            {
                var sym = doc.GetElement(symbolId) as FamilySymbol;
                if (sym == null) return string.Empty;

                var p = sym.LookupParameter(paramName);
                var s = p?.AsString() ?? p?.AsValueString() ?? string.Empty;
                return (s ?? string.Empty).Trim();
            }

            string GetTypeName(ElementId symbolId)
            {
                var sym = doc.GetElement(symbolId) as FamilySymbol;
                return (sym?.Name ?? string.Empty).Trim();
            }

            using (Transaction trans = new Transaction(doc, "Нумерация вложенных элементов"))
            {
                trans.Start();

                try
                {
                    if (!check)
                    {
                        // Сбор всех элементов категории OST_StructuralFraming
                        var framingElements = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .WhereElementIsNotElementType()
                            .Cast<FamilyInstance>()
                            .Where(LintelServiceElementFilterV3.IsLintel)
                            .ToList();
                        Dictionary<string, int> dict = new Dictionary<string, int>();
                        int nestedCounter = 1;
                        // 24.02.26 - изменена сортировка
                        Dictionary<ElementId, List<Element>> nestedTypes = new Dictionary<ElementId, List<Element>>();

                        foreach (var element in framingElements)
                        {
                            if (element.SuperComponent != null) continue;

                            var subElements = element.GetSubComponentIds();
                            if (subElements.Count == 0) continue;

                            foreach (var aSubElemId in subElements)
                            {
                                var nestedElement = doc.GetElement(aSubElemId) as FamilyInstance;
                                if (nestedElement == null) continue;

                                var symId = nestedElement.Symbol.Id;

                                if (nestedTypes.ContainsKey(symId))
                                    nestedTypes[symId].Add(nestedElement);
                                else
                                    nestedTypes.Add(symId, new List<Element> { nestedElement });
                            }
                        }

                        // сортировка: 1) ADSK_Обозначение (тип), 2) имя типа
                        var nestedTypesSorted = nestedTypes
                            .OrderBy(kv => GetTypeParamString(kv.Key, "ADSK_Обозначение"), alphaNum)
                            .ThenBy(kv => GetTypeName(kv.Key), alphaNum)
                            .ToList();

                        foreach (var group in nestedTypesSorted)
                        {
                            foreach (var el in group.Value)
                            {
                                var positionParam = el.LookupParameter("ADSK_Позиция");
                                if (positionParam != null && !positionParam.IsReadOnly)
                                    positionParam.Set(nestedCounter.ToString());
                            }
                            nestedCounter++;
                        }
                    }
                    //18.02.26 - изменения в перемычках
                    else
                    {
                        // Сбор всех элементов категории OST_StructuralFraming
                        var framingElementsUp = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .WhereElementIsNotElementType()
                            .Cast<FamilyInstance>()
                            .Where(LintelServiceElementFilterV3.IsLintel)
                            .Where(x => LintelServiceElementFilterV3.GetStoreyNumber(x) > 0)
                            .ToList();
                        var framingElementsDown = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .WhereElementIsNotElementType()
                            .Cast<FamilyInstance>()
                            .Where(LintelServiceElementFilterV3.IsLintel)
                            .Where(x => LintelServiceElementFilterV3.GetStoreyNumber(x) < 0)
                            .ToList();
                        Dictionary<string, int> dict = new Dictionary<string, int>();
                        int nestedCounterUp = 1;
                        int nestedCounterDown = 1;
                        Dictionary<ElementId, List<Element>> nestedTypesUp = new Dictionary<ElementId, List<Element>>();

                        foreach (var element in framingElementsUp)
                        {
                            if (element.SuperComponent != null) continue;

                            var subElements = element.GetSubComponentIds();
                            if (subElements.Count == 0) continue;

                            foreach (var aSubElemId in subElements)
                            {
                                var nestedElement = doc.GetElement(aSubElemId) as FamilyInstance;
                                if (nestedElement == null) continue;

                                var symId = nestedElement.Symbol.Id;

                                if (nestedTypesUp.ContainsKey(symId))
                                    nestedTypesUp[symId].Add(nestedElement);
                                else
                                    nestedTypesUp.Add(symId, new List<Element> { nestedElement });
                            }
                        }

                        var nestedTypesUpSorted = nestedTypesUp
                            .OrderBy(kv => GetTypeParamString(kv.Key, "ADSK_Обозначение"), alphaNum)
                            .ThenBy(kv => GetTypeName(kv.Key), alphaNum)
                            .ToList();

                        foreach (var group in nestedTypesUpSorted)
                        {
                            foreach (var el in group.Value)
                            {
                                var positionParam = el.LookupParameter("ADSK_Позиция");
                                if (positionParam != null && !positionParam.IsReadOnly)
                                    positionParam.Set(nestedCounterUp.ToString());
                            }
                            nestedCounterUp++;
                        }

                        Dictionary<ElementId, List<Element>> nestedTypesDown = new Dictionary<ElementId, List<Element>>();

                        foreach (var element in framingElementsDown)
                        {
                            if (element.SuperComponent != null) continue;

                            var subElements = element.GetSubComponentIds();
                            if (subElements.Count == 0) continue;

                            foreach (var aSubElemId in subElements)
                            {
                                var nestedElement = doc.GetElement(aSubElemId) as FamilyInstance;
                                if (nestedElement == null) continue;

                                var symId = nestedElement.Symbol.Id;

                                if (nestedTypesDown.ContainsKey(symId))
                                    nestedTypesDown[symId].Add(nestedElement);
                                else
                                    nestedTypesDown.Add(symId, new List<Element> { nestedElement });
                            }
                        }

                        var nestedTypesDownSorted = nestedTypesDown
                            .OrderBy(kv => GetTypeParamString(kv.Key, "ADSK_Обозначение"), alphaNum)
                            .ThenBy(kv => GetTypeName(kv.Key), alphaNum)
                            .ToList();

                        foreach (var group in nestedTypesDownSorted)
                        {
                            foreach (var el in group.Value)
                            {
                                var positionParam = el.LookupParameter("ADSK_Позиция");
                                if (positionParam != null && !positionParam.IsReadOnly)
                                    positionParam.Set(nestedCounterDown.ToString());
                            }
                            nestedCounterDown++;
                        }
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка");
                    trans.RollBack();
                }
            }
        }

        public string GetName()
        {
            return "Нумерация вложенных элементов перемычек";
        }
    }

    // 07.08.26 - отдельная кнопка тип основы в перемычках, новая логика армирования ростверка
    public class SetLintelBaseTypeV3 : IExternalEventHandler
    {
        private const string BaseTypeParameterName = "ZH_Тип_Основы_Стена";
        private const double WallSearchTolerance = 300.0 / 304.8;
        private const double VerticalTolerance = 100.0 / 304.8;

        public void Execute(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<FamilyInstance> selectedLintels = uidoc.Selection.GetElementIds()
                .Select(id => GetTopLevelFamilyInstance(doc.GetElement(id) as FamilyInstance))
                .Where(IsLintel)
                .GroupBy(x => x.Id)
                .Select(group => group.First())
                .ToList();

            List<FamilyInstance> lintels = selectedLintels.Any()
                ? selectedLintels
                : new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>()
                    .Where(IsLintel)
                    .ToList();

            if (!lintels.Any())
            {
                TaskDialog.Show("Тип основы", "Перемычки для обработки не найдены.");
                return;
            }

            int processedLintels = 0;
            int updatedParameters = 0;
            int wallsNotFound = 0;

            using (Transaction trans = new Transaction(doc, "Определение типа основы перемычек"))
            {
                trans.Start();

                try
                {
                    foreach (FamilyInstance lintel in lintels)
                    {
                        Wall wall = FindHostWall(doc, lintel);
                        if (wall == null)
                        {
                            wallsNotFound++;
                            continue;
                        }

                        string baseType = wall.WallType.Name.IndexOf("_НСЩ_", StringComparison.OrdinalIgnoreCase) >= 0
                                    ? "Каркас"
                                    : "Перегородка";
                        //21.08.26 - параметр основы стены у вложенных семейств + Марка КС
                        string constructionMark = GetParameterText(wall?.LookupParameter("ZH_Марка КС"));
                        string baseTypeValue = string.IsNullOrWhiteSpace(constructionMark)
                            ? baseType
                            : $"{baseType}_{constructionMark.Trim().Trim('_')}";


                        foreach (ElementId id in lintel.GetSubComponentIds())
                        {
                            Element subElement = doc.GetElement(id);
                            Parameter parameter = subElement?.LookupParameter(BaseTypeParameterName);

                            if (parameter != null
                                && !parameter.IsReadOnly
                                && parameter.StorageType == StorageType.String)
                            {
                                parameter.Set(baseTypeValue);
                                updatedParameters++;
                            }
                        }

                        processedLintels++;
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    TaskDialog.Show("Ошибка", ex.Message);
                    return;
                }
            }

            string scope = selectedLintels.Any() ? "выбранным перемычкам" : "всем перемычкам модели";
            string result = $"Тип основы назначен по {scope}.\n" +
                            $"Обработано перемычек: {processedLintels}.\n" +
                            $"Изменено параметров: {updatedParameters}.";

            if (wallsNotFound > 0)
                result += $"\nНе найдена стена для перемычек: {wallsNotFound}.";

            TaskDialog.Show("Тип основы", result);
        }
        private static string GetParameterText(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue)
                return null;

            return parameter.StorageType == StorageType.String
                ? parameter.AsString()
                : parameter.AsValueString();
        }

        private static FamilyInstance GetTopLevelFamilyInstance(FamilyInstance instance)
        {
            while (instance?.SuperComponent is FamilyInstance parent)
                instance = parent;

            return instance;
        }

        private static bool IsLintel(FamilyInstance instance)
        {
            if (instance == null
                || instance.SuperComponent != null
                || instance.Category == null
                || instance.Category.Id.Value != (long)BuiltInCategory.OST_StructuralFraming)
            {
                return false;
            }

            string grouping = instance.LookupParameter("ADSK_Группирование")?.AsString();
            string keyNote = instance.Symbol?.LookupParameter("Ключевая пометка")?.AsString();

            return string.Equals(grouping, "ПР", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(keyNote, "ПР", StringComparison.OrdinalIgnoreCase);
        }

        internal static Wall FindHostWall(
            Document doc,
            FamilyInstance lintel,
            string wallTypeName = null)
        {
            BoundingBoxXYZ lintelBox = lintel.get_BoundingBox(null);
            if (lintelBox == null)
                return null;

            // Сначала быстрым BoundingBox-фильтром оставляем только стены рядом с перемычкой.
            XYZ expansion = new XYZ(WallSearchTolerance, WallSearchTolerance, VerticalTolerance);
            Outline searchOutline = new Outline(lintelBox.Min - expansion, lintelBox.Max + expansion);
            List<Wall> candidateWalls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(searchOutline))
                .OfType<Wall>()
                .Where(wall => wall.WallType != null && wall.WallType.Kind != WallKind.Curtain)
                .Where(wall => string.IsNullOrWhiteSpace(wallTypeName)
                               || string.Equals(
                                   wall.WallType.Name,
                                   wallTypeName,
                                   StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!candidateWalls.Any())
                return null;

            // Затем проверяем фактическое пересечение Solid-геометрии перемычки и стен.
            Options options = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            List<Solid> lintelSolids = GetElementSolids(doc, lintel, options, true);
            Wall wallByGeometry = candidateWalls
                .Select(wall => new
                {
                    Wall = wall,
                    IntersectionVolume = GetIntersectionVolume(
                        lintelSolids,
                        GetElementSolids(doc, wall, options, false))
                })
                .Where(item => item.IntersectionVolume > 1e-9)
                .OrderByDescending(item => item.IntersectionVolume)
                .Select(item => item.Wall)
                .FirstOrDefault();

            if (wallByGeometry != null)
                return wallByGeometry;

            // Резерв для семейств, которые не отдают Solid или только касаются грани стены.
            XYZ lintelPoint = (lintel.Location as LocationPoint)?.Point;

            if (lintelPoint == null)
                lintelPoint = (lintelBox.Min + lintelBox.Max) / 2.0;

            XYZ lintelDirection = lintel.FacingOrientation;
            var candidates = new List<Tuple<Wall, bool, double>>();

            foreach (Wall wall in candidateWalls)
            {
                LocationCurve wallLocation = wall.Location as LocationCurve;
                Curve wallCurve = wallLocation?.Curve;
                BoundingBoxXYZ wallBox = wall.get_BoundingBox(null);

                if (wallCurve == null || wallBox == null)
                    continue;

                if (lintelBox != null
                    && (lintelBox.Max.Z < wallBox.Min.Z - VerticalTolerance
                        || lintelBox.Min.Z > wallBox.Max.Z + VerticalTolerance))
                {
                    continue;
                }

                XYZ pointAtWallElevation = new XYZ(
                    lintelPoint.X,
                    lintelPoint.Y,
                    wallCurve.GetEndPoint(0).Z);
                IntersectionResult projection = wallCurve.Project(pointAtWallElevation);

                if (projection == null)
                    continue;

                double distance = projection.XYZPoint.DistanceTo(pointAtWallElevation);
                if (distance > wall.Width / 2.0 + WallSearchTolerance)
                    continue;

                double directionMatch = Math.Abs(lintelDirection.DotProduct(wall.Orientation));
                candidates.Add(Tuple.Create(wall, directionMatch >= 0.8, distance));
            }

            return candidates
                .OrderByDescending(candidate => candidate.Item2)
                .ThenBy(candidate => candidate.Item3)
                .Select(candidate => candidate.Item1)
                .FirstOrDefault();
        }

        private static List<Solid> GetElementSolids(
            Document doc,
            Element element,
            Options options,
            bool includeSubComponents)
        {
            var solids = new List<Solid>();
            AddSolids(element?.get_Geometry(options), solids);

            if (includeSubComponents && element is FamilyInstance familyInstance)
            {
                foreach (ElementId subComponentId in familyInstance.GetSubComponentIds())
                {
                    Element subComponent = doc.GetElement(subComponentId);
                    solids.AddRange(GetElementSolids(doc, subComponent, options, true));
                }
            }

            return solids;
        }

        private static void AddSolids(GeometryElement geometry, ICollection<Solid> solids)
        {
            if (geometry == null)
                return;

            foreach (GeometryObject geometryObject in geometry)
            {
                if (geometryObject is Solid solid && solid.Volume > 1e-9)
                {
                    solids.Add(solid);
                }
                else if (geometryObject is GeometryInstance geometryInstance)
                {
                    AddSolids(geometryInstance.GetInstanceGeometry(), solids);
                }
            }
        }

        private static double GetIntersectionVolume(
            IEnumerable<Solid> firstSolids,
            IEnumerable<Solid> secondSolids)
        {
            double volume = 0.0;

            foreach (Solid first in firstSolids)
            {
                foreach (Solid second in secondSolids)
                {
                    try
                    {
                        Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                            first,
                            second,
                            BooleanOperationsType.Intersect);

                        if (intersection != null)
                            volume += intersection.Volume;
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        // У отдельных тел Revit не может выполнить boolean-операцию.
                    }
                }
            }

            return volume;
        }

        public string GetName()
        {
            return "Определение типа основы перемычек";
        }
    }

    public class CreateSectionsForLintelsV3 : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;

            // 20.04.26 - фильтр перемычек
            var framingElements = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(LintelServiceElementFilterV3.IsLintel)
                .Where(el => el.LookupParameter("ADSK_Позиция")?.AsString() != null)
                .ToList();

            // Группировка перемычек по параметру ADSK_Позиция
            var groupedElements = framingElements.OrderBy(el => el.LookupParameter("ADSK_Позиция").AsString()).GroupBy(el => el.LookupParameter("ADSK_Позиция").AsString() + (el.LookupParameter("ZH_Этаж_Числовой").AsDouble() > 0).ToString());
            // 03.02.26 - изменен фильтр для перемычек + создание базовых разрезов и шаблонов
            using (Transaction tr = new Transaction(doc, "Назначение линии видимости"))
            {
                tr.Start();
                // 17.04.26 - игнор отсутствия параметра Видимость.Глубина
                var output = "";
                foreach (var element in groupedElements)
                {
                    if (element.First().LookupParameter("Видимость.Глубина") == null)
                    {
                        output += "Для перемычек с 'ADSK_Позицией' = " + element.Key + " отсутствует параметр 'Видимость.Глубина'\nВозможно неправильное создание разреза";
                    }
                    else
                    {
                        var d = element.First().LookupParameter("Видимость.Глубина").AsDouble();
                        if (d < 2000 / 304.8)
                            element.First().LookupParameter("Видимость.Глубина").Set(2000 / 304.8);
                    }
                }
                // 23.04.26 - проверка разреза в перемычках
                if (output != "")
                    TaskDialog.Show("Внимание", output);
                tr.Commit();
            }

            using (Transaction trans = new Transaction(doc, "Создание разрезов для перемычек"))
            {
                trans.Start();

                try
                {
                    // Шаблон для разрезов
                    ViewFamilyType sectionViewType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault<ViewFamilyType>(x =>
                      ViewFamily.Section == x.ViewFamily && x.Name == "Номер вида");

                    if (sectionViewType == null)
                    {
                        // Берём любой существующий тип разреза как базу
                        var baseSectionType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(x => x.ViewFamily == ViewFamily.Section);

                        if (baseSectionType == null)
                        {
                            MessageBox.Show("Не найден ни один тип разреза.", "Ошибка");
                            trans.RollBack();
                            return;
                        }

                        // Дублируем и переименовываем
                        ElementType newTypeId = baseSectionType.Duplicate("Номер вида");
                        sectionViewType = doc.GetElement(newTypeId.Id) as ViewFamilyType;
                    }

                    ViewSection viewSection = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSection))
                    .OfType<ViewSection>()
                    .FirstOrDefault(vt => vt.Name == "4_К_Пр");

                    if (viewSection == null)
                    {
                        // Попробуем продублировать любой другой шаблон-разрез (самый безопасный способ)
                        var anySectionTemplate = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSection))
                            .Cast<ViewSection>()
                            .FirstOrDefault(v => v.IsTemplate);

                        if (anySectionTemplate != null)
                        {
                            ElementId dupId = ElementTransformUtils.CopyElement(doc, anySectionTemplate.Id, XYZ.Zero).FirstOrDefault();
                            viewSection = doc.GetElement(dupId) as ViewSection;
                            viewSection.Name = "4_К_Пр";
                        }
                        else
                        {
                            MessageBox.Show("Не найден ни один шаблон разреза.", "Ошибка");
                            trans.RollBack();
                            return;
                        }
                    }



                        // Создание разрезов для каждой уникальной группы
                        foreach (var group in groupedElements)
                    {
                        var firstElement = group.FirstOrDefault();
                        if (firstElement == null) continue;



                        // Определение размера разреза
                        LocationPoint locationPoint = firstElement.Location as LocationPoint;
                        double rotationAngle = locationPoint.Rotation;
                        XYZ direction;

                        if (Math.Abs(rotationAngle) < 1e-6 || Math.Abs(rotationAngle - Math.PI) < 1e-6)
                        {
                            direction = XYZ.BasisX; // Без поворота или 180 градусов
                        }
                        else if (Math.Abs(rotationAngle - Math.PI / 2) < 1e-6 || Math.Abs(rotationAngle - 3 * Math.PI / 2) < 1e-6)
                        {
                            direction = XYZ.BasisY; // 90 или 270 градусов
                        }
                        else
                        {
                            // Случай произвольного угла
                            direction = new XYZ(Math.Cos(rotationAngle), Math.Sin(rotationAngle), 0).Normalize();
                        }

                        // Определение направления "вверх" для разреза
                        XYZ upDirection = XYZ.BasisZ;
                        XYZ crossDirection = direction.CrossProduct(upDirection).Negate();

                        // Определение центра перемычки
                        XYZ center = (firstElement.get_BoundingBox(null).Max + (firstElement.get_BoundingBox(null).Min + XYZ.BasisZ * 2000 / 304.8)) / 2;

                        Transform t = Transform.Identity;
                        t.Origin = center;
                        t.BasisX = crossDirection;
                        t.BasisY = upDirection;
                        t.BasisZ = direction;

                        // Размеры разреза с учетом отступов в футах
                        double offsetX = 100 / 304.8; // 100 мм по X (влево и вправо)
                        double offsetZ = 200 / 304.8; // 200 мм по Z (вверх и вниз)

                        // Размеры элемента                       
                        double elementWidth = firstElement.get_BoundingBox(null).Max.X - firstElement.get_BoundingBox(null).Min.X;
                        double elementHeight = firstElement.get_BoundingBox(null).Max.Y - firstElement.get_BoundingBox(null).Min.Y;
                        double elementDepth = firstElement.get_BoundingBox(null).Max.Z - firstElement.get_BoundingBox(null).Min.Z - 1900 / 304.8;

                        // 17.04.26 - игнор отсутствия параметра Видимость.Глубина
                        if (elementDepth < 0)
                        {
                            elementDepth = firstElement.get_BoundingBox(null).Max.Z - firstElement.get_BoundingBox(null).Min.Z;
                        }

                        BoundingBoxXYZ boundingBox = new BoundingBoxXYZ();
                        boundingBox.Transform = t;

                        // Настройка границ BoundingBox с учетом отступов
                        if (Math.Abs(rotationAngle) < 1e-6 || Math.Abs(rotationAngle - Math.PI) < 1e-6)
                        {
                            boundingBox.Min = new XYZ(-elementHeight / 2 - offsetX, -elementDepth / 2 - offsetZ, 0); // Отступы по краям
                            boundingBox.Max = new XYZ(elementHeight / 2 + offsetX, elementDepth / 2 + offsetZ, offsetZ);   // Отступы по краям
                        }
                        else if (Math.Abs(rotationAngle - Math.PI / 2) < 1e-6 || Math.Abs(rotationAngle - 3 * Math.PI / 2) < 1e-6)
                        {
                            boundingBox.Min = new XYZ(-elementWidth / 2 - offsetX, -elementDepth / 2 - offsetZ, 0); // Отступы по краям
                            boundingBox.Max = new XYZ(elementWidth / 2 + offsetX, elementDepth / 2 + offsetZ, offsetZ);   // Отступы по краям
                        }

                        // Создание разреза
                        ViewSection section = ViewSection.CreateSection(doc, sectionViewType.Id, boundingBox);
                        if (section == null)
                            continue;

                        // Установка имени разреза
                        string positionName = firstElement.LookupParameter("ADSK_Позиция").AsString();
                        //18.02.26 - изменения в перемычках
                        bool lower0 = firstElement.LookupParameter("ZH_Этаж_Числовой").AsDouble() < 0;
                        // 09.06.26 - исправление имен разрезов
                        string elevationSuffix = lower0 ? " ниже 0.000" : " выше 0.000";
                        string sectionName = positionName + elevationSuffix;

                        // 23.04.26 - проверка разреза в перемычках
                        // 09.06.26 - исправление имен разрезов
                        var view = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Views)
                            .Where(x => x.Id != section.Id && x.Name.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
                            .FirstOrDefault();
                        bool sectionNameReleased = false;
                        if (view != null)
                        {
                            // 29.01.26 - уникальное имя для разреза
                            var framingElements_ = new FilteredElementCollector(doc, view.Id)
                            .OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .WhereElementIsNotElementType()
                            .Cast<FamilyInstance>()
                            .Where(LintelServiceElementFilterV3.IsLintel)
                            .Where(el => el.LookupParameter("ADSK_Позиция")?.AsString() != null)
                            .ToList().FirstOrDefault();
                            if (framingElements_ != null)
                            {
                                string positionName_ = framingElements_.LookupParameter("ADSK_Позиция").AsString();
                                bool lower0_ = framingElements_.LookupParameter("ZH_Этаж_Числовой").AsInteger() < 0;
                                // 23.04.26 - проверка разреза в перемычках
                                if (view.LookupParameter("ADSK_Назначение вида").AsString() == "Перемычки")
                                {
                                    if (positionName == positionName_)
                                    {
                                        doc.Delete(section.Id);
                                        continue;
                                    }
                                    else
                                    {
                                        if (lower0_)
                                            view.Name = MakeUniqueViewName(doc, positionName_ + " ниже 0.000_");
                                        else
                                            view.Name = MakeUniqueViewName(doc, positionName_ + " выше 0.000_");
                                        sectionNameReleased = true;
                                    }
                                }
                                else
                                {
                                    // 23.04.26 - проверка разреза в перемычках
                                    view.Name = MakeUniqueViewName(doc, "!" + view.Name);
                                    sectionNameReleased = true;
                                }
                            }
                            else
                            {
                                doc.Delete(view.Id);
                                sectionNameReleased = true;
                            }
                        }

                        // 09.06.26 - исправление имен разрезов
                        if (sectionNameReleased)
                            doc.Regenerate();

                        section.Name = sectionName;



                        section.LookupParameter("Шаблон вида").Set(viewSection.Id);
                        section.LookupParameter("Масштаб вида").Set(20);
                    }
                    var views = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Views).Where(x => x.Name.Contains("Пр") && x.Name.Contains("0.000_")).ToList();
                    foreach (var view in views)
                    {
                        view.Name = MakeUniqueViewName(doc, view.Name.Replace("_", ""));
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка");
                    trans.RollBack();
                }
            }
        }
        // 29.01.26 - уникальное имя для разреза
        private static string MakeUniqueViewName(Document doc, string desiredName)
        {
            // Собираем все имена видов (без шаблонов)
            var names = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!names.Contains(desiredName))
                return desiredName;

            int i = 1;
            while (true)
            {
                string candidate = $"{desiredName} ({i})";
                if (!names.Contains(candidate))
                    return candidate;
                i++;
            }
        }


        public string GetName()
        {
            return "Создание разрезов для уникальных перемычек";
        }
    }

    public class TagLintelsV3 : IExternalEventHandler
    {
        //16.02.26 - отметка для марок перемычек
        private static string FormatProjectElevationMeters(double meters)
        {
            return meters.ToString("+0.000;-0.000",new CultureInfo("ru-RU"));
        }
        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            UIDocument uidoc = app.ActiveUIDocument;
            if (doc.ActiveView.ViewType != ViewType.FloorPlan)
            {
                MessageBox.Show("Перейдите на план этажа для создания марок", "Ошибка");
                return;
            }

            using (Transaction trans = new Transaction(doc, "Маркировка перемычек"))
            {
                trans.Start();

                try
                {
                    // Сбор всех перемычек
                    //16.02.26 - отметка для марок перемычек
                    var lintelInstances = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>()
                        .Where(LintelServiceElementFilterV3.IsLintel)
                        .Where(el => el.LookupParameter("ADSK_Позиция")?.AsString() != null)
                        .Where(x => x.LevelId == doc.ActiveView.GenLevel.Id)
                        .ToList();

                    if (lintelInstances.Count == 0)
                    {
                        MessageBox.Show("Не найдено ни одной перемычки для маркировки.", "Ошибка");
                        trans.RollBack();
                        return;
                    }

                    // Поиск типа марки
                    //18.02.26 - изменения в перемычках
                    var tagType = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfType<FamilySymbol>().FirstOrDefault(tag => tag.FamilyName == "ADSK_Марка_Балка" && tag.Name == "ZH_Перемычка");


                    if (tagType == null)
                    {
                        MessageBox.Show("Не найден тип марки 'Экземпляр_ADSK_Позиция' для семейства 'ADSK_Марка_Балка'.", "Ошибка");
                        trans.RollBack();
                        return;
                    }

                    // Активируем тип марки, если не активирован
                    if (!tagType.IsActive)
                    {
                        tagType.Activate();
                        doc.Regenerate();
                    }

                    // Маркировка всех перемычек
                    foreach (var lintel in lintelInstances)
                    {
                        //12.02.26 - марки под углом + нумерация + разрезы
                        FamilyInstance fi = (FamilyInstance)lintel;
                        View view = doc.ActiveView;
                        //16.02.26 - отметка для марок перемычек
                        fi.LookupParameter("ZH_Отм_низ конструкции").Set(FormatProjectElevationMeters(Math.Round((doc.GetElement(fi.LevelId) as Level).Elevation * 0.3048 + fi.LookupParameter("Смещение от главной модели").AsDouble() * 0.3048, 6)));

                        XYZ vd = view.ViewDirection.Normalize();
                        XYZ up = view.UpDirection.Normalize();
                        XYZ right = view.RightDirection.Normalize();

                        XYZ hand = fi.HandOrientation;
                        XYZ handProj = hand - vd.Multiply(hand.DotProduct(vd));
                        if (handProj.GetLength() < 1e-9) handProj = right;
                        handProj = handProj.Normalize();

                        double rot = Math.Atan2(handProj.DotProduct(up), handProj.DotProduct(right)); // (-pi..pi)

                        if (rot < 0) rot += 2.0 * Math.PI;

                        XYZ facing = fi.FacingOrientation;
                        const double tol = 1e-6;
                        if (Math.Abs(Math.Abs(facing.Y) - 1.0) < tol) rot = 0.0;
                        else if (Math.Abs(Math.Abs(facing.X) - 1.0) < tol) rot = Math.PI / 2.0;

                        XYZ facingProj = facing - vd.Multiply(facing.DotProduct(vd));
                        if (facingProj.GetLength() < 1e-9) facingProj = up;
                        facingProj = facingProj.Normalize();

                        if (facingProj.DotProduct(up) < 0) facingProj = facingProj.Negate();

                        // точка вставки
                        XYZ insertPt = (fi.Location as LocationPoint)?.Point ?? XYZ.Zero;

                        // bbox
                        BoundingBoxXYZ bb = lintel.get_BoundingBox(view) ?? lintel.get_BoundingBox(null);

                        // одинаковый размер вдоль направления: 8 углов bbox
                        XYZ[] corners = new XYZ[]
                        {
    new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
    new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
    new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
    new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
    new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
    new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
    new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
    new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z),
                        };

                        double minP = double.PositiveInfinity, maxP = double.NegativeInfinity;
                        foreach (var c in corners)
                        {
                            double p = c.DotProduct(facingProj);
                            if (p < minP) minP = p;
                            if (p > maxP) maxP = p;
                        }
                        double halfAlong = 0.5 * (maxP - minP);

                        // Смещение
                        double gap = 250.0 / 304.8;

                        XYZ tagPoint = insertPt + facingProj.Multiply(halfAlong + gap);

                        IndependentTag newTag = IndependentTag.Create(
                            doc,
                            tagType.Id,
                            view.Id,
                            new Reference(lintel),
                            false,
                            TagOrientation.AnyModelDirection,
                            tagPoint
                        );

                        newTag.RotationAngle = rot;



                        if (newTag == null)
                        {
                            //MessageBox.Show("Не удалось создать марку для перемычки.", "Ошибка");
                            continue;
                        }
                    }
                    //18.02.26 - изменения в перемычках
                    doc.Regenerate();

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка");
                    trans.RollBack();
                }
            }
        }

        public string GetName()
        {
            return "Маркировка перемычек";
        }
    }
}
