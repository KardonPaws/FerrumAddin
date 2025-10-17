#region Namespaces
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AW = Autodesk.Windows;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Collections.Generic;
using RibbonPanel = Autodesk.Revit.UI.RibbonPanel;
using RibbonItem = Autodesk.Revit.UI.RibbonItem;
using ComboBox = Autodesk.Revit.UI.ComboBox;
using Autodesk.Revit.UI.Events;
using System.Threading.Tasks;
using System.Windows.Controls;
using Autodesk.Windows;
using System.Xml.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using Autodesk.Revit.ApplicationServices;
using Transform = Autodesk.Revit.DB.Transform;
using System.Runtime.InteropServices;
using Autodesk.Revit.DB.Events;
using FerrumAddinDev.FM;
using System.Windows;
#endregion

namespace FerrumAddinDev
{
    public class App : IExternalApplication
    {
        public AW.RibbonItem GetButton(string tabName, string panelName, string itemName)
        {
            AW.RibbonControl ribbon = AW.ComponentManager.Ribbon;
            foreach (AW.RibbonTab tab in ribbon.Tabs)
            {
                if (tab.Name == tabName)
                {
                    foreach (AW.RibbonPanel panel in tab.Panels)
                    {
                        if (panel.Source.Title == panelName)
                        {
                            return panel.FindItem("CustomCtrl_%CustomCtrl_%"
                              + tabName + "%" + panelName + "%" + itemName,
                              true) as AW.RibbonItem;
                        }
                    }
                }
            }
            return null;
        }


        public static BitmapImage Convert(System.Drawing.Image img)
        {
            using (var memory = new MemoryStream())
            {
                img.Save(memory, ImageFormat.Png);
                memory.Position = 0;
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                return bitmapImage;

            }
        }
        public static string downloadDir;
        private static readonly string[] fileUrls =
        {
        "https://raw.githubusercontent.com/KardonPaws/FerrumAddin/master/DLL/FerrumAddinDev.dll"
    };
        private static async Task CheckForUpdates()
        {

            foreach (var url in fileUrls)
            {
                var fileName = Path.GetFileName(url);
                var localPath = Path.Combine(downloadDir, fileName);

                string oldHash = null;
                if (File.Exists(localPath))
                {
                    oldHash = GetFileHash(localPath);
                }

                var tempPath = Path.Combine(downloadDir, "new" + fileName);
                await DownloadFile(url, tempPath);
                var newHash = GetFileHash(tempPath);

                if (oldHash != newHash)
                {
                    
                    Update update = new Update();
                    update.ShowDialog();
                    Console.WriteLine($"{fileName} был обновлен.");
                }
                else
                {
                    File.Delete(tempPath);
                    Console.WriteLine($"{fileName} не изменился.");
                }
            }
        }

        private static async Task DownloadFile(string url, string localPath)
        {
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsByteArrayAsync();
                File.WriteAllBytes(localPath, content);
            }
        }

