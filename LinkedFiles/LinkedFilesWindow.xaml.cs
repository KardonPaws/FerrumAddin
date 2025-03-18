using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using MessageBox = System.Windows.MessageBox;
using View = Autodesk.Revit.DB.View;
using Window = System.Windows.Window;

namespace FerrumAddin.LinkedFiles
{
    public partial class LinkedFilesWindow : Window
    {
        private Document doc;
        private UIDocument _uiDocument;

        public LinkedFilesWindow(UIDocument uiDocument)
        {
            InitializeComponent();
            _uiDocument = uiDocument;
            doc = uiDocument.Document;

            // Заполнение списка связей
            LoadLinkedFiles();
        }

        private void LoadLinkedFiles()
        {
            var linkedFiles = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .Select(link => new LinkedFileModel
                {
                    Name = link.Name,
                    Id = link.Id,
                    IsSelected = false
                })
                .ToList();

            LinkedFilesList.ItemsSource = linkedFiles;
        }

        private void DisableVisibility_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Получаем выбранные связанные модели
                var selectedLinkedFiles = LinkedFilesList.ItemsSource
                    .Cast<LinkedFileModel>()
                    .Where(x => x.IsSelected)
                    .Select(x => x.Id)
                    .ToList();

                if (selectedLinkedFiles.Count == 0)
                {
                    MessageBox.Show("Не выбрано ни одной связанной модели.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Начинаем транзакцию
                using (Transaction trans = new Transaction(doc, "Отключение видимости связей"))
                {
                    trans.Start();

                    // Получаем все виды и шаблоны в проекте
                    var views = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => !v.IsTemplate) // Исключаем шаблоны
                        .ToList();

                    var viewTemplates = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => v.IsTemplate) // Только шаблоны
                        .ToList();

                    // Отключаем видимость выбранных связанных моделей в видах
                    foreach (View view in views)
                    {
                        foreach (ElementId linkedFileId in selectedLinkedFiles)
                        {
                            if (linkedFileId != null)
                            {
                                if (doc.GetElement(linkedFileId).CanBeHidden(view))
                                {
                                    view.HideElements(new List<ElementId> { linkedFileId });
                                }
                                
                            }
                        }
                    }

                    // Отключаем видимость выбранных связанных моделей в шаблонах
                    foreach (View viewTemplate in viewTemplates)
                    {
                        foreach (ElementId linkedFileId in selectedLinkedFiles)
                        {
                            if (linkedFileId != null)
                            {
                                if (doc.GetElement(linkedFileId).CanBeHidden(viewTemplate))
                                {
                                    viewTemplate.HideElements(new List<ElementId> { linkedFileId });
                                }
                            }
                        }
                    }

                    trans.Commit();
                }

