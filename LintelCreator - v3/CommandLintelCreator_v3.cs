using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FerrumAddinDev.LintelCreator_v3
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class CommandLintelCreator_v3 : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDocument = commandData.Application.ActiveUIDocument;
            LintelCreatorForm_v3 form = null;

            try
            {
                var workspace = new LintelOpeningWorkspaceV3(uiDocument.Document, uiDocument.Selection);
                var selectionHandler = new OpeningSelectionHandlerV3();
                ExternalEvent selectionEvent = ExternalEvent.Create(selectionHandler);
                form = new LintelCreatorForm_v3(workspace, selectionHandler, selectionEvent);
                form.Show();
                form.RefreshProgressDisplay(true);
                workspace.Reload((processed, total) => form.RefreshProgressDisplay());
                form.RefreshProgressDisplay(true);

                if (workspace.TotalOpeningCount == 0)
                {
                    form.Close();
                    message = "В активном виде или текущем выборе не найдены поддерживаемые дверные, оконные проёмы и витражи со стеной-основой.";
                    return Result.Cancelled;
                }

                form.StartInitialCalculation();
                return Result.Succeeded;
            }
            catch (Exception exception)
            {
                form?.Close();
                message = exception.Message;
                return Result.Failed;
            }
        }
    }

    public abstract class NotifyObjectV3 : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum OpeningStatusV3
    {
        Success = 0,
        Warning = 1,
        Error = 2
    }

    public enum OpeningSortFieldV3
    {
        None = 0,
        OpeningType,
        Status,
        Support,
        OpeningWidth,
        WallType,
        WallThickness,
        Category,
        Level,
        Count
    }

    public enum OpeningSearchFieldV3
    {
        All = 0,
        OpeningKind,
        SourceType,
        OpeningWidth,
        OpeningHeight,
        Support,
        SupportWidth,
        WallType,
        WallThickness,
        Category,
        Level,
        Status,
        Count,
        Id
    }

    public sealed class OpeningSortOptionV3
    {
        public OpeningSortFieldV3 Field { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class OpeningSearchOptionV3
    {
        public OpeningSearchFieldV3 Field { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class OpeningSortCriterionV3 : NotifyObjectV3
    {
        private OpeningSortOptionV3 _selectedOption;
        private bool _isDescending;
        private int _order;
        private bool _isLocked;

        public int Order
        {
            get => _order;
            set
            {
                if (_order == value) return;
                _order = value;
                RaisePropertyChanged(nameof(Order));
                RaisePropertyChanged(nameof(OrderText));
            }
        }
        public string OrderText => Order.ToString(CultureInfo.InvariantCulture) + ".";

        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked == value) return;
                _isLocked = value;
                RaisePropertyChanged(nameof(IsLocked));
                RaisePropertyChanged(nameof(CanEdit));
                RaisePropertyChanged(nameof(CanRemove));
            }
        }

        public bool CanEdit => !IsLocked;
        public bool CanRemove => !IsLocked;

        public OpeningSortOptionV3 SelectedOption
        {
            get => _selectedOption;
            set
            {
                if (ReferenceEquals(_selectedOption, value)) return;
                _selectedOption = value;
                RaisePropertyChanged(nameof(SelectedOption));
            }
        }

        public bool IsDescending
        {
            get => _isDescending;
            set
            {
                if (_isDescending == value) return;
                _isDescending = value;
                RaisePropertyChanged(nameof(IsDescending));
                RaisePropertyChanged(nameof(DirectionText));
            }
        }

        public string DirectionText => IsDescending ? "↓" : "↑";
    }

    public sealed class OpeningRecordV3
    {
        public ElementId OpeningId { get; set; }
        public ElementId WallId { get; set; }
        public ElementId WallTypeId { get; set; }
        public ElementId LevelId { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string OpeningKind { get; set; }
        public string CategoryName { get; set; }
        public string WallTypeName { get; set; }
        public string LevelName { get; set; }
        public double OpeningWidthMm { get; set; }
        public double OpeningHeightMm { get; set; }
        public double WallWidthMm { get; set; }
        public XYZ Location { get; set; }
        public double TopElevation { get; set; }
        public XYZ WallOrientation { get; set; }
        public XYZ WidthDirection { get; set; }
        public XYZ SupportDirection { get; set; }
        public int SupportType { get; set; }
        public double RequiredSupportWidthMm { get; set; }
        public double RequiredSupportWidth1Mm { get; set; }
        public double RequiredSupportWidth2Mm { get; set; }
        public string SupportParameterError { get; set; }
        public XYZ BoundingMinimum { get; set; }
        public XYZ BoundingMaximum { get; set; }
        public int ComponentCount { get; set; }
        public bool HasExistingLintel { get; set; }
        public string ExistingLintelFamilyNames { get; set; }
        public string ExistingLintelTypeNames { get; set; }
        public List<ElementId> ElementIds { get; } = new List<ElementId>();
        public List<ElementId> ExistingLintelIds { get; } = new List<ElementId>();
        public List<ExistingLintelComponentV3> ExistingLintelComponents { get; } = new List<ExistingLintelComponentV3>();
    }

    public sealed class ExistingLintelComponentV3
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public double Order { get; set; }
        public double OffsetToNextMm { get; set; }
    }

    public sealed class OpeningGroupCardV3 : NotifyObjectV3
    {
        private bool _isChecked;
        private OpeningStatusV3 _status;
        private string _statusText;

        public string Key { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string OpeningKind { get; set; }
        public string CategoryName { get; set; }
        public string WallTypeName { get; set; }
        public string LevelName { get; set; }
        public double OpeningWidthMm { get; set; }
        public double OpeningHeightMm { get; set; }
        public double WallWidthMm { get; set; }
        public int SupportType { get; set; }
        public double RequiredSupportWidthMm { get; set; }
        public double RequiredSupportWidth1Mm { get; set; }
        public double RequiredSupportWidth2Mm { get; set; }
        public string SupportParameterError { get; set; }
        public int InstanceCount { get; set; }
        public bool HasExistingLintel { get; set; }
        public bool IsExistingLintelAggregate { get; set; }
        public string ExistingLintelFamilyNames { get; set; }
        public string ExistingLintelTypeNames { get; set; }
        public List<ElementId> ElementIds { get; } = new List<ElementId>();
        public List<ElementId> ExistingLintelIds { get; } = new List<ElementId>();
        public List<ExistingLintelComponentV3> ExistingLintelComponents { get; } = new List<ExistingLintelComponentV3>();
        public List<LintelSelectionVariantV3> CalculatedVariants { get; } = new List<LintelSelectionVariantV3>();
        public bool IsCalculated { get; set; }
        public string CalculationMessage { get; set; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                RaisePropertyChanged(nameof(IsChecked));
            }
        }

        public OpeningStatusV3 Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                RaisePropertyChanged(nameof(Status));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value;
                RaisePropertyChanged(nameof(StatusText));
            }
        }

        public int Count => InstanceCount;
        public string CountText => Count + " экз.";
        public string DisplayName => OpeningKind;
        public string OpeningDescription => OpeningKind + " — " + Math.Round(OpeningWidthMm) + " мм";
        public string SupportDescription => "Опирание: " + SupportType;
        public string WallDescription => WallTypeName + " · " + Math.Round(WallWidthMm) + " мм";
        public string SupportText => SupportType == 2
            ? "опирание с двух сторон"
            : SupportType == 1
                ? "опирание с одной стороны"
                : "без опирания";
        public string IdsText => "ID: " + string.Join(", ", ElementIds.Select(x => x.Value.ToString(CultureInfo.InvariantCulture)));
        public string ExistingLintelIdsText => ExistingLintelIds.Count == 0
            ? string.Empty
            : "ID перемычек: " + string.Join(", ", ExistingLintelIds.Select(x => x.Value.ToString(CultureInfo.InvariantCulture)));
        public string SourceTypeText => FamilyName + " : " + TypeName;
        public string ExistingLintelDescription => string.IsNullOrWhiteSpace(ExistingLintelTypeNames)
            ? "Существующая перемычка"
            : ExistingLintelTypeNames;
        public string ExistingCardTitle => IsExistingLintelAggregate
            ? string.IsNullOrWhiteSpace(ExistingLintelTypeNames) ? "Существующая перемычка" : ExistingLintelTypeNames
            : OpeningDescription;
        public string ExistingCardDescription => IsExistingLintelAggregate
            ? Count + " проёмов · " + WallTypeName
            : ExistingLintelDescription;
        public string ExistingLintelSearchText => (ExistingLintelFamilyNames ?? string.Empty)
                                                  + " " + (ExistingLintelTypeNames ?? string.Empty)
                                                  + " " + ExistingLintelIdsText;
        public string DetailsText => SourceTypeText + Environment.NewLine + IdsText
                                     + (HasExistingLintel
                                         ? Environment.NewLine + ExistingLintelDescription + Environment.NewLine + ExistingLintelIdsText
                                         : string.Empty);
    }

    public sealed class LintelEditorRowV3 : NotifyObjectV3
    {
        private int _index;
        private LintelCatalogItemV3 _selectedCatalogItem;
        private string _purpose;
        private int _lengthMm;
        private int _widthMm;
        private int _gapMm;
        private bool _canMoveUp;
        private bool _canMoveDown;
        private bool _isApplyingCatalogItem;

        public LintelEditorRowV3(LintelCatalogItemV3 item)
        {
            ApplyCatalogItem(item);
        }

        public int Index
        {
            get => _index;
            internal set
            {
                if (_index == value) return;
                _index = value;
                RaisePropertyChanged(nameof(Index));
            }
        }

        public LintelCatalogItemV3 SelectedCatalogItem
        {
            get => _selectedCatalogItem;
            set
            {
                if (ReferenceEquals(_selectedCatalogItem, value) || value == null) return;
                ApplyCatalogItem(value);
            }
        }

        public string Purpose
        {
            get => _purpose;
            set
            {
                string normalized = value == "Несущая" ? "Несущая" : "Ненесущая";
                if (_purpose == normalized) return;
                _purpose = normalized;
                RaisePropertyChanged(nameof(Purpose));
                RaisePropertyChanged(nameof(IsBearing));
            }
        }

        public bool IsBearing => string.Equals(Purpose, "Несущая", StringComparison.Ordinal);
        internal bool IsApplyingCatalogItem => _isApplyingCatalogItem;
        internal void ApplyCatalogSuggestion(LintelCatalogItemV3 item)
        {
            ApplyCatalogItem(item);
        }

        public int LengthMm
        {
            get => _lengthMm;
            set
            {
                int normalized = Math.Max(0, value);
                if (_lengthMm == normalized) return;
                _lengthMm = normalized;
                RaisePropertyChanged(nameof(LengthMm));
            }
        }

        public int WidthMm
        {
            get => _widthMm;
            set
            {
                int normalized = Math.Max(0, value);
                if (_widthMm == normalized) return;
                _widthMm = normalized;
                RaisePropertyChanged(nameof(WidthMm));
            }
        }

        public int GapMm
        {
            get => _gapMm;
            set
            {
                int normalized = Math.Max(0, value);
                if (_gapMm == normalized) return;
                _gapMm = normalized;
                RaisePropertyChanged(nameof(GapMm));
            }
        }

        public bool CanMoveUp
        {
            get => _canMoveUp;
            internal set
            {
                if (_canMoveUp == value) return;
                _canMoveUp = value;
                RaisePropertyChanged(nameof(CanMoveUp));
            }
        }

        public bool CanMoveDown
        {
            get => _canMoveDown;
            internal set
            {
                if (_canMoveDown == value) return;
                _canMoveDown = value;
                RaisePropertyChanged(nameof(CanMoveDown));
            }
        }

        private void ApplyCatalogItem(LintelCatalogItemV3 item)
        {
            if (item == null) return;
            _isApplyingCatalogItem = true;
            try
            {
                _selectedCatalogItem = item;
                RaisePropertyChanged(nameof(SelectedCatalogItem));
                LengthMm = item.LengthMm;
                WidthMm = item.WidthMm;
                Purpose = item.IsBearing ? "Несущая" : "Ненесущая";
            }
            finally
            {
                _isApplyingCatalogItem = false;
            }
        }
    }

    public sealed class LintelOpeningWorkspaceV3 : NotifyObjectV3
    {
        private readonly Document _document;
        private readonly List<ElementId> _initialSelectionIds;
        private readonly AlphanumComparatorFastString _naturalComparer = new AlphanumComparatorFastString();
        private List<OpeningGroupCardV3> _allGroups = new List<OpeningGroupCardV3>();
        private List<OpeningGroupCardV3> _existingLintelTypeGroups = new List<OpeningGroupCardV3>();
        private OpeningGroupCardV3 _selectedGroup;
        private string _searchText = string.Empty;
        private OpeningSearchOptionV3 _selectedSearchOption;
        private OpeningStatusV3? _statusFilter;
        private string _collectionDurationText;
        private int _totalOpeningCount;
        private int _skippedOpeningCount;
        private readonly IReadOnlyList<LintelCatalogItemV3> _lintelCatalog;
        private readonly string _catalogLoadError;
        private LintelSelectionVariantV3 _selectedVariant;
        private LintelMasonryTypeV3 _masonryType = LintelMasonryTypeV3.Brick65;
        private LintelMaterialV3 _lintelMaterial = LintelMaterialV3.ReinforcedConcrete;
        private int _minimumBearingMm = 250;
        private int _wallWidthToleranceMm = 20;
        private string _selectionMessage = "Выберите группу проёмов для расчёта.";
        private readonly HashSet<string> _existingCompositeTypeNames;
        private string _selectedLeftSupportPad;
        private string _selectedRightSupportPad;
        private bool _isCalculationInProgress;
        private int _calculatedOpeningCount;
        private int _calculationOpeningTotal;
        private bool _isCollectionInProgress = true;
        private int _processedCollectionOpeningCount;
        private int _collectionOpeningTotal;
        private bool _isExistingGroupedByOpening = true;

        public LintelOpeningWorkspaceV3(Document document, Selection selection)
        {
            _document = document;
            _initialSelectionIds = selection.GetElementIds()
                .Where(id => OpeningCollectorV3.IsSupportedOpening(document, document.GetElement(id)))
                .ToList();
            _existingCompositeTypeNames = CollectExistingCompositeTypeNames(document);

            EditorPurposeOptions = new ObservableCollection<string> { "Несущая", "Ненесущая" };
            SupportPadOptions = CollectSupportPadOptions(document);
            _selectedLeftSupportPad = SupportPadOptions.FirstOrDefault();
            _selectedRightSupportPad = SupportPadOptions.FirstOrDefault();

            try
            {
                _lintelCatalog = LintelCatalogLoaderV3.Load().Items;
                ApplyRevitFamilyNames(document, _lintelCatalog);
            }
            catch (Exception exception)
            {
                _lintelCatalog = new List<LintelCatalogItemV3>();
                _catalogLoadError = exception.Message;
            }

            SortOptions = new ObservableCollection<OpeningSortOptionV3>
            {
                new OpeningSortOptionV3 { Field = OpeningSortFieldV3.OpeningType, Name = "Тип проёма" },
                new OpeningSortOptionV3 { Field = OpeningSortFieldV3.Status, Name = "Статус" },
                new OpeningSortOptionV3 { Field = OpeningSortFieldV3.Support, Name = "Опирание" },
                new OpeningSortOptionV3 { Field = OpeningSortFieldV3.OpeningWidth, Name = "Ширина проёма" },
                new OpeningSortOptionV3 { Field = OpeningSortFieldV3.WallType, Name = "Тип стены" },
                new OpeningSortOptionV3 { Field = OpeningSortFieldV3.WallThickness, Name = "Толщина стены" },
                new OpeningSortOptionV3 { Field = OpeningSortFieldV3.Category, Name = "Категория" },
                new OpeningSortOptionV3 { Field = OpeningSortFieldV3.Level, Name = "Уровень" },
                new OpeningSortOptionV3 { Field = OpeningSortFieldV3.Count, Name = "Количество" }
            };

            SearchOptions = new ObservableCollection<OpeningSearchOptionV3>
            {
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.All, Name = "Все поля" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.OpeningKind, Name = "Вид проёма" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.SourceType, Name = "Семейство/тип" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.OpeningWidth, Name = "Ширина" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.OpeningHeight, Name = "Высота" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.Support, Name = "Опирание" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.SupportWidth, Name = "Ширина опоры" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.WallType, Name = "Тип стены" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.WallThickness, Name = "Толщина стены" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.Category, Name = "Категория" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.Level, Name = "Уровень" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.Status, Name = "Статус" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.Count, Name = "Количество" },
                new OpeningSearchOptionV3 { Field = OpeningSearchFieldV3.Id, Name = "ID" }
            };
            _selectedSearchOption = SearchOptions.First();

            SortCriteria = new ObservableCollection<OpeningSortCriterionV3>
            {
                CreateCriterion(1, OpeningSortFieldV3.Status, true)
            };
            foreach (OpeningSortCriterionV3 criterion in SortCriteria)
                criterion.PropertyChanged += SortCriterion_PropertyChanged;

        }

        public ObservableCollection<OpeningGroupCardV3> VisibleGroups { get; } = new ObservableCollection<OpeningGroupCardV3>();
        public ObservableCollection<OpeningGroupCardV3> ExistingLintelGroups { get; } = new ObservableCollection<OpeningGroupCardV3>();
        public ObservableCollection<OpeningSortOptionV3> SortOptions { get; }
        public ObservableCollection<OpeningSortCriterionV3> SortCriteria { get; }
        public ObservableCollection<OpeningSearchOptionV3> SearchOptions { get; }
        public ObservableCollection<LintelSelectionVariantV3> Variants { get; } = new ObservableCollection<LintelSelectionVariantV3>();
        public ObservableCollection<LintelEditorRowV3> EditorRows { get; } = new ObservableCollection<LintelEditorRowV3>();
        public ObservableCollection<LintelCatalogItemV3> EditorCatalogItems { get; } = new ObservableCollection<LintelCatalogItemV3>();
        public ObservableCollection<string> EditorPurposeOptions { get; }
        public ObservableCollection<string> SupportPadOptions { get; }

        public OpeningGroupCardV3 SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (ReferenceEquals(_selectedGroup, value)) return;
                _selectedGroup = value;
                RaisePropertyChanged(nameof(SelectedGroup));
                RaiseSelectedGroupProperties();
                if (SelectedGroup?.HasExistingLintel == true)
                    DisplayExistingLintel(SelectedGroup);
                else if (SelectedGroup?.IsCalculated == true)
                    DisplayStoredCalculation(SelectedGroup);
                else if (!IsCalculationInProgress)
                    RecalculateVariants();
                else
                {
                    Variants.Clear();
                    SelectedVariant = null;
                    SelectionMessage = "Выполняется расчёт вариантов для всех проёмов.";
                    RaiseVariantsProperties();
                }
            }
        }

        public LintelSelectionVariantV3 SelectedVariant
        {
            get => _selectedVariant;
            set
            {
                if (ReferenceEquals(_selectedVariant, value)) return;
                _selectedVariant = value;
                RaisePropertyChanged(nameof(SelectedVariant));
                RestoreEditorFromCalculation();
            }
        }

        public bool HasEditorVariant => SelectedVariant != null || SelectedGroup?.HasExistingLintel == true;
        public bool CanReverseEditor => EditorRows.Count > 1;
        public string EditorRestoreButtonText => SelectedGroup?.HasExistingLintel == true
            ? "Вернуть тип"
            : "Вернуть расчёт";
        public string EditorTypeName => BuildEditorTypeName();
        public bool EditorTypeExists => !string.IsNullOrWhiteSpace(EditorTypeName)
                                        && _existingCompositeTypeNames.Contains(EditorTypeName);
        public string EditorTypeStatusText => string.IsNullOrWhiteSpace(EditorTypeName)
            ? "Тип не определён"
            : EditorTypeExists ? "Существующий тип" : "Будет создан";
        public string EditorTypeStatusGlyph => EditorTypeExists ? "✓" : "+";
        public int EditorWallWidthMm => SelectedGroup == null ? 0 : (int)Math.Round(SelectedGroup.WallWidthMm);
        public int EditorPackageWidthMm => EditorRows.Sum(row => row.WidthMm + row.GapMm);
        public int EditorSignedWidthDeltaMm => EditorPackageWidthMm - EditorWallWidthMm;
        public int EditorWidthDeltaMm => Math.Abs(EditorSignedWidthDeltaMm);
        public bool EditorWidthIsWithinTolerance => EditorWidthDeltaMm <= WallWidthToleranceMm;
        public string EditorWallWidthText => SelectedGroup == null ? "—" : EditorWallWidthMm + " мм";
        public string EditorPackageWidthText => EditorRows.Count == 0 ? "—" : EditorPackageWidthMm + " мм";
        public string EditorWidthDeltaText => EditorRows.Count == 0
            ? "—"
            : (EditorSignedWidthDeltaMm > 0 ? "+" : string.Empty) + EditorSignedWidthDeltaMm + " мм";

        public string SelectedLeftSupportPad
        {
            get => _selectedLeftSupportPad;
            set
            {
                if (_selectedLeftSupportPad == value) return;
                _selectedLeftSupportPad = value;
                RaisePropertyChanged(nameof(SelectedLeftSupportPad));
            }
        }

        public string SelectedRightSupportPad
        {
            get => _selectedRightSupportPad;
            set
            {
                if (_selectedRightSupportPad == value) return;
                _selectedRightSupportPad = value;
                RaisePropertyChanged(nameof(SelectedRightSupportPad));
            }
        }

        public bool HasSelectedGroup => SelectedGroup != null;
        public bool CanInteract => !IsCollectionInProgress && !IsCalculationInProgress;
        public bool IsVariantsPanelEnabled => CanInteract
                                              && SelectedGroup != null
                                              && !SelectedGroup.HasExistingLintel;
        public bool IsExistingGroupedByOpening
        {
            get => _isExistingGroupedByOpening;
            set
            {
                if (!value || _isExistingGroupedByOpening) return;
                _isExistingGroupedByOpening = true;
                RaisePropertyChanged(nameof(IsExistingGroupedByOpening));
                RaisePropertyChanged(nameof(IsExistingGroupedByLintel));
                RefreshExistingGrouping();
            }
        }
        public bool IsExistingGroupedByLintel
        {
            get => !_isExistingGroupedByOpening;
            set
            {
                if (!value || !_isExistingGroupedByOpening) return;
                _isExistingGroupedByOpening = false;
                RaisePropertyChanged(nameof(IsExistingGroupedByOpening));
                RaisePropertyChanged(nameof(IsExistingGroupedByLintel));
                RefreshExistingGrouping();
            }
        }
        public bool CanRecalculate => CanInteract
                                      && SelectedGroup != null
                                      && !SelectedGroup.HasExistingLintel
                                      && _lintelCatalog.Count > 0;
        public bool CanRecalculateAll => CanInteract
                                         && _allGroups.Any(group => !group.HasExistingLintel)
                                         && _lintelCatalog.Count > 0;
        public bool IsCalculationInProgress
        {
            get => _isCalculationInProgress;
            private set
            {
                if (_isCalculationInProgress == value) return;
                _isCalculationInProgress = value;
                RaisePropertyChanged(nameof(IsCalculationInProgress));
                RaisePropertyChanged(nameof(CanInteract));
                RaisePropertyChanged(nameof(IsVariantsPanelEnabled));
                RaisePropertyChanged(nameof(CanRecalculate));
                RaisePropertyChanged(nameof(CanRecalculateAll));
                RaisePropertyChanged(nameof(CalculationProgressText));
                RaiseActiveProgressProperties();
            }
        }
        public int CalculatedOpeningCount
        {
            get => _calculatedOpeningCount;
            private set
            {
                int normalized = Math.Max(0, Math.Min(CalculationOpeningTotal, value));
                if (_calculatedOpeningCount == normalized) return;
                _calculatedOpeningCount = normalized;
                RaisePropertyChanged(nameof(CalculatedOpeningCount));
                RaisePropertyChanged(nameof(CalculationProgressText));
                RaiseActiveProgressProperties();
            }
        }
        public int CalculationOpeningTotal
        {
            get => _calculationOpeningTotal;
            private set
            {
                int normalized = Math.Max(0, value);
                if (_calculationOpeningTotal == normalized) return;
                _calculationOpeningTotal = normalized;
                RaisePropertyChanged(nameof(CalculationOpeningTotal));
                RaisePropertyChanged(nameof(CalculationProgressMaximum));
                RaisePropertyChanged(nameof(CalculationProgressText));
                RaiseActiveProgressProperties();
            }
        }
        public int CalculationProgressMaximum => Math.Max(1, CalculationOpeningTotal);
        public string CalculationProgressText => CalculatedOpeningCount.ToString(CultureInfo.InvariantCulture)
                                                 + "/"
                                                 + CalculationOpeningTotal.ToString(CultureInfo.InvariantCulture);
        public bool IsCollectionInProgress
        {
            get => _isCollectionInProgress;
            private set
            {
                if (_isCollectionInProgress == value) return;
                _isCollectionInProgress = value;
                RaisePropertyChanged(nameof(IsCollectionInProgress));
                RaisePropertyChanged(nameof(CanInteract));
                RaiseActiveProgressProperties();
            }
        }
        public int ProcessedCollectionOpeningCount
        {
            get => _processedCollectionOpeningCount;
            private set
            {
                int normalized = Math.Max(0, Math.Min(CollectionOpeningTotal, value));
                if (_processedCollectionOpeningCount == normalized) return;
                _processedCollectionOpeningCount = normalized;
                RaisePropertyChanged(nameof(ProcessedCollectionOpeningCount));
                RaisePropertyChanged(nameof(CollectionProgressText));
                RaiseActiveProgressProperties();
            }
        }
        public int CollectionOpeningTotal
        {
            get => _collectionOpeningTotal;
            private set
            {
                int normalized = Math.Max(0, value);
                if (_collectionOpeningTotal == normalized) return;
                _collectionOpeningTotal = normalized;
                RaisePropertyChanged(nameof(CollectionOpeningTotal));
                RaisePropertyChanged(nameof(CollectionProgressMaximum));
                RaisePropertyChanged(nameof(CollectionProgressText));
                RaiseActiveProgressProperties();
            }
        }
        public int CollectionProgressMaximum => Math.Max(1, CollectionOpeningTotal);
        public string CollectionProgressText => ProcessedCollectionOpeningCount.ToString(CultureInfo.InvariantCulture)
                                                + "/"
                                                + CollectionOpeningTotal.ToString(CultureInfo.InvariantCulture);
        public string ActiveProgressTitle => IsCollectionInProgress ? "Сбор проёмов" : "Расчёт вариантов";
        public int ActiveProgressMaximum => IsCollectionInProgress
            ? CollectionProgressMaximum
            : CalculationProgressMaximum;
        public int ActiveProgressValue => IsCollectionInProgress
            ? ProcessedCollectionOpeningCount
            : CalculatedOpeningCount;
        public string ActiveProgressText => IsCollectionInProgress
            ? CollectionProgressText
            : CalculationProgressText;
        public string SelectedOpeningCaption
        {
            get
            {
                if (SelectedGroup == null) return "Проём не выбран";
                long firstId = SelectedGroup.ElementIds.FirstOrDefault()?.Value ?? 0;
                return SelectedGroup.Count > 1
                    ? "Группа · " + SelectedGroup.Count + " экз. · первый ID " + firstId
                    : "Проём ID " + firstId;
            }
        }
        public string SelectedOpeningWidthText => SelectedGroup == null ? "—" : Math.Round(SelectedGroup.OpeningWidthMm) + " мм";
        public string RequiredLengthText => SelectedGroup == null
            ? "—"
            : "≥ " + Math.Ceiling(SelectedGroup.OpeningWidthMm + 2.0 * MinimumBearingMm) + " мм";
        public string SelectedWallWidthText => SelectedGroup == null ? "—" : Math.Round(SelectedGroup.WallWidthMm) + " мм";
        public string SelectedWallTypeText => SelectedGroup?.WallTypeName ?? "—";
        public string SelectedBearingSideText => SelectedGroup == null
            ? "—"
            : SelectedGroup.SupportType >= 2
                ? "Обе стороны"
                : SelectedGroup.SupportType == 1
                    ? SelectedGroup.RequiredSupportWidth1Mm > 0 ? "Сторона 1" : "Сторона 2"
                    : "Не определена";
        public string SelectedBearingZoneText
        {
            get
            {
                if (SelectedGroup == null) return "—";
                if (SelectedGroup.SupportType <= 0) return "Не требуется";
                if (!string.IsNullOrWhiteSpace(SelectedGroup.SupportParameterError))
                    return "Ошибка параметров плит";
                if (SelectedGroup.SupportType >= 2)
                    return "1: " + Math.Ceiling(SelectedGroup.RequiredSupportWidth1Mm)
                           + " мм · 2: " + Math.Ceiling(SelectedGroup.RequiredSupportWidth2Mm) + " мм";

                double zone = SelectedGroup.RequiredSupportWidth1Mm > 0
                    ? SelectedGroup.RequiredSupportWidth1Mm
                    : SelectedGroup.RequiredSupportWidth2Mm;
                return Math.Ceiling(zone) + " мм";
            }
        }
        public string CatalogSummaryText => _catalogLoadError == null
            ? "Каталог: " + _lintelCatalog.Count + " типов"
            : "Ошибка каталога";
        public string VariantsSummaryText => SelectedGroup == null
            ? "Выберите проём"
            : SelectedGroup.HasExistingLintel ? "Перемычка уже существует"
            : Variants.Count == 0 ? "Варианты не найдены" : Variants.Count + " из 5 вариантов";

        public string SelectionMessage
        {
            get => _selectionMessage;
            private set
            {
                if (_selectionMessage == value) return;
                _selectionMessage = value;
                RaisePropertyChanged(nameof(SelectionMessage));
            }
        }

        public bool IsMasonry65
        {
            get => _masonryType == LintelMasonryTypeV3.Brick65;
            set { if (value) SetMasonryType(LintelMasonryTypeV3.Brick65); }
        }

        public bool IsMasonry88
        {
            get => _masonryType == LintelMasonryTypeV3.Brick88;
            set { if (value) SetMasonryType(LintelMasonryTypeV3.Brick88); }
        }

        public bool IsPartition
        {
            get => _masonryType == LintelMasonryTypeV3.Partition;
            set { if (value) SetMasonryType(LintelMasonryTypeV3.Partition); }
        }

        public bool IsReinforcedConcrete
        {
            get => _lintelMaterial == LintelMaterialV3.ReinforcedConcrete;
            set { if (value) SetLintelMaterial(LintelMaterialV3.ReinforcedConcrete); }
        }

        public bool IsMetal
        {
            get => _lintelMaterial == LintelMaterialV3.Metal;
            set { if (value) SetLintelMaterial(LintelMaterialV3.Metal); }
        }

        public int MinimumBearingMm
        {
            get => _minimumBearingMm;
            set
            {
                int normalized = Math.Max(0, Math.Min(1000, value));
                if (_minimumBearingMm == normalized) return;
                _minimumBearingMm = normalized;
                RaisePropertyChanged(nameof(MinimumBearingMm));
                RaisePropertyChanged(nameof(RequiredLengthText));
                RecalculateVariants();
            }
        }

        public int WallWidthToleranceMm
        {
            get => _wallWidthToleranceMm;
            set
            {
                int normalized = Math.Max(0, Math.Min(200, value));
                if (_wallWidthToleranceMm == normalized) return;
                _wallWidthToleranceMm = normalized;
                RaisePropertyChanged(nameof(WallWidthToleranceMm));
                RaiseEditorProperties();
                RecalculateVariants();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                string normalized = value ?? string.Empty;
                if (_searchText == normalized) return;
                _searchText = normalized;
                RaisePropertyChanged(nameof(SearchText));
                RefreshView();
            }
        }

        public OpeningSearchOptionV3 SelectedSearchOption
        {
            get => _selectedSearchOption;
            set
            {
                if (ReferenceEquals(_selectedSearchOption, value) || value == null) return;
                _selectedSearchOption = value;
                RaisePropertyChanged(nameof(SelectedSearchOption));
                RaisePropertyChanged(nameof(SearchHint));
                RefreshView();
            }
        }

        public OpeningStatusV3? StatusFilter
        {
            get => _statusFilter;
            set
            {
                if (_statusFilter == value) return;
                _statusFilter = value;
                RaisePropertyChanged(nameof(StatusFilter));
                RefreshView();
            }
        }

        public string CollectionDurationText
        {
            get => _collectionDurationText;
            private set
            {
                _collectionDurationText = value;
                RaisePropertyChanged(nameof(CollectionDurationText));
            }
        }

        public int TotalOpeningCount
        {
            get => _totalOpeningCount;
            private set
            {
                _totalOpeningCount = value;
                RaisePropertyChanged(nameof(TotalOpeningCount));
                RaisePropertyChanged(nameof(HeaderSummary));
                RaisePropertyChanged(nameof(OpeningsSummary));
            }
        }

        public int SkippedOpeningCount
        {
            get => _skippedOpeningCount;
            private set
            {
                _skippedOpeningCount = value;
                RaisePropertyChanged(nameof(SkippedOpeningCount));
                RaisePropertyChanged(nameof(OpeningsSummary));
            }
        }

        public int GroupCount => _allGroups.Count;
        public int OpeningsWithoutLintelCount => _allGroups.Where(group => !group.HasExistingLintel).Sum(group => group.Count);
        public int OpeningsWithLintelCount => _allGroups.Where(group => group.HasExistingLintel).Sum(group => group.Count);
        public string OpeningsWithoutLintelTabHeader => "Проёмы без перемычек (" + OpeningsWithoutLintelCount + ")";
        public string ExistingLintelsTabHeader => "Существующие перемычки (" + OpeningsWithLintelCount + ")";
        public int SelectedOpeningCount => _allGroups.Where(x => x.IsChecked).Sum(x => x.Count);
        public int ErrorGroupCount => _allGroups.Count(x => x.Status == OpeningStatusV3.Error);
        public string HeaderSummary => "Собрано · " + TotalOpeningCount + " проёмов";
        public string OpeningsSummary => TotalOpeningCount + " проёмов · " + GroupCount + " групп"
            + (OpeningsWithLintelCount > 0 ? " · с перемычками: " + OpeningsWithLintelCount : string.Empty)
            + (SkippedOpeningCount > 0 ? " · пропущено: " + SkippedOpeningCount : string.Empty);
        public string SelectedCountText => SelectedOpeningCount + " из " + TotalOpeningCount;
        public string SortSummary => "Сортировка: " + string.Join(" → ", SortCriteria.Select(x => x.SelectedOption?.Name ?? string.Empty));
        public string SearchHint
        {
            get
            {
                switch (SelectedSearchOption?.Field ?? OpeningSearchFieldV3.All)
                {
                    case OpeningSearchFieldV3.OpeningKind:
                        return "Например: дверь, окно, витраж или сборный проём.";
                    case OpeningSearchFieldV3.SourceType:
                        return "Поиск по скрытым в карточке именам семейства и типа.";
                    case OpeningSearchFieldV3.OpeningWidth:
                        return "Например: 1500; =1500 — точное значение ширины.";
                    case OpeningSearchFieldV3.OpeningHeight:
                        return "Например: 2100; =2100 — точное значение высоты.";
                    case OpeningSearchFieldV3.Support:
                        return "Введите значение опирания: 0, 1 или 2.";
                    case OpeningSearchFieldV3.SupportWidth:
                        return "Например: 160; =160 — точная ширина опорной зоны.";
                    case OpeningSearchFieldV3.WallType:
                        return "Введите часть имени типа стены.";
                    case OpeningSearchFieldV3.WallThickness:
                        return "Например: 380; =380 — точная толщина стены.";
                    case OpeningSearchFieldV3.Category:
                        return "Поиск по категории Revit.";
                    case OpeningSearchFieldV3.Level:
                        return "Введите часть имени уровня.";
                    case OpeningSearchFieldV3.Status:
                        return "Например: ошибка, предупреждение или успешно.";
                    case OpeningSearchFieldV3.Count:
                        return "Количество экземпляров в группе, например: =3.";
                    case OpeningSearchFieldV3.Id:
                        return "Введите полный ID или его часть.";
                    default:
                        return "Поиск по всем полям. Для точного поиска выберите поле слева.";
                }
            }
        }

        public void RecalculateVariants()
        {
            if (SelectedGroup == null)
            {
                Variants.Clear();
                SelectedVariant = null;
                SelectionMessage = "Выберите группу проёмов для расчёта.";
                RaiseVariantsProperties();
                return;
            }
            if (SelectedGroup.HasExistingLintel)
            {
                DisplayExistingLintel(SelectedGroup);
                return;
            }

            OpeningGroupCardV3 calculatedGroup = SelectedGroup;
            if (!string.IsNullOrWhiteSpace(_catalogLoadError))
            {
                StoreCalculation(calculatedGroup, CreateCatalogErrorResult());
            }
            else
            {
                StoreCalculation(calculatedGroup, CalculateGroup(calculatedGroup));
            }

            RefreshView();
            if (VisibleGroups.Contains(calculatedGroup))
                DisplayStoredCalculation(calculatedGroup);
            else
                SelectedGroup = VisibleGroups.FirstOrDefault();
            RaiseSummaryProperties();
        }

        public async Task RecalculateAllVariantsAsync(CancellationToken cancellationToken)
        {
            if (IsCalculationInProgress) return;

            List<OpeningGroupCardV3> groupsToCalculate = _allGroups
                .Where(group => !group.HasExistingLintel)
                .ToList();
            string selectedGroupKey = SelectedGroup?.Key;
            IsCalculationInProgress = true;
            CalculationOpeningTotal = groupsToCalculate.Sum(group => group.Count);
            CalculatedOpeningCount = 0;
            SelectedGroup = null;
            SelectionMessage = "Выполняется расчёт вариантов: " + CalculationProgressText;

            foreach (OpeningGroupCardV3 group in groupsToCalculate)
            {
                group.IsCalculated = false;
                group.Status = OpeningStatusV3.Warning;
                group.StatusText = "Ожидает расчёта";
            }

            try
            {
                await Task.Delay(1, cancellationToken);
                foreach (OpeningGroupCardV3 group in groupsToCalculate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LintelSelectionResultV3 result = string.IsNullOrWhiteSpace(_catalogLoadError)
                        ? await Task.Run(() => CalculateGroup(group), cancellationToken)
                        : CreateCatalogErrorResult();
                    StoreCalculation(group, result);
                    CalculatedOpeningCount += group.Count;
                    SelectionMessage = "Выполняется расчёт вариантов: " + CalculationProgressText;
                    await Task.Delay(1, cancellationToken);
                }

                CalculatedOpeningCount = CalculationOpeningTotal;
                RefreshView();
                OpeningGroupCardV3 selectedGroup = _allGroups.FirstOrDefault(group => group.Key == selectedGroupKey)
                                                   ?? VisibleGroups.FirstOrDefault()
                                                   ?? ExistingLintelGroups.FirstOrDefault();
                SelectedGroup = selectedGroup;
                SelectionMessage = selectedGroup?.CalculationMessage
                                   ?? "Расчёт вариантов завершён.";
            }
            catch (OperationCanceledException)
            {
                SelectionMessage = "Расчёт вариантов прерван: " + CalculationProgressText;
            }
            finally
            {
                IsCalculationInProgress = false;
                RefreshView();
                RaiseSummaryProperties();
            }
        }

        private void SetMasonryType(LintelMasonryTypeV3 value)
        {
            if (_masonryType == value) return;
            _masonryType = value;
            RaisePropertyChanged(nameof(IsMasonry65));
            RaisePropertyChanged(nameof(IsMasonry88));
            RaisePropertyChanged(nameof(IsPartition));
            RecalculateVariants();
        }

        private void SetLintelMaterial(LintelMaterialV3 value)
        {
            if (_lintelMaterial == value) return;
            _lintelMaterial = value;
            RaisePropertyChanged(nameof(IsReinforcedConcrete));
            RaisePropertyChanged(nameof(IsMetal));
            RecalculateVariants();
        }

        private void RaiseSelectedGroupProperties()
        {
            RaisePropertyChanged(nameof(HasSelectedGroup));
            RaisePropertyChanged(nameof(IsVariantsPanelEnabled));
            RaisePropertyChanged(nameof(CanRecalculate));
            RaisePropertyChanged(nameof(CanRecalculateAll));
            RaisePropertyChanged(nameof(SelectedOpeningCaption));
            RaisePropertyChanged(nameof(SelectedOpeningWidthText));
            RaisePropertyChanged(nameof(RequiredLengthText));
            RaisePropertyChanged(nameof(SelectedWallWidthText));
            RaisePropertyChanged(nameof(SelectedWallTypeText));
            RaisePropertyChanged(nameof(SelectedBearingSideText));
            RaisePropertyChanged(nameof(SelectedBearingZoneText));
        }

        private void RaiseVariantsProperties()
        {
            RaisePropertyChanged(nameof(Variants));
            RaisePropertyChanged(nameof(VariantsSummaryText));
            RaisePropertyChanged(nameof(CatalogSummaryText));
        }

        private void RaiseActiveProgressProperties()
        {
            RaisePropertyChanged(nameof(ActiveProgressTitle));
            RaisePropertyChanged(nameof(ActiveProgressMaximum));
            RaisePropertyChanged(nameof(ActiveProgressValue));
            RaisePropertyChanged(nameof(ActiveProgressText));
        }

        private LintelSelectionRequestV3 CreateSelectionRequest(OpeningGroupCardV3 group)
        {
            return new LintelSelectionRequestV3
            {
                OpeningWidthMm = group.OpeningWidthMm,
                WallWidthMm = group.WallWidthMm,
                SupportType = group.SupportType,
                RequiredBearingWidth1Mm = group.RequiredSupportWidth1Mm,
                RequiredBearingWidth2Mm = group.RequiredSupportWidth2Mm,
                ValidationError = group.SupportParameterError,
                MasonryCourseHeightMm = (int)_masonryType,
                Material = _lintelMaterial,
                MinimumBearingMm = MinimumBearingMm,
                WallWidthToleranceMm = WallWidthToleranceMm,
                MaximumVariants = 5
            };
        }

        private LintelSelectionResultV3 CalculateGroup(OpeningGroupCardV3 group)
        {
            return LintelSelectionEngineV3.Calculate(_lintelCatalog, CreateSelectionRequest(group));
        }

        private LintelSelectionResultV3 CreateCatalogErrorResult()
        {
            return new LintelSelectionResultV3 { Message = _catalogLoadError };
        }

        private static void StoreCalculation(OpeningGroupCardV3 group, LintelSelectionResultV3 result)
        {
            group.CalculatedVariants.Clear();
            group.CalculatedVariants.AddRange(result.Variants);
            group.CalculationMessage = result.Message;
            group.IsCalculated = true;
            ApplyCalculationStatus(group, result);
        }

        private void DisplayStoredCalculation(OpeningGroupCardV3 group)
        {
            Variants.Clear();
            foreach (LintelSelectionVariantV3 variant in group.CalculatedVariants)
                Variants.Add(variant);
            SelectedVariant = Variants.FirstOrDefault();
            SelectionMessage = group.CalculationMessage ?? "Варианты не рассчитаны.";
            RaiseVariantsProperties();
        }

        private void DisplayExistingLintel(OpeningGroupCardV3 group)
        {
            Variants.Clear();
            if (SelectedVariant != null)
                SelectedVariant = null;
            else
                RestoreEditorFromCalculation();
            SelectionMessage = group?.CalculationMessage
                               ?? group?.ExistingLintelDescription
                               ?? "Для проёма найдена существующая перемычка.";
            RaiseVariantsProperties();
        }

        public void AddEditorRow()
        {
            LintelCatalogItemV3 item = EditorRows.LastOrDefault()?.SelectedCatalogItem
                                       ?? EditorCatalogItems.FirstOrDefault();
            if (item == null) return;

            var row = new LintelEditorRowV3(item);
            SubscribeEditorRow(row);
            EditorRows.Add(row);
            UpdateEditorRowIndexes();
            RaiseEditorProperties();
        }

        public void RemoveEditorRow(LintelEditorRowV3 row)
        {
            if (row == null || !EditorRows.Contains(row)) return;
            row.PropertyChanged -= EditorRow_PropertyChanged;
            EditorRows.Remove(row);
            UpdateEditorRowIndexes();
            RaiseEditorProperties();
        }

        public void MoveEditorRow(LintelEditorRowV3 row, int direction)
        {
            if (row == null || direction == 0) return;
            int oldIndex = EditorRows.IndexOf(row);
            int newIndex = oldIndex + Math.Sign(direction);
            if (oldIndex < 0 || newIndex < 0 || newIndex >= EditorRows.Count) return;

            EditorRows.Move(oldIndex, newIndex);
            UpdateEditorRowIndexes();
            RaiseEditorProperties();
        }

        public void ReverseEditorRows()
        {
            if (EditorRows.Count < 2) return;
            List<LintelEditorRowV3> reversed = EditorRows.Reverse().ToList();
            EditorRows.Clear();
            foreach (LintelEditorRowV3 row in reversed)
                EditorRows.Add(row);
            UpdateEditorRowIndexes();
            RaiseEditorProperties();
        }

        public void RestoreEditorFromCalculation()
        {
            foreach (LintelEditorRowV3 row in EditorRows)
                row.PropertyChanged -= EditorRow_PropertyChanged;
            EditorRows.Clear();
            RefreshEditorCatalogItems();

            if (SelectedGroup?.HasExistingLintel == true)
            {
                RestoreExistingLintelRows(SelectedGroup);
            }
            else if (SelectedVariant != null)
            {
                int leadingGap = 0;
                LintelEditorRowV3 previousRow = null;
                foreach (LintelLayoutSegmentV3 segment in SelectedVariant.LayoutSegments)
                {
                    if (segment.IsGap)
                    {
                        if (previousRow == null)
                            leadingGap += segment.WidthMm;
                        else
                            previousRow.GapMm += segment.WidthMm;
                        continue;
                    }

                    LintelCatalogItemV3 item = FindCatalogItem(segment.Mark, segment.WidthMm);
                    if (item == null) continue;
                    if (!EditorCatalogItems.Contains(item))
                        EditorCatalogItems.Add(item);

                    var row = new LintelEditorRowV3(item);
                    if (leadingGap > 0)
                    {
                        row.GapMm = leadingGap;
                        leadingGap = 0;
                    }
                    SubscribeEditorRow(row);
                    EditorRows.Add(row);
                    previousRow = row;
                }
            }

            UpdateEditorRowIndexes();
            RaiseEditorProperties();
        }

        private void RestoreExistingLintelRows(OpeningGroupCardV3 group)
        {
            foreach (ExistingLintelComponentV3 component in group.ExistingLintelComponents.OrderBy(item => item.Order))
            {
                LintelCatalogItemV3 item = FindCatalogItem(component.TypeName, 0);
                if (item == null) continue;
                LintelEditorRowV3 row = AddExistingEditorRow(item);
                if (component.OffsetToNextMm > 0)
                {
                    row.GapMm = Math.Max(
                        0,
                        (int)Math.Round(component.OffsetToNextMm - row.WidthMm));
                }
            }

            if (EditorRows.Count == 0)
                RestoreExistingRowsFromTypeName(group.ExistingLintelTypeNames);
        }

        private void RestoreExistingRowsFromTypeName(string typeName)
        {
            string name = (typeName ?? string.Empty).Split('+').FirstOrDefault()?.Trim();
            string[] parts = name?.Split('_');
            if (parts == null || parts.Length < 4) return;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int maximumOpeningWidth))
                maximumOpeningWidth = 0;

            foreach (string token in parts.Skip(3).SelectMany(part => part.Split('-')))
            {
                bool isBearing = token.Any(char.IsLetter);
                string number = new string(token.Where(char.IsDigit).ToArray());
                if (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int widthMm))
                    continue;

                LintelCatalogItemV3 item = EditorCatalogItems
                    .Where(candidate => candidate.WidthMm == widthMm && candidate.IsBearing == isBearing)
                    .OrderBy(candidate => maximumOpeningWidth > 0
                        ? Math.Abs(candidate.MaximumOpeningWidthMm - maximumOpeningWidth)
                        : 0)
                    .ThenBy(candidate => candidate.LengthMm)
                    .FirstOrDefault()
                    ?? _lintelCatalog.FirstOrDefault(candidate =>
                        candidate.WidthMm == widthMm && candidate.IsBearing == isBearing);
                if (item != null)
                    AddExistingEditorRow(item);
            }
        }

        private LintelEditorRowV3 AddExistingEditorRow(LintelCatalogItemV3 item)
        {
            if (!EditorCatalogItems.Contains(item))
                EditorCatalogItems.Add(item);
            var row = new LintelEditorRowV3(item);
            SubscribeEditorRow(row);
            EditorRows.Add(row);
            return row;
        }

        private void RefreshEditorCatalogItems()
        {
            EditorCatalogItems.Clear();
            if (SelectedGroup == null) return;

            LintelSelectionRequestV3 request = CreateSelectionRequest(SelectedGroup);
            int? existingMasonry = GetExistingLintelMasonryCourse(SelectedGroup);
            if (existingMasonry.HasValue)
                request.MasonryCourseHeightMm = existingMasonry.Value;
            string materialCode = request.Material == LintelMaterialV3.Metal
                ? "metal"
                : "reinforcedConcrete";

            IEnumerable<LintelCatalogItemV3> items = _lintelCatalog
                .Where(item => item != null
                               && item.WidthMm > 0
                               && string.Equals(item.Material, materialCode, StringComparison.OrdinalIgnoreCase)
                               && item.MasonryCourseHeightMm == request.MasonryCourseHeightMm
                               && request.OpeningWidthMm + 0.5 >= item.MinimumOpeningWidthMm
                               && (item.MaximumOpeningWidthMm <= 0
                                   || request.OpeningWidthMm <= item.MaximumOpeningWidthMm + 0.5)
                               && item.LengthMm + 0.5 >= LintelSelectionEngineV3.GetRequiredLength(request, item))
                .OrderBy(item => item.Mark, _naturalComparer);

            foreach (LintelCatalogItemV3 item in items)
                EditorCatalogItems.Add(item);
            RaisePropertyChanged(nameof(EditorCatalogItems));
        }

        private static int? GetExistingLintelMasonryCourse(OpeningGroupCardV3 group)
        {
            if (group?.HasExistingLintel != true) return null;
            string typeName = (group.ExistingLintelTypeNames ?? string.Empty).Split('+').FirstOrDefault()?.Trim();
            string firstPart = typeName?.Split('_').FirstOrDefault();
            return int.TryParse(firstPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int masonry)
                ? (int?)masonry
                : null;
        }

        private LintelCatalogItemV3 FindCatalogItem(string mark, int widthMm)
        {
            return EditorCatalogItems.FirstOrDefault(item =>
                       string.Equals(item.Mark, mark, StringComparison.OrdinalIgnoreCase)
                       && item.WidthMm == widthMm)
                   ?? _lintelCatalog.FirstOrDefault(item =>
                       string.Equals(item.Mark, mark, StringComparison.OrdinalIgnoreCase)
                       && item.WidthMm == widthMm)
                   ?? _lintelCatalog.FirstOrDefault(item =>
                       string.Equals(item.Mark, mark, StringComparison.OrdinalIgnoreCase));
        }

        private void SubscribeEditorRow(LintelEditorRowV3 row)
        {
            if (row != null)
                row.PropertyChanged += EditorRow_PropertyChanged;
        }

        private void EditorRow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is LintelEditorRowV3 row
                && !row.IsApplyingCatalogItem
                && (e.PropertyName == nameof(LintelEditorRowV3.LengthMm)
                    || e.PropertyName == nameof(LintelEditorRowV3.WidthMm)
                    || e.PropertyName == nameof(LintelEditorRowV3.Purpose)))
            {
                LintelCatalogItemV3 suggestedItem = FindBestEditorCatalogItem(row, e.PropertyName);
                if (suggestedItem != null)
                    row.ApplyCatalogSuggestion(suggestedItem);
            }
            RaiseEditorProperties();
        }

        private LintelCatalogItemV3 FindBestEditorCatalogItem(LintelEditorRowV3 row, string changedProperty)
        {
            if (row == null || EditorCatalogItems.Count == 0) return null;

            List<LintelCatalogItemV3> candidates = EditorCatalogItems
                .Where(item => item != null && item.IsBearing == row.IsBearing)
                .ToList();
            if (candidates.Count == 0)
                candidates = EditorCatalogItems.Where(item => item != null).ToList();

            IOrderedEnumerable<LintelCatalogItemV3> ordered;
            if (changedProperty == nameof(LintelEditorRowV3.LengthMm))
            {
                ordered = candidates
                    .OrderBy(item => Math.Abs(item.LengthMm - row.LengthMm))
                    .ThenBy(item => Math.Abs(item.WidthMm - row.WidthMm));
            }
            else if (changedProperty == nameof(LintelEditorRowV3.WidthMm))
            {
                ordered = candidates
                    .OrderBy(item => Math.Abs(item.WidthMm - row.WidthMm))
                    .ThenBy(item => Math.Abs(item.LengthMm - row.LengthMm));
            }
            else
            {
                ordered = candidates
                    .OrderBy(item => Math.Abs(item.WidthMm - row.WidthMm))
                    .ThenBy(item => Math.Abs(item.LengthMm - row.LengthMm));
            }

            return ordered
                .ThenBy(item => item.Priority)
                .ThenBy(item => item.Mark, _naturalComparer)
                .FirstOrDefault();
        }

        private void UpdateEditorRowIndexes()
        {
            for (int index = 0; index < EditorRows.Count; index++)
            {
                EditorRows[index].Index = index + 1;
                EditorRows[index].CanMoveUp = index > 0;
                EditorRows[index].CanMoveDown = index < EditorRows.Count - 1;
            }
        }

        private void RaiseEditorProperties()
        {
            RaisePropertyChanged(nameof(EditorRows));
            RaisePropertyChanged(nameof(HasEditorVariant));
            RaisePropertyChanged(nameof(CanReverseEditor));
            RaisePropertyChanged(nameof(EditorRestoreButtonText));
            RaisePropertyChanged(nameof(EditorTypeName));
            RaisePropertyChanged(nameof(EditorTypeExists));
            RaisePropertyChanged(nameof(EditorTypeStatusText));
            RaisePropertyChanged(nameof(EditorTypeStatusGlyph));
            RaisePropertyChanged(nameof(EditorWallWidthMm));
            RaisePropertyChanged(nameof(EditorPackageWidthMm));
            RaisePropertyChanged(nameof(EditorSignedWidthDeltaMm));
            RaisePropertyChanged(nameof(EditorWidthDeltaMm));
            RaisePropertyChanged(nameof(EditorWidthIsWithinTolerance));
            RaisePropertyChanged(nameof(EditorWallWidthText));
            RaisePropertyChanged(nameof(EditorPackageWidthText));
            RaisePropertyChanged(nameof(EditorWidthDeltaText));
        }

        private string BuildEditorTypeName()
        {
            if (SelectedGroup == null || EditorRows.Count == 0) return string.Empty;

            int wallWidth = NormalizeWallWidth(EditorWallWidthMm);
            int maximumOpeningWidth = EditorRows
                .Select(GetEditorRowMaximumOpeningWidth)
                .Where(value => value > 0)
                .DefaultIfEmpty((int)Math.Round(SelectedGroup.OpeningWidthMm))
                .Min();
            string layout = BuildEditorLayoutName();
            if (string.IsNullOrWhiteSpace(layout)) return string.Empty;

            int masonryCourse = GetExistingLintelMasonryCourse(SelectedGroup) ?? (int)_masonryType;
            return masonryCourse.ToString(CultureInfo.InvariantCulture)
                   + "_" + wallWidth.ToString(CultureInfo.InvariantCulture)
                   + "_" + maximumOpeningWidth.ToString(CultureInfo.InvariantCulture)
                   + "_" + layout;
        }

        private int GetEditorRowMaximumOpeningWidth(LintelEditorRowV3 row)
        {
            LintelCatalogItemV3 item = row.SelectedCatalogItem;
            if (item == null) return 0;
            if (item.MaximumOpeningWidthMm > 0)
                return Math.Max(0, item.MaximumOpeningWidthMm + row.LengthMm - item.LengthMm);

            int minimumBearing = Math.Max(0, item.MinimumBearingMm);
            return Math.Max(0, row.LengthMm - 2 * minimumBearing);
        }

        private string BuildEditorLayoutName()
        {
            List<LintelEditorRowV3> rows = EditorRows.ToList();
            List<string> tokens = rows
                .Select(row => (row.IsBearing ? "Н" : string.Empty)
                               + row.WidthMm.ToString(CultureInfo.InvariantCulture))
                .ToList();
            if (tokens.Count == 0) return string.Empty;

            int splitIndex = FindSecondBearingSideStart(rows);
            if (splitIndex <= 0 || splitIndex >= tokens.Count)
                return string.Join("-", tokens);

            return string.Join("-", tokens.Take(splitIndex))
                   + "_" + string.Join("-", tokens.Skip(splitIndex));
        }

        private int FindSecondBearingSideStart(IReadOnlyList<LintelEditorRowV3> rows)
        {
            if (SelectedGroup == null || SelectedGroup.SupportType < 2 || rows.Count < 2)
                return -1;

            int trailingStart = rows.Count;
            while (trailingStart > 0 && rows[trailingStart - 1].IsBearing)
                trailingStart--;
            if (trailingStart > 0
                && trailingStart < rows.Count
                && rows.Take(trailingStart).Any(row => row.IsBearing))
                return trailingStart;

            if (trailingStart != 0) return -1;

            double requiredFirst = Math.Max(0, SelectedGroup.RequiredSupportWidth1Mm);
            int accumulated = 0;
            for (int index = 0; index < rows.Count - 1; index++)
            {
                accumulated += rows[index].WidthMm;
                if (accumulated + 0.5 >= requiredFirst)
                    return index + 1;
            }
            return rows.Count / 2;
        }

        private static int NormalizeWallWidth(int wallWidthMm)
        {
            if (wallWidthMm == 400) return 380;
            if (wallWidthMm == 500) return 510;
            if (wallWidthMm == 600) return 640;
            return wallWidthMm;
        }

        private static HashSet<string> CollectExistingCompositeTypeNames(Document document)
        {
            IEnumerable<FamilySymbol> symbols = new FilteredElementCollector(document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(symbol => string.Equals(
                    symbol.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString(),
                    "Перемычки составные",
                    StringComparison.OrdinalIgnoreCase));

            return new HashSet<string>(
                symbols.Select(symbol => symbol.Name?.Trim()).Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
        }

        private ObservableCollection<string> CollectSupportPadOptions(Document document)
        {
            var result = new ObservableCollection<string> { "<Нет>" };
            IEnumerable<string> names = new FilteredElementCollector(document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(IsSupportPadSymbol)
                .Select(symbol => symbol.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, _naturalComparer);

            foreach (string name in names)
                result.Add(name);
            return result;
        }

        private static bool IsSupportPadSymbol(FamilySymbol symbol)
        {
            string familyName = symbol?.FamilyName ?? string.Empty;
            string typeName = symbol?.Name ?? string.Empty;
            return familyName.IndexOf("опорн", StringComparison.OrdinalIgnoreCase) >= 0
                   || typeName.IndexOf("опорн", StringComparison.OrdinalIgnoreCase) >= 0
                   || familyName.StartsWith("ОП", StringComparison.OrdinalIgnoreCase)
                   || typeName.StartsWith("ОП", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyRevitFamilyNames(
            Document document,
            IEnumerable<LintelCatalogItemV3> catalog)
        {
            Dictionary<string, List<FamilySymbol>> symbolsByTypeName = new FilteredElementCollector(document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Name))
                .GroupBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (LintelCatalogItemV3 item in catalog.Where(item => item != null))
            {
                if (!symbolsByTypeName.TryGetValue(item.Mark ?? string.Empty, out List<FamilySymbol> matches))
                    continue;

                string masonry = item.MasonryCourseHeightMm.ToString(CultureInfo.InvariantCulture);
                FamilySymbol bestMatch = matches
                    .OrderByDescending(symbol => GetUnitFamilyMatchScore(symbol.FamilyName, item.TypeCode, masonry))
                    .ThenBy(symbol => symbol.FamilyName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                item.RevitFamilyName = bestMatch?.FamilyName;
            }
        }

        private static int GetUnitFamilyMatchScore(string familyName, string typeCode, string masonry)
        {
            string name = familyName ?? string.Empty;
            int score = 0;
            if (!string.IsNullOrWhiteSpace(typeCode)
                && name.IndexOf(typeCode, StringComparison.OrdinalIgnoreCase) >= 0)
                score += 4;
            if (!string.IsNullOrWhiteSpace(masonry)
                && name.IndexOf(masonry, StringComparison.OrdinalIgnoreCase) >= 0)
                score += 3;
            if (name.IndexOf("ЖБ", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 2;
            if (name.IndexOf("перемыч", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 1;
            return score;
        }

        private static void ApplyCalculationStatus(OpeningGroupCardV3 group, LintelSelectionResultV3 result)
        {
            if (result.Variants.Count == 0)
            {
                group.Status = OpeningStatusV3.Error;
                group.StatusText = "Варианты не найдены";
            }
            else if (result.Variants[0].IsExact)
            {
                group.Status = OpeningStatusV3.Success;
                group.StatusText = "Подобрано " + result.Variants.Count + " вар.";
            }
            else
            {
                group.Status = OpeningStatusV3.Warning;
                group.StatusText = "Отклонение " + result.Variants[0].WidthDeltaMm + " мм";
            }
        }

        public void Reload(Action<int, int> progress = null)
        {
            var checkedKeys = new HashSet<string>(_allGroups.Where(x => x.IsChecked).Select(x => x.Key));
            Stopwatch stopwatch = Stopwatch.StartNew();
            IsCollectionInProgress = true;
            CollectionOpeningTotal = 0;
            ProcessedCollectionOpeningCount = 0;
            OpeningCollectionResultV3 collected;
            try
            {
                collected = OpeningCollectorV3.Collect(
                    _document,
                    _initialSelectionIds,
                    (processed, total) =>
                    {
                        CollectionOpeningTotal = total;
                        ProcessedCollectionOpeningCount = processed;
                        progress?.Invoke(processed, total);
                    });
                _allGroups = BuildGroups(collected.Openings);
                _existingLintelTypeGroups = BuildExistingLintelTypeGroups(_allGroups.Where(group => group.HasExistingLintel));

                foreach (OpeningGroupCardV3 group in _allGroups)
                {
                    group.IsChecked = checkedKeys.Contains(group.Key);
                    group.PropertyChanged += Group_PropertyChanged;
                    if (group.HasExistingLintel)
                    {
                        group.IsCalculated = false;
                        group.Status = OpeningStatusV3.Success;
                        group.StatusText = group.ExistingLintelDescription;
                        group.CalculationMessage = group.ExistingLintelDescription + ". " + group.ExistingLintelIdsText + ".";
                    }
                    else
                    {
                        group.IsCalculated = false;
                        group.Status = OpeningStatusV3.Warning;
                        group.StatusText = "Ожидает расчёта";
                    }
                }

                TotalOpeningCount = collected.Openings.Count;
                SkippedOpeningCount = collected.SkippedCount;
                CalculationOpeningTotal = OpeningsWithoutLintelCount;
                CalculatedOpeningCount = 0;
                SelectedGroup = null;
                RefreshView();
                stopwatch.Stop();
                CollectionDurationText = "Сбор и группировка: " + stopwatch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " с";
                SelectionMessage = "Ожидание расчёта вариантов: " + CalculationProgressText;
                RaiseSummaryProperties();
            }
            finally
            {
                IsCollectionInProgress = false;
            }
        }

        public void SetAllChecked(bool isChecked)
        {
            foreach (OpeningGroupCardV3 group in VisibleGroups)
                group.IsChecked = isChecked;
            RaiseSummaryProperties();
        }

        public void AddSortCriterion()
        {
            var usedFields = new HashSet<OpeningSortFieldV3>(SortCriteria
                .Where(x => x.SelectedOption != null)
                .Select(x => x.SelectedOption.Field));
            OpeningSortOptionV3 next = SortOptions.FirstOrDefault(x => !usedFields.Contains(x.Field));
            if (next == null) return;

            OpeningSortCriterionV3 criterion = CreateCriterion(SortCriteria.Count + 1, next.Field, false);
            criterion.PropertyChanged += SortCriterion_PropertyChanged;
            SortCriteria.Add(criterion);
            RaisePropertyChanged(nameof(SortCriteria));
            RaisePropertyChanged(nameof(SortSummary));
            RefreshView();
        }

        public void RemoveSortCriterion(OpeningSortCriterionV3 criterion)
        {
            if (criterion == null || criterion.IsLocked) return;
            criterion.PropertyChanged -= SortCriterion_PropertyChanged;
            SortCriteria.Remove(criterion);
            for (int index = 0; index < SortCriteria.Count; index++)
                SortCriteria[index].Order = index + 1;
            RaisePropertyChanged(nameof(SortCriteria));
            RaisePropertyChanged(nameof(SortSummary));
            RefreshView();
        }

        public void RefreshView()
        {
            List<OpeningGroupCardV3> withoutLintels = ApplyViewFilters(
                _allGroups.Where(group => !group.HasExistingLintel));
            IEnumerable<OpeningGroupCardV3> existingSource = IsExistingGroupedByOpening
                ? _allGroups.Where(group => group.HasExistingLintel)
                : _existingLintelTypeGroups;
            List<OpeningGroupCardV3> existingLintels = ApplyViewFilters(existingSource);

            VisibleGroups.Clear();
            ExistingLintelGroups.Clear();
            foreach (OpeningGroupCardV3 group in withoutLintels)
                VisibleGroups.Add(group);
            foreach (OpeningGroupCardV3 group in existingLintels)
                ExistingLintelGroups.Add(group);

            RaisePropertyChanged(nameof(VisibleGroups));
            RaisePropertyChanged(nameof(ExistingLintelGroups));
        }

        private List<OpeningGroupCardV3> ApplyViewFilters(IEnumerable<OpeningGroupCardV3> source)
        {
            IEnumerable<OpeningGroupCardV3> query = source;
            if (StatusFilter.HasValue)
                query = query.Where(group => group.Status == StatusFilter.Value);

            string search = (SearchText ?? string.Empty).Trim();
            if (search.Length > 0)
                query = query.Where(group => MatchesSearch(
                    group,
                    search,
                    SelectedSearchOption?.Field ?? OpeningSearchFieldV3.All));

            List<OpeningGroupCardV3> result = query.ToList();
            result.Sort(CompareGroups);
            return result;
        }

        private void RefreshExistingGrouping()
        {
            OpeningGroupCardV3 previousSelection = SelectedGroup;
            string lintelTypes = previousSelection?.ExistingLintelTypeNames;
            RefreshView();
            if (previousSelection?.HasExistingLintel != true) return;

            OpeningGroupCardV3 target = ExistingLintelGroups.FirstOrDefault(group =>
                string.Equals(
                    group.ExistingLintelTypeNames,
                    lintelTypes,
                    StringComparison.OrdinalIgnoreCase));
            SelectedGroup = target ?? ExistingLintelGroups.FirstOrDefault();
        }

        public void NotifySelectionChanged()
        {
            RaiseSummaryProperties();
        }

        private OpeningSortCriterionV3 CreateCriterion(int order, OpeningSortFieldV3 field, bool isLocked)
        {
            return new OpeningSortCriterionV3
            {
                Order = order,
                SelectedOption = SortOptions.First(x => x.Field == field),
                IsLocked = isLocked
            };
        }

        private void SortCriterion_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(SortSummary));
            RefreshView();
        }

        private void Group_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OpeningGroupCardV3.IsChecked))
                RaiseSummaryProperties();
        }

        private void RaiseSummaryProperties()
        {
            RaisePropertyChanged(nameof(GroupCount));
            RaisePropertyChanged(nameof(OpeningsWithoutLintelCount));
            RaisePropertyChanged(nameof(OpeningsWithLintelCount));
            RaisePropertyChanged(nameof(OpeningsWithoutLintelTabHeader));
            RaisePropertyChanged(nameof(ExistingLintelsTabHeader));
            RaisePropertyChanged(nameof(SelectedOpeningCount));
            RaisePropertyChanged(nameof(ErrorGroupCount));
            RaisePropertyChanged(nameof(HeaderSummary));
            RaisePropertyChanged(nameof(OpeningsSummary));
            RaisePropertyChanged(nameof(SelectedCountText));
            RaisePropertyChanged(nameof(CanRecalculateAll));
        }

        private int CompareGroups(OpeningGroupCardV3 left, OpeningGroupCardV3 right)
        {
            foreach (OpeningSortCriterionV3 criterion in SortCriteria)
            {
                OpeningSortFieldV3 field = criterion.SelectedOption?.Field ?? OpeningSortFieldV3.None;
                if (field == OpeningSortFieldV3.None) continue;
                int result = CompareByField(left, right, field);
                if (result == 0) continue;
                return criterion.IsDescending ? -result : result;
            }

            int fallback = _naturalComparer.Compare(left.OpeningKind, right.OpeningKind);
            if (fallback != 0) return fallback;
            fallback = _naturalComparer.Compare(left.WallTypeName, right.WallTypeName);
            return fallback != 0 ? fallback : left.OpeningWidthMm.CompareTo(right.OpeningWidthMm);
        }

        private int CompareByField(OpeningGroupCardV3 left, OpeningGroupCardV3 right, OpeningSortFieldV3 field)
        {
            switch (field)
            {
                case OpeningSortFieldV3.OpeningType:
                    int kindResult = _naturalComparer.Compare(left.OpeningKind, right.OpeningKind);
                    return kindResult != 0 ? kindResult : _naturalComparer.Compare(left.SourceTypeText, right.SourceTypeText);
                case OpeningSortFieldV3.Status:
                    return left.Status.CompareTo(right.Status);
                case OpeningSortFieldV3.Support:
                    return left.SupportType.CompareTo(right.SupportType);
                case OpeningSortFieldV3.OpeningWidth:
                    return left.OpeningWidthMm.CompareTo(right.OpeningWidthMm);
                case OpeningSortFieldV3.WallType:
                    return _naturalComparer.Compare(left.WallTypeName, right.WallTypeName);
                case OpeningSortFieldV3.WallThickness:
                    return left.WallWidthMm.CompareTo(right.WallWidthMm);
                case OpeningSortFieldV3.Category:
                    return _naturalComparer.Compare(left.CategoryName, right.CategoryName);
                case OpeningSortFieldV3.Level:
                    return _naturalComparer.Compare(left.LevelName, right.LevelName);
                case OpeningSortFieldV3.Count:
                    return left.Count.CompareTo(right.Count);
                default:
                    return 0;
            }
        }

        private static bool Contains(string source, string value)
        {
            return (source ?? string.Empty).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MatchesSearch(OpeningGroupCardV3 group, string search, OpeningSearchFieldV3 field)
        {
            switch (field)
            {
                case OpeningSearchFieldV3.OpeningKind:
                    return Contains(group.OpeningKind, search);
                case OpeningSearchFieldV3.SourceType:
                    return Contains(group.SourceTypeText, search)
                           || Contains(group.ExistingLintelSearchText, search);
                case OpeningSearchFieldV3.OpeningWidth:
                    return MatchesNumber(group.OpeningWidthMm, search);
                case OpeningSearchFieldV3.OpeningHeight:
                    return MatchesNumber(group.OpeningHeightMm, search);
                case OpeningSearchFieldV3.Support:
                    return MatchesNumber(group.SupportType, search);
                case OpeningSearchFieldV3.SupportWidth:
                    return MatchesNumber(group.RequiredSupportWidth1Mm, search)
                           || MatchesNumber(group.RequiredSupportWidth2Mm, search);
                case OpeningSearchFieldV3.WallType:
                    return Contains(group.WallTypeName, search);
                case OpeningSearchFieldV3.WallThickness:
                    return MatchesNumber(group.WallWidthMm, search);
                case OpeningSearchFieldV3.Category:
                    return Contains(group.CategoryName, search);
                case OpeningSearchFieldV3.Level:
                    return Contains(group.LevelName, search);
                case OpeningSearchFieldV3.Status:
                    return Contains(GetStatusSearchText(group), search);
                case OpeningSearchFieldV3.Count:
                    return MatchesNumber(group.Count, search);
                case OpeningSearchFieldV3.Id:
                    return Contains(group.IdsText, search)
                           || Contains(group.ExistingLintelIdsText, search);
                default:
                    return Contains(group.OpeningKind, search)
                           || Contains(group.SourceTypeText, search)
                           || Contains(group.ExistingLintelSearchText, search)
                           || MatchesNumber(group.OpeningWidthMm, search)
                           || MatchesNumber(group.OpeningHeightMm, search)
                           || MatchesNumber(group.SupportType, search)
                           || MatchesNumber(group.RequiredSupportWidth1Mm, search)
                           || MatchesNumber(group.RequiredSupportWidth2Mm, search)
                           || Contains(group.WallTypeName, search)
                           || MatchesNumber(group.WallWidthMm, search)
                           || Contains(group.CategoryName, search)
                           || Contains(group.LevelName, search)
                           || Contains(GetStatusSearchText(group), search)
                           || MatchesNumber(group.Count, search)
                           || Contains(group.IdsText, search)
                           || Contains(group.ExistingLintelIdsText, search);
            }
        }

        private static string GetStatusSearchText(OpeningGroupCardV3 group)
        {
            string statusName = group.Status == OpeningStatusV3.Success
                ? "Успешно"
                : group.Status == OpeningStatusV3.Warning ? "Предупреждение" : "Ошибка";
            return statusName + " " + group.StatusText;
        }

        private static bool MatchesNumber(double value, string search)
        {
            string normalized = (search ?? string.Empty).Trim();
            bool exact = normalized.StartsWith("=", StringComparison.Ordinal);
            if (exact) normalized = normalized.Substring(1).Trim();
            if (TryParseSearchNumber(normalized, out double parsed))
            {
                if (exact) return Math.Abs(value - parsed) < 0.5;
            }
            else if (exact)
            {
                return false;
            }

            string rounded = Math.Round(value).ToString(CultureInfo.InvariantCulture);
            string precise = value.ToString("0.##", CultureInfo.InvariantCulture);
            return Contains(rounded, normalized) || Contains(precise, normalized.Replace(',', '.'));
        }

        private static bool TryParseSearchNumber(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value)
                   || double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
                   || double.TryParse((text ?? string.Empty).Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static List<OpeningGroupCardV3> BuildGroups(IEnumerable<OpeningRecordV3> openings)
        {
            var groups = new List<OpeningGroupCardV3>();
            foreach (IGrouping<string, OpeningRecordV3> sourceGroup in openings.GroupBy(BuildGroupKey))
            {
                OpeningRecordV3 first = sourceGroup.First();
                var card = new OpeningGroupCardV3
                {
                    Key = sourceGroup.Key,
                    FamilyName = first.FamilyName,
                    TypeName = first.TypeName,
                    OpeningKind = first.OpeningKind,
                    CategoryName = first.CategoryName,
                    WallTypeName = first.WallTypeName,
                    LevelName = first.LevelName,
                    OpeningWidthMm = first.OpeningWidthMm,
                    OpeningHeightMm = first.OpeningHeightMm,
                    WallWidthMm = first.WallWidthMm,
                    SupportType = first.SupportType,
                    RequiredSupportWidthMm = first.RequiredSupportWidthMm,
                    RequiredSupportWidth1Mm = first.RequiredSupportWidth1Mm,
                    RequiredSupportWidth2Mm = first.RequiredSupportWidth2Mm,
                    SupportParameterError = string.Join(" ", sourceGroup
                        .Select(x => x.SupportParameterError)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)),
                    HasExistingLintel = sourceGroup.Any(x => x.HasExistingLintel),
                    ExistingLintelFamilyNames = JoinDistinctGroupValues(sourceGroup.Select(x => x.ExistingLintelFamilyNames)),
                    ExistingLintelTypeNames = JoinDistinctGroupValues(sourceGroup.Select(x => x.ExistingLintelTypeNames)),
                    InstanceCount = sourceGroup.Count(),
                    Status = OpeningStatusV3.Error,
                    StatusText = "Подбор перемычки не выполнен"
                };
                card.ElementIds.AddRange(sourceGroup.SelectMany(x => x.ElementIds).GroupBy(x => x.Value).Select(x => x.First()));
                card.ExistingLintelIds.AddRange(sourceGroup
                    .SelectMany(x => x.ExistingLintelIds)
                    .GroupBy(x => x.Value)
                    .Select(x => x.First()));
                card.ExistingLintelComponents.AddRange(first.ExistingLintelComponents
                    .OrderBy(component => component.Order)
                    .Select(component => new ExistingLintelComponentV3
                    {
                        FamilyName = component.FamilyName,
                        TypeName = component.TypeName,
                        Order = component.Order,
                        OffsetToNextMm = component.OffsetToNextMm
                    }));
                groups.Add(card);
            }
            return groups;
        }

        private static List<OpeningGroupCardV3> BuildExistingLintelTypeGroups(
            IEnumerable<OpeningGroupCardV3> openingGroups)
        {
            var result = new List<OpeningGroupCardV3>();
            IEnumerable<IGrouping<string, OpeningGroupCardV3>> typeGroups = openingGroups.GroupBy(group =>
                (group.ExistingLintelFamilyNames ?? string.Empty)
                + "\u001f" + (group.ExistingLintelTypeNames ?? string.Empty));
            foreach (IGrouping<string, OpeningGroupCardV3> sourceGroup in typeGroups)
            {
                OpeningGroupCardV3 first = sourceGroup.First();
                var card = new OpeningGroupCardV3
                {
                    Key = "lintel\u001f" + sourceGroup.Key,
                    FamilyName = JoinDistinctGroupValues(sourceGroup.Select(group => group.FamilyName)),
                    TypeName = JoinDistinctGroupValues(sourceGroup.Select(group => group.TypeName)),
                    OpeningKind = "Проёмы",
                    CategoryName = JoinDistinctGroupValues(sourceGroup.Select(group => group.CategoryName)),
                    WallTypeName = JoinDistinctGroupValues(sourceGroup.Select(group => group.WallTypeName)),
                    LevelName = JoinDistinctGroupValues(sourceGroup.Select(group => group.LevelName)),
                    OpeningWidthMm = first.OpeningWidthMm,
                    OpeningHeightMm = first.OpeningHeightMm,
                    WallWidthMm = first.WallWidthMm,
                    SupportType = first.SupportType,
                    RequiredSupportWidthMm = first.RequiredSupportWidthMm,
                    RequiredSupportWidth1Mm = first.RequiredSupportWidth1Mm,
                    RequiredSupportWidth2Mm = first.RequiredSupportWidth2Mm,
                    InstanceCount = sourceGroup.Sum(group => group.Count),
                    HasExistingLintel = true,
                    IsExistingLintelAggregate = true,
                    ExistingLintelFamilyNames = first.ExistingLintelFamilyNames,
                    ExistingLintelTypeNames = first.ExistingLintelTypeNames,
                    Status = OpeningStatusV3.Success,
                    StatusText = first.ExistingLintelDescription,
                    CalculationMessage = first.ExistingLintelDescription
                };
                card.ElementIds.AddRange(sourceGroup
                    .SelectMany(group => group.ElementIds)
                    .GroupBy(id => id.Value)
                    .Select(group => group.First()));
                card.ExistingLintelIds.AddRange(sourceGroup
                    .SelectMany(group => group.ExistingLintelIds)
                    .GroupBy(id => id.Value)
                    .Select(group => group.First()));
                card.ExistingLintelComponents.AddRange(first.ExistingLintelComponents
                    .Select(component => new ExistingLintelComponentV3
                    {
                        FamilyName = component.FamilyName,
                        TypeName = component.TypeName,
                        Order = component.Order,
                        OffsetToNextMm = component.OffsetToNextMm
                    }));
                result.Add(card);
            }
            return result;
        }

        private static string JoinDistinctGroupValues(IEnumerable<string> values)
        {
            return string.Join(" + ", values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }

        private static string BuildGroupKey(OpeningRecordV3 opening)
        {
            return string.Join("\u001f", new[]
            {
                opening.CategoryName ?? string.Empty,
                opening.OpeningKind ?? string.Empty,
                opening.FamilyName ?? string.Empty,
                opening.TypeName ?? string.Empty,
                opening.WallTypeName ?? string.Empty,
                Math.Round(opening.OpeningWidthMm).ToString(CultureInfo.InvariantCulture),
                Math.Round(opening.WallWidthMm).ToString(CultureInfo.InvariantCulture),
                opening.SupportType.ToString(CultureInfo.InvariantCulture),
                Math.Round(opening.RequiredSupportWidth1Mm).ToString(CultureInfo.InvariantCulture),
                Math.Round(opening.RequiredSupportWidth2Mm).ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(opening.SupportParameterError) ? "0" : "1",
                opening.HasExistingLintel ? "1" : "0",
                opening.ExistingLintelFamilyNames ?? string.Empty,
                opening.ExistingLintelTypeNames ?? string.Empty
            });
        }
    }

    internal sealed class OpeningCollectionResultV3
    {
        public List<OpeningRecordV3> Openings { get; } = new List<OpeningRecordV3>();
        public int SkippedCount { get; set; }
    }

    internal sealed class SupportBoxV3
    {
        public ElementId ElementId { get; set; }
        public BoundingBoxXYZ Box { get; set; }
        public double BearingZoneMm { get; set; }
        public string ParameterError { get; set; }
    }

    internal sealed class ExistingLintelBoxV3
    {
        public FamilyInstance Instance { get; set; }
        public BoundingBoxXYZ Box { get; set; }
    }

    internal static class OpeningCollectorV3
    {
        private const double MillimetersPerFoot = 304.8;
        private static readonly string[] IgnoredWallTokens = { "_пгп_", "_гкл_", "_фсд_", "_прг_" };

        public static OpeningCollectionResultV3 Collect(
            Document document,
            ICollection<ElementId> selectedOpeningIds,
            Action<int, int> progress = null)
        {
            List<Element> candidates = GetCandidates(document, selectedOpeningIds);
            var result = new OpeningCollectionResultV3();
            progress?.Invoke(0, candidates.Count);
            if (candidates.Count == 0) return result;

            var boxes = candidates
                .Select(element => new { Element = element, Box = element.get_BoundingBox(null) })
                .Where(x => x.Box != null)
                .GroupBy(x => x.Element.Id.Value)
                .ToDictionary(x => x.Key, x => x.First().Box);

            Dictionary<long, Wall> curtainHosts = BuildCurtainHostIndex(document, candidates.OfType<Wall>());
            List<SupportBoxV3> supports = CollectSupportBoxes(document, boxes.Values);

            List<Element> uniqueCandidates = candidates
                .GroupBy(x => x.Id.Value)
                .Select(x => x.First())
                .ToList();
            int processedCount = 0;
            progress?.Invoke(0, uniqueCandidates.Count);
            foreach (Element opening in uniqueCandidates)
            {
                try
                {
                    if (!boxes.TryGetValue(opening.Id.Value, out BoundingBoxXYZ box))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    Wall hostWall = FindHostWall(document, opening, box, curtainHosts);
                    if (!IsUsableHostWall(hostWall))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    XYZ location = GetLocation(opening, hostWall, box);
                    double width = GetOpeningWidthMm(opening, box);
                    if (width <= 0)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    DetectSupport(hostWall, location, box.Max.Z, width, supports,
                        out int supportType,
                        out XYZ supportDirection,
                        out double supportWidth,
                        out double supportWidth1,
                        out double supportWidth2,
                        out string supportParameterError);

                    ElementId levelId = opening.LevelId != null && opening.LevelId != ElementId.InvalidElementId
                        ? opening.LevelId
                        : hostWall.LevelId;
                    string categoryName = opening.Category?.Name ?? "Проёмы";

                    var record = new OpeningRecordV3
                    {
                        OpeningId = opening.Id,
                        WallId = hostWall.Id,
                        WallTypeId = hostWall.GetTypeId(),
                        LevelId = levelId,
                        FamilyName = opening is FamilyInstance familyInstance ? familyInstance.Symbol.FamilyName : "Витраж",
                        TypeName = opening is FamilyInstance typedInstance ? typedInstance.Symbol.Name : opening.Name,
                        OpeningKind = GetOpeningKind(opening),
                        CategoryName = categoryName,
                        WallTypeName = hostWall.WallType.Name,
                        LevelName = document.GetElement(levelId)?.Name ?? "Без уровня",
                        OpeningWidthMm = width,
                        OpeningHeightMm = GetOpeningHeightMm(opening, box),
                        WallWidthMm = hostWall.Width * MillimetersPerFoot,
                        Location = location,
                        TopElevation = box.Max.Z,
                        WallOrientation = opening is FamilyInstance orientedInstance ? orientedInstance.FacingOrientation : hostWall.Orientation,
                        WidthDirection = GetWidthDirection(opening, hostWall),
                        SupportDirection = supportDirection,
                        SupportType = supportType,
                        RequiredSupportWidthMm = supportWidth,
                        RequiredSupportWidth1Mm = supportWidth1,
                        RequiredSupportWidth2Mm = supportWidth2,
                        SupportParameterError = supportParameterError,
                        BoundingMinimum = box.Min,
                        BoundingMaximum = box.Max,
                        ComponentCount = 1
                    };
                    record.ElementIds.Add(opening.Id);
                    result.Openings.Add(record);
                }
                finally
                {
                    processedCount++;
                    progress?.Invoke(processedCount, uniqueCandidates.Count);
                }
            }

            List<OpeningRecordV3> merged = MergeNearbyOpenings(result.Openings);
            List<ExistingLintelBoxV3> existingLintels = CollectExistingLintels(document);
            foreach (OpeningRecordV3 opening in merged)
                DetectExistingLintels(opening, existingLintels);
            result.Openings.Clear();
            result.Openings.AddRange(merged);
            return result;
        }

        private static List<ExistingLintelBoxV3> CollectExistingLintels(Document document)
        {
            return new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(IsExistingCompositeLintel)
                .Select(instance => new ExistingLintelBoxV3
                {
                    Instance = instance,
                    Box = instance.get_BoundingBox(null)
                })
                .Where(item => item.Box != null)
                .ToList();
        }

        private static bool IsExistingCompositeLintel(FamilyInstance instance)
        {
            if (instance?.SuperComponent != null) return false;
            FamilySymbol symbol = instance?.Symbol;
            if (symbol == null) return false;
            string familyName = symbol.FamilyName ?? string.Empty;
            string model = symbol.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString() ?? string.Empty;
            bool isCompositeLintel = familyName.IndexOf("_Перемычки", StringComparison.OrdinalIgnoreCase) >= 0
                                     || string.Equals(model, "Перемычки составные", StringComparison.OrdinalIgnoreCase);
            if (!isCompositeLintel) return false;

            return string.Equals(
                instance.LookupParameter("ADSK_Группирование")?.AsString(),
                "ПР",
                StringComparison.OrdinalIgnoreCase);
        }

        private static void DetectExistingLintels(
            OpeningRecordV3 opening,
            IEnumerable<ExistingLintelBoxV3> lintels)
        {
            if (opening?.BoundingMinimum == null || opening.BoundingMaximum == null) return;

            XYZ center = (opening.BoundingMinimum + opening.BoundingMaximum) / 2.0;
            double horizontalExpansionMm = Math.Max(100.0, opening.WallWidthMm / 2.0 + 50.0);
            double horizontalExpansion = horizontalExpansionMm / MillimetersPerFoot;
            double searchMinimumZ = opening.TopElevation - 50.0 / MillimetersPerFoot;
            double searchMaximumZ = opening.TopElevation + 100.0 / MillimetersPerFoot;
            var searchBox = new BoundingBoxXYZ
            {
                Min = new XYZ(
                    center.X - horizontalExpansion,
                    center.Y - horizontalExpansion,
                    searchMinimumZ),
                Max = new XYZ(
                    center.X + horizontalExpansion,
                    center.Y + horizontalExpansion,
                    searchMaximumZ)
            };

            List<FamilyInstance> matches = lintels
                .Where(item => BoundingBoxesIntersect(searchBox, item.Box))
                .Select(item => item.Instance)
                .GroupBy(instance => instance.Id.Value)
                .Select(group => group.First())
                .ToList();
            if (matches.Count == 0) return;

            opening.HasExistingLintel = true;
            opening.ExistingLintelFamilyNames = JoinDistinct(matches.Select(instance => instance.Symbol?.FamilyName));
            opening.ExistingLintelTypeNames = JoinDistinct(matches.Select(instance => instance.Symbol?.Name));
            opening.ExistingLintelIds.AddRange(matches.Select(instance => instance.Id));

            FamilyInstance representative = matches
                .OrderBy(instance => instance.Id.Value)
                .First();
            XYZ origin = (representative.Location as LocationPoint)?.Point;
            if (origin == null)
            {
                BoundingBoxXYZ representativeBox = representative.get_BoundingBox(null);
                if (representativeBox != null)
                    origin = (representativeBox.Min + representativeBox.Max) / 2.0;
            }
            XYZ orderDirection = NormalizeInPlan(representative.FacingOrientation)
                                 ?? NormalizeInPlan(opening.WallOrientation)
                                 ?? XYZ.BasisX;
            int fallbackOrder = 0;
            var components = new List<ExistingLintelComponentV3>();
            foreach (ElementId componentId in representative.GetSubComponentIds())
            {
                FamilyInstance component = representative.Document.GetElement(componentId) as FamilyInstance;
                if (component?.Symbol == null) continue;
                XYZ componentPoint = (component.Location as LocationPoint)?.Point;
                double order = componentPoint != null && origin != null
                    ? (componentPoint - origin).DotProduct(orderDirection)
                    : fallbackOrder;
                components.Add(new ExistingLintelComponentV3
                {
                    FamilyName = component.Symbol.FamilyName,
                    TypeName = component.Symbol.Name,
                    Order = order
                });
                fallbackOrder++;
            }

            List<ExistingLintelComponentV3> orderedComponents = components
                .OrderBy(component => component.Order)
                .ToList();
            int visibleComponentCount = GetVisibleLintelComponentCount(
                representative.Symbol,
                orderedComponents.Count);
            orderedComponents = orderedComponents
                .Take(visibleComponentCount)
                .ToList();
            for (int index = 0; index < orderedComponents.Count; index++)
            {
                ExistingLintelComponentV3 component = orderedComponents[index];
                component.Order = index;
                if (index < orderedComponents.Count - 1)
                {
                    component.OffsetToNextMm = GetTypeLengthParameterMm(
                        representative.Symbol,
                        "Отступ от " + (index + 1).ToString(CultureInfo.InvariantCulture)
                        + " до " + (index + 2).ToString(CultureInfo.InvariantCulture));
                }
                opening.ExistingLintelComponents.Add(component);
            }
        }

        private static int GetVisibleLintelComponentCount(FamilySymbol symbol, int availableComponentCount)
        {
            if (availableComponentCount <= 0 || symbol == null) return 0;

            int visibleCount = 1;
            bool hasVisibilityParameters = false;
            int maximumSlot = Math.Max(availableComponentCount, 16);
            for (int slot = 2; slot <= maximumSlot; slot++)
            {
                Parameter parameter = symbol.LookupParameter(
                    slot.ToString(CultureInfo.InvariantCulture) + "ПР.Видимость");
                if (parameter == null) continue;

                hasVisibilityParameters = true;
                if (IsTypeParameterEnabled(parameter))
                    visibleCount++;
            }

            return hasVisibilityParameters
                ? Math.Min(availableComponentCount, visibleCount)
                : availableComponentCount;
        }

        private static bool IsTypeParameterEnabled(Parameter parameter)
        {
            if (parameter == null) return false;
            switch (parameter.StorageType)
            {
                case StorageType.Integer:
                    return parameter.AsInteger() != 0;
                case StorageType.Double:
                    return Math.Abs(parameter.AsDouble()) > 1e-9;
                case StorageType.String:
                    string text = (parameter.AsString() ?? string.Empty).Trim();
                    return string.Equals(text, "1", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(text, "Да", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(text, "True", StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        private static double GetTypeLengthParameterMm(FamilySymbol symbol, string parameterName)
        {
            Parameter parameter = symbol?.LookupParameter(parameterName);
            if (parameter == null) return 0;
            if (parameter.StorageType == StorageType.Double)
                return parameter.AsDouble() * MillimetersPerFoot;
            if (parameter.StorageType == StorageType.Integer)
                return parameter.AsInteger();

            string text = parameter.AsString() ?? parameter.AsValueString();
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out double currentCultureValue)
                || double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out currentCultureValue))
            {
                return currentCultureValue;
            }
            return 0;
        }

        private static bool BoundingBoxesIntersect(BoundingBoxXYZ first, BoundingBoxXYZ second)
        {
            if (first == null || second == null) return false;
            return first.Min.X <= second.Max.X && first.Max.X >= second.Min.X
                   && first.Min.Y <= second.Max.Y && first.Max.Y >= second.Min.Y
                   && first.Min.Z <= second.Max.Z && first.Max.Z >= second.Min.Z;
        }

        public static bool IsSupportedOpening(Document document, Element element)
        {
            if (element?.Category == null) return false;
            long categoryId = element.Category.Id.Value;
            if (categoryId == (long)BuiltInCategory.OST_Doors || categoryId == (long)BuiltInCategory.OST_Windows)
            {
                var instance = element as FamilyInstance;
                if (instance == null || instance.SuperComponent != null) return false;
                var hostWall = instance.Host as Wall;
                if (hostWall == null || hostWall.WallType.Kind == WallKind.Curtain) return false;
                if (categoryId == (long)BuiltInCategory.OST_Windows
                    && (instance.Symbol?.FamilyName ?? string.Empty).IndexOf("Угловое", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
                return IsUsableHostWall(hostWall);
            }

            var curtain = element as Wall;
            if (categoryId != (long)BuiltInCategory.OST_Walls || curtain == null || curtain.WallType.Kind != WallKind.Curtain)
                return false;
            if ((curtain.Name ?? string.Empty).IndexOf("Лоджий", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            double code = GetDoubleValue(document.GetElement(curtain.GetTypeId()), "ZH_Код_Тип_Число", "ZH_Код_Тип");
            return Math.Abs(code - 211.002) > 0.0001;
        }

        private static List<Element> GetCandidates(Document document, ICollection<ElementId> selectedOpeningIds)
        {
            if (selectedOpeningIds != null && selectedOpeningIds.Count > 0)
            {
                return selectedOpeningIds.Select(document.GetElement)
                    .Where(element => IsSupportedOpening(document, element))
                    .ToList();
            }

            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Walls
            };
            var categoryFilter = new ElementMulticategoryFilter(categories);
            return new FilteredElementCollector(document, document.ActiveView.Id)
                .WhereElementIsNotElementType()
                .WherePasses(categoryFilter)
                .Where(element => IsSupportedOpening(document, element))
                .ToList();
        }

        private static Dictionary<long, Wall> BuildCurtainHostIndex(Document document, IEnumerable<Wall> curtainWalls)
        {
            var curtainIds = new HashSet<long>(curtainWalls.Select(x => x.Id.Value));
            var result = new Dictionary<long, Wall>();
            if (curtainIds.Count == 0) return result;

            foreach (Wall wall in new FilteredElementCollector(document)
                         .OfCategory(BuiltInCategory.OST_Walls)
                         .WhereElementIsNotElementType()
                         .OfType<Wall>()
                         .Where(x => x.WallType.Kind != WallKind.Curtain))
            {
                foreach (ElementId insertId in wall.FindInserts(false, false, true, false))
                {
                    if (curtainIds.Contains(insertId.Value) && !result.ContainsKey(insertId.Value))
                        result.Add(insertId.Value, wall);
                }
                if (result.Count == curtainIds.Count) break;
            }
            return result;
        }

        private static Wall FindHostWall(
            Document document,
            Element opening,
            BoundingBoxXYZ box,
            IDictionary<long, Wall> curtainHosts)
        {
            if (opening is FamilyInstance instance && instance.Host is Wall directHost)
                return directHost;
            if (curtainHosts.TryGetValue(opening.Id.Value, out Wall indexedHost))
                return indexedHost;

            double expansion = 200.0 / MillimetersPerFoot;
            var outline = new Outline(
                new XYZ(box.Min.X - expansion, box.Min.Y - expansion, box.Min.Z - expansion),
                new XYZ(box.Max.X + expansion, box.Max.Y + expansion, box.Max.Z + expansion));
            XYZ center = (box.Min + box.Max) / 2.0;

            return new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .OfType<Wall>()
                .Where(IsUsableHostWall)
                .Select(wall => new { Wall = wall, Distance = DistanceToWallCurve(wall, center) })
                .Where(x => x.Distance < expansion + x.Wall.Width / 2.0)
                .OrderBy(x => x.Distance)
                .Select(x => x.Wall)
                .FirstOrDefault();
        }

        private static bool IsUsableHostWall(Wall wall)
        {
            if (wall == null || wall.WallType == null || wall.WallType.Kind == WallKind.Curtain) return false;
            string name = (wall.WallType.Name ?? string.Empty).ToLowerInvariant();
            return IgnoredWallTokens.All(token => !name.Contains(token));
        }

        private static double DistanceToWallCurve(Wall wall, XYZ point)
        {
            Curve curve = (wall.Location as LocationCurve)?.Curve;
            if (curve == null) return double.MaxValue;
            XYZ pointInPlane = new XYZ(point.X, point.Y, curve.GetEndPoint(0).Z);
            IntersectionResult projection = curve.Project(pointInPlane);
            return projection == null ? double.MaxValue : projection.XYZPoint.DistanceTo(pointInPlane);
        }

        private static List<OpeningRecordV3> MergeNearbyOpenings(IList<OpeningRecordV3> openings)
        {
            if (openings.Count < 2) return openings.ToList();

            double threshold = 380.0 / MillimetersPerFoot;
            double broadPhaseMargin = threshold * Math.Sqrt(2.0);
            List<int> order = Enumerable.Range(0, openings.Count)
                .OrderBy(index => openings[index].BoundingMinimum.X)
                .ToList();
            var disjointSet = new DisjointSetV3(openings.Count);

            for (int orderedIndex = 0; orderedIndex < order.Count; orderedIndex++)
            {
                int firstIndex = order[orderedIndex];
                OpeningRecordV3 first = openings[firstIndex];
                XYZ firstDirection = NormalizeInPlan(first.WidthDirection);
                if (firstDirection == null) continue;

                for (int nextOrderedIndex = orderedIndex + 1; nextOrderedIndex < order.Count; nextOrderedIndex++)
                {
                    int secondIndex = order[nextOrderedIndex];
                    OpeningRecordV3 second = openings[secondIndex];
                    if (second.BoundingMinimum.X > first.BoundingMaximum.X + broadPhaseMargin) break;
                    if (AxisGap(first.BoundingMinimum.Y, first.BoundingMaximum.Y, second.BoundingMinimum.Y, second.BoundingMaximum.Y) > broadPhaseMargin
                        || AxisGap(first.BoundingMinimum.Z, first.BoundingMaximum.Z, second.BoundingMinimum.Z, second.BoundingMaximum.Z) > broadPhaseMargin)
                        continue;

                    XYZ secondDirection = NormalizeInPlan(second.WidthDirection);
                    if (secondDirection == null || Math.Abs(firstDirection.DotProduct(secondDirection)) < 0.999)
                        continue;

                    XYZ normal = firstDirection.CrossProduct(XYZ.BasisZ).Normalize();
                    GetOpeningRange(first, firstDirection, out double firstAlongMinimum, out double firstAlongMaximum);
                    GetOpeningRange(second, firstDirection, out double secondAlongMinimum, out double secondAlongMaximum);
                    double firstNormal = first.Location.DotProduct(normal);
                    double secondNormal = second.Location.DotProduct(normal);

                    bool nearAlong = AxisGap(firstAlongMinimum, firstAlongMaximum, secondAlongMinimum, secondAlongMaximum) <= threshold;
                    bool nearNormal = Math.Abs(firstNormal - secondNormal) <= threshold;
                    bool nearVertical = AxisGap(first.BoundingMinimum.Z, first.BoundingMaximum.Z, second.BoundingMinimum.Z, second.BoundingMaximum.Z) <= threshold;
                    if (nearAlong && nearNormal && nearVertical)
                        disjointSet.Union(firstIndex, secondIndex);
                }
            }

            return Enumerable.Range(0, openings.Count)
                .GroupBy(disjointSet.Find)
                .Select(group => MergeCluster(group.Select(index => openings[index]).ToList()))
                .ToList();
        }

        private static OpeningRecordV3 MergeCluster(IList<OpeningRecordV3> cluster)
        {
            if (cluster.Count == 1) return cluster[0];
            OpeningRecordV3 first = cluster[0];
            XYZ direction = NormalizeInPlan(first.WidthDirection) ?? XYZ.BasisX;
            double minimum = double.PositiveInfinity;
            double maximum = double.NegativeInfinity;

            foreach (OpeningRecordV3 opening in cluster)
            {
                GetOpeningRange(opening, direction, out double currentMinimum, out double currentMaximum);
                minimum = Math.Min(minimum, currentMinimum);
                maximum = Math.Max(maximum, currentMaximum);
            }

            var merged = new OpeningRecordV3
            {
                OpeningId = first.OpeningId,
                WallId = first.WallId,
                WallTypeId = first.WallTypeId,
                LevelId = first.LevelId,
                FamilyName = JoinDistinct(cluster.Select(x => x.FamilyName)),
                TypeName = JoinDistinct(cluster.Select(x => x.TypeName)),
                OpeningKind = cluster.Sum(x => x.ComponentCount) > 1 ? "Сборный проём" : first.OpeningKind,
                CategoryName = JoinDistinct(cluster.Select(x => x.CategoryName)),
                WallTypeName = JoinDistinct(cluster.Select(x => x.WallTypeName)),
                LevelName = JoinDistinct(cluster.Select(x => x.LevelName)),
                OpeningWidthMm = (maximum - minimum) * MillimetersPerFoot,
                OpeningHeightMm = cluster.Max(x => x.OpeningHeightMm),
                WallWidthMm = cluster.Max(x => x.WallWidthMm),
                Location = cluster.Aggregate(XYZ.Zero, (sum, item) => sum + item.Location) / cluster.Count,
                TopElevation = cluster.Max(x => x.TopElevation),
                WallOrientation = first.WallOrientation,
                WidthDirection = direction,
                SupportDirection = cluster.FirstOrDefault(x => x.SupportType > 0)?.SupportDirection ?? XYZ.Zero,
                SupportType = cluster.Max(x => x.SupportType),
                RequiredSupportWidthMm = cluster.Max(x => x.RequiredSupportWidthMm),
                RequiredSupportWidth1Mm = cluster.Max(x => x.RequiredSupportWidth1Mm),
                RequiredSupportWidth2Mm = cluster.Max(x => x.RequiredSupportWidth2Mm),
                SupportParameterError = JoinDistinct(cluster.Select(x => x.SupportParameterError)),
                BoundingMinimum = new XYZ(
                    cluster.Min(x => x.BoundingMinimum.X),
                    cluster.Min(x => x.BoundingMinimum.Y),
                    cluster.Min(x => x.BoundingMinimum.Z)),
                BoundingMaximum = new XYZ(
                    cluster.Max(x => x.BoundingMaximum.X),
                    cluster.Max(x => x.BoundingMaximum.Y),
                    cluster.Max(x => x.BoundingMaximum.Z)),
                ComponentCount = cluster.Sum(x => x.ComponentCount)
            };
            merged.RequiredSupportWidth1Mm = Math.Min(merged.WallWidthMm, merged.RequiredSupportWidth1Mm);
            merged.RequiredSupportWidth2Mm = Math.Min(merged.WallWidthMm, merged.RequiredSupportWidth2Mm);
            merged.RequiredSupportWidthMm = Math.Max(merged.RequiredSupportWidth1Mm, merged.RequiredSupportWidth2Mm);
            merged.ElementIds.AddRange(cluster.SelectMany(x => x.ElementIds).GroupBy(x => x.Value).Select(x => x.First()));
            return merged;
        }

        private static XYZ GetWidthDirection(Element opening, Wall hostWall)
        {
            if (opening is FamilyInstance instance)
            {
                XYZ hand = NormalizeInPlan(instance.HandOrientation);
                if (hand != null) return hand;
            }
            if (opening.Location is LocationCurve curve)
            {
                XYZ curveDirection = NormalizeInPlan(curve.Curve.GetEndPoint(1) - curve.Curve.GetEndPoint(0));
                if (curveDirection != null) return curveDirection;
            }
            XYZ wallAlong = NormalizeInPlan(hostWall.Orientation.CrossProduct(XYZ.BasisZ));
            return wallAlong ?? XYZ.BasisX;
        }

        private static string GetOpeningKind(Element opening)
        {
            long categoryId = opening?.Category?.Id.Value ?? 0;
            if (categoryId == (long)BuiltInCategory.OST_Doors) return "Дверь";
            if (categoryId == (long)BuiltInCategory.OST_Windows) return "Окно";
            if (categoryId == (long)BuiltInCategory.OST_Walls && opening is Wall wall && wall.WallType.Kind == WallKind.Curtain)
                return "Витраж";
            return "Проём";
        }

        private static XYZ NormalizeInPlan(XYZ value)
        {
            if (value == null) return null;
            var planar = new XYZ(value.X, value.Y, 0);
            return planar.GetLength() < 1e-9 ? null : planar.Normalize();
        }

        private static void GetOpeningRange(OpeningRecordV3 opening, XYZ axis, out double minimum, out double maximum)
        {
            double halfWidth = opening.OpeningWidthMm / (2.0 * MillimetersPerFoot);
            XYZ direction = NormalizeInPlan(opening.WidthDirection) ?? axis;
            XYZ first = opening.Location - direction * halfWidth;
            XYZ second = opening.Location + direction * halfWidth;
            minimum = Math.Min(first.DotProduct(axis), second.DotProduct(axis));
            maximum = Math.Max(first.DotProduct(axis), second.DotProduct(axis));
        }

        private static double AxisGap(double firstMinimum, double firstMaximum, double secondMinimum, double secondMaximum)
        {
            if (firstMaximum >= secondMinimum && secondMaximum >= firstMinimum) return 0;
            return Math.Max(secondMinimum - firstMaximum, firstMinimum - secondMaximum);
        }

        private static string JoinDistinct(IEnumerable<string> values)
        {
            return string.Join(" + ", values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, new AlphanumComparatorFastString()));
        }

        private static List<SupportBoxV3> CollectSupportBoxes(Document document, IEnumerable<BoundingBoxXYZ> openingBoxes)
        {
            List<BoundingBoxXYZ> boxes = openingBoxes.Where(x => x != null).ToList();
            if (boxes.Count == 0) return new List<SupportBoxV3>();

            double horizontal = 1000.0 / MillimetersPerFoot;
            double lower = 100.0 / MillimetersPerFoot;
            double upper = 1200.0 / MillimetersPerFoot;
            var outline = new Outline(
                new XYZ(boxes.Min(x => x.Min.X) - horizontal, boxes.Min(x => x.Min.Y) - horizontal, boxes.Min(x => x.Max.Z) - lower),
                new XYZ(boxes.Max(x => x.Max.X) + horizontal, boxes.Max(x => x.Max.Y) + horizontal, boxes.Max(x => x.Max.Z) + upper));
            var typeCodeCache = new Dictionary<long, double>();

            return new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .Where(element =>
                {
                    long typeId = element.GetTypeId().Value;
                    if (!typeCodeCache.TryGetValue(typeId, out double code))
                    {
                        code = GetDoubleValue(document.GetElement(element.GetTypeId()), "ZH_Код_Тип_Число", "ZH_Код_Тип");
                        typeCodeCache.Add(typeId, code);
                    }
                    if (Math.Abs(code) < 1e-9)
                        code = GetDoubleValue(element, "ZH_Код_Тип_Число", "ZH_Код_Тип");
                    return (code >= 311 && code < 312) || (code >= 317 && code < 318);
                })
                .Select(CreateSupportBox)
                .Where(x => x.Box != null)
                .OrderBy(x => x.ElementId.Value)
                .ToList();
        }

        private static SupportBoxV3 CreateSupportBox(Element element)
        {
            double firstBearing = GetLengthMm(element, "Опирание 1 итог");
            double secondBearing = GetLengthMm(element, "Опирание 2 итог");
            string error = null;
            double bearingZone = 0;

            if (firstBearing <= 0 || secondBearing <= 0)
            {
                error = "Для плиты ID " + element.Id.Value
                        + " не заполнен хотя бы один из параметров «Опирание 1 итог» и «Опирание 2 итог».";
            }
            else if (Math.Abs(firstBearing - secondBearing) > 0.5)
            {
                error = "У плиты ID " + element.Id.Value
                        + " параметры «Опирание 1 итог» и «Опирание 2 итог» имеют разные значения.";
            }
            else
            {
                bearingZone = (firstBearing + secondBearing) / 2.0;
            }

            return new SupportBoxV3
            {
                ElementId = element.Id,
                Box = element.get_BoundingBox(null),
                BearingZoneMm = bearingZone,
                ParameterError = error
            };
        }

        private static void DetectSupport(
            Wall wall,
            XYZ location,
            double top,
            double openingWidthMm,
            IEnumerable<SupportBoxV3> supports,
            out int supportType,
            out XYZ direction,
            out double requiredSupportWidthMm,
            out double requiredSupportWidth1Mm,
            out double requiredSupportWidth2Mm,
            out string supportParameterError)
        {
            XYZ normal = wall.Orientation.Normalize();
            XYZ along = normal.CrossProduct(XYZ.BasisZ).Normalize();
            XYZ center = new XYZ(location.X, location.Y, top);
            double centerNormal = center.DotProduct(normal);
            double centerAlong = center.DotProduct(along);
            double wallMinimum = centerNormal - wall.Width / 2.0;
            double wallMaximum = centerNormal + wall.Width / 2.0;
            double openingHalfWidth = Math.Max(350, openingWidthMm / 2.0) / MillimetersPerFoot;
            bool first = false;
            bool second = false;
            double firstZone = 0;
            double secondZone = 0;
            var firstErrors = new List<string>();
            var secondErrors = new List<string>();
            double wallWidthMm = wall.Width * MillimetersPerFoot;

            foreach (SupportBoxV3 support in supports)
            {
                BoundingBoxXYZ box = support.Box;
                if (box.Max.Z < top - 50.0 / MillimetersPerFoot || box.Min.Z > top + 1000.0 / MillimetersPerFoot)
                    continue;

                GetProjectedRange(box, along, out double alongMinimum, out double alongMaximum);
                if (centerAlong + openingHalfWidth < alongMinimum || centerAlong - openingHalfWidth > alongMaximum)
                    continue;

                GetProjectedRange(box, normal, out double supportMinimum, out double supportMaximum);
                double overlapMinimum = Math.Max(wallMinimum, supportMinimum);
                double overlapMaximum = Math.Min(wallMaximum, supportMaximum);
                if (overlapMaximum <= overlapMinimum + 1e-6) continue;

                double supportCenterNormal = (supportMinimum + supportMaximum) / 2.0;
                bool isFirstSide = supportCenterNormal < centerNormal - 1e-6;
                bool isSecondSide = supportCenterNormal > centerNormal + 1e-6;
                if (!isFirstSide && !isSecondSide)
                {
                    double firstExtension = centerNormal - supportMinimum;
                    double secondExtension = supportMaximum - centerNormal;
                    isFirstSide = firstExtension >= secondExtension;
                    isSecondSide = !isFirstSide;
                }

                if (isFirstSide)
                {
                    first = true;
                    if (support.BearingZoneMm > 0 && firstZone <= 0)
                        firstZone = Math.Min(wallWidthMm, support.BearingZoneMm);
                    else if (!string.IsNullOrWhiteSpace(support.ParameterError))
                        firstErrors.Add(support.ParameterError);
                }
                else if (isSecondSide)
                {
                    second = true;
                    if (support.BearingZoneMm > 0 && secondZone <= 0)
                        secondZone = Math.Min(wallWidthMm, support.BearingZoneMm);
                    else if (!string.IsNullOrWhiteSpace(support.ParameterError))
                        secondErrors.Add(support.ParameterError);
                }
            }

            supportType = first && second ? 2 : first || second ? 1 : 0;
            direction = supportType == 1 ? (first ? -normal : normal) : XYZ.Zero;
            requiredSupportWidth1Mm = first ? firstZone : 0;
            requiredSupportWidth2Mm = second ? secondZone : 0;
            requiredSupportWidthMm = Math.Max(requiredSupportWidth1Mm, requiredSupportWidth2Mm);

            var errors = new List<string>();
            if (first && firstZone <= 0)
                errors.Add(firstErrors.FirstOrDefault() ?? "Не удалось прочитать параметры опирания плиты со стороны 1.");
            if (second && secondZone <= 0)
                errors.Add(secondErrors.FirstOrDefault() ?? "Не удалось прочитать параметры опирания плиты со стороны 2.");
            supportParameterError = string.Join(" ", errors.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static void GetProjectedRange(BoundingBoxXYZ box, XYZ direction, out double minimum, out double maximum)
        {
            var corners = new[]
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            };
            minimum = corners.Min(x => x.DotProduct(direction));
            maximum = corners.Max(x => x.DotProduct(direction));
        }

        private static XYZ GetLocation(Element opening, Wall hostWall, BoundingBoxXYZ box)
        {
            if (opening.Location is LocationPoint point) return point.Point;
            if (opening.Location is LocationCurve locationCurve)
            {
                XYZ source = (locationCurve.Curve.GetEndPoint(0) + locationCurve.Curve.GetEndPoint(1)) / 2.0;
                Curve hostCurve = (hostWall.Location as LocationCurve)?.Curve;
                return hostCurve?.Project(source)?.XYZPoint ?? source;
            }
            return (box.Min + box.Max) / 2.0;
        }

        private static double GetOpeningWidthMm(Element opening, BoundingBoxXYZ box)
        {
            double value = GetLengthMm(opening, "ADSK_Размер_Ширина", "Ширина", "Длина");
            if (value > 0) return value;
            XYZ size = box.Max - box.Min;
            return Math.Max(size.X, size.Y) * MillimetersPerFoot;
        }

        private static double GetOpeningHeightMm(Element opening, BoundingBoxXYZ box)
        {
            double value = GetLengthMm(opening, "ADSK_Размер_Высота", "Высота", "Неприсоединенная высота");
            return value > 0 ? value : (box.Max.Z - box.Min.Z) * MillimetersPerFoot;
        }

        private static double GetLengthMm(Element element, params string[] parameterNames)
        {
            foreach (string parameterName in parameterNames)
            {
                Parameter parameter = element?.LookupParameter(parameterName);
                if (parameter == null && element is FamilyInstance instance)
                    parameter = instance.Symbol?.LookupParameter(parameterName);
                if (parameter == null) continue;
                if (parameter.StorageType == StorageType.Double && parameter.AsDouble() > 0)
                    return parameter.AsDouble() * MillimetersPerFoot;
                if (TryParseNumber(parameter.AsValueString(), out double value) && value > 0)
                    return value;
            }
            return 0;
        }

        private static double GetDoubleValue(Element element, params string[] parameterNames)
        {
            foreach (string parameterName in parameterNames)
            {
                Parameter parameter = element?.LookupParameter(parameterName);
                if (parameter == null) continue;
                if (parameter.StorageType == StorageType.Double && Math.Abs(parameter.AsDouble()) > 1e-9)
                    return parameter.AsDouble();
                string text = parameter.AsString() ?? parameter.AsValueString();
                if (TryParseNumber(text, out double value)) return value;
            }
            return 0;
        }

        private static bool TryParseNumber(string text, out double value)
        {
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return true;
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
            return double.TryParse((text ?? string.Empty).Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }
    }

    internal sealed class DisjointSetV3
    {
        private readonly int[] _parents;
        private readonly byte[] _ranks;

        public DisjointSetV3(int count)
        {
            _parents = Enumerable.Range(0, count).ToArray();
            _ranks = new byte[count];
        }

        public int Find(int value)
        {
            if (_parents[value] != value)
                _parents[value] = Find(_parents[value]);
            return _parents[value];
        }

        public void Union(int first, int second)
        {
            int firstRoot = Find(first);
            int secondRoot = Find(second);
            if (firstRoot == secondRoot) return;
            if (_ranks[firstRoot] < _ranks[secondRoot])
                _parents[firstRoot] = secondRoot;
            else if (_ranks[firstRoot] > _ranks[secondRoot])
                _parents[secondRoot] = firstRoot;
            else
            {
                _parents[secondRoot] = firstRoot;
                _ranks[firstRoot]++;
            }
        }
    }

    public sealed class OpeningSelectionHandlerV3 : IExternalEventHandler
    {
        private readonly object _sync = new object();
        private List<ElementId> _requestedIds = new List<ElementId>();

        public void Request(IEnumerable<ElementId> elementIds)
        {
            lock (_sync)
                _requestedIds = (elementIds ?? Enumerable.Empty<ElementId>()).Distinct().ToList();
        }

        public void Execute(UIApplication application)
        {
            List<ElementId> ids;
            lock (_sync)
                ids = _requestedIds.ToList();

            UIDocument uiDocument = application.ActiveUIDocument;
            if (uiDocument == null) return;
            List<ElementId> validIds = ids.Where(id => uiDocument.Document.GetElement(id) != null).ToList();
            uiDocument.Selection.SetElementIds(validIds);
        }

        public string GetName()
        {
            return "Выбор группы проёмов v3";
        }
    }
}