        private static string GetFileHash(string filePath)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }
        public static UIControlledApplication application;
        public static UIApplication uiapp;
        public static string name;
        public RibbonPanel panelMEP;
        public RibbonPanel panelKR;
        public RibbonPanel panelControl;

        public Result OnStartup(UIControlledApplication a)
        {
            application = a;
            Type type = a.GetType();

            string propertyName = "m_uiapplication";
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
              | BindingFlags.GetField | BindingFlags.Instance;
            Binder binder = null;
            object[] args = null;

            object result = type.InvokeMember(
                propertyName, flags, binder, a, args);

            uiapp = (UIApplication)result;

            name = uiapp.Application.Username;
            List<string> admins = new List<string>();
            string filePath = "P:\\10_Документы\\Bim\\Библиотека ресурсов\\Revit\\Плагины\\Железно\\Admin.txt";
            if (File.Exists(filePath))
            {
                using (StreamReader reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        admins.Add(line);
                    }
                }
                
            }
            else
            {
                AlwaysLoad = true;
            }
            AllowLoad = false;
            downloadDir = a.ControlledApplication.CurrentUserAddinsLocation;
            CheckForUpdates();
            
            xmlFilePath = a.ControlledApplication.CurrentUserAddinsLocation + "\\Settings.xml";
            XElement root;
            if (System.IO.File.Exists(xmlFilePath))
            {
                root = XElement.Load(xmlFilePath);
            }
            else
            {
                root = new XElement("Settings");
            }

            // 17.10.25 - полное откоючение фм
            XElement frmMangerElement = root.Element("frmManager");
            if (frmMangerElement == null)
            {
                frmMangerElement = new XElement("frmManager");
                frmMangerElement.SetAttributeValue("IsChecked", false);
                root.Add(frmMangerElement);
            }
            // 23.95.25 - Новый функционал BigPicture для FM
            XElement BP = root.Element("BigPicture");
            if (BP == null)
            {
                BP = new XElement("BigPicture");
                BP.SetAttributeValue("IsChecked", false);
                BigPicture = false;
                root.Add(BP);
            }
            BigPicture = BP.Attribute("IsChecked").Value == "false" ? false : true;
            
            XElement frmTabPath = root.Element("TabPath");
            if (frmTabPath == null)
            {
                frmTabPath = new XElement("TabPath");
                frmTabPath.SetAttributeValue("Path", a.ControlledApplication.CurrentUserAddinsLocation + "\\TabItems.xml");
                root.Add(frmTabPath);
            }
            TabPath = frmTabPath.Attribute("Path").Value;
            root.Save(xmlFilePath);

            string tabName = "Железно-разработка";

            a.CreateRibbonTab(tabName);
            RibbonPanel panelFerrum = a.CreateRibbonPanel(tabName, "Железно");
            PushButtonData conf = new PushButtonData("frmConfig", "Настройки", Assembly.GetExecutingAssembly().Location, "FerrumAddinDev.ConfiguratorShow");
            conf.Image = Convert(Properties.Resources.ferrum);
            conf.LargeImage = Convert(Properties.Resources.ferrum);
            ComboBoxData comboBoxData = new ComboBoxData("ChangeRazd");
            List<RibbonItem> items = panelFerrum.AddStackedItems(conf, comboBoxData).ToList();
            AW.RibbonItem ri = GetButton(tabName, "Железно", comboBoxData.Name);
            ri.Width = 110;
            ComboBox cb = (items[1] as ComboBox);
            cb.AddItems(new List<ComboBoxMemberData>() { new ComboBoxMemberData("Common", "Общие"),
                                                                             new ComboBoxMemberData("Views", "Виды"),
                                                                             new ComboBoxMemberData("KR", "КР"),
                                                                             new ComboBoxMemberData("MEP", "MEP"),
                                                                             new ComboBoxMemberData("Control", "Управление")});

            cb.CurrentChanged += Cb_CurrentChanged;


            PushButtonData FamilyManager = new PushButtonData("frmManager", "Менеджер\nсемейств", Assembly.GetExecutingAssembly().Location, "FerrumAddinDev.FamilyManagerShow");
            FamilyManager.Image = Convert(Properties.Resources.FamilyManager);
            FamilyManager.LargeImage = Convert(Properties.Resources.FamilyManager);

            panelFerrum.AddItem(FamilyManager);

            PushButtonData Comparison = new PushButtonData("frmComparison", "Сопоставление\nсемейств", Assembly.GetExecutingAssembly().Location, "FerrumAddinDev.FM.ComparisonWindowShow");
            Comparison.Image = Convert(Properties.Resources.FamilyManager);
            Comparison.LargeImage = Convert(Properties.Resources.FamilyManager);          

            panelFerrum.AddItem(Comparison);


            panelMEP = a.CreateRibbonPanel(tabName, "ВИС");
            panelMEP.Visible = false;

            PushButtonData MEPName = new PushButtonData("mepName", "Наименование труб|воздуховодов", Assembly.GetExecutingAssembly().Location, "FerrumAddinDev.CommandMepName");

            panelMEP.AddItem(MEPName);


            panelKR = a.CreateRibbonPanel(tabName, "КР");
            panelKR.Visible = false;

            PushButtonData LintelCreator = new PushButtonData("LintelCreator", "Создание перемычек", Assembly.GetExecutingAssembly().Location, "FerrumAddinDev.LintelCreator_v2.CommandLintelCreator_v2");
            panelKR.AddItem(LintelCreator);

            PushButtonData GrillageCreator = new PushButtonData("GrillageCreator", "Армирование ростверка", Assembly.GetExecutingAssembly().Location, "FerrumAddinDev.GrillageCreator_v2.CommandGrillageCreator_v2");
            panelKR.AddItem(GrillageCreator);

            PushButtonData FBSCreator = new PushButtonData("FBSCreator", "Раскладка ФБС", Assembly.GetExecutingAssembly().Location, "FerrumAddinDev.FBS.FBSLayoutCommand");
            panelKR.AddItem(FBSCreator);

            PushButtonData ColumnSections = new PushButtonData("Column Sections", "Сечения по пилонам", Assembly.GetExecutingAssembly().Location, "FerrumAddinDev.ColumnSections.CreateColumnSections");
            panelKR.AddItem(ColumnSections);

            PushButtonData PilonsDimensions = new PushButtonData("PilonsDimensions", "Размеры по пилонам", 
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "zhConstructionPilonsDimensions24.dll"),
                "zhConstructionPilonsDimensions24.PilonsDimensions");
            try
            {
                panelKR.AddItem(PilonsDimensions);
            }
            catch
            {

            }

            PushButtonData PilessFromDWG = new PushButtonData("PilessFromDWG", "Пилоны из подложки",
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "ZhConstructionPilesFromDWG24.dll"),
                "ZhConstructionPilesFromDWG24.PilesFromDWG");
            try
            {
                panelKR.AddItem(PilessFromDWG);
            }
            catch
            {

            }

            panelControl = a.CreateRibbonPanel(tabName, "Управление");
            panelControl.Visible = false;

            PushButtonData LinkFiles = new PushButtonData("LinkedFiles", "Управление связями", Assembly.GetExecutingAssembly().Location, "FerrumAddinDev.LinkedFilesCommand");
            panelControl.AddItem(LinkFiles);

            var CommandWorksets = new PushButtonData("Распределение элементов\nпо рабочим наборам", "Распределение элементов\nпо рабочим наборам", Assembly.GetExecutingAssembly().Location, "FerrumAddinDev.CommandWorksets");
            var ComandWorksets = panelControl.AddItem(CommandWorksets) as PushButton;
            ComandWorksets.Enabled = true;
            ContextualHelp helpComand = new ContextualHelp(ContextualHelpType.Url, "https://docs.google.com/document/d/1XSpM4HcRagr0BNgYW2WUo8hMjI7oEH93g94kwdJLwC0/edit?usp=sharing");
            ComandWorksets.SetContextualHelp(helpComand);
            ComandWorksets.ToolTip = "В соответсвии с выбранным xml файлом разносит элементы по рабочим наборам, в случае, если рабочего набора не существует - создает новый рабочий набор.";

            var CommandStats = new PushButtonData("Статистика", "Статистика", Assembly.GetExecutingAssembly().Location, "FerrumAddinDev.StatsShow");
            var ComandStats = panelControl.AddItem(CommandStats) as PushButton;
            ComandStats.Enabled = true;

            FamilyManagerWindow dock = new FamilyManagerWindow();
            dockableWindow = dock;

            DockablePaneId id = new DockablePaneId(new Guid("{3496B5BA-F8C4-403D-AF7E-B95D25F15CED}"));
            // 17.10.25 - полное откоючение фм
            bool manager = (bool)(GetElementStates(root).Where(x => x.Key.Equals("frmManager"))?.First().Value);
            if (manager)
            {
                try
                {
                    a.RegisterDockablePane(id, "Менеджер семейств Железно_Тест",
                            dockableWindow as IDockablePaneProvider);
                    if ((admins.Count != 0 && admins.Contains(name)) || AlwaysLoad == true)
                    {
                    }
                    else
                    {
                        a.ControlledApplication.FamilyLoadingIntoDocument += ControlledApplication_FamilyLoadingIntoDocument;
                        a.ControlledApplication.ElementTypeDuplicating += ControlledApplication_ElementTypeDuplicating;
                    }

                    a.ControlledApplication.DocumentOpening += ControlledApplication_DocumentOpening;
                    a.ControlledApplication.DocumentOpened += ControlledApplication_DocumentOpened;
                    a.ControlledApplication.DocumentClosing += ControlledApplication_DocumentClosing;

                    a.ControlledApplication.DocumentSynchronizingWithCentral += ControlledApplication_DocumentSynchronizingWithCentral;
                    a.ControlledApplication.DocumentSynchronizedWithCentral += ControlledApplication_DocumentSynchronizedWithCentral;

                    a.ControlledApplication.DocumentChanged += ControlledApplication_DocumentChanged;

                    a.ViewActivated += A_ViewActivated;

                    LoadEvent = ExternalEvent.Create(new LoadEvent());
                }
                catch (Exception ex)
                {

                }
            }

            ButtonConf(root);
            CleanOldLogFiles();

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication a)
        {
            try
            {
                a.ControlledApplication.FamilyLoadingIntoDocument -= ControlledApplication_FamilyLoadingIntoDocument;
                a.ControlledApplication.ElementTypeDuplicating -= ControlledApplication_ElementTypeDuplicating;
            }
            catch
            {

            }

            a.ControlledApplication.DocumentOpening -= ControlledApplication_DocumentOpening;
            a.ControlledApplication.DocumentOpened -= ControlledApplication_DocumentOpened;
            a.ControlledApplication.DocumentClosing -= ControlledApplication_DocumentClosing;

            a.ControlledApplication.DocumentSynchronizingWithCentral -= ControlledApplication_DocumentSynchronizingWithCentral;
            a.ControlledApplication.DocumentSynchronizedWithCentral -= ControlledApplication_DocumentSynchronizedWithCentral;

            a.ControlledApplication.DocumentChanged -= ControlledApplication_DocumentChanged;

            a.ViewActivated -= A_ViewActivated;

            Process process = Process.GetCurrentProcess();
            var updaterProcess = Process.Start(new ProcessStartInfo(downloadDir + "\\Updater.exe", process.Id.ToString()));
            return Result.Succeeded;
        }

        private void ControlledApplication_ElementTypeDuplicating(object sender, ElementTypeDuplicatingEventArgs e)
        {
            if (!e.Document.IsFamilyDocument)
            {
                e.Cancel();
                MessageBox.Show("Запрет дублирования типов, загрузите тип через менеджер семейств", "Ошибка");
            }
        }

        public void CleanOldLogFiles()
        {
            try
            {
                string logsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FerrumLogs");

                if (!Directory.Exists(logsFolder))
                    return;

                // Определяем дату, старше которой файлы будем удалять
                DateTime thresholdDate = DateTime.Now.AddDays(-14);

                // Получаем все XML-файлы в папке логов
                DirectoryInfo di = new DirectoryInfo(logsFolder);
                FileInfo[] logFiles = di.GetFiles("*.xml");

                foreach (FileInfo file in logFiles)
                {
                    try
                    {
                        // Проверяем дату создания файла
                        if (file.CreationTime < thresholdDate)
                        {
                            // Удаляем файлы старше 2 недель
                            file.Delete();
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }
        public static bool BigPicture;
        public static string xmlFilePath;
        public static string TabPath;
        public static string FamilyFolder;
        
        public static Dictionary<string, bool> GetElementStates(XElement root)
        {
            var elementStates = new Dictionary<string, bool>();

            foreach (var element in root.Elements())
            {
                if (bool.TryParse(element.Attribute("IsChecked")?.Value, out bool isChecked))
                {
                    elementStates[element.Name.LocalName] = isChecked;
                }
            }

            return elementStates;
        }
        public static void ButtonConf(XElement root)
        {
            Dictionary<string,bool> names = GetElementStates(root);
            Autodesk.Windows.RibbonControl ribbon = Autodesk.Windows.ComponentManager.Ribbon;
            foreach (Autodesk.Windows.RibbonTab tab in ribbon.Tabs)
            {
                if (tab.Title.Contains("Железно"))
                {
                    foreach (Autodesk.Windows.RibbonPanel panel in tab.Panels)
                    {

                        RibbonItemCollection collctn = panel.Source.Items;
                        foreach (Autodesk.Windows.RibbonItem ri in collctn)
                        {
                            string name = ri.Id.Split('%').Last();
                            if (names.Keys.ToList().Contains(name))
                            {
                                ri.IsVisible = names[name];
                                ri.ShowText = names[name];
                                ri.ShowImage = names[name];
                            }
                        }
                        
                    }
                }
            }
        }

        private void A_ViewActivated(object sender, ViewActivatedEventArgs e)
        {
            if (AllowLoad == false)
            {
                Document d = e.Document;
                dockableWindow.CustomInitiator(d);
            }
        }
        private DateTime openTimeStart;
        private DateTime synchronizingTimeStart;
        private Dictionary<string, int> countStart = new Dictionary<string, int>();
        private readonly Dictionary<string, int> modifiedDict = new Dictionary<string, int>();
        private readonly Dictionary<string, int> deletedDict = new Dictionary<string, int>();
        private readonly Dictionary<string, int> createdDict = new Dictionary<string, int>();

        private void ControlledApplication_DocumentOpening(object sender, DocumentOpeningEventArgs e)
        {
            try
            {
                openTimeStart = DateTime.Now;
            }
            catch (Exception ex)
            {

            }
        }

        private void ControlledApplication_DocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            try
            {
                if (e?.Document == null) return;

                if (AllowLoad == false)
                {
                    Document d = e.Document;
                    dockableWindow?.GetType().GetMethod("CustomInitiator")?.Invoke(dockableWindow, new object[] { d });
                }

                TimeSpan openingTime = DateTime.Now - openTimeStart;

                // Сбор элементов
                List<Element> elements = new FilteredElementCollector(e.Document)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(x => x.Category != null)
                    .ToList();
                countStart[e.Document.Title] = elements?.Count ?? 0;

                // Запись в файл
                SaveOpenTimeStat(e.Document, openingTime);
            }
            catch (Exception ex)
            {

            }
        }

        private void ControlledApplication_DocumentSynchronizedWithCentral(object sender, DocumentSynchronizedWithCentralEventArgs e)
        {
            try
            {
                if (e?.Document == null) return;

                TimeSpan synchroTime = DateTime.Now - synchronizingTimeStart;

                // Запись в файл
                SaveSynchroTimeStat(e.Document, synchroTime);
            }
            catch (Exception ex)
            {

            }
        }

        private void ControlledApplication_DocumentSynchronizingWithCentral(object sender, DocumentSynchronizingWithCentralEventArgs e)
        {
            try
            {
                if (e?.Document == null) return;

                string docTitle = e.Document.Title;

                // Сбор элементов
                List<Element> elements = new FilteredElementCollector(e.Document)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(x => x.Category != null)
                    .ToList();
                int totalCount = elements?.Count ?? 0;

                modifiedDict.TryGetValue(docTitle, out int modified);
                deletedDict.TryGetValue(docTitle, out int deleted);
                createdDict.TryGetValue(docTitle, out int created);

                // Запись в файл
                SaveElementsStat(e.Document, countStart[e.Document.Title], modified, deleted, created, totalCount);
                SaveTransactionsToXml(e.Document.Title);

                transactionsLog[e.Document.Title].Clear();

                // Обновляем счетчик элементов
                countStart[docTitle] = totalCount;
                synchronizingTimeStart = DateTime.Now;

                modifiedDict[docTitle] = 0;
                deletedDict[docTitle] = 0;
                createdDict[docTitle] = 0;
            }
            catch (Exception ex)
            {

            }
        }

        private void ControlledApplication_DocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            try
            {
                if (e?.Document == null) return;

                string docTitle = e.Document.Title;

                modifiedDict.Remove(docTitle);
                deletedDict.Remove(docTitle);
                createdDict.Remove(docTitle);
                countStart.Remove(docTitle);
            }
            catch (Exception ex)
            {

            }
        }

        private readonly Dictionary<string, List<string>> transactionsLog = new Dictionary<string, List<string>>();

        private void ControlledApplication_DocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            try
            {
                if (e?.GetDocument() == null) return;

                Document doc = e.GetDocument();
                string documentName = doc.Title;
                DateTime changeTime = DateTime.Now;

                // Инициализация списка транзакций для документа
                if (!transactionsLog.ContainsKey(documentName))
                {
                    transactionsLog[documentName] = new List<string>();
                }

                // Обработка всех транзакций в этом событии
                foreach (string transactionName in e.GetTransactionNames())
                {
                    // Обработка измененных элементов
                    foreach (ElementId id in e.GetModifiedElementIds())
                    {
                        Element element = doc.GetElement(id);
                        if (element == null || element.Category == null) continue;
                        LogElementChange(doc, documentName, "Изменение", transactionName, id, changeTime, element.Category.Name);
                    }

                    // Обработка удаленных элементов
                    foreach (ElementId id in e.GetDeletedElementIds())
                    {
                        LogElementChange(doc, documentName, "Удаление", transactionName, id, changeTime, "*Удалено*");
                    }

                    // Обработка созданных элементов
                    foreach (ElementId id in e.GetAddedElementIds())
                    {
                        Element element = doc.GetElement(id);
                        if (element == null || element.Category == null) continue;
                        LogElementChange(doc, documentName, "Создание", transactionName, id, changeTime, element.Category.Name);
                    }
                }

                // Обновляем общую статистику (как в предыдущей версии)
                modifiedDict[documentName] = e.GetModifiedElementIds().Count;
                deletedDict[documentName] = e.GetDeletedElementIds().Count;
                createdDict[documentName] = e.GetAddedElementIds().Count;
            }
            catch (Exception ex)
            {

            }
        }

        private void LogElementChange(Document doc, string documentName, string changeType,
                                    string transactionName, ElementId elementId, DateTime changeTime, string categoryName)
        {       
            string elementInfo = $"{changeType} - {transactionName} - " +
                               $"Id элемента: {elementId.IntegerValue} - " +
                               $"Категория: {categoryName} - " +
                               $"Время: {changeTime:HH:mm:ss}";

            transactionsLog[documentName].Add(elementInfo);
        }

        private void SaveTransactionsToXml(string documentName)
        {
            try
            {
                // Создаем папку для логов, если ее нет
                string logsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FerrumLogs");

                if (!Directory.Exists(logsFolder))
                {
                    Directory.CreateDirectory(logsFolder);
                }

                // Формируем имя файла
                string fileName = $"Транзакции - {Environment.UserName} - {DateTime.Now:dd.MM.yyyy}.xml";
                string filePath = Path.Combine(logsFolder, fileName);

                XDocument xdoc;
                XElement rootElement;

                // Проверяем существует ли файл
                if (File.Exists(filePath))
                {
                    // Загружаем существующий документ
                    xdoc = XDocument.Load(filePath);
                    rootElement = xdoc.Root;
                }
                else
                {
                    // Создаем новый документ
                    xdoc = new XDocument();
                    rootElement = new XElement("RevitTransactions");
                    xdoc.Add(rootElement);
                }

                // Ищем или создаем элемент для текущего документа
                XElement docElement = rootElement.Elements("Document")
                    .FirstOrDefault(e => e.Attribute("Name")?.Value == documentName);

                if (docElement == null)
                {
                    docElement = new XElement("Document",
                        new XAttribute("Name", documentName));
                    rootElement.Add(docElement);
                }

                // Добавляем новые транзакции
                if (transactionsLog.TryGetValue(documentName, out List<string> newTransactions))
                {
                    // Получаем существующие транзакции (если нужно избежать дублирования)
                    var existingTransactions = docElement.Elements("Transaction")
                        .Select(t => t.Value)
                        .ToList();

                    // Добавляем только новые уникальные транзакции
                    foreach (var transaction in newTransactions)
                    {
                        if (!existingTransactions.Contains(transaction))
                        {
                            docElement.Add(new XElement("Transaction", transaction));
                        }
                    }
                }

                // Сохраняем изменения
                xdoc.Save(filePath);
            }
            catch (Exception ex)
            {

            }
        }

        private void SaveOpenTimeStat(Document doc, TimeSpan duration)
        {
            try
            {
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FerrumLogs");
                if (!Directory.Exists(Path.GetDirectoryName(filePath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                }
                filePath = Path.Combine(filePath, System.Environment.UserName + " - " + DateTime.Now.ToShortDateString() + ".xml");


                XDocument xdoc;

                if (File.Exists(filePath))
                {
                    xdoc = XDocument.Load(filePath);
                }
                else
                {
                    xdoc = new XDocument(new XElement("RevitStatistics"));
                }

                // Находим или создаем запись для этого документа
                XElement docElement = xdoc.Root.Elements("Document")
                    .FirstOrDefault(e => e.Attribute("Name")?.Value == doc.Title);

                if (docElement == null)
                {
                    docElement = new XElement("Document",
                        new XAttribute("Name", doc.Title));
                    xdoc.Root.Add(docElement);
                }

                // Обновляем или добавляем раздел OpenTime
                XElement openTimeElement = docElement.Element("OpenTime");
                if (openTimeElement == null)
                {
                    openTimeElement = new XElement("OpenTime");
                    docElement.Add(openTimeElement);
                }

                openTimeElement.Add(new XElement("Record",
                    new XAttribute("Timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                    new XAttribute("Duration", duration.ToString())));

                xdoc.Save(filePath);
            }
            catch (Exception ex)
            {

            }
        }

        private void SaveSynchroTimeStat(Document doc, TimeSpan duration)
        {
            try
            {
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FerrumLogs");
                if (!Directory.Exists(Path.GetDirectoryName(filePath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                }
                filePath = Path.Combine(filePath, System.Environment.UserName + " - " + DateTime.Now.ToShortDateString() + ".xml");

                XDocument xdoc = XDocument.Load(filePath);

                // Находим запись для этого документа
                XElement docElement = xdoc.Root.Elements("Document")
                    .FirstOrDefault(e => e.Attribute("Name")?.Value == doc.Title);

                if (docElement == null) return;

                // Обновляем или добавляем раздел SynchroTime
                XElement synchroTimeElement = docElement.Element("SynchroTime");
                if (synchroTimeElement == null)
                {
                    synchroTimeElement = new XElement("SynchroTime");
                    docElement.Add(synchroTimeElement);
                }

                synchroTimeElement.Add(new XElement("Record",
                    new XAttribute("Timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                    new XAttribute("Duration", duration.ToString())));

                xdoc.Save(filePath);
            }
            catch (Exception ex)
            {

            }
        }

        private void SaveElementsStat(Document doc, int initialCount, int modified, int deleted, int created, int finalCount)
        {
            try
            {
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FerrumLogs");
                if (!Directory.Exists(Path.GetDirectoryName(filePath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                }
                filePath = Path.Combine(filePath, System.Environment.UserName + " - " + DateTime.Now.ToShortDateString() + ".xml");

                XDocument xdoc = XDocument.Load(filePath);

                // Находим запись для этого документа
                XElement docElement = xdoc.Root.Elements("Document")
                    .FirstOrDefault(e => e.Attribute("Name")?.Value == doc.Title);

                if (docElement == null) return;

                // Обновляем или добавляем раздел Elements
                XElement elementsElement = docElement.Element("Elements");
                if (elementsElement == null)
                {
                    elementsElement = new XElement("Elements");
                    docElement.Add(elementsElement);
                }

                elementsElement.Add(new XElement("Record",
                    new XAttribute("Timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                    new XElement("InitialCount", initialCount),
                    new XElement("FinalCount", finalCount),
                    new XElement("Modified", modified),
                    new XElement("Deleted", deleted),
                    new XElement("Created", created)));

                xdoc.Save(filePath);
            }
            catch (Exception ex)
            {

            }
        }

        public static ExternalEvent LoadEvent;
        public static bool AllowLoad = false;
        public static bool AlwaysLoad = false;
        private void ControlledApplication_FamilyLoadingIntoDocument(object sender, Autodesk.Revit.DB.Events.FamilyLoadingIntoDocumentEventArgs e)
        {
            if (AllowLoad == true || AlwaysLoad == true)
            {
              
            }
            else
            {
                e.Cancel();
                MessageBox.Show("Загрузите семейство из менеджера семейств", "Запрет загрузки");
            }
        }

        public static FamilyManagerWindow dockableWindow = null;
        ExternalCommandData edata = null;

        

        private void Cb_CurrentChanged(object sender, Autodesk.Revit.UI.Events.ComboBoxCurrentChangedEventArgs e)
        {
            string vkl = e.NewValue.ItemText;
            switch (vkl)
            {
                case "MEP":
                    panelMEP.Visible = true;
                    panelKR.Visible = false;
                    panelControl.Visible = false;
                    break;
                case "КР":
                    panelMEP.Visible = false;
                    panelKR.Visible = true;
                    panelControl.Visible = false;
                    break;
                case "Управление":
                    panelMEP.Visible = false;
                    panelKR.Visible = false;
                    panelControl.Visible = true;
                    break;
                default:
                    panelMEP.Visible=false;
                    panelKR.Visible = false;
                    panelControl.Visible = false;
                    break;
            }
        } 
    }

    public class LoadEvent : IExternalEventHandler
    {
        public static void a_DialogBoxShowing(
  object sender,
  DialogBoxShowingEventArgs e)
        {
            if (e.DialogId == "Dialog_Revit_PasteSimilarSymbolsPaste")
                e.OverrideResult((int)System.Windows.Forms.DialogResult.OK);
        }
        public void Execute(UIApplication app)
        {
            app.DialogBoxShowing += a_DialogBoxShowing;
            var nameAndCat = new Dictionary<string, BuiltInCategory>
        {
            { "Стены", BuiltInCategory.OST_Walls },
            { "Перекрытия", BuiltInCategory.OST_Floors },
            { "Потолки", BuiltInCategory.OST_Ceilings },
            { "Витражи", BuiltInCategory.OST_Walls },
            { "Крыши" , BuiltInCategory.OST_Roofs},
            { "Ограждения" , BuiltInCategory.OST_StairsRailing},
            { "Пандусы", BuiltInCategory.OST_Ramps },
            {"Материалы", BuiltInCategory.OST_Materials }
        };

            Document docToCopy = FamilyManagerWindow.doc;
            List<MenuItem> list = new List<MenuItem>();
            foreach (TabItemViewModel tab in FamilyManagerWindow.mvm.TabItems)
            {
                list.AddRange(tab.MenuItems.Where(x => x.IsSelected).ToList());
            }
            App.AllowLoad = true;
            List<Document> documents = new List<Document>();
            List<MenuItem> typeMenuIems = new List<MenuItem>();
            foreach (MenuItem item in list)
            {
                if (item.Path.EndsWith("rfa") && File.Exists(item.Path.Remove(item.Path.Length-3, 3) + "txt"))
                {
                    typeMenuIems.Add(item);
                }
            }
            list = (List<MenuItem>)list.Except(typeMenuIems).ToList();
            if (typeMenuIems.Count > 0)
            {
                ChooseTypesWindow window = new ChooseTypesWindow(typeMenuIems);
                window.ShowDialog();

                if (window.DialogResult == true)
                {
                    Dictionary<string, List<string>> selectedTypes = ChooseTypesWindow.selectedTypes;
                    foreach (var fam in selectedTypes.Keys)
                    {
                        foreach (var type in selectedTypes[fam])
                        {
                            using (Transaction tx = new Transaction(docToCopy))
                            {
                                tx.Start("Загрузка типа семейства" + fam + "-" + type);
                                docToCopy.LoadFamilySymbol(typeMenuIems.Where(x => x.Name == fam).FirstOrDefault().Path, type);
                                tx.Commit();
                            }
                        }
                    }

                }
            }
            foreach (MenuItem tab in list)
            {
                bool isFirstOptionChecked = FamilyManagerWindow.IsFirstOptionChecked();
                if (tab.Path.EndsWith("rfa"))
                {
                    string familyName = System.IO.Path.GetFileNameWithoutExtension(tab.Path);
                    Family existingFamily = new FilteredElementCollector(docToCopy)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .FirstOrDefault(fam => fam.Name == familyName);

                    if (existingFamily != null)
                    {
                        if (isFirstOptionChecked)
                        {
                            // Замена существующего семейства
                            using (Transaction tx = new Transaction(docToCopy))
                            {
                                tx.Start("Загрузка семейств");
                                FailureHandlingOptions failureOptions = tx.GetFailureHandlingOptions();
                                //failureOptions.SetFailuresPreprocessor(new MyFailuresPreprocessor());
                                //failureOptions.SetClearAfterRollback(true); // Опционально
                                //tx.SetFailureHandlingOptions(failureOptions);
                                MyFamilyLoadOptions loadOptions = new MyFamilyLoadOptions();
                                docToCopy.LoadFamily(tab.Path, loadOptions, out Family load);
                                tx.Commit();
                                
                            }
                        }
                        else
                        {
                            using (Transaction tx = new Transaction(docToCopy))
                            {
                                tx.Start("Загрузка семейств");
                                FailureHandlingOptions failureOptions = tx.GetFailureHandlingOptions();
                                //failureOptions.SetFailuresPreprocessor(new MyFailuresPreprocessor());
                                //failureOptions.SetClearAfterRollback(true); // Опционально
                                //tx.SetFailureHandlingOptions(failureOptions);
                                MyFamilyLoadOptions loadOptions = new MyFamilyLoadOptions();
                                string famPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.GetFileNameWithoutExtension(tab.Path )+ "_1.rfa");
                                File.Copy(tab.Path, famPath, true);
                                docToCopy.LoadFamily(famPath, loadOptions, out Family load);
                                File.Delete(famPath);
                                tx.Commit();
                                if (list.Count == 1 && FamilyManagerWindow.IsCreateInstanceChecked())
                                {
                                    Family family = new FilteredElementCollector(docToCopy)
                                     .OfClass(typeof(Family))
                                     .Cast<Family>()
                                     .FirstOrDefault(fam => fam.Name == familyName);
                                    ElementType type = docToCopy.GetElement(family.GetFamilySymbolIds().FirstOrDefault()) as ElementType;
                                    app.ActiveUIDocument.PostRequestForElementTypePlacement(type);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Загрузка без конфликтов
                        using (Transaction tx = new Transaction(docToCopy))
                        {
                            tx.Start("Загрузка семейств");
                            FailureHandlingOptions failureOptions = tx.GetFailureHandlingOptions();
                            //failureOptions.SetFailuresPreprocessor(new MyFailuresPreprocessor());
                            //failureOptions.SetClearAfterRollback(true); // Опционально
                            //tx.SetFailureHandlingOptions(failureOptions);
                            MyFamilyLoadOptions loadOptions = new MyFamilyLoadOptions();
                            docToCopy.LoadFamily(tab.Path, loadOptions, out Family load);
                            tx.Commit();
                            if (list.Count == 1 && FamilyManagerWindow.IsCreateInstanceChecked())
                            {
                                Family family = new FilteredElementCollector(docToCopy)
                                .OfClass(typeof(Family))
                                .Cast<Family>()
                                .FirstOrDefault(fam => fam.Name == familyName);
                                ElementType type = docToCopy.GetElement(family.GetFamilySymbolIds().FirstOrDefault()) as ElementType;
                                app.ActiveUIDocument.PostRequestForElementTypePlacement(type);
                            }
                        }
                    }
                }
                else
                {
                    Document document = app.Application.OpenDocumentFile(tab.Path);
                    documents.Add(document);
                    List<ElementId> el = new List<ElementId>();
                    if (tab.Category != "Материалы")
                    {
                        el = new FilteredElementCollector(document)
                        .OfCategory(nameAndCat[tab.Category])
                        .WhereElementIsElementType()
                        .Where(x => x.Name == tab.Name)
                        .Select(x => x.Id)
                        .ToList();
                    }
                    else
                    {
                        el = new FilteredElementCollector(document)
                        .OfCategory(nameAndCat[tab.Category])
                        .Where(x => x.Name == tab.Name)
                        .Select(x => x.Id)
                        .ToList();
                    }

                    bool elementExists = ElementExists(docToCopy, nameAndCat[tab.Category], tab.Name);

                    if (!elementExists)
                    {
                        // Копирование без конфликтов
                        using (Transaction tx = new Transaction(docToCopy))
                        {
                            tx.Start("Загрузка семейств");
                            FailureHandlingOptions failureOptions = tx.GetFailureHandlingOptions();
                            //failureOptions.SetFailuresPreprocessor(new MyFailuresPreprocessor());
                            //failureOptions.SetClearAfterRollback(true); // Опционально
                            //tx.SetFailureHandlingOptions(failureOptions);
                            ElementTransformUtils.CopyElements(document, el, docToCopy, null, null);
                            tx.Commit();
                            if (list.Count == 1 && FamilyManagerWindow.IsCreateInstanceChecked())
                            {
                                ElementType type = new FilteredElementCollector(docToCopy)
                                 .OfCategory(nameAndCat[tab.Category]).WhereElementIsElementType()
                                 .FirstOrDefault(fam => fam.Name == tab.Name) as ElementType;
                                app.ActiveUIDocument.PostRequestForElementTypePlacement(type);
                            }
                        }
                    }
                    else
                    {
                        CopyPasteOptions options = new CopyPasteOptions();
                        options.SetDuplicateTypeNamesHandler(new MyCopyHandler());
                        if (isFirstOptionChecked)
                        {
                            // Замена существующего элемента
                            using (Transaction tx = new Transaction(docToCopy))
                            {
                                tx.Start("Загрузка семейств");
                                FailureHandlingOptions failureOptions = tx.GetFailureHandlingOptions();
                                //failureOptions.SetFailuresPreprocessor(new MyFailuresPreprocessor());
                                //failureOptions.SetClearAfterRollback(true); // Опционально
                                //tx.SetFailureHandlingOptions(failureOptions);

                                // Получаем элементы из исходного документа
                                List<Element> elementsToCopy = new List<Element>();
                                foreach (ElementId id in el)
                                {
                                    Element elem = document.GetElement(id);
                                    elementsToCopy.Add(elem);
                                }

                                // Проходим по каждому элементу для обработки
                                foreach (Element sourceElement in elementsToCopy)
                                {
                                    if (sourceElement is ElementType sourceType)
                                    {
                                        // Копируем тип в целевой документ
                                        ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElements(
                                            document,
                                            new List<ElementId> { sourceType.Id },
                                            docToCopy,
                                            Transform.Identity,
                                            options);

                                        ElementId copiedTypeId = copiedIds.First();
                                        ElementType copiedType = docToCopy.GetElement(copiedTypeId) as ElementType;

                                        // Ищем существующий тип с таким же именем в целевом документе
                                        ElementType existingType = FindTypeByNameAndClass(docToCopy, sourceType.Name, sourceType.GetType());

                                        if (existingType != null && existingType.Id != copiedType.Id)
                                        {
                                            // Заменяем все элементы, использующие старый тип, на новый тип
                                            ReplaceElementsType(docToCopy, existingType.Id, copiedType.Id);

                                            // Удаляем старый тип
                                            docToCopy.Delete(existingType.Id);
                                        }
                                        // Если типа не было, ничего дополнительно делать не нужно
                                    }
                                }

                                tx.Commit();
                            }
                        }

                        else
                        {
                            // Переименование копируемого элемента
                            using (Transaction tx = new Transaction(docToCopy))
                            {
                                tx.Start("Загрузка семейств");
                                FailureHandlingOptions failureOptions = tx.GetFailureHandlingOptions();
                                //failureOptions.SetFailuresPreprocessor(new MyFailuresPreprocessor());
                                //failureOptions.SetClearAfterRollback(true); // Опционально
                                //tx.SetFailureHandlingOptions(failureOptions);
                                ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElements(document, el, docToCopy, Transform.Identity, options);
                                ElementId copiedId = copiedIds.First();
                                Element copiedElement = docToCopy.GetElement(copiedId);

                                string newName = GetUniqueElementName(docToCopy, nameAndCat[tab.Category], tab.Name);
                                copiedElement.Name = newName;

                                tx.Commit();
                                if (list.Count == 1 && FamilyManagerWindow.IsCreateInstanceChecked())
                                {
                                    ElementType type = new FilteredElementCollector(docToCopy)
                                     .OfCategory(nameAndCat[tab.Category]).WhereElementIsElementType()
                                     .FirstOrDefault(fam => fam.Name == tab.Name) as ElementType;
                                    app.ActiveUIDocument.PostRequestForElementTypePlacement(type);
                                }
                            }
                        }
                    }
                    
                }
            }
            App.AllowLoad = false;
            FamilyManagerWindow.Reload();
            app.DialogBoxShowing -= a_DialogBoxShowing;
        }
        public string GetName()
        {
            return "LoadEventHandler";
        }

        // Метод для поиска типа по имени и классу в целевом документе
        private ElementType FindTypeByNameAndClass(Document doc, string typeName, Type typeClass)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeClass)
                .Cast<ElementType>()
                .FirstOrDefault(e => e.Name.Equals(typeName, StringComparison.InvariantCultureIgnoreCase));
        }

        // Метод для замены типа у всех элементов
        private void ReplaceElementsType(Document doc, ElementId oldTypeId, ElementId newTypeId)
        {
            // Находим все элементы, использующие старый тип
            List<Element> collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.GetTypeId() == oldTypeId).ToList();

            foreach (Element elem in collector)
            {
                // Устанавливаем новый тип
                elem.ChangeTypeId(newTypeId);
            }
        }


        private bool ElementExists(Document doc, BuiltInCategory category, string elementName)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsElementType()
                .Any(x => x.Name == elementName);
        }

        private string GetUniqueFamilyName(Document doc, string baseName)
        {
            string newName = baseName;
            int i = 1;
            while (new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Any(fam => fam.Name == newName))
            {
                newName = $"{baseName}_{i}";
                i++;
            }
            return newName;
        }

        private string GetUniqueElementName(Document doc, BuiltInCategory category, string baseName)
        {
            string newName = baseName;
            int i = 1;
            while (new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsElementType()
                .Any(e => e.Name == newName))
            {
                newName = $"{baseName}_{i}";
                i++;
            }
            return newName;
        }
    }

    public class MyCopyHandler : IDuplicateTypeNamesHandler
    {


        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }

    public class MyFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(
       bool familyInUse,
       out bool overwriteParameterValues)
        {
            overwriteParameterValues = false;
            return true;
        }

        public bool OnSharedFamilyFound(
          Family sharedFamily,
          bool familyInUse,
          out FamilySource source,
          out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = false;
            return true;
        }
    }

    public class MyFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            /*IList<FailureMessageAccessor> failures
    = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor f in failures)
            {
                FailureSeverity fseverity = failuresAccessor.GetSeverity();

                if (fseverity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(f);
                }
                else
                {
                    failuresAccessor.ResolveFailure(f);
                    return FailureProcessingResult.ProceedWithCommit;
                }
            }
            return FailureProcessingResult.Continue;*/
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor failure in failures)
            {
                // Определяем степень серьезности ошибки
                FailureSeverity severity = failure.GetSeverity();

                // Обрабатываем в зависимости от степени серьезности
                if (severity == FailureSeverity.Warning)
                {
                    // Удаляем предупреждения
                    failuresAccessor.DeleteWarning(failure);
                }
                else
                {
                    // Проверяем, можно ли автоматически решить ошибку
                    if (failure.HasResolutions())
                    {
                        // Применяем первое доступное решение
                        failuresAccessor.ResolveFailure(failure);
                        return FailureProcessingResult.ProceedWithCommit;
                    }
                    else
                    {
                        // Если решений нет, удаляем ошибку или логируем
                        failuresAccessor.DeleteWarning(failure);
                    }
                }
            }

            // Продолжаем транзакцию без отображения диалоговых окон
            return FailureProcessingResult.Continue;
        }
    }


    public class CommandAvailability : IExternalCommandAvailability
    {
        // interface member method
        public bool IsCommandAvailable(UIApplication app, CategorySet cate)
        {
            // zero doc state
            if (app.ActiveUIDocument == null)
            {
                // disable register btn
                return true;
            }
            // enable register btn
            return false;
        }
    }


}