                MessageBox.Show("Видимость выбранных связанных моделей отключена во всех видах и шаблонах.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MoveToWorksets_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Получаем выбранные связанные модели
                var selectedLinkedFiles = LinkedFilesList.ItemsSource
                    .Cast<LinkedFileModel>()
                    .Where(x => x.IsSelected)
                    .Select(x => x.Id)
                    .ToList();

                if (selectedLinkedFiles.Count == 0)
                {
                    MessageBox.Show("Не выбрано ни одной связанной модели.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Начинаем транзакцию
                using (Transaction trans = new Transaction(doc, "Перенос связей в рабочие наборы"))
                {
                    trans.Start();

                    // Получаем все рабочие наборы в проекте
                    IList<Workset> worksets = new FilteredWorksetCollector(doc)
                        .Cast<Workset>()
                        .ToList();

                    // Для каждой выбранной связи
                    foreach (ElementId linkedFileId in selectedLinkedFiles)
                    {
                        // Получаем связанную модель                        
                        RevitLinkInstance linkInstance = new FilteredElementCollector(doc)
                                .OfClass(typeof(RevitLinkInstance)).Where(x=>x.GetTypeId() == linkedFileId)
                                .Cast<RevitLinkInstance>().FirstOrDefault();
                        if (linkInstance == null)
                            continue;

                        // Получаем название связанной модели
                        string linkName = linkInstance.Name;

                        // Определяем раздел по названию связи (ОВ, ВК, АР, КР и т.д.)
                        string section = DetermineSectionFromLinkName(linkName);

                        if (string.IsNullOrEmpty(section))
                        {
                            MessageBox.Show($"Не удалось определить раздел для связи: {linkName}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                            continue;
                        }

                        // Название рабочего набора
                        string worksetName = createWorkset.IsChecked == true ? $"#Связи_{section}" : section;

                        // Ищем или создаем рабочий набор
                        Workset targetWorkset = worksets.FirstOrDefault(w => w.Name.Equals(worksetName, StringComparison.OrdinalIgnoreCase));

                        if (targetWorkset == null && createWorkset.IsChecked == true)
                        {
                            // Создаем новый рабочий набор
                            targetWorkset = Workset.Create(doc, worksetName);
                            worksets.Add(targetWorkset); // Добавляем новый рабочий набор в список
                        }

                        if (targetWorkset == null)
                        {
                            MessageBox.Show($"Рабочий набор '{worksetName}' не найден.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                            continue;
                        }

                        // Переносим связанную модель в рабочий набор
                        Parameter worksetParam = linkInstance.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (worksetParam != null && worksetParam.IsReadOnly == false)
                        {
                            worksetParam.Set(targetWorkset.Id.IntegerValue);
                        }
                    }

                    trans.Commit();
                }

                MessageBox.Show("Выбранные связи успешно перенесены в рабочие наборы.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для определения раздела по названию связи
        private string DetermineSectionFromLinkName(string linkName)
        {
            // Пример логики определения раздела
            if (linkName.Contains("ОВ"))
                return "ОВ";
            else if (linkName.Contains("ВК"))
                return "ВК";
            else if (linkName.Contains("АР"))
                return "АР";
            else if (linkName.Contains("КР"))
                return "КР";
            else if (linkName.Contains("ЭОМ"))
                return "ЭОМ";
            else if (linkName.Contains("СС"))
                return "СС";
            else if (linkName.Contains("ТХ"))
                return "ТХ";
            else if (linkName.Contains("ПТ"))
                return "ПТ";
            else
                return null; // Если раздел не определен
        }

        private void SelectFromFileServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Открываем диалог выбора файла
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Revit Files (*.rvt)|*.rvt"; // Фильтр для Revit-файлов
                openFileDialog.Title = "Выберите файл с файлового сервера";

                if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;

                    // Загружаем выбранный файл и получаем рабочие наборы
                    var worksets = GetWorksetsFromFile(filePath);

                    if (worksets != null && worksets.Any())
                    {
                        // Заполняем ListView рабочими наборами
                        WorksetsList.ItemsSource = worksets;
                    }
                    else
                    {
                        MessageBox.Show("В выбранном файле нет рабочих наборов.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для получения рабочих наборов из файла через GetUserWorksetInfo
        private List<WorksetModel> GetWorksetsFromFile(string filePath)
        {
            List<WorksetModel> worksets = new List<WorksetModel>();

            try
            {
                // Создаем ModelPath из пути к файлу
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);

                // Получаем WorksetConfiguration через GetUserWorksetInfo
                List<WorksetPreview> worksetConfig = (List<WorksetPreview>)WorksharingUtils.GetUserWorksetInfo(modelPath);

                // Получаем все рабочие наборы из WorksetConfiguration
                foreach (WorksetPreview worksetId in worksetConfig)
                {
                    // Получаем имя рабочего набора
                    string worksetName = worksetId.Name;

                    worksets.Add(new WorksetModel
                    {
                        Name = worksetName,
                        Id = worksetId,
                        IsSelected = false,
                        ModelPath = modelPath
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при чтении файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return worksets;
        }

        // Модель для отображения рабочих наборов в ListView
        public class WorksetModel
        {
            public string Name { get; set; }
            public WorksetPreview Id { get; set; }
            public bool IsSelected { get; set; }
            public ModelPath ModelPath { get; set; }
        }

        private void SelectFromRevitServer_Click(object sender, RoutedEventArgs e)
        {
           
        }

        private void LoadLinkedFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Получаем выбранные рабочие наборы
                var selectedWorksets = WorksetsList.ItemsSource
                    .Cast<WorksetModel>()
                    .Where(x => x.IsSelected)
                    .Select(x => x.Id.Id)
                    .ToList();

                if (selectedWorksets.Count == 0)
                {
                    MessageBox.Show("Не выбрано ни одного рабочего набора.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Начинаем транзакцию
                using (Transaction trans = new Transaction(doc, "Загрузка связи с рабочими наборами"))
                {
                    trans.Start();

                    // Создаем конфигурацию рабочих наборов
                    WorksetConfiguration worksetConfig = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);
                    RevitLinkOptions options = new RevitLinkOptions(false, worksetConfig);
                    // Загружаем связь с указанной конфигурацией рабочих наборов
                    var linkInstance = RevitLinkType.Create(doc, WorksetsList.ItemsSource.Cast<WorksetModel>().FirstOrDefault().ModelPath, options);
                    RevitLinkType linkType = (doc.GetElement(linkInstance.ElementId) as RevitLinkType);
                    
                    trans.Commit();

                    foreach (Document _linkdoc in doc.Application.Documents)
                    {
                        if (_linkdoc.Title == linkType.Name.Remove(linkType.Name.Length-4, 4))
                        {
                            WorksetConfiguration wk = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);
                            ModelPath _modelpath = _linkdoc.GetWorksharingCentralModelPath();
                            WorksetTable _worksetTable = _linkdoc.GetWorksetTable();

                            wk.Open(selectedWorksets);
                            
                            linkType.LoadFrom(_modelpath, wk);
                        }
                    }

                    trans.Start("Создание связи");
                    RevitLinkInstance.Create(doc, linkType.Id);
                    trans.Commit();

                    if (linkInstance != null)
                    {
                        MessageBox.Show("Связь успешно загружена с выбранными рабочими наборами.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Не удалось загрузить связь.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Получаем все элементы в LinkedFilesList
                var linkedFiles = LinkedFilesList.ItemsSource.Cast<LinkedFileModel>().ToList();

                // Устанавливаем флаг IsSelected для всех элементов
                foreach (var linkedFile in linkedFiles)
                {
                    linkedFile.IsSelected = true;
                }

                // Обновляем отображение ListView
                LinkedFilesList.Items.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DisableAnnotationVisibility_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Получаем выбранные связанные файлы
                var selectedLinkedFiles = LinkedFilesList.ItemsSource
                    .Cast<LinkedFileModel>()
                    .Where(x => x.IsSelected)
                    .ToList();
                List<Element> lstRvtLinkType = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).ToList();

                if (selectedLinkedFiles.Count == 0)
                {
                    MessageBox.Show("Не выбрано ни одной связанной модели.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                foreach (Document _linkdoc in doc.Application.Documents)
                {
                    foreach (LinkedFileModel model in selectedLinkedFiles)
                    {
                        if (model.Name.Remove(model.Name.Length - 4, 4) == _linkdoc.Title)
                        {
                            WorksetConfiguration wk = new WorksetConfiguration(WorksetConfigurationOption.OpenLastViewed);
                            ModelPath _modelpath = _linkdoc.GetWorksharingCentralModelPath();
                            IList<WorksetPreview> lstPreview = WorksharingUtils.GetUserWorksetInfo(_modelpath);
                            WorksetTable _worksetTable = _linkdoc.GetWorksetTable();
                            List<WorksetId> lstWkSet_Close = new List<WorksetId>();

                            foreach (WorksetPreview item in lstPreview)
                            {
                                Workset wkset = _worksetTable.GetWorkset(item.Id);
                                if (!wkset.IsOpen)
                                {
                                    lstWkSet_Close.Add(item.Id);
                                }
                                else
                                {
                                    if ((item.Name.CompareTo("Общие уровни и сетки") == 0) || (item.Name.CompareTo("Аннотации") == 0))
                                    {
                                        lstWkSet_Close.Add(item.Id);
                                        continue;
                                    }
                                }
                            }
                            wk.Close(lstWkSet_Close);
                            (doc.GetElement(model.Id) as RevitLinkType).LoadFrom(_modelpath, wk);
                        }
                    }
                }

                MessageBox.Show("Аннотации в выбранных связанных файлах отключены.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class LinkedFileModel
    {
        public string Name { get; set; }
        public ElementId Id { get; set; }
        public bool IsSelected { get; set; }
    }

    public class WorksetModel
    {
        public string Name { get; set; }
        public WorksetId Id { get; set; }
        public bool IsSelected { get; set; }
    }
}