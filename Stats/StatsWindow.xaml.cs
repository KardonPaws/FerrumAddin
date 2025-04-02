using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Xml.Linq;
using System.IO;
using Path = System.IO.Path;
using System.Linq;

namespace FerrumAddin.Stats
{
    /// <summary>
    /// Логика взаимодействия для StatsWindow.xaml
    /// </summary>
    public partial class StatsWindow : Window
    {
        
           private string LogsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FerrumLogs");

        public StatsWindow()
        {
            InitializeComponent();
            datePicker.SelectedDate = DateTime.Today;
            LoadDataForDate(DateTime.Today);
        }

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (datePicker.SelectedDate.HasValue)
            {
                LoadDataForDate(datePicker.SelectedDate.Value);
            }
        }

        private void LoadDataForDate(DateTime date)
        {
            try
            {
                // Загружаем файл статистики
                string statsFile = $"{Environment.UserName} - {date:dd.MM.yyyy}.xml";
                string statsPath = Path.Combine(LogsFolder, statsFile);

                if (File.Exists(statsPath))
                {
                    var statsDoc = XDocument.Load(statsPath);
                    DisplayStatistics(statsDoc);
                }
                else
                {
                    statisticsGrid.ItemsSource = null;
                    MessageBox.Show($"Файл статистики за {date:dd.MM.yyyy} не найден");
                }

                // Загружаем файл транзакций
                string transFile = $"Транзакции - {Environment.UserName} - {date:dd.MM.yyyy}.xml";
                string transPath = Path.Combine(LogsFolder, transFile);

                if (File.Exists(transPath))
                {
                    var transDoc = XDocument.Load(transPath);
                    DisplayTransactions(transDoc);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}");
            }
        }

        private void DisplayStatistics(XDocument statsDoc)
        {
            var statsData = new List<StatisticRecord>();

            foreach (var docElement in statsDoc.Root.Elements("Document"))
            {
                string docName = docElement.Attribute("Name").Value;

                // Обрабатываем все записи времени открытия
                foreach (var openRecord in docElement.Element("OpenTime")?.Elements("Record") ?? Enumerable.Empty<XElement>())
                {
                    statsData.Add(new StatisticRecord
                    {
                        Document = docName,
                        Operation = "Открытие",
                        Timestamp = DateTime.Parse(openRecord.Attribute("Timestamp").Value),
                        Duration = TimeSpan.Parse(openRecord.Attribute("Duration").Value),
                        InitialCount = 0,
                        FinalCount = 0,
                        Modified = 0,
                        Deleted = 0,
                        Created = 0
                    });
                }

                // Обрабатываем все записи синхронизации
                foreach (var syncRecord in docElement.Element("SynchroTime")?.Elements("Record") ?? Enumerable.Empty<XElement>())
                {
                    statsData.Add(new StatisticRecord
                    {
                        Document = docName,
                        Operation = "Синхронизация",
                        Timestamp = DateTime.Parse(syncRecord.Attribute("Timestamp").Value),
                        Duration = TimeSpan.Parse(syncRecord.Attribute("Duration").Value),
                        InitialCount = 0,
                        FinalCount = 0,
                        Modified = 0,
                        Deleted = 0,
                        Created = 0
                    });
                }

                // Обрабатываем все записи изменений элементов
                foreach (var elementRecord in docElement.Element("Elements")?.Elements("Record") ?? Enumerable.Empty<XElement>())
                {
                    statsData.Add(new StatisticRecord
                    {
                        Document = docName,
                        Operation = "Изменения элементов",
                        Timestamp = DateTime.Parse(elementRecord.Attribute("Timestamp").Value),
                        Duration = TimeSpan.Zero,
                        InitialCount = int.Parse(elementRecord.Element("InitialCount").Value),
                        FinalCount = int.Parse(elementRecord.Element("FinalCount").Value),
                        Modified = int.Parse(elementRecord.Element("Modified").Value),
                        Deleted = int.Parse(elementRecord.Element("Deleted").Value),
                        Created = int.Parse(elementRecord.Element("Created").Value)
                    });
                }
            }

            // Сортируем данные по времени для удобства просмотра
            statsData = statsData.OrderBy(r => r.Timestamp).ToList();
            statisticsGrid.ItemsSource = statsData;
        }

        private void DisplayTransactions(XDocument transDoc)
        {
            var transData = new List<TransactionRecord>();

            foreach (var docElement in transDoc.Root.Elements("Document"))
            {
                string docName = docElement.Attribute("Name").Value;

                foreach (var transElement in docElement.Elements("Transaction"))
                {
                    string[] parts = transElement.Value.Split(new[] { " - " }, StringSplitOptions.None);

                    if (parts.Length >= 5)
                    {
                        transData.Add(new TransactionRecord
                        {
                            Document = docName,
                            ChangeType = parts[0],
                            TransactionName = parts[1],
                            ElementId = parts[2].Replace("Id элемента: ", ""),
                            Category = parts[3].Replace("Категория: ", ""),
                            Time = parts[4].Replace("Время: ", "")
                        });
                    }
                }
            }

            transactionsGrid.ItemsSource = transData;
        }
    }

    public class StatisticRecord
    {
        public string Document { get; set; }
        public string Operation { get; set; }
        public DateTime Timestamp { get; set; }
        public TimeSpan Duration { get; set; }
        public int InitialCount { get; set; }
        public int FinalCount { get; set; }
        public int Modified { get; set; }
        public int Deleted { get; set; }
        public int Created { get; set; }
    }

    public class TransactionRecord
    {
        public string Document { get; set; }
        public string ChangeType { get; set; }
        public string TransactionName { get; set; }
        public string ElementId { get; set; }
        public string Category { get; set; }
        public string Time { get; set; }
    }
}
