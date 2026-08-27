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
                var reloadHandler = new OpeningReloadHandlerV3(workspace);
                ExternalEvent reloadEvent = ExternalEvent.Create(reloadHandler);
                var placementHandler = new LintelPlacementHandlerV3(uiDocument.Document, workspace);
                ExternalEvent placementEvent = ExternalEvent.Create(placementHandler);
                var typeReplacementHandler = new LintelTypeReplacementHandlerV3(
                    uiDocument.Document,
                    workspace);
                ExternalEvent typeReplacementEvent = ExternalEvent.Create(typeReplacementHandler);
                form = new LintelCreatorForm_v3(
                    workspace,
                    selectionHandler,
                    selectionEvent,
                    reloadHandler,
                    reloadEvent,
                    placementHandler,
                    placementEvent,
                    typeReplacementHandler,
                    typeReplacementEvent);
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

    public enum CompositeTypeNameConflictActionV3
    {
        UseExisting = 1,
        ReplaceExisting = 2,
        AppendNumber = 3,
        Cancel = 4
    }

    public sealed class CompositeTypeNameConflictOptionV3
    {
        public CompositeTypeNameConflictActionV3 Action { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public override string ToString()
        {
            return Name;
        }
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

    public sealed class ExistingLintelTypeOptionV3
    {
        public ElementId TypeId { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public int SupportCategory { get; set; }
        public List<ExistingLintelComponentV3> Components { get; }
            = new List<ExistingLintelComponentV3>();

        public string DisplayName => (FamilyName ?? string.Empty) + " : " + (TypeName ?? string.Empty);
        public string SupportDescription => SupportCategory == 2
            ? "Опирание с двух сторон"
            : SupportCategory == 1
                ? "Опирание с одной стороны"
                : "Без опирания";
    }

    public sealed class OpeningPlacementTargetV3
    {
        public ElementId WallId { get; set; }
        public ElementId LevelId { get; set; }
        public XYZ Location { get; set; }
        public double TopElevation { get; set; }
        public XYZ WallOrientation { get; set; }
        public XYZ SupportDirection { get; set; }
        public int SupportType { get; set; }
        public List<ElementId> OpeningIds { get; } = new List<ElementId>();
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
        public List<OpeningPlacementTargetV3> PlacementTargets { get; } = new List<OpeningPlacementTargetV3>();
        public List<LintelSelectionVariantV3> CalculatedVariants { get; } = new List<LintelSelectionVariantV3>();
        public LintelSelectionVariantV3 ActiveVariant { get; set; }
        public bool IsCalculated { get; set; }
        public string CalculationMessage { get; set; }
        public string CalculationBaseMessage { get; set; }

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
        private int _heightMm;
        private int _widthMm;
        private int _gapMm;
        private bool _canMoveUp;
        private bool _canMoveDown;
        private bool _isApplyingCatalogItem;
        private bool _hasExistingTypeDifference;
        private string _existingTypeDifferenceText = string.Empty;

        public LintelEditorRowV3(LintelCatalogItemV3 item)
        {
            ApplyCatalogItem(item);
        }

        public ObservableCollection<LintelCatalogItemV3> AvailableCatalogItems { get; }
            = new ObservableCollection<LintelCatalogItemV3>();

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

        public bool HasExistingTypeDifference
        {
            get => _hasExistingTypeDifference;
            internal set
            {
                if (_hasExistingTypeDifference == value) return;
                _hasExistingTypeDifference = value;
                RaisePropertyChanged(nameof(HasExistingTypeDifference));
            }
        }

        public string ExistingTypeDifferenceText
        {
            get => _existingTypeDifferenceText;
            internal set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_existingTypeDifferenceText, normalized, StringComparison.Ordinal)) return;
                _existingTypeDifferenceText = normalized;
                RaisePropertyChanged(nameof(ExistingTypeDifferenceText));
            }
        }

        internal void ApplyCatalogSuggestion(LintelCatalogItemV3 item)
        {
            ApplyCatalogItem(item);
        }

        internal void ReplaceAvailableCatalogItems(IEnumerable<LintelCatalogItemV3> items)
        {
            AvailableCatalogItems.Clear();
            foreach (LintelCatalogItemV3 item in items ?? Enumerable.Empty<LintelCatalogItemV3>())
                AvailableCatalogItems.Add(item);
            RaisePropertyChanged(nameof(AvailableCatalogItems));
            RaisePropertyChanged(nameof(SelectedCatalogItem));
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

        public int HeightMm
        {
            get => _heightMm;
            set
            {
                int normalized = Math.Max(0, value);
                if (_heightMm == normalized) return;
                _heightMm = normalized;
                RaisePropertyChanged(nameof(HeightMm));
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
                HeightMm = item.HeightMm;
                WidthMm = item.WidthMm;
                Purpose = item.IsBearing ? "Несущая" : "Ненесущая";
            }
            finally
            {
                _isApplyingCatalogItem = false;
            }
        }
    }

    internal sealed class LintelPlacementRequestV3
    {
        public CompositeTypeNameConflictActionV3 NameConflictAction { get; set; }
        public List<LintelPlacementGroupRequestV3> Groups { get; } = new List<LintelPlacementGroupRequestV3>();
    }

    internal sealed class LintelPlacementGroupRequestV3
    {
        public string GroupKey { get; set; }
        public string CompositeTypeName { get; set; }
        public string WallTypeName { get; set; }
        public bool HasExistingTypeDifference { get; set; }
        public string ExistingTypeDifferenceText { get; set; }
        public List<OpeningPlacementTargetV3> Targets { get; } = new List<OpeningPlacementTargetV3>();
        public List<LintelPlacementComponentRequestV3> Components { get; } = new List<LintelPlacementComponentRequestV3>();
    }

    internal sealed class LintelPlacementComponentRequestV3
    {
        public string Mark { get; set; }
        public string RevitFamilyName { get; set; }
        public int WidthMm { get; set; }
        public int GapAfterMm { get; set; }
        public bool IsBearing { get; set; }
        public int MaximumOpeningWidthMm { get; set; }
        public int MasonryCourseHeightMm { get; set; }
        public string TypeCode { get; set; }
    }

    internal sealed class LintelPlacementResultV3
    {
        public List<LintelPlacementGroupResultV3> Groups { get; } = new List<LintelPlacementGroupResultV3>();
        public string FatalError { get; set; }
    }

    internal sealed class LintelPlacementGroupResultV3
    {
        public string GroupKey { get; set; }
        public bool IsSuccess { get; set; }
        public string Error { get; set; }
        public string FamilyName { get; set; }
        public string RequestedTypeName { get; set; }
        public string TypeName { get; set; }
        public bool HasTypeNameConflict { get; set; }
        public bool WasCancelledByTypeNameConflict { get; set; }
        public string TypeNameConflictAction { get; set; }
        public string TypeNameConflictDifferences { get; set; }
        public List<ElementId> CreatedLintelIds { get; } = new List<ElementId>();
        public List<ExistingLintelComponentV3> Components { get; } = new List<ExistingLintelComponentV3>();
    }

    internal sealed class LintelTypeReplacementRequestV3
    {
        public ElementId TypeId { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public List<ElementId> LintelIds { get; } = new List<ElementId>();
    }

    internal sealed class LintelTypeReplacementItemResultV3
    {
        public ElementId OriginalId { get; set; }
        public ElementId ResultId { get; set; }
    }

    internal sealed class LintelTypeReplacementResultV3
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string FatalError { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public List<LintelTypeReplacementItemResultV3> ChangedItems { get; }
            = new List<LintelTypeReplacementItemResultV3>();
        public List<ExistingLintelComponentV3> Components { get; }
            = new List<ExistingLintelComponentV3>();
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
        private bool _isExistingLintelsTabActive;
        private bool _isPlacementInProgress;
        private List<ExistingLintelTypeOptionV3> _allExistingLintelTypeOptions
            = new List<ExistingLintelTypeOptionV3>();
        private ExistingLintelTypeOptionV3 _selectedExistingLintelType;
        private CompositeTypeNameConflictOptionV3 _selectedTypeNameConflictOption;
        private bool _isUpdatingEditorDifferences;
        private int _editorExistingTypeDifferenceCount;
        private string _editorExistingTypeDifferencesText = string.Empty;

        public LintelOpeningWorkspaceV3(Document document, Selection selection)
        {
            _document = document;
            _initialSelectionIds = selection.GetElementIds()
                .Where(id => OpeningCollectorV3.IsSupportedOpening(document, document.GetElement(id)))
                .ToList();
            _existingCompositeTypeNames = new HashSet<string>(StringComparer.Ordinal);
            RefreshExistingCompositeTypeCache(document);

            EditorPurposeOptions = new ObservableCollection<string> { "Несущая", "Ненесущая" };
            TypeNameConflictOptions = new ObservableCollection<CompositeTypeNameConflictOptionV3>
            {
                new CompositeTypeNameConflictOptionV3
                {
                    Action = CompositeTypeNameConflictActionV3.UseExisting,
                    Name = "1. Использовать текущее",
                    Description = "Разместить существующий тип без изменения его состава."
                },
                new CompositeTypeNameConflictOptionV3
                {
                    Action = CompositeTypeNameConflictActionV3.ReplaceExisting,
                    Name = "2. Заменить на новое",
                    Description = "Изменить состав существующего типа на выбранный вариант."
                },
                new CompositeTypeNameConflictOptionV3
                {
                    Action = CompositeTypeNameConflictActionV3.AppendNumber,
                    Name = "3. Добавить номер к имени",
                    Description = "Создать новый тип с суффиксом _2, _3 и далее."
                },
                new CompositeTypeNameConflictOptionV3
                {
                    Action = CompositeTypeNameConflictActionV3.Cancel,
                    Name = "4. Отменить",
                    Description = "Не размещать группы с конфликтующим именем."
                }
            };
            _selectedTypeNameConflictOption = TypeNameConflictOptions.Last();
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
        public ObservableCollection<ExistingLintelTypeOptionV3> ExistingLintelTypeOptions { get; }
            = new ObservableCollection<ExistingLintelTypeOptionV3>();
        public ObservableCollection<CompositeTypeNameConflictOptionV3> TypeNameConflictOptions { get; }
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
                RaisePropertyChanged(nameof(SelectedOpeningGroup));
                RaisePropertyChanged(nameof(SelectedExistingLintelGroup));
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

        public OpeningGroupCardV3 SelectedOpeningGroup
        {
            get => SelectedGroup != null && !SelectedGroup.HasExistingLintel
                ? SelectedGroup
                : null;
            set
            {
                if (value != null && !value.HasExistingLintel)
                    SelectedGroup = value;
            }
        }

        public OpeningGroupCardV3 SelectedExistingLintelGroup
        {
            get => SelectedGroup?.HasExistingLintel == true
                ? SelectedGroup
                : null;
            set
            {
                if (value?.HasExistingLintel == true)
                    SelectedGroup = value;
            }
        }

        public LintelSelectionVariantV3 SelectedVariant
        {
            get => _selectedVariant;
            set
            {
                if (ReferenceEquals(_selectedVariant, value)) return;
                _selectedVariant = value;
                if (SelectedGroup != null
                    && !SelectedGroup.HasExistingLintel
                    && value != null
                    && SelectedGroup.CalculatedVariants.Contains(value))
                {
                    SelectedGroup.ActiveVariant = value;
                    ApplyActiveVariantStatus(SelectedGroup);
                }
                RaisePropertyChanged(nameof(SelectedVariant));
                RaisePropertyChanged(nameof(CanSaveVariantChanges));
                RaisePropertyChanged(nameof(CanPlaceSelectedLintels));
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
        public bool CanLoadExistingEditorType => CanInteract
                                                 && EditorTypeExists
                                                 && FindExistingEditorTypeOption(EditorTypeName) != null;
        public bool EditorHasExistingTypeDifferences => _editorExistingTypeDifferenceCount > 0;
        public int EditorExistingTypeDifferenceCount => _editorExistingTypeDifferenceCount;
        public string EditorExistingTypeDifferencesText => _editorExistingTypeDifferencesText;
        public string EditorTypeStatusText => string.IsNullOrWhiteSpace(EditorTypeName)
            ? "Тип не определён"
            : EditorTypeExists
                ? EditorHasExistingTypeDifferences
                    ? "Существующий тип · состав отличается ("
                      + EditorExistingTypeDifferenceCount.ToString(CultureInfo.InvariantCulture) + ")"
                    : "Существующий тип"
                : "Будет создан";
        public string EditorTypeStatusGlyph => EditorHasExistingTypeDifferences
            ? "!"
            : EditorTypeExists ? "✓" : "+";
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

        public ExistingLintelTypeOptionV3 SelectedExistingLintelType
        {
            get => _selectedExistingLintelType;
            set
            {
                if (ReferenceEquals(_selectedExistingLintelType, value)) return;
                _selectedExistingLintelType = value;
                RaisePropertyChanged(nameof(SelectedExistingLintelType));
                RaisePropertyChanged(nameof(CanSaveVariantChanges));
                RaisePropertyChanged(nameof(ExistingLintelTypeSelectionText));
            }
        }

        public string ExistingLintelTypesSummaryText => ExistingLintelTypeOptions.Count == 0
            ? "Подходящие типы не найдены"
            : "Подходящих типов: " + ExistingLintelTypeOptions.Count.ToString(CultureInfo.InvariantCulture);
        public string ExistingLintelCurrentTypeText => SelectedGroup?.HasExistingLintel == true
            ? "Текущий тип: " + (SelectedGroup.ExistingLintelTypeNames ?? "—")
            : "Текущий тип: —";
        public string ExistingLintelTypeSelectionText => SelectedExistingLintelType == null
            ? "Выберите тип для замены."
            : "Будет установлен тип «" + SelectedExistingLintelType.TypeName + "».";

        public CompositeTypeNameConflictOptionV3 SelectedTypeNameConflictOption
        {
            get => _selectedTypeNameConflictOption;
            set
            {
                if (ReferenceEquals(_selectedTypeNameConflictOption, value) || value == null) return;
                _selectedTypeNameConflictOption = value;
                RaisePropertyChanged(nameof(SelectedTypeNameConflictOption));
                RaisePropertyChanged(nameof(TypeNameConflictDescription));
            }
        }

        public string TypeNameConflictDescription => SelectedTypeNameConflictOption?.Description
                                                     ?? string.Empty;

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
        public bool CanInteract => !IsCollectionInProgress
                                   && !IsCalculationInProgress
                                   && !IsPlacementInProgress;
        public bool IsPlacementInProgress
        {
            get => _isPlacementInProgress;
            private set
            {
                if (_isPlacementInProgress == value) return;
                _isPlacementInProgress = value;
                RaisePropertyChanged(nameof(IsPlacementInProgress));
                RaisePropertyChanged(nameof(CanInteract));
                RaisePropertyChanged(nameof(IsVariantsPanelEnabled));
                RaisePropertyChanged(nameof(CanRecalculate));
                RaisePropertyChanged(nameof(CanRecalculateAll));
                RaisePropertyChanged(nameof(CanSaveVariantChanges));
                RaisePropertyChanged(nameof(CanPlaceSelectedLintels));
                RaisePropertyChanged(nameof(ExistingLintelTypesSummaryText));
            }
        }
        public bool IsExistingLintelsTabActive
        {
            get => _isExistingLintelsTabActive;
            set
            {
                if (_isExistingLintelsTabActive == value) return;
                _isExistingLintelsTabActive = value;
                RaisePropertyChanged(nameof(IsExistingLintelsTabActive));
                RaisePropertyChanged(nameof(SaveVariantButtonText));
                RaisePropertyChanged(nameof(CreateLintelsButtonText));
                RaisePropertyChanged(nameof(CanSaveVariantChanges));
                RaisePropertyChanged(nameof(CanPlaceSelectedLintels));
            }
        }
        public string SaveVariantButtonText => IsExistingLintelsTabActive
            ? "Заменить тип"
            : "Сохранить изменения варианта";
        public string CreateLintelsButtonText => IsExistingLintelsTabActive
            ? "Пересоздать перемычки"
            : "Создать типы и проставить";
        public bool CanSaveVariantChanges => CanInteract
                                             && (IsExistingLintelsTabActive
                                                 ? SelectedGroup?.HasExistingLintel == true
                                                   && SelectedExistingLintelType != null
                                                   && SelectedGroup.ExistingLintelIds.Count > 0
                                                 : SelectedGroup != null
                                                   && !SelectedGroup.HasExistingLintel
                                                   && SelectedVariant != null
                                                   && EditorRows.Count > 0);
        public bool CanPlaceSelectedLintels => CanInteract
                                               && !IsExistingLintelsTabActive
                                               && _allGroups
                                                   .Where(group => group.IsChecked && !group.HasExistingLintel)
                                                   .Any()
                                               && _allGroups
                                                   .Where(group => group.IsChecked && !group.HasExistingLintel)
                                                   .All(group => group.ActiveVariant != null
                                                                 && group.PlacementTargets.Count > 0);
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
                RaisePropertyChanged(nameof(CanSaveVariantChanges));
                RaisePropertyChanged(nameof(CanPlaceSelectedLintels));
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
                RaisePropertyChanged(nameof(CanSaveVariantChanges));
                RaisePropertyChanged(nameof(CanPlaceSelectedLintels));
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
            if (SelectedGroup?.HasExistingLintel == true)
                RefreshExistingLintelTypeOptions(SelectedGroup);
            else
                RecalculateVariants();
        }

        private void SetLintelMaterial(LintelMaterialV3 value)
        {
            if (_lintelMaterial == value) return;
            _lintelMaterial = value;
            RaisePropertyChanged(nameof(IsReinforcedConcrete));
            RaisePropertyChanged(nameof(IsMetal));
            if (SelectedGroup?.HasExistingLintel == true)
                RefreshExistingLintelTypeOptions(SelectedGroup);
            else
                RecalculateVariants();
        }

        private void RaiseSelectedGroupProperties()
        {
            RaisePropertyChanged(nameof(HasSelectedGroup));
            RaisePropertyChanged(nameof(IsVariantsPanelEnabled));
            RaisePropertyChanged(nameof(CanRecalculate));
            RaisePropertyChanged(nameof(CanRecalculateAll));
            RaisePropertyChanged(nameof(CanSaveVariantChanges));
            RaisePropertyChanged(nameof(CanPlaceSelectedLintels));
            RaisePropertyChanged(nameof(SaveVariantButtonText));
            RaisePropertyChanged(nameof(CreateLintelsButtonText));
            RaisePropertyChanged(nameof(SelectedOpeningCaption));
            RaisePropertyChanged(nameof(SelectedOpeningWidthText));
            RaisePropertyChanged(nameof(SelectedWallWidthText));
            RaisePropertyChanged(nameof(SelectedWallTypeText));
            RaisePropertyChanged(nameof(SelectedBearingSideText));
            RaisePropertyChanged(nameof(SelectedBearingZoneText));
            RaisePropertyChanged(nameof(ExistingLintelCurrentTypeText));
            RaisePropertyChanged(nameof(ExistingLintelTypesSummaryText));
            RaisePropertyChanged(nameof(ExistingLintelTypeSelectionText));
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

        private void StoreCalculation(OpeningGroupCardV3 group, LintelSelectionResultV3 result)
        {
            string activeCompositionKey = group.ActiveVariant?.CompositionKey;
            int activeRank = group.ActiveVariant?.Rank ?? 0;
            group.CalculatedVariants.Clear();
            group.CalculatedVariants.AddRange(result.Variants);
            foreach (LintelSelectionVariantV3 variant in group.CalculatedVariants)
                ApplyExistingTypeWarning(group, variant);
            group.ActiveVariant = group.CalculatedVariants.FirstOrDefault(variant =>
                                      !string.IsNullOrWhiteSpace(activeCompositionKey)
                                      && string.Equals(
                                          variant.CompositionKey,
                                          activeCompositionKey,
                                          StringComparison.OrdinalIgnoreCase))
                                  ?? group.CalculatedVariants.FirstOrDefault(variant => variant.Rank == activeRank)
                                  ?? group.CalculatedVariants.FirstOrDefault();
            group.CalculationBaseMessage = result.Message;
            group.CalculationMessage = result.Message;
            group.IsCalculated = true;
            ApplyCalculationStatus(group, result);
        }

        private void DisplayStoredCalculation(OpeningGroupCardV3 group)
        {
            Variants.Clear();
            foreach (LintelSelectionVariantV3 variant in group.CalculatedVariants)
                Variants.Add(variant);
            LintelSelectionVariantV3 activeVariant = group.ActiveVariant;
            if (activeVariant == null || !Variants.Contains(activeVariant))
                activeVariant = Variants.FirstOrDefault();
            SelectedVariant = activeVariant;
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
            ApplyExistingTypeSettings(group);
            RefreshExistingLintelTypeOptions(group);
            RaiseVariantsProperties();
        }

        private void ApplyExistingTypeSettings(OpeningGroupCardV3 group)
        {
            string typeName = (group?.ExistingLintelTypeNames ?? string.Empty)
                .Split('+')
                .Select(value => value.Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            string firstPart = (typeName ?? string.Empty).Split('_').FirstOrDefault();
            LintelMasonryTypeV3 masonry = firstPart == "88"
                ? LintelMasonryTypeV3.Brick88
                : firstPart == "65"
                    ? LintelMasonryTypeV3.Brick65
                    : LintelMasonryTypeV3.Partition;
            if (_masonryType != masonry)
            {
                _masonryType = masonry;
                RaisePropertyChanged(nameof(IsMasonry65));
                RaisePropertyChanged(nameof(IsMasonry88));
                RaisePropertyChanged(nameof(IsPartition));
            }

            LintelMaterialV3 material = IsMetalCompositeTypeName(typeName)
                ? LintelMaterialV3.Metal
                : LintelMaterialV3.ReinforcedConcrete;
            if (_lintelMaterial != material)
            {
                _lintelMaterial = material;
                RaisePropertyChanged(nameof(IsReinforcedConcrete));
                RaisePropertyChanged(nameof(IsMetal));
            }
        }

        private void RefreshExistingLintelTypeOptions(OpeningGroupCardV3 selectedGroup)
        {
            long previousTypeId = SelectedExistingLintelType?.TypeId?.Value ?? -1;
            List<OpeningGroupCardV3> sourceGroups = GetExistingSourceGroups(selectedGroup);
            IEnumerable<ExistingLintelTypeOptionV3> filtered = _allExistingLintelTypeOptions
                .Where(option => IsExistingTypeGeometrySuitable(option, sourceGroups))
                .ToList();

            List<ExistingLintelTypeOptionV3> baseFiltered = filtered.ToList();
            int requiredSupport = sourceGroups.Count == 0
                ? selectedGroup?.SupportType ?? 0
                : sourceGroups.Max(group => group.SupportType);
            bool has0 = baseFiltered.Any(option => option.SupportCategory == 0);
            bool has1 = baseFiltered.Any(option => option.SupportCategory == 1);
            bool has2 = baseFiltered.Any(option => option.SupportCategory == 2);
            int chosenSupport;
            if (requiredSupport >= 2)
                chosenSupport = 2;
            else if (requiredSupport == 1)
                chosenSupport = has1 ? 1 : has2 ? 2 : 1;
            else
                chosenSupport = has0 ? 0 : has1 ? 1 : 2;

            List<ExistingLintelTypeOptionV3> options = baseFiltered
                .Where(option => option.SupportCategory == chosenSupport)
                .OrderBy(option => option.FamilyName, _naturalComparer)
                .ThenBy(option => option.TypeName, _naturalComparer)
                .ToList();

            ExistingLintelTypeOptions.Clear();
            foreach (ExistingLintelTypeOptionV3 option in options)
                ExistingLintelTypeOptions.Add(option);

            ExistingLintelTypeOptionV3 current = options.FirstOrDefault(option =>
                string.Equals(option.FamilyName, selectedGroup?.ExistingLintelFamilyNames, StringComparison.OrdinalIgnoreCase)
                && string.Equals(option.TypeName, selectedGroup?.ExistingLintelTypeNames, StringComparison.Ordinal));
            SelectedExistingLintelType = options.FirstOrDefault(option => option.TypeId.Value == previousTypeId)
                                         ?? current
                                         ?? options.FirstOrDefault();
            RaisePropertyChanged(nameof(ExistingLintelTypeOptions));
            RaisePropertyChanged(nameof(ExistingLintelTypesSummaryText));
            RaisePropertyChanged(nameof(ExistingLintelCurrentTypeText));
            RaisePropertyChanged(nameof(ExistingLintelTypeSelectionText));
            RaisePropertyChanged(nameof(CanSaveVariantChanges));
        }

        private List<OpeningGroupCardV3> GetExistingSourceGroups(OpeningGroupCardV3 selectedGroup)
        {
            if (selectedGroup?.HasExistingLintel != true)
                return new List<OpeningGroupCardV3>();
            var lintelIds = new HashSet<long>(selectedGroup.ExistingLintelIds.Select(id => id.Value));
            List<OpeningGroupCardV3> result = _allGroups
                .Where(group => group.HasExistingLintel
                                && group.ExistingLintelIds.Any(id => lintelIds.Contains(id.Value)))
                .ToList();
            if (result.Count == 0 && !selectedGroup.IsExistingLintelAggregate)
                result.Add(selectedGroup);
            return result;
        }

        private bool IsExistingTypeGeometrySuitable(
            ExistingLintelTypeOptionV3 option,
            IList<OpeningGroupCardV3> sourceGroups)
        {
            if (option == null || string.IsNullOrWhiteSpace(option.TypeName)) return false;
            if (IsErrorCompositeTypeName(option.TypeName)) return true;
            string[] parts = option.TypeName.Split('_');
            if (parts.Length < 4) return false;
            if (_masonryType == LintelMasonryTypeV3.Brick65 && parts[0] != "65") return false;
            if (_masonryType == LintelMasonryTypeV3.Brick88 && parts[0] != "88") return false;

            if (!TryParseTypeNumber(parts[1], out double typeWallWidth)) return false;
            if (!TryParseTypeNumber(parts[2], out double maximumOpeningWidth)) return false;
            foreach (OpeningGroupCardV3 group in sourceGroups)
            {
                if (Math.Abs(typeWallWidth - NormalizeWallWidth((int)Math.Round(group.WallWidthMm))) > 0.5)
                    return false;
                if (maximumOpeningWidth + 0.5 < group.OpeningWidthMm)
                    return false;
            }

            bool isMetal = IsMetalCompositeTypeName(option.TypeName);
            return _lintelMaterial == LintelMaterialV3.Metal ? isMetal : !isMetal;
        }

        private static bool TryParseTypeNumber(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value)
                   || double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static bool IsMetalCompositeTypeName(string typeName)
        {
            string name = typeName ?? string.Empty;
            return name.Contains("у")
                   || name.Contains("У")
                   || name.Contains("А")
                   || name.Contains("Шв")
                   || name.Contains("Дв");
        }

        private static bool IsErrorCompositeTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return false;
            string cleaned = new string(typeName
                    .Where(character => !char.IsControl(character)
                                        && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format)
                    .ToArray())
                .Trim();
            return string.Equals(cleaned, "Тестовый вариант", StringComparison.OrdinalIgnoreCase);
        }

        public bool SaveEditorChangesToActiveVariant()
        {
            if (!CanSaveVariantChanges) return false;

            OpeningGroupCardV3 group = SelectedGroup;
            LintelSelectionVariantV3 previousVariant = SelectedVariant;
            LintelSelectionVariantV3 savedVariant = CreateVariantFromEditor(group, previousVariant.Rank);
            if (savedVariant == null) return false;
            ApplyExistingTypeWarning(group, savedVariant);

            int storedIndex = group.CalculatedVariants.IndexOf(previousVariant);
            int visibleIndex = Variants.IndexOf(previousVariant);
            if (storedIndex < 0 || visibleIndex < 0) return false;

            group.CalculatedVariants[storedIndex] = savedVariant;
            group.ActiveVariant = savedVariant;
            Variants[visibleIndex] = savedVariant;
            SelectedVariant = savedVariant;
            SelectionMessage = "Изменения варианта " + savedVariant.Rank + " сохранены для выбранного проёма.";
            RaiseVariantsProperties();
            return true;
        }

        internal LintelTypeReplacementRequestV3 CreateTypeReplacementRequest()
        {
            if (!IsExistingLintelsTabActive
                || SelectedGroup?.HasExistingLintel != true
                || SelectedExistingLintelType == null)
                return null;

            var request = new LintelTypeReplacementRequestV3
            {
                TypeId = SelectedExistingLintelType.TypeId,
                FamilyName = SelectedExistingLintelType.FamilyName,
                TypeName = SelectedExistingLintelType.TypeName
            };
            request.LintelIds.AddRange(SelectedGroup.ExistingLintelIds
                .GroupBy(id => id.Value)
                .Select(group => group.First()));
            return request.LintelIds.Count == 0 ? null : request;
        }

        internal void BeginLintelTypeReplacement(int lintelCount)
        {
            IsPlacementInProgress = true;
            SelectionMessage = "Замена типа перемычек: 0/"
                               + lintelCount.ToString(CultureInfo.InvariantCulture) + ".";
        }

        internal void CancelLintelTypeReplacement(string message)
        {
            IsPlacementInProgress = false;
            SelectionMessage = string.IsNullOrWhiteSpace(message)
                ? "Замена типа перемычек не запущена."
                : message;
        }

        internal void ApplyLintelTypeReplacementResult(LintelTypeReplacementResultV3 result)
        {
            try
            {
                Dictionary<long, ElementId> changedIds = (result?.ChangedItems
                                                          ?? new List<LintelTypeReplacementItemResultV3>())
                    .GroupBy(item => item.OriginalId.Value)
                    .ToDictionary(group => group.Key, group => group.Last().ResultId);
                if (changedIds.Count > 0)
                {
                    foreach (OpeningGroupCardV3 group in _allGroups.Where(group => group.HasExistingLintel))
                    {
                        bool isAffected = group.ExistingLintelIds.Any(id => changedIds.ContainsKey(id.Value));
                        if (!isAffected) continue;

                        List<ElementId> updatedIds = group.ExistingLintelIds
                            .Select(id => changedIds.TryGetValue(id.Value, out ElementId changedId)
                                ? changedId
                                : id)
                            .GroupBy(id => id.Value)
                            .Select(ids => ids.First())
                            .ToList();
                        group.ExistingLintelIds.Clear();
                        group.ExistingLintelIds.AddRange(updatedIds);
                        group.ExistingLintelFamilyNames = result.FamilyName;
                        group.ExistingLintelTypeNames = result.TypeName;
                        group.ExistingLintelComponents.Clear();
                        group.ExistingLintelComponents.AddRange(result.Components.Select(component =>
                            new ExistingLintelComponentV3
                            {
                                FamilyName = component.FamilyName,
                                TypeName = component.TypeName,
                                Order = component.Order,
                                OffsetToNextMm = component.OffsetToNextMm
                            }));
                        group.Status = OpeningStatusV3.Success;
                        group.StatusText = group.ExistingLintelDescription;
                        group.CalculationMessage = group.ExistingLintelDescription
                                                   + ". " + group.ExistingLintelIdsText + ".";
                    }

                    RefreshExistingCompositeTypeCache(_document);
                    _existingLintelTypeGroups = BuildExistingLintelTypeGroups(
                        _allGroups.Where(group => group.HasExistingLintel));
                    SelectedGroup = null;
                    RefreshView();
                    if (IsExistingGroupedByOpening)
                    {
                        var resultIds = new HashSet<long>(changedIds.Values.Select(id => id.Value));
                        SelectedGroup = ExistingLintelGroups.FirstOrDefault(group =>
                            group.ExistingLintelIds.Any(id => resultIds.Contains(id.Value)));
                    }
                    else
                    {
                        SelectedGroup = ExistingLintelGroups.FirstOrDefault(group =>
                            string.Equals(group.ExistingLintelFamilyNames, result.FamilyName, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(group.ExistingLintelTypeNames, result.TypeName, StringComparison.Ordinal));
                    }
                }

                var errors = new List<string>();
                if (!string.IsNullOrWhiteSpace(result?.FatalError))
                    errors.Add(result.FatalError);
                errors.AddRange(result?.Errors.Where(error => !string.IsNullOrWhiteSpace(error))
                                ?? Enumerable.Empty<string>());
                SelectionMessage = changedIds.Count > 0
                    ? "Тип заменён у " + changedIds.Count.ToString(CultureInfo.InvariantCulture)
                      + " перемычек на «" + result.TypeName + "»."
                      + (errors.Count > 0 ? " Ошибок: " + errors.Count + "." : string.Empty)
                    : errors.FirstOrDefault() ?? "Тип перемычек не изменён.";
                RaiseSummaryProperties();
            }
            finally
            {
                IsPlacementInProgress = false;
                RaiseSelectedGroupProperties();
            }
        }

        internal LintelPlacementRequestV3 CreatePlacementRequest()
        {
            var request = new LintelPlacementRequestV3
            {
                NameConflictAction = SelectedTypeNameConflictOption?.Action
                                     ?? CompositeTypeNameConflictActionV3.Cancel
            };
            foreach (OpeningGroupCardV3 group in _allGroups
                         .Where(item => item.IsChecked && !item.HasExistingLintel))
            {
                if (group.ActiveVariant == null || group.PlacementTargets.Count == 0)
                    continue;

                List<LintelPlacementComponentRequestV3> components =
                    CreatePlacementComponents(group.ActiveVariant);
                if (components.Count == 0)
                    continue;

                var groupRequest = new LintelPlacementGroupRequestV3
                {
                    GroupKey = group.Key,
                    WallTypeName = group.WallTypeName,
                    CompositeTypeName = BuildPlacementTypeName(group, components),
                    HasExistingTypeDifference = group.ActiveVariant.HasExistingTypeDifference,
                    ExistingTypeDifferenceText = group.ActiveVariant.ExistingTypeDifferenceText
                };
                groupRequest.Targets.AddRange(group.PlacementTargets);
                groupRequest.Components.AddRange(components);
                request.Groups.Add(groupRequest);
            }
            return request.Groups.Count == 0 ? null : request;
        }

        internal void BeginLintelPlacement(int groupCount)
        {
            IsPlacementInProgress = true;
            SelectionMessage = "Создание типов и размещение перемычек: 0/"
                               + groupCount.ToString(CultureInfo.InvariantCulture) + ".";
        }

        internal void CancelLintelPlacement(string message)
        {
            IsPlacementInProgress = false;
            SelectionMessage = string.IsNullOrWhiteSpace(message)
                ? "Размещение перемычек не запущено."
                : message;
        }

        internal void ApplyLintelPlacementResult(LintelPlacementResultV3 result)
        {
            try
            {
                List<LintelPlacementGroupResultV3> successful = result?.Groups
                    .Where(item => item.IsSuccess)
                    .ToList() ?? new List<LintelPlacementGroupResultV3>();
                var successfulKeys = new HashSet<string>(
                    successful.Select(item => item.GroupKey),
                    StringComparer.Ordinal);

                foreach (LintelPlacementGroupResultV3 groupResult in successful)
                {
                    OpeningGroupCardV3 group = _allGroups.FirstOrDefault(item =>
                        string.Equals(item.Key, groupResult.GroupKey, StringComparison.Ordinal));
                    if (group == null) continue;

                    group.HasExistingLintel = true;
                    group.IsChecked = false;
                    group.ExistingLintelFamilyNames = groupResult.FamilyName;
                    group.ExistingLintelTypeNames = groupResult.TypeName;
                    group.ExistingLintelIds.Clear();
                    group.ExistingLintelIds.AddRange(groupResult.CreatedLintelIds);
                    group.ExistingLintelComponents.Clear();
                    group.ExistingLintelComponents.AddRange(groupResult.Components);
                    group.Status = OpeningStatusV3.Success;
                    group.StatusText = group.ExistingLintelDescription;
                    group.CalculationMessage = group.ExistingLintelDescription
                                               + ". " + group.ExistingLintelIdsText + ".";
                    if (!string.IsNullOrWhiteSpace(groupResult.TypeName))
                        _existingCompositeTypeNames.Add(groupResult.TypeName);
                }

                foreach (LintelPlacementGroupResultV3 failed in result?.Groups
                             .Where(item => !item.IsSuccess)
                         ?? Enumerable.Empty<LintelPlacementGroupResultV3>())
                {
                    OpeningGroupCardV3 group = _allGroups.FirstOrDefault(item =>
                        string.Equals(item.Key, failed.GroupKey, StringComparison.Ordinal));
                    if (group == null) continue;
                    group.Status = OpeningStatusV3.Error;
                    if (failed.WasCancelledByTypeNameConflict)
                    {
                        group.StatusText = "Размещение отменено";
                        group.CalculationMessage = "Размещение отменено: имя типа «"
                                                   + failed.RequestedTypeName
                                                   + "» совпало, но состав отличается. Отличия: "
                                                   + failed.TypeNameConflictDifferences + ".";
                    }
                    else
                    {
                        group.StatusText = "Ошибка размещения";
                        group.CalculationMessage = string.IsNullOrWhiteSpace(failed.Error)
                            ? "Размещение перемычки отменено или не выполнено."
                            : failed.Error;
                    }
                }

                if (!string.IsNullOrWhiteSpace(result?.FatalError))
                {
                    foreach (OpeningGroupCardV3 group in _allGroups
                                 .Where(item => item.IsChecked && !item.HasExistingLintel))
                    {
                        group.Status = OpeningStatusV3.Error;
                        group.StatusText = "Размещение отменено";
                        group.CalculationMessage = result.FatalError;
                    }
                }

                if (successful.Count > 0)
                    RefreshExistingCompositeTypeCache(_document);

                bool selectedGroupWasPlaced = SelectedGroup != null
                                              && successfulKeys.Contains(SelectedGroup.Key);
                if (selectedGroupWasPlaced)
                    SelectedGroup = null;

                _existingLintelTypeGroups = BuildExistingLintelTypeGroups(
                    _allGroups.Where(group => group.HasExistingLintel));
                RefreshView();
                if (selectedGroupWasPlaced)
                    SelectedGroup = VisibleGroups.FirstOrDefault();

                int placedOpeningCount = successful
                    .Select(item => _allGroups.FirstOrDefault(group => group.Key == item.GroupKey))
                    .Where(group => group != null)
                    .Sum(group => group.Count);
                List<string> errors = result?.Groups
                    .Where(item => !item.IsSuccess && !string.IsNullOrWhiteSpace(item.Error))
                    .Select(item => item.Error)
                    .ToList() ?? new List<string>();
                if (!string.IsNullOrWhiteSpace(result?.FatalError))
                    errors.Insert(0, result.FatalError);

                SelectionMessage = "Размещено перемычек: "
                                   + successful.Sum(item => item.CreatedLintelIds.Count)
                                   + ". Обработано проёмов: " + placedOpeningCount + "."
                                   + (errors.Count > 0
                                       ? " Ошибки: " + string.Join(" ", errors)
                                       : string.Empty);
                RaiseSummaryProperties();
                RaiseSelectedGroupProperties();
                RaiseEditorProperties();
            }
            finally
            {
                IsPlacementInProgress = false;
            }
        }

        private List<LintelPlacementComponentRequestV3> CreatePlacementComponents(
            LintelSelectionVariantV3 variant)
        {
            var result = new List<LintelPlacementComponentRequestV3>();
            foreach (LintelLayoutSegmentV3 segment in variant.LayoutSegments)
            {
                if (segment.IsGap)
                {
                    if (result.Count > 0)
                        result[result.Count - 1].GapAfterMm += segment.WidthMm;
                    continue;
                }

                LintelCatalogItemV3 item = _lintelCatalog.FirstOrDefault(candidate =>
                                               string.Equals(
                                                   candidate.Mark,
                                                   segment.Mark,
                                                   StringComparison.OrdinalIgnoreCase)
                                               && candidate.WidthMm == segment.WidthMm)
                                           ?? _lintelCatalog.FirstOrDefault(candidate =>
                                               string.Equals(
                                                   candidate.Mark,
                                                   segment.Mark,
                                                   StringComparison.OrdinalIgnoreCase));
                if (item == null) return new List<LintelPlacementComponentRequestV3>();

                result.Add(new LintelPlacementComponentRequestV3
                {
                    Mark = item.Mark,
                    RevitFamilyName = item.RevitFamilyName,
                    WidthMm = segment.WidthMm,
                    IsBearing = segment.IsBearing,
                    MaximumOpeningWidthMm = item.MaximumOpeningWidthMm,
                    MasonryCourseHeightMm = item.MasonryCourseHeightMm,
                    TypeCode = GetLintelTypeCode(item, segment.IsBearing, segment.WidthMm)
                });
            }
            return result;
        }

        private void ApplyExistingTypeWarning(
            OpeningGroupCardV3 group,
            LintelSelectionVariantV3 variant)
        {
            if (variant == null)
                return;

            variant.HasExistingTypeDifference = false;
            variant.ExistingTypeDifferenceText = string.Empty;
            List<LintelPlacementComponentRequestV3> components = CreatePlacementComponents(variant);
            if (group == null || components.Count == 0)
                return;

            string typeName = BuildPlacementTypeName(group, components);
            ExistingLintelTypeOptionV3 existingType = FindExistingTypeOption(typeName);
            if (existingType == null)
                return;

            List<string> differences = GetCachedCompositionDifferences(existingType, components);
            if (differences.Count == 0)
                return;

            variant.HasExistingTypeDifference = true;
            variant.ExistingTypeDifferenceText = "Имя типа «" + typeName
                                                 + "» уже существует, но состав отличается: "
                                                 + string.Join("; ", differences) + "."
                                                 + " При размещении будет использован существующий тип.";
        }

        private List<string> GetCachedCompositionDifferences(
            ExistingLintelTypeOptionV3 existingType,
            IList<LintelPlacementComponentRequestV3> selectedComponents)
        {
            var differences = new List<string>();
            List<ExistingLintelComponentV3> existingComponents = existingType?.Components
                .OrderBy(component => component.Order)
                .ToList() ?? new List<ExistingLintelComponentV3>();
            int commonCount = Math.Min(existingComponents.Count, selectedComponents?.Count ?? 0);
            for (int index = 0; index < commonCount; index++)
            {
                ExistingLintelComponentV3 existing = existingComponents[index];
                LintelPlacementComponentRequestV3 selected = selectedComponents[index];
                bool typeMatches = string.Equals(
                    existing.TypeName,
                    selected.Mark,
                    StringComparison.Ordinal);
                bool familyMatches = string.IsNullOrWhiteSpace(existing.FamilyName)
                                     || string.IsNullOrWhiteSpace(selected.RevitFamilyName)
                                     || string.Equals(
                                         existing.FamilyName,
                                         selected.RevitFamilyName,
                                         StringComparison.OrdinalIgnoreCase);
                if (!typeMatches || !familyMatches)
                {
                    differences.Add(
                        (index + 1).ToString(CultureInfo.InvariantCulture)
                        + "ПР: существует «" + FormatExistingComponent(existing)
                        + "», выбрано «" + FormatPlacementComponent(selected) + "»");
                }

                LintelCatalogItemV3 existingItem = FindExistingComponentCatalogItem(existing);
                if (existingItem != null && existingItem.IsBearing != selected.IsBearing)
                {
                    differences.Add(
                        (index + 1).ToString(CultureInfo.InvariantCulture)
                        + "ПР: назначение существует «"
                        + (existingItem.IsBearing ? "Несущая" : "Ненесущая")
                        + "», выбрано «"
                        + (selected.IsBearing ? "Несущая" : "Ненесущая") + "»");
                }

                if (index < commonCount - 1)
                {
                    int selectedOffsetMm = selected.WidthMm + selected.GapAfterMm;
                    if (Math.Abs(existing.OffsetToNextMm - selectedOffsetMm) > 0.5)
                    {
                        differences.Add(
                            "отступ от " + (index + 1).ToString(CultureInfo.InvariantCulture)
                            + " до " + (index + 2).ToString(CultureInfo.InvariantCulture)
                            + "ПР: существует "
                            + Math.Round(existing.OffsetToNextMm).ToString(CultureInfo.InvariantCulture)
                            + " мм, выбрано "
                            + selectedOffsetMm.ToString(CultureInfo.InvariantCulture) + " мм");
                    }
                }
            }

            if (existingComponents.Count != (selectedComponents?.Count ?? 0))
            {
                differences.Add(
                    "количество вложенных перемычек: существует "
                    + existingComponents.Count.ToString(CultureInfo.InvariantCulture)
                    + ", выбрано "
                    + (selectedComponents?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            }
            return differences;
        }

        private static string FormatPlacementComponent(LintelPlacementComponentRequestV3 component)
        {
            if (component == null) return "не определено";
            return string.IsNullOrWhiteSpace(component.RevitFamilyName)
                ? component.Mark ?? "не определено"
                : component.RevitFamilyName + " : " + (component.Mark ?? "не определено");
        }

        private static string BuildPlacementTypeName(
            OpeningGroupCardV3 group,
            IList<LintelPlacementComponentRequestV3> components)
        {
            int wallWidth = NormalizeWallWidth((int)Math.Round(group.WallWidthMm));
            int maximumOpeningWidth = components
                .Select(component => component.MaximumOpeningWidthMm)
                .Where(value => value > 0)
                .DefaultIfEmpty((int)Math.Round(group.OpeningWidthMm))
                .Min();
            int masonryCourse = components
                .Select(component => component.MasonryCourseHeightMm)
                .FirstOrDefault(value => value >= 0);
            string layout = string.Join("_", components
                .Select(component => component.TypeCode)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return masonryCourse.ToString(CultureInfo.InvariantCulture)
                   + "_" + wallWidth.ToString(CultureInfo.InvariantCulture)
                   + "_" + maximumOpeningWidth.ToString(CultureInfo.InvariantCulture)
                   + "_" + layout;
        }

        private LintelSelectionVariantV3 CreateVariantFromEditor(OpeningGroupCardV3 group, int rank)
        {
            List<LintelEditorRowV3> rows = EditorRows
                .Where(row => row?.SelectedCatalogItem != null)
                .ToList();
            if (group == null || rows.Count == 0 || rows.Count != EditorRows.Count)
                return null;

            List<IGrouping<string, LintelEditorRowV3>> markGroups = rows
                .GroupBy(
                    row => row.SelectedCatalogItem.Mark ?? row.SelectedCatalogItem.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
            string composition = string.Join(" + ", markGroups.Select(markGroup =>
                markGroup.Count() > 1
                    ? markGroup.Key + " × " + markGroup.Count()
                    : markGroup.Key));
            string compositionKey = string.Join("|", markGroups
                .OrderBy(markGroup => markGroup.Key, StringComparer.OrdinalIgnoreCase)
                .Select(markGroup => markGroup.Key + "x" + markGroup.Count()));

            int totalWidth = rows.Sum(row => row.WidthMm + row.GapMm);
            double scale = 348.0 / Math.Max(1, totalWidth);
            var segments = new List<LintelLayoutSegmentV3>();
            foreach (LintelEditorRowV3 row in rows)
            {
                segments.Add(new LintelLayoutSegmentV3
                {
                    Mark = row.SelectedCatalogItem.Mark,
                    WidthMm = row.WidthMm,
                    DisplayWidth = Math.Max(1, row.WidthMm * scale),
                    IsBearing = row.IsBearing
                });
                if (row.GapMm > 0)
                {
                    segments.Add(new LintelLayoutSegmentV3
                    {
                        Mark = "Зазор",
                        WidthMm = row.GapMm,
                        DisplayWidth = Math.Max(1, row.GapMm * scale),
                        IsGap = true
                    });
                }
            }

            LintelSelectionRequestV3 request = CreateSelectionRequest(group);
            int requiredBearingWidth = group.SupportType <= 0
                ? 0
                : (int)Math.Ceiling(Math.Min(
                    group.WallWidthMm,
                    Math.Max(0, group.RequiredSupportWidth1Mm)
                    + Math.Max(0, group.RequiredSupportWidth2Mm)));
            int roundedWallWidth = (int)Math.Round(group.WallWidthMm);
            return new LintelSelectionVariantV3
            {
                Rank = rank,
                CompositionKey = compositionKey,
                CompositionText = composition,
                TotalWidthMm = totalWidth,
                SignedWidthDeltaMm = totalWidth - roundedWallWidth,
                WidthDeltaMm = Math.Abs(totalWidth - roundedWallWidth),
                BearingWidthMm = rows.Where(row => row.IsBearing).Sum(row => row.WidthMm),
                RequiredBearingWidthMm = requiredBearingWidth,
                ElementCount = rows.Count,
                DistinctMarkCount = markGroups.Count,
                MinimumLengthMm = rows.Min(row => row.LengthMm),
                MaximumLengthMm = rows.Max(row => row.LengthMm),
                OpeningWidthExcessScore = rows.Max(row =>
                    LintelSelectionEngineV3.GetOpeningWidthExcess(request, row.SelectedCatalogItem)),
                LengthExcessScore = rows.Sum(row =>
                    LintelSelectionEngineV3.GetLengthExcess(request, row.SelectedCatalogItem)),
                PriorityScore = rows.Sum(row => row.SelectedCatalogItem.Priority),
                MinimumPriority = rows.Min(row => row.SelectedCatalogItem.Priority),
                AveragePriority = rows.Average(row => row.SelectedCatalogItem.Priority),
                WallWidthToleranceMm = WallWidthToleranceMm,
                LayoutSegments = segments
            };
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

        public bool LoadEditorFromExistingType()
        {
            ExistingLintelTypeOptionV3 option = FindExistingEditorTypeOption(EditorTypeName);
            if (!CanLoadExistingEditorType || option == null) return false;

            foreach (LintelEditorRowV3 row in EditorRows)
                row.PropertyChanged -= EditorRow_PropertyChanged;
            EditorRows.Clear();
            RefreshEditorCatalogItems();

            foreach (ExistingLintelComponentV3 component in option.Components
                         .OrderBy(item => item.Order))
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
                RestoreExistingRowsFromTypeName(option.TypeName);

            UpdateEditorRowIndexes();
            RaiseEditorProperties();
            SelectionMessage = EditorRows.Count > 0
                ? "В редактор загружен существующий тип «" + option.TypeName + "»."
                : "Не удалось прочитать вложенные типы из «" + option.TypeName + "».";
            return EditorRows.Count > 0;
        }

        private ExistingLintelTypeOptionV3 FindExistingEditorTypeOption(string typeName)
        {
            string preferredFamily = null;
            if (SelectedGroup?.HasExistingLintel == true
                && !string.IsNullOrWhiteSpace(SelectedGroup.ExistingLintelFamilyNames))
                preferredFamily = SelectedGroup.ExistingLintelFamilyNames;
            return FindExistingTypeOption(typeName, preferredFamily);
        }

        private ExistingLintelTypeOptionV3 FindExistingTypeOption(
            string typeName,
            string preferredFamilyName = null)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            List<ExistingLintelTypeOptionV3> matches = _allExistingLintelTypeOptions
                .Where(option => string.Equals(option.TypeName, typeName, StringComparison.Ordinal))
                .ToList();
            if (!string.IsNullOrWhiteSpace(preferredFamilyName))
            {
                ExistingLintelTypeOptionV3 sameFamily = matches.FirstOrDefault(option => string.Equals(
                    option.FamilyName,
                    preferredFamilyName,
                    StringComparison.OrdinalIgnoreCase));
                if (sameFamily != null) return sameFamily;
            }
            return matches
                .OrderByDescending(option => option.Components.Count)
                .ThenBy(option => option.FamilyName, _naturalComparer)
                .FirstOrDefault();
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
                string normalizedToken = token.Trim();
                IEnumerable<LintelCatalogItemV3> candidates = EditorCatalogItems
                    .Concat(_lintelCatalog)
                    .Where(candidate => candidate != null)
                    .Distinct();
                LintelCatalogItemV3 item = candidates
                    .Where(candidate => string.Equals(
                        GetLintelTypeCode(candidate, candidate.IsBearing, candidate.WidthMm),
                        normalizedToken,
                        StringComparison.Ordinal))
                    .OrderBy(candidate => maximumOpeningWidth > 0
                        ? Math.Abs(candidate.MaximumOpeningWidthMm - maximumOpeningWidth)
                        : 0)
                    .ThenBy(candidate => candidate.LengthMm)
                    .FirstOrDefault();

                if (item == null)
                {
                    bool isBearing = normalizedToken.StartsWith("Н", StringComparison.Ordinal);
                    string number = new string(normalizedToken.Where(char.IsDigit).ToArray());
                    if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int widthMm))
                    {
                        item = candidates
                            .Where(candidate => candidate.WidthMm == widthMm && candidate.IsBearing == isBearing)
                            .OrderBy(candidate => maximumOpeningWidth > 0
                                ? Math.Abs(candidate.MaximumOpeningWidthMm - maximumOpeningWidth)
                                : 0)
                            .ThenBy(candidate => candidate.LengthMm)
                            .FirstOrDefault();
                    }
                }
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

            LintelSelectionRequestV3 request = CreateEditorSelectionRequest();

            IEnumerable<LintelCatalogItemV3> items = _lintelCatalog
                .Where(item => LintelSelectionEngineV3.IsSuitableCatalogItem(item, request, false)
                                 && item.WidthMm <= request.WallWidthMm
                                                    + request.WallWidthToleranceMm + 0.5)
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.DisplayName, _naturalComparer);

            foreach (LintelCatalogItemV3 item in items)
                EditorCatalogItems.Add(item);
            RaisePropertyChanged(nameof(EditorCatalogItems));
            RefreshAllEditorRowCatalogItems();
        }

        private LintelSelectionRequestV3 CreateEditorSelectionRequest()
        {
            if (SelectedGroup == null) return null;
            LintelSelectionRequestV3 request = CreateSelectionRequest(SelectedGroup);
            int? existingMasonry = GetExistingLintelMasonryCourse(SelectedGroup);
            if (existingMasonry.HasValue)
                request.MasonryCourseHeightMm = existingMasonry.Value;
            return request;
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
            if (row == null) return;
            row.PropertyChanged += EditorRow_PropertyChanged;
            RefreshEditorRowCatalogItems(row);
        }

        private void EditorRow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isUpdatingEditorDifferences) return;
            if (sender is LintelEditorRowV3 row)
            {
                bool shouldRefreshCatalog = e.PropertyName == nameof(LintelEditorRowV3.Purpose);
                if (!row.IsApplyingCatalogItem
                    && (e.PropertyName == nameof(LintelEditorRowV3.LengthMm)
                        || e.PropertyName == nameof(LintelEditorRowV3.HeightMm)
                        || e.PropertyName == nameof(LintelEditorRowV3.WidthMm)
                        || e.PropertyName == nameof(LintelEditorRowV3.Purpose)))
                {
                    LintelCatalogItemV3 suggestedItem = FindBestEditorCatalogItem(row, e.PropertyName);
                    if (suggestedItem != null)
                        row.ApplyCatalogSuggestion(suggestedItem);
                    shouldRefreshCatalog = true;
                }
                if (shouldRefreshCatalog)
                    RefreshAllEditorRowCatalogItems();
            }
            RaiseEditorProperties();
        }

        private LintelCatalogItemV3 FindBestEditorCatalogItem(LintelEditorRowV3 row, string changedProperty)
        {
            if (row == null || EditorCatalogItems.Count == 0) return null;

            List<LintelCatalogItemV3> candidates = GetAvailableEditorCatalogItems(row).ToList();
            if (candidates.Count == 0) return null;

            IOrderedEnumerable<LintelCatalogItemV3> ordered;
            if (changedProperty == nameof(LintelEditorRowV3.LengthMm))
            {
                candidates = candidates
                    .Where(item => item.WidthMm <= row.WidthMm)
                    .ToList();
                if (candidates.Count == 0) return null;
                ordered = candidates
                    .OrderBy(item => Math.Abs(item.LengthMm - row.LengthMm))
                    .ThenBy(item => Math.Abs(item.HeightMm - row.HeightMm))
                    .ThenBy(item => Math.Abs(item.WidthMm - row.WidthMm));
            }
            else if (changedProperty == nameof(LintelEditorRowV3.HeightMm))
            {
                candidates = candidates
                    .Where(item => item.WidthMm <= row.WidthMm)
                    .ToList();
                if (candidates.Count == 0) return null;
                ordered = candidates
                    .OrderBy(item => Math.Abs(item.HeightMm - row.HeightMm))
                    .ThenBy(item => Math.Abs(item.LengthMm - row.LengthMm))
                    .ThenBy(item => Math.Abs(item.WidthMm - row.WidthMm));
            }
            else if (changedProperty == nameof(LintelEditorRowV3.WidthMm))
            {
                LintelSelectionRequestV3 request = CreateEditorSelectionRequest();
                candidates = candidates
                    .Where(item => request == null
                                   || LintelSelectionEngineV3.IsSuitableCatalogItem(item, request, false))
                    .ToList();
                if (candidates.Count == 0) return null;
                ordered = candidates
                    .OrderBy(item => Math.Abs(item.WidthMm - row.WidthMm))
                    .ThenBy(item => Math.Abs(item.HeightMm - row.HeightMm))
                    .ThenBy(item => Math.Abs(item.LengthMm - row.LengthMm));
            }
            else
            {
                ordered = candidates
                    .OrderBy(item => Math.Abs(item.WidthMm - row.WidthMm))
                    .ThenBy(item => Math.Abs(item.HeightMm - row.HeightMm))
                    .ThenBy(item => Math.Abs(item.LengthMm - row.LengthMm));
            }

            return ordered
                .ThenBy(item => SelectedGroup == null
                    ? 1000000
                    : item.MaximumOpeningWidthMm <= 0
                        ? 1000000
                        : Math.Max(0, item.MaximumOpeningWidthMm - (int)Math.Round(SelectedGroup.OpeningWidthMm)))
                .ThenByDescending(item => item.Priority)
                .ThenBy(item => item.Mark, _naturalComparer)
                .FirstOrDefault();
        }

        private void RefreshEditorRowCatalogItems(LintelEditorRowV3 row)
        {
            if (row == null) return;
            row.ReplaceAvailableCatalogItems(GetAvailableEditorCatalogItems(row));
        }

        private void RefreshAllEditorRowCatalogItems()
        {
            foreach (LintelEditorRowV3 editorRow in EditorRows)
                RefreshEditorRowCatalogItems(editorRow);
        }

        private IEnumerable<LintelCatalogItemV3> GetAvailableEditorCatalogItems(LintelEditorRowV3 row)
        {
            if (row == null) return Enumerable.Empty<LintelCatalogItemV3>();
            double requiredBearingZoneWidth = row.IsBearing
                ? GetRequiredEditorBearingZoneWidth()
                : 0;
            double maximumAvailableWidth = GetMaximumAvailableEditorRowWidth(row);
            return EditorCatalogItems.Where(item => IsAvailableForEditorRow(
                item,
                row.IsBearing,
                requiredBearingZoneWidth,
                maximumAvailableWidth));
        }

        internal static bool IsAvailableForEditorRow(
            LintelCatalogItemV3 item,
            bool isBearing,
            double requiredBearingZoneWidth,
            double maximumAvailableWidth)
        {
            return item != null
                   && item.IsBearing == isBearing
                   && item.WidthMm <= maximumAvailableWidth + 0.5
                   && (!isBearing
                       || requiredBearingZoneWidth <= 0
                       || item.WidthMm + 0.5 >= requiredBearingZoneWidth);
        }

        private double GetMaximumAvailableEditorRowWidth(LintelEditorRowV3 row)
        {
            if (SelectedGroup == null) return double.MaxValue;
            double maximumPackageWidth = SelectedGroup.WallWidthMm + WallWidthToleranceMm;
            double occupiedByOtherRowsAndGaps = EditorRows.Sum(editorRow =>
                ReferenceEquals(editorRow, row)
                    ? editorRow.GapMm
                    : editorRow.WidthMm + editorRow.GapMm);
            return Math.Max(0, maximumPackageWidth - occupiedByOtherRowsAndGaps);
        }

        private double GetRequiredEditorBearingZoneWidth()
        {
            if (SelectedGroup == null || SelectedGroup.SupportType <= 0) return 0;
            double requiredWidth = Math.Max(
                SelectedGroup.RequiredSupportWidthMm,
                Math.Max(
                    SelectedGroup.RequiredSupportWidth1Mm,
                    SelectedGroup.RequiredSupportWidth2Mm));
            return Math.Min(SelectedGroup.WallWidthMm, Math.Max(0, requiredWidth));
        }

        private void UpdateEditorRowIndexes()
        {
            for (int index = 0; index < EditorRows.Count; index++)
            {
                EditorRows[index].Index = index + 1;
                EditorRows[index].CanMoveUp = index > 0;
                EditorRows[index].CanMoveDown = index < EditorRows.Count - 1;
                EditorRows[index].GapMm = index < EditorRows.Count - 1
                    ? LintelSelectionEngineV3.InterElementGapMm
                    : 0;
            }
            RefreshAllEditorRowCatalogItems();
        }

        private void RaiseEditorProperties()
        {
            UpdateEditorExistingTypeDifferences();
            RaisePropertyChanged(nameof(EditorRows));
            RaisePropertyChanged(nameof(HasEditorVariant));
            RaisePropertyChanged(nameof(CanReverseEditor));
            RaisePropertyChanged(nameof(EditorRestoreButtonText));
            RaisePropertyChanged(nameof(EditorTypeName));
            RaisePropertyChanged(nameof(EditorTypeExists));
            RaisePropertyChanged(nameof(CanLoadExistingEditorType));
            RaisePropertyChanged(nameof(EditorHasExistingTypeDifferences));
            RaisePropertyChanged(nameof(EditorExistingTypeDifferenceCount));
            RaisePropertyChanged(nameof(EditorExistingTypeDifferencesText));
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
            RaisePropertyChanged(nameof(CanSaveVariantChanges));
        }

        private void UpdateEditorExistingTypeDifferences()
        {
            if (_isUpdatingEditorDifferences) return;
            _isUpdatingEditorDifferences = true;
            try
            {
                foreach (LintelEditorRowV3 row in EditorRows)
                {
                    row.HasExistingTypeDifference = false;
                    row.ExistingTypeDifferenceText = string.Empty;
                }

                _editorExistingTypeDifferenceCount = 0;
                _editorExistingTypeDifferencesText = string.Empty;

                ExistingLintelTypeOptionV3 existingType = FindExistingEditorTypeOption(EditorTypeName);
                if (!EditorTypeExists || existingType == null) return;

                List<ExistingLintelComponentV3> existingComponents = existingType.Components
                    .OrderBy(component => component.Order)
                    .ToList();
                int commonCount = Math.Min(EditorRows.Count, existingComponents.Count);
                for (int index = 0; index < EditorRows.Count; index++)
                {
                    LintelEditorRowV3 row = EditorRows[index];
                    var differences = new List<string>();
                    if (index >= existingComponents.Count)
                    {
                        differences.Add("в существующем типе эта вложенная перемычка отсутствует");
                    }
                    else
                    {
                        ExistingLintelComponentV3 component = existingComponents[index];
                        LintelCatalogItemV3 selectedItem = row.SelectedCatalogItem;
                        string selectedTypeName = selectedItem?.Mark ?? string.Empty;
                        string selectedFamilyName = selectedItem?.RevitFamilyName ?? string.Empty;
                        bool typeMatches = string.Equals(
                            selectedTypeName,
                            component.TypeName,
                            StringComparison.Ordinal);
                        bool familyMatches = string.IsNullOrWhiteSpace(selectedFamilyName)
                                             || string.IsNullOrWhiteSpace(component.FamilyName)
                                             || string.Equals(
                                                 selectedFamilyName,
                                                 component.FamilyName,
                                                 StringComparison.OrdinalIgnoreCase);
                        if (!typeMatches || !familyMatches)
                        {
                            differences.Add(
                                "тип: существует «" + FormatExistingComponent(component)
                                + "», выбран «" + FormatEditorComponent(selectedItem) + "»");
                        }

                        LintelCatalogItemV3 existingItem = FindExistingComponentCatalogItem(component);
                        if (existingItem != null)
                        {
                            AddDimensionDifference(differences, "длина", existingItem.LengthMm, row.LengthMm);
                            AddDimensionDifference(differences, "высота", existingItem.HeightMm, row.HeightMm);
                            AddDimensionDifference(differences, "ширина", existingItem.WidthMm, row.WidthMm);
                            if (existingItem.IsBearing != row.IsBearing)
                            {
                                differences.Add(
                                    "назначение: существует «"
                                    + (existingItem.IsBearing ? "Несущая" : "Ненесущая")
                                    + "», выбрано «" + row.Purpose + "»");
                            }
                        }

                        if (index < commonCount - 1)
                        {
                            int selectedOffsetMm = row.WidthMm + row.GapMm;
                            int existingOffsetMm = (int)Math.Round(component.OffsetToNextMm);
                            if (Math.Abs(component.OffsetToNextMm - selectedOffsetMm) > 0.5)
                            {
                                differences.Add(
                                    "отступ до следующей: существует "
                                    + existingOffsetMm.ToString(CultureInfo.InvariantCulture)
                                    + " мм, выбрано "
                                    + selectedOffsetMm.ToString(CultureInfo.InvariantCulture) + " мм");
                            }
                        }
                    }

                    if (differences.Count == 0) continue;
                    row.HasExistingTypeDifference = true;
                    row.ExistingTypeDifferenceText = "Вложенная перемычка "
                                                     + (index + 1).ToString(CultureInfo.InvariantCulture)
                                                     + " отличается: " + string.Join("; ", differences) + ".";
                    _editorExistingTypeDifferenceCount++;
                }

                int additionalExistingCount = Math.Max(0, existingComponents.Count - EditorRows.Count);
                _editorExistingTypeDifferenceCount += additionalExistingCount;
                if (_editorExistingTypeDifferenceCount == 0) return;

                var summaryParts = new List<string>
                {
                    "Состав выбранного варианта отличается от существующего типа."
                };
                int highlightedRows = EditorRows.Count(row => row.HasExistingTypeDifference);
                if (highlightedRows > 0)
                {
                    summaryParts.Add(
                        "Строк с отличиями: "
                        + highlightedRows.ToString(CultureInfo.InvariantCulture)
                        + ". Наведите указатель на предупреждение для подробностей.");
                }
                if (additionalExistingCount > 0)
                {
                    string additionalComponents = string.Join(
                        ", ",
                        existingComponents.Skip(EditorRows.Count).Select(FormatExistingComponent));
                    summaryParts.Add(
                        "В существующем типе дополнительно: " + additionalComponents + ".");
                }
                _editorExistingTypeDifferencesText = string.Join(" ", summaryParts);
            }
            finally
            {
                _isUpdatingEditorDifferences = false;
            }
        }

        private LintelCatalogItemV3 FindExistingComponentCatalogItem(ExistingLintelComponentV3 component)
        {
            if (component == null) return null;
            return _lintelCatalog.FirstOrDefault(item =>
                       string.Equals(item.Mark, component.TypeName, StringComparison.OrdinalIgnoreCase)
                       && (string.IsNullOrWhiteSpace(component.FamilyName)
                           || string.IsNullOrWhiteSpace(item.RevitFamilyName)
                           || string.Equals(
                               item.RevitFamilyName,
                               component.FamilyName,
                               StringComparison.OrdinalIgnoreCase)))
                   ?? _lintelCatalog.FirstOrDefault(item =>
                       string.Equals(item.Mark, component.TypeName, StringComparison.OrdinalIgnoreCase));
        }

        private static void AddDimensionDifference(
            ICollection<string> differences,
            string dimensionName,
            int existingValueMm,
            int selectedValueMm)
        {
            if (existingValueMm == selectedValueMm) return;
            differences.Add(
                dimensionName + ": существует "
                + existingValueMm.ToString(CultureInfo.InvariantCulture)
                + " мм, выбрано "
                + selectedValueMm.ToString(CultureInfo.InvariantCulture) + " мм");
        }

        private static string FormatExistingComponent(ExistingLintelComponentV3 component)
        {
            if (component == null) return "не определено";
            return string.IsNullOrWhiteSpace(component.FamilyName)
                ? component.TypeName ?? "не определено"
                : component.FamilyName + " : " + (component.TypeName ?? "не определено");
        }

        private static string FormatEditorComponent(LintelCatalogItemV3 item)
        {
            if (item == null) return "не определено";
            return string.IsNullOrWhiteSpace(item.RevitFamilyName)
                ? item.Mark ?? "не определено"
                : item.RevitFamilyName + " : " + (item.Mark ?? "не определено");
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
            List<string> tokens = EditorRows
                .Select(row => GetLintelTypeCode(row.SelectedCatalogItem, row.IsBearing, row.WidthMm))
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToList();
            return string.Join("_", tokens);
        }

        private static string GetLintelTypeCode(
            LintelCatalogItemV3 item,
            bool isBearing,
            int widthMm)
        {
            if (item == null) return string.Empty;

            string description = string.Join(" ", new[]
            {
                item.Family,
                item.TypeCode,
                item.Mark,
                item.RevitFamilyName,
                item.Material
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            if (Contains(description, "пенополист")
                || Contains(description, "легкий бетон")
                || Contains(description, "лёгкий бетон"))
                return "лб";
            if (Contains(description, "монолит"))
                return "МП";
            if (Contains(description, "прогон"))
                return "ПР";
            if (Contains(description, "швел"))
                return "Шв";
            if (Contains(description, "двутавр") || Contains(description, "двутав"))
                return "Дв";
            if (Contains(description, "арматур"))
                return "А";
            if (Contains(description, "угол"))
                return widthMm >= 125 || Contains(description, "125") ? "У" : "у";
            if (Contains(description, "плит") || widthMm >= 350)
                return isBearing ? "ППП" : "ппп";
            if (widthMm >= 200)
                return isBearing ? "ББ" : "бб";
            return isBearing ? "Б" : "б";
        }

        private static int NormalizeWallWidth(int wallWidthMm)
        {
            if (wallWidthMm == 400) return 380;
            if (wallWidthMm == 500) return 510;
            if (wallWidthMm == 600) return 640;
            return wallWidthMm;
        }

        private void RefreshExistingCompositeTypeCache(Document document)
        {
            List<FamilySymbol> symbols = new FilteredElementCollector(document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(symbol => string.Equals(
                    symbol.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString(),
                    "Перемычки составные",
                    StringComparison.OrdinalIgnoreCase))
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Name))
                .ToList();
            var options = new List<ExistingLintelTypeOptionV3>();
            foreach (FamilySymbol symbol in symbols)
            {
                var option = new ExistingLintelTypeOptionV3
                {
                    TypeId = symbol.Id,
                    FamilyName = symbol.FamilyName,
                    TypeName = symbol.Name,
                    SupportCategory = GetCompositeTypeSupportCategory(symbol.Name)
                };
                option.Components.AddRange(
                    LintelPlacementEngineV3.ReadCompositeSymbolComponents(document, symbol));
                options.Add(option);
            }

            _allExistingLintelTypeOptions = options;
            _existingCompositeTypeNames.Clear();
            foreach (string name in options
                         .Select(option => option.TypeName?.Trim())
                         .Where(name => !string.IsNullOrWhiteSpace(name)))
                _existingCompositeTypeNames.Add(name);
        }

        private static int GetCompositeTypeSupportCategory(string typeName)
        {
            if (IsErrorCompositeTypeName(typeName)) return 2;
            string[] parts = (typeName ?? string.Empty).Split('_');
            if (parts.Length < 4) return 0;
            bool firstBearing = (parts[3] ?? string.Empty).Any(char.IsUpper);
            if (parts.Length == 4) return firstBearing ? 1 : 0;
            bool secondBearing = (parts[parts.Length - 1] ?? string.Empty).Any(char.IsUpper);
            if (firstBearing && secondBearing) return 2;
            return firstBearing || secondBearing ? 1 : 0;
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
                item.RevitFamilyName = null;
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
            else
            {
                ApplyActiveVariantStatus(group);
            }
        }

        private static void ApplyActiveVariantStatus(OpeningGroupCardV3 group)
        {
            if (group == null || group.HasExistingLintel) return;
            LintelSelectionVariantV3 activeVariant = group.ActiveVariant
                                                     ?? group.CalculatedVariants.FirstOrDefault();
            if (activeVariant == null)
            {
                group.Status = OpeningStatusV3.Error;
                group.StatusText = "Варианты не найдены";
            }
            else if (activeVariant.HasExistingTypeDifference)
            {
                group.Status = OpeningStatusV3.Warning;
                group.StatusText = "Состав существующего типа отличается";
                group.CalculationMessage = (group.CalculationBaseMessage ?? string.Empty)
                                           + (string.IsNullOrWhiteSpace(group.CalculationBaseMessage)
                                               ? string.Empty
                                               : " ")
                                           + "Предупреждение: "
                                           + activeVariant.ExistingTypeDifferenceText;
            }
            else if (activeVariant.IsExact)
            {
                group.Status = OpeningStatusV3.Success;
                group.StatusText = "Подобрано " + group.CalculatedVariants.Count + " вар.";
                group.CalculationMessage = group.CalculationBaseMessage;
            }
            else
            {
                group.Status = OpeningStatusV3.Warning;
                group.StatusText = "Отклонение " + activeVariant.WidthDeltaMm + " мм";
                group.CalculationMessage = group.CalculationBaseMessage;
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
                RefreshExistingCompositeTypeCache(_document);
                ApplyRevitFamilyNames(_document, _lintelCatalog);
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

        internal bool BeginReload()
        {
            if (!CanInteract) return false;
            IsCollectionInProgress = true;
            CollectionOpeningTotal = 0;
            ProcessedCollectionOpeningCount = 0;
            SelectionMessage = "Повторный сбор проёмов из модели Revit.";
            return true;
        }

        internal void CancelReload(string message)
        {
            IsCollectionInProgress = false;
            SelectionMessage = string.IsNullOrWhiteSpace(message)
                ? "Повторный сбор проёмов не выполнен."
                : message;
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
                    StringComparison.Ordinal));
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
            RaisePropertyChanged(nameof(CanPlaceSelectedLintels));
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
                    ExistingLintelTypeNames = JoinDistinctTypeNames(sourceGroup.Select(x => x.ExistingLintelTypeNames)),
                    InstanceCount = sourceGroup.Count(),
                    Status = OpeningStatusV3.Error,
                    StatusText = "Подбор перемычки не выполнен"
                };
                card.ElementIds.AddRange(sourceGroup.SelectMany(x => x.ElementIds).GroupBy(x => x.Value).Select(x => x.First()));
                foreach (OpeningRecordV3 opening in sourceGroup)
                {
                    var target = new OpeningPlacementTargetV3
                    {
                        WallId = opening.WallId,
                        LevelId = opening.LevelId,
                        Location = opening.Location,
                        TopElevation = opening.TopElevation,
                        WallOrientation = opening.WallOrientation,
                        SupportDirection = opening.SupportDirection,
                        SupportType = opening.SupportType
                    };
                    target.OpeningIds.AddRange(opening.ElementIds);
                    card.PlacementTargets.Add(target);
                }
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

        private static string JoinDistinctTypeNames(IEnumerable<string> values)
        {
            return string.Join(" + ", values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal));
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

                    DetectSupport(
                        hostWall,
                        GetSupportNormal(opening, hostWall),
                        location,
                        GetSupportCheckTop(opening, box),
                        width,
                        supports,
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
            opening.ExistingLintelTypeNames = JoinDistinctTypeNames(
                matches.Select(instance => instance.Symbol?.Name));
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

        private static string JoinDistinctTypeNames(IEnumerable<string> values)
        {
            return string.Join(" + ", values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal));
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
                    if (!HasParameter(element, "Опирание 1 итог")
                        || !HasParameter(element, "Опирание 2 итог"))
                        return false;

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

        private static bool HasParameter(Element element, string parameterName)
        {
            if (element?.LookupParameter(parameterName) != null) return true;
            return element is FamilyInstance familyInstance
                   && familyInstance.Symbol?.LookupParameter(parameterName) != null;
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
                bearingZone = Math.Round((firstBearing + secondBearing) / 2.0);
            }

            return new SupportBoxV3
            {
                ElementId = element.Id,
                Box = GetLargestSolidBoundingBox(element),
                BearingZoneMm = bearingZone,
                ParameterError = error
            };
        }

        private static BoundingBoxXYZ GetLargestSolidBoundingBox(Element element)
        {
            try
            {
                GeometryElement geometry = element?.get_Geometry(new Options
                {
                    ComputeReferences = false,
                    DetailLevel = ViewDetailLevel.Fine
                });
                if (geometry == null) return null;

                var solids = new List<Solid>();
                foreach (GeometryObject geometryObject in geometry)
                {
                    if (geometryObject is Solid directSolid && directSolid.Volume > 0)
                    {
                        solids.Add(directSolid);
                        continue;
                    }

                    if (!(geometryObject is GeometryInstance geometryInstance)) continue;
                    foreach (GeometryObject instanceObject in geometryInstance.GetInstanceGeometry())
                    {
                        if (instanceObject is Solid instanceSolid && instanceSolid.Volume > 0)
                            solids.Add(instanceSolid);
                    }
                }

                Solid largestSolid = solids
                    .OrderByDescending(solid => solid.Volume)
                    .FirstOrDefault();
                return largestSolid == null
                    ? null
                    : ToWorldAxisAlignedBox(largestSolid.GetBoundingBox());
            }
            catch
            {
                // Как и в v2, элементы без доступного объёмного Solid не участвуют
                // в определении опирания.
                return null;
            }
        }

        private static BoundingBoxXYZ ToWorldAxisAlignedBox(BoundingBoxXYZ source)
        {
            if (source == null) return null;
            Transform transform = source.Transform ?? Transform.Identity;
            var corners = new[]
            {
                new XYZ(source.Min.X, source.Min.Y, source.Min.Z),
                new XYZ(source.Min.X, source.Min.Y, source.Max.Z),
                new XYZ(source.Min.X, source.Max.Y, source.Min.Z),
                new XYZ(source.Min.X, source.Max.Y, source.Max.Z),
                new XYZ(source.Max.X, source.Min.Y, source.Min.Z),
                new XYZ(source.Max.X, source.Min.Y, source.Max.Z),
                new XYZ(source.Max.X, source.Max.Y, source.Min.Z),
                new XYZ(source.Max.X, source.Max.Y, source.Max.Z)
            }.Select(transform.OfPoint).ToList();

            return new BoundingBoxXYZ
            {
                Min = new XYZ(corners.Min(point => point.X), corners.Min(point => point.Y), corners.Min(point => point.Z)),
                Max = new XYZ(corners.Max(point => point.X), corners.Max(point => point.Y), corners.Max(point => point.Z))
            };
        }

        private static void DetectSupport(
            Wall wall,
            XYZ openingNormal,
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
            XYZ normal = NormalizeInPlan(openingNormal)
                         ?? NormalizeInPlan(wall.Orientation)
                         ?? XYZ.BasisX;
            XYZ along = normal.CrossProduct(XYZ.BasisZ).Normalize();
            XYZ center = new XYZ(location.X, location.Y, top);
            XYZ firstFaceCenter = center - normal * (wall.Width / 2.0);
            XYZ secondFaceCenter = center + normal * (wall.Width / 2.0);
            double openingHalfWidth = openingWidthMm / (2.0 * MillimetersPerFoot);
            double maximumVerticalDistance = openingWidthMm / MillimetersPerFoot;
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
                // Правило исходной реализации: низ опоры должен быть выше верха
                // проёма, но не дальше величины ширины проёма.
                double verticalDistance = box.Min.Z + 0.0001 - top;
                if (verticalDistance < 1e-6 || verticalDistance > maximumVerticalDistance)
                    continue;

                bool isFirstSide = ContainsSupportSample(box, firstFaceCenter, along, openingHalfWidth);
                bool isSecondSide = ContainsSupportSample(box, secondFaceCenter, along, openingHalfWidth);

                if (isFirstSide)
                {
                    first = true;
                    if (support.BearingZoneMm > 0 && firstZone <= 0)
                        firstZone = Math.Min(wallWidthMm, support.BearingZoneMm);
                    else if (!string.IsNullOrWhiteSpace(support.ParameterError))
                        firstErrors.Add(support.ParameterError);
                }
                if (isSecondSide)
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

        private static XYZ GetSupportNormal(Element opening, Wall hostWall)
        {
            if (opening is FamilyInstance familyInstance)
                return NormalizeInPlan(familyInstance.FacingOrientation) ?? hostWall.Orientation;
            if (opening?.Location is LocationCurve locationCurve)
            {
                XYZ lineDirection = NormalizeInPlan(
                    locationCurve.Curve.GetEndPoint(1) - locationCurve.Curve.GetEndPoint(0));
                XYZ curveNormal = NormalizeInPlan(lineDirection?.CrossProduct(XYZ.BasisZ));
                if (curveNormal != null) return curveNormal;
            }
            return hostWall.Orientation;
        }

        private static double GetSupportCheckTop(Element opening, BoundingBoxXYZ box)
        {
            if (opening is FamilyInstance && opening.Location is LocationPoint locationPoint)
                return locationPoint.Point.Z + box.Max.Z - box.Min.Z;
            return box.Max.Z;
        }

        private static bool ContainsSupportSample(
            BoundingBoxXYZ box,
            XYZ faceCenter,
            XYZ widthDirection,
            double openingHalfWidth)
        {
            const double tolerance = 5e-6;
            XYZ firstEdge = faceCenter - widthDirection * openingHalfWidth;
            XYZ secondEdge = faceCenter + widthDirection * openingHalfWidth;
            return IsInsideHorizontalBounds(box, faceCenter, tolerance)
                   || IsInsideHorizontalBounds(box, firstEdge, tolerance)
                   || IsInsideHorizontalBounds(box, secondEdge, tolerance);
        }

        private static bool IsInsideHorizontalBounds(BoundingBoxXYZ box, XYZ point, double tolerance)
        {
            return point.X > box.Min.X + tolerance
                   && point.X < box.Max.X - tolerance
                   && point.Y > box.Min.Y + tolerance
                   && point.Y < box.Max.Y - tolerance;
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

    internal static class LintelPlacementEngineV3
    {
        private const double MillimetersPerFoot = 304.8;

        public static LintelPlacementResultV3 Execute(
            Document document,
            LintelPlacementRequestV3 request)
        {
            var result = new LintelPlacementResultV3();
            if (document == null || request?.Groups == null || request.Groups.Count == 0)
            {
                result.FatalError = "Не сформировано задание на размещение перемычек.";
                return result;
            }

            List<FamilySymbol> compositeSymbols = new FilteredElementCollector(document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(IsCompositeLintelSymbol)
                .ToList();
            if (compositeSymbols.Count == 0)
            {
                result.FatalError = "В проекте не найдено семейство с моделью типа «Перемычки составные».";
                return result;
            }

            Dictionary<string, List<FamilySymbol>> compositeSymbolsByName = compositeSymbols
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Name))
                .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            var symbolsByGroup = new Dictionary<string, FamilySymbol>(StringComparer.Ordinal);
            var typeResolutionsByGroup = new Dictionary<string, CompositeTypeResolutionV3>(StringComparer.Ordinal);
            var typeErrors = new Dictionary<string, string>(StringComparer.Ordinal);
            var groupsRequiringTypeChanges = new List<LintelPlacementGroupRequestV3>();
            foreach (LintelPlacementGroupRequestV3 group in request.Groups)
            {
                FamilySymbol existing = compositeSymbolsByName.TryGetValue(
                        group.CompositeTypeName,
                        out List<FamilySymbol> exactNameMatches)
                    ? exactNameMatches
                        .OrderByDescending(CountAvailableSlots)
                        .ThenBy(symbol => symbol.FamilyName, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault()
                    : null;
                if (existing == null)
                {
                    groupsRequiringTypeChanges.Add(group);
                    continue;
                }

                if (!group.HasExistingTypeDifference
                    || request.NameConflictAction == CompositeTypeNameConflictActionV3.UseExisting)
                {
                    symbolsByGroup[group.GroupKey] = existing;
                    typeResolutionsByGroup[group.GroupKey] = new CompositeTypeResolutionV3
                    {
                        Symbol = existing,
                        ActualTypeName = existing.Name,
                        HasConflict = group.HasExistingTypeDifference,
                        ActionText = group.HasExistingTypeDifference
                            ? "Использован текущий существующий тип без изменения."
                            : "Использован существующий тип с совпадающим именем.",
                        Differences = group.HasExistingTypeDifference
                            ? group.ExistingTypeDifferenceText
                            : string.Empty
                    };
                    continue;
                }

                if (request.NameConflictAction == CompositeTypeNameConflictActionV3.Cancel)
                {
                    string differences = group.ExistingTypeDifferenceText ?? string.Empty;
                    typeResolutionsByGroup[group.GroupKey] = new CompositeTypeResolutionV3
                    {
                        ActualTypeName = group.CompositeTypeName,
                        HasConflict = true,
                        WasCancelled = true,
                        ActionText = "Размещение конфликтующей группы отменено.",
                        Differences = differences,
                        Error = "Имя «" + group.CompositeTypeName
                                + "» уже занято отличающимся типом. Размещение отменено согласно настройке."
                    };
                    typeErrors[group.GroupKey] = typeResolutionsByGroup[group.GroupKey].Error;
                    continue;
                }

                groupsRequiringTypeChanges.Add(group);
            }

            TransactionGroup transactionGroup = groupsRequiringTypeChanges.Count > 0
                ? new TransactionGroup(document, "Создание и размещение перемычек v3")
                : null;
            using (transactionGroup)
            {
                transactionGroup?.Start();
                try
                {
                    if (groupsRequiringTypeChanges.Count > 0)
                    {
                        Dictionary<string, List<FamilySymbol>> unitSymbolsByName =
                            new FilteredElementCollector(document)
                                .OfClass(typeof(FamilySymbol))
                                .Cast<FamilySymbol>()
                                .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Name))
                                .GroupBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(
                                    group => group.Key,
                                    group => group.ToList(),
                                    StringComparer.OrdinalIgnoreCase);
                        List<FamilySymbol> compositeCandidates = compositeSymbols
                            .OrderByDescending(CountAvailableSlots)
                            .ThenBy(symbol => symbol.FamilyName, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        using (var typeTransaction = new Transaction(document, "Создание типов перемычек"))
                        {
                            typeTransaction.Start();
                            foreach (LintelPlacementGroupRequestV3 group in groupsRequiringTypeChanges)
                            {
                                try
                                {
                                    List<FamilySymbol> componentSymbols = group.Components
                                        .Select(component => FindUnitSymbol(unitSymbolsByName, component))
                                        .ToList();
                                    if (componentSymbols.Any(symbol => symbol == null))
                                    {
                                        LintelPlacementComponentRequestV3 missing = group.Components
                                            .Zip(componentSymbols, (component, symbol) => new { component, symbol })
                                            .First(item => item.symbol == null)
                                            .component;
                                        throw new InvalidOperationException(
                                            "Не найден тип вложенной перемычки «" + missing.Mark + "»"
                                            + (string.IsNullOrWhiteSpace(missing.RevitFamilyName)
                                                ? "."
                                                : " в семействе «" + missing.RevitFamilyName + "»."));
                                    }

                                    CompositeTypeResolutionV3 resolution = GetOrCreateCompositeSymbol(
                                        document,
                                        compositeCandidates,
                                        compositeSymbolsByName,
                                        group,
                                        componentSymbols,
                                        request.NameConflictAction);
                                    typeResolutionsByGroup[group.GroupKey] = resolution;
                                    if (resolution.Symbol == null)
                                    {
                                        typeErrors[group.GroupKey] = resolution.Error
                                                                     ?? "Создание типа отменено.";
                                        continue;
                                    }
                                    symbolsByGroup[group.GroupKey] = resolution.Symbol;
                                }
                                catch (Exception exception)
                                {
                                    typeErrors[group.GroupKey] = exception.Message;
                                }
                            }
                            typeTransaction.Commit();
                        }
                    }

                    if (symbolsByGroup.Count == 0)
                    {
                        foreach (LintelPlacementGroupRequestV3 group in request.Groups)
                        {
                            var groupResult = new LintelPlacementGroupResultV3
                            {
                                GroupKey = group.GroupKey,
                                RequestedTypeName = group.CompositeTypeName,
                                TypeName = group.CompositeTypeName,
                                Error = typeErrors.TryGetValue(group.GroupKey, out string typeError)
                                    ? "Тип «" + group.CompositeTypeName + "»: " + typeError
                                    : "Тип «" + group.CompositeTypeName + "»: размещение отменено."
                            };
                            if (typeResolutionsByGroup.TryGetValue(
                                    group.GroupKey,
                                    out CompositeTypeResolutionV3 typeResolution))
                            {
                                groupResult.TypeName = typeResolution.ActualTypeName
                                                       ?? group.CompositeTypeName;
                                groupResult.HasTypeNameConflict = typeResolution.HasConflict;
                                groupResult.WasCancelledByTypeNameConflict = typeResolution.WasCancelled;
                                groupResult.TypeNameConflictAction = typeResolution.ActionText;
                                groupResult.TypeNameConflictDifferences = typeResolution.Differences;
                            }
                            result.Groups.Add(groupResult);
                        }
                        transactionGroup?.Assimilate();
                        return result;
                    }

                    List<double> nonNegativeLevelElevations = new FilteredElementCollector(document)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .Where(level => level.Elevation >= 0)
                        .OrderBy(level => level.Elevation)
                        .Select(level => level.Elevation)
                        .ToList();

                    using (var placementTransaction = new Transaction(document, "Размещение перемычек"))
                    {
                        placementTransaction.Start();
                        try
                        {
                            var allPlacedLintels = new List<PlacedLintelDataV3>();
                            List<FamilySymbol> symbolsToActivate = symbolsByGroup.Values
                                .Where(symbol => symbol != null && !symbol.IsActive)
                                .GroupBy(symbol => symbol.Id.Value)
                                .Select(group => group.First())
                                .ToList();
                            foreach (FamilySymbol symbolToActivate in symbolsToActivate)
                                symbolToActivate.Activate();
                            // Одна регенерация подготавливает и активированные, и только что
                            // созданные типы перед пакетным размещением всех экземпляров.
                            bool hasChangedOrCreatedTypes = groupsRequiringTypeChanges.Any(group =>
                                symbolsByGroup.ContainsKey(group.GroupKey));
                            if (symbolsToActivate.Count > 0 || hasChangedOrCreatedTypes)
                                document.Regenerate();

                            var componentsBySymbolId = new Dictionary<long, List<ExistingLintelComponentV3>>();
                            foreach (LintelPlacementGroupRequestV3 group in request.Groups)
                            {
                                var groupResult = new LintelPlacementGroupResultV3
                                {
                                    GroupKey = group.GroupKey,
                                    RequestedTypeName = group.CompositeTypeName,
                                    TypeName = group.CompositeTypeName
                                };
                                if (typeResolutionsByGroup.TryGetValue(
                                        group.GroupKey,
                                        out CompositeTypeResolutionV3 typeResolution))
                                {
                                    groupResult.TypeName = typeResolution.ActualTypeName
                                                           ?? group.CompositeTypeName;
                                    groupResult.HasTypeNameConflict = typeResolution.HasConflict;
                                    groupResult.WasCancelledByTypeNameConflict = typeResolution.WasCancelled;
                                    groupResult.TypeNameConflictAction = typeResolution.ActionText;
                                    groupResult.TypeNameConflictDifferences = typeResolution.Differences;
                                }
                                result.Groups.Add(groupResult);

                                if (typeErrors.TryGetValue(group.GroupKey, out string typeError)
                                    || !symbolsByGroup.TryGetValue(group.GroupKey, out FamilySymbol symbol))
                                {
                                    groupResult.Error = "Тип «" + group.CompositeTypeName + "»: "
                                                        + (typeError ?? "не удалось создать тип.");
                                    continue;
                                }

                                if (!componentsBySymbolId.TryGetValue(
                                        symbol.Id.Value,
                                        out List<ExistingLintelComponentV3> actualComponents))
                                {
                                    actualComponents = ReadCompositeSymbolComponents(document, symbol);
                                    componentsBySymbolId[symbol.Id.Value] = actualComponents;
                                }
                                groupResult.Components.AddRange(actualComponents.Count > 0
                                    ? actualComponents
                                    : group.Components.Select((component, index) =>
                                        new ExistingLintelComponentV3
                                        {
                                            FamilyName = component.RevitFamilyName,
                                            TypeName = component.Mark,
                                            Order = index,
                                            OffsetToNextMm = index < group.Components.Count - 1
                                                ? component.WidthMm + component.GapAfterMm
                                                : 0
                                        }));

                                using (var groupSubTransaction = new SubTransaction(document))
                                {
                                    groupSubTransaction.Start();
                                    try
                                    {
                                        var placedLintels = new List<PlacedLintelDataV3>();
                                        foreach (OpeningPlacementTargetV3 target in group.Targets)
                                        {
                                            placedLintels.Add(PlaceLintel(
                                                document,
                                                symbol,
                                                group.WallTypeName,
                                                target,
                                                nonNegativeLevelElevations));
                                        }

                                        groupSubTransaction.Commit();
                                        allPlacedLintels.AddRange(placedLintels);
                                        groupResult.IsSuccess = true;
                                        groupResult.FamilyName = symbol.FamilyName;
                                        groupResult.CreatedLintelIds.AddRange(
                                            placedLintels.Select(item => item.Instance.Id));
                                    }
                                    catch (Exception exception)
                                    {
                                        groupSubTransaction.RollBack();
                                        groupResult.Error = "Проёмы группы «" + group.CompositeTypeName
                                                            + "»: " + exception.Message;
                                    }
                                }
                            }

                            if (allPlacedLintels.Count > 0)
                            {
                                // Вложенные экземпляры становятся доступны после одной общей
                                // регенерации вместо отдельной регенерации для каждой группы.
                                document.Regenerate();
                                foreach (PlacedLintelDataV3 placedLintel in allPlacedLintels)
                                {
                                    ApplyBaseWallType(
                                        document,
                                        placedLintel.Instance,
                                        placedLintel.Wall,
                                        placedLintel.WallTypeName);
                                }
                            }

                            placementTransaction.Commit();
                        }
                        catch
                        {
                            placementTransaction.RollBack();
                            throw;
                        }
                    }

                    transactionGroup?.Assimilate();
                }
                catch (Exception exception)
                {
                    transactionGroup?.RollBack();
                    result.Groups.Clear();
                    result.FatalError = exception.Message;
                }
            }
            return result;
        }

        private sealed class PlacedLintelDataV3
        {
            public FamilyInstance Instance { get; set; }
            public Wall Wall { get; set; }
            public string WallTypeName { get; set; }
        }

        private sealed class CompositeTypeResolutionV3
        {
            public FamilySymbol Symbol { get; set; }
            public string ActualTypeName { get; set; }
            public bool HasConflict { get; set; }
            public bool WasCancelled { get; set; }
            public string ActionText { get; set; }
            public string Differences { get; set; }
            public string Error { get; set; }
        }

        private static bool IsCompositeLintelSymbol(FamilySymbol symbol)
        {
            return symbol != null
                   && string.Equals(
                       symbol.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString(),
                       "Перемычки составные",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static FamilySymbol FindUnitSymbol(
            IDictionary<string, List<FamilySymbol>> symbolsByName,
            LintelPlacementComponentRequestV3 component)
        {
            if (symbolsByName == null
                || component == null
                || string.IsNullOrWhiteSpace(component.Mark)
                || !symbolsByName.TryGetValue(component.Mark, out List<FamilySymbol> matches))
                return null;
            if (!string.IsNullOrWhiteSpace(component.RevitFamilyName))
            {
                FamilySymbol exact = matches.FirstOrDefault(symbol => string.Equals(
                    symbol.FamilyName,
                    component.RevitFamilyName,
                    StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }
            return matches.FirstOrDefault();
        }

        private static CompositeTypeResolutionV3 GetOrCreateCompositeSymbol(
            Document document,
            IList<FamilySymbol> compositeCandidates,
            IDictionary<string, List<FamilySymbol>> compositeSymbolsByName,
            LintelPlacementGroupRequestV3 group,
            IList<FamilySymbol> componentSymbols,
            CompositeTypeNameConflictActionV3 conflictAction)
        {
            List<FamilySymbol> exactNameMatches = compositeSymbolsByName.TryGetValue(
                group.CompositeTypeName,
                out List<FamilySymbol> matches)
                ? matches
                : new List<FamilySymbol>();
            FamilySymbol existing = exactNameMatches
                .OrderByDescending(CountAvailableSlots)
                .ThenBy(symbol => symbol.FamilyName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (existing != null)
            {
                if (!group.HasExistingTypeDifference)
                {
                    return new CompositeTypeResolutionV3
                    {
                        Symbol = existing,
                        ActualTypeName = existing.Name,
                        ActionText = "Использован существующий тип с совпадающим именем."
                    };
                }

                if (conflictAction == CompositeTypeNameConflictActionV3.ReplaceExisting)
                {
                    using (var replaceTransaction = new SubTransaction(document))
                    {
                        replaceTransaction.Start();
                        try
                        {
                            ConfigureCompositeSymbol(existing, group.Components, componentSymbols);
                            if (!IsCompositeSymbolConfigured(existing, group.Components, componentSymbols))
                                throw new InvalidOperationException(
                                    "После замены состав типа не соответствует выбранному варианту.");
                            replaceTransaction.Commit();
                        }
                        catch
                        {
                            replaceTransaction.RollBack();
                            throw;
                        }
                    }
                    return new CompositeTypeResolutionV3
                    {
                        Symbol = existing,
                        ActualTypeName = existing.Name,
                        HasConflict = true,
                        ActionText = "Состав существующего типа заменён на выбранный;"
                                     + " изменение применилось ко всем его экземплярам.",
                        Differences = group.ExistingTypeDifferenceText
                    };
                }

                if (conflictAction == CompositeTypeNameConflictActionV3.AppendNumber)
                {
                    string numberedName = GetAvailableNumberedTypeName(
                        group.CompositeTypeName,
                        compositeSymbolsByName.Keys);
                    FamilySymbol numberedCreated = CreateCompositeSymbol(
                        document,
                        compositeCandidates,
                        compositeSymbolsByName,
                        numberedName,
                        group,
                        componentSymbols);
                    return new CompositeTypeResolutionV3
                    {
                        Symbol = numberedCreated,
                        ActualTypeName = numberedCreated.Name,
                        HasConflict = true,
                        ActionText = "Создан новый тип с номером «" + numberedCreated.Name + "».",
                        Differences = group.ExistingTypeDifferenceText
                    };
                }

                return new CompositeTypeResolutionV3
                {
                    Symbol = existing,
                    ActualTypeName = existing.Name,
                    HasConflict = group.HasExistingTypeDifference,
                    ActionText = "Использован текущий существующий тип без изменения.",
                    Differences = group.ExistingTypeDifferenceText
                };
            }

            FamilySymbol created = CreateCompositeSymbol(
                document,
                compositeCandidates,
                compositeSymbolsByName,
                group.CompositeTypeName,
                group,
                componentSymbols);
            return new CompositeTypeResolutionV3
            {
                Symbol = created,
                ActualTypeName = created.Name
            };
        }

        private static FamilySymbol CreateCompositeSymbol(
            Document document,
            IList<FamilySymbol> compositeCandidates,
            IDictionary<string, List<FamilySymbol>> compositeSymbolsByName,
            string typeName,
            LintelPlacementGroupRequestV3 group,
            IList<FamilySymbol> componentSymbols)
        {
            var errors = new List<string>();
            foreach (FamilySymbol candidate in compositeCandidates)
            {
                using (var subTransaction = new SubTransaction(document))
                {
                    subTransaction.Start();
                    try
                    {
                        FamilySymbol created = candidate.Duplicate(typeName) as FamilySymbol;
                        if (created == null)
                            throw new InvalidOperationException("Не удалось дублировать базовый тип.");
                        ConfigureCompositeSymbol(created, group.Components, componentSymbols);
                        if (!string.Equals(created.Name, typeName, StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                "Revit создал тип с именем «" + created.Name
                                + "» вместо «" + typeName + "».");
                        if (!IsCompositeSymbolConfigured(created, group.Components, componentSymbols))
                            throw new InvalidOperationException(
                                "После создания состав типа, видимость или отступы не совпали"
                                + " с выбранным вариантом.");
                        subTransaction.Commit();
                        if (!compositeSymbolsByName.TryGetValue(
                                typeName,
                                out List<FamilySymbol> createdMatches))
                        {
                            createdMatches = new List<FamilySymbol>();
                            compositeSymbolsByName[typeName] = createdMatches;
                        }
                        createdMatches.Add(created);
                        return created;
                    }
                    catch (Exception exception)
                    {
                        errors.Add(candidate.FamilyName + ": " + exception.Message);
                        subTransaction.RollBack();
                    }
                }
            }

            throw new InvalidOperationException(
                "Ни одно составное семейство не поддерживает выбранный комплект. "
                + string.Join(" ", errors.Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        private static string GetAvailableNumberedTypeName(
            string baseName,
            IEnumerable<string> existingNames)
        {
            var names = new HashSet<string>(existingNames ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            for (int number = 2; number < int.MaxValue; number++)
            {
                string candidate = baseName + "_" + number.ToString(CultureInfo.InvariantCulture);
                if (!names.Contains(candidate)) return candidate;
            }
            throw new InvalidOperationException("Не удалось подобрать свободный номер имени типа.");
        }

        private static bool IsNumberedTypeName(string baseName, string candidate)
        {
            return GetTypeNameNumber(baseName, candidate) >= 2;
        }

        private static int GetTypeNameNumber(string baseName, string candidate)
        {
            string prefix = (baseName ?? string.Empty) + "_";
            if (string.IsNullOrWhiteSpace(candidate)
                || !candidate.StartsWith(prefix, StringComparison.Ordinal))
                return -1;
            return int.TryParse(
                candidate.Substring(prefix.Length),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int number)
                ? number
                : -1;
        }

        private static bool IsCompositeSymbolConfigured(
            FamilySymbol symbol,
            IList<LintelPlacementComponentRequestV3> components,
            IList<FamilySymbol> componentSymbols)
        {
            if (symbol == null
                || components == null
                || componentSymbols == null
                || components.Count != componentSymbols.Count)
                return false;

            for (int index = 0; index < components.Count; index++)
            {
                int slot = index + 1;
                Parameter typeParameter = FindNestedTypeParameter(symbol, slot);
                if (typeParameter?.StorageType != StorageType.ElementId
                    || typeParameter.AsElementId()?.Value != componentSymbols[index].Id.Value)
                    return false;

                if (slot >= 2)
                {
                    Parameter visibility = FindParameterByNormalizedName(
                        symbol,
                        slot.ToString(CultureInfo.InvariantCulture) + "ПР.Видимость");
                    if (!IsBooleanParameterEnabled(visibility)) return false;
                }

                if (index < components.Count - 1)
                {
                    Parameter offset = FindParameterByNormalizedName(
                        symbol,
                        "Отступ от " + slot.ToString(CultureInfo.InvariantCulture)
                        + " до " + (slot + 1).ToString(CultureInfo.InvariantCulture));
                    double expectedOffset = components[index].WidthMm + components[index].GapAfterMm;
                    double actualOffset = GetLengthParameterMm(offset);
                    if (double.IsNaN(actualOffset)
                        || Math.Abs(actualOffset - expectedOffset) > 0.5)
                        return false;
                }
            }

            foreach (Parameter visibility in symbol.Parameters.Cast<Parameter>())
            {
                if (TryGetVisibilitySlot(visibility.Definition?.Name, out int slot)
                    && slot > components.Count
                    && IsBooleanParameterEnabled(visibility))
                    return false;
            }
            return true;
        }

        private static List<string> GetCompositeConfigurationDifferences(
            Document document,
            FamilySymbol symbol,
            IList<LintelPlacementComponentRequestV3> components,
            IList<FamilySymbol> componentSymbols)
        {
            var differences = new List<string>();
            if (symbol == null || components == null || componentSymbols == null)
            {
                differences.Add("Не удалось прочитать состав существующего типа");
                return differences;
            }

            for (int index = 0; index < components.Count; index++)
            {
                int slot = index + 1;
                Parameter typeParameter = FindNestedTypeParameter(symbol, slot);
                FamilySymbol actualSymbol = typeParameter?.StorageType == StorageType.ElementId
                    ? document.GetElement(typeParameter.AsElementId()) as FamilySymbol
                    : null;
                FamilySymbol expectedSymbol = componentSymbols[index];
                if (actualSymbol?.Id.Value != expectedSymbol?.Id.Value)
                {
                    differences.Add(
                        slot.ToString(CultureInfo.InvariantCulture) + "ПР: было «"
                        + FormatFamilySymbol(actualSymbol) + "», требуется «"
                        + FormatFamilySymbol(expectedSymbol) + "»");
                }

                if (slot >= 2)
                {
                    Parameter visibility = FindParameterByNormalizedName(
                        symbol,
                        slot.ToString(CultureInfo.InvariantCulture) + "ПР.Видимость");
                    if (!IsBooleanParameterEnabled(visibility))
                    {
                        differences.Add(
                            slot.ToString(CultureInfo.InvariantCulture)
                            + "ПР: было скрыто, требуется показать");
                    }
                }

                if (index < components.Count - 1)
                {
                    string offsetName = "Отступ от " + slot.ToString(CultureInfo.InvariantCulture)
                                        + " до " + (slot + 1).ToString(CultureInfo.InvariantCulture);
                    double actualOffset = GetLengthParameterMm(
                        FindParameterByNormalizedName(symbol, offsetName));
                    double expectedOffset = components[index].WidthMm + components[index].GapAfterMm;
                    if (double.IsNaN(actualOffset) || Math.Abs(actualOffset - expectedOffset) > 0.5)
                    {
                        differences.Add(
                            offsetName + ": было "
                            + (double.IsNaN(actualOffset)
                                ? "не задано"
                                : Math.Round(actualOffset).ToString(CultureInfo.InvariantCulture) + " мм")
                            + ", требуется " + Math.Round(expectedOffset).ToString(CultureInfo.InvariantCulture)
                            + " мм");
                    }
                }
            }

            foreach (Parameter visibility in symbol.Parameters.Cast<Parameter>())
            {
                if (TryGetVisibilitySlot(visibility.Definition?.Name, out int slot)
                    && slot > components.Count
                    && IsBooleanParameterEnabled(visibility))
                {
                    differences.Add(
                        slot.ToString(CultureInfo.InvariantCulture)
                        + "ПР: было показано, требуется скрыть");
                }
            }

            if (differences.Count == 0)
                differences.Add("Параметры типа отличаются от выбранного варианта");
            return differences;
        }

        private static string FormatFamilySymbol(FamilySymbol symbol)
        {
            return symbol == null
                ? "не задано"
                : (symbol.FamilyName ?? string.Empty) + " : " + (symbol.Name ?? string.Empty);
        }

        internal static List<ExistingLintelComponentV3> ReadCompositeSymbolComponents(
            Document document,
            FamilySymbol symbol)
        {
            var result = new List<ExistingLintelComponentV3>();
            if (document == null || symbol == null) return result;

            for (int slot = 1; slot <= 16; slot++)
            {
                Parameter typeParameter = FindNestedTypeParameter(symbol, slot);
                FamilySymbol componentSymbol = typeParameter?.StorageType == StorageType.ElementId
                    ? document.GetElement(typeParameter.AsElementId()) as FamilySymbol
                    : null;
                if (componentSymbol == null) continue;
                if (slot >= 2)
                {
                    Parameter visibility = FindParameterByNormalizedName(
                        symbol,
                        slot.ToString(CultureInfo.InvariantCulture) + "ПР.Видимость");
                    if (visibility != null && !IsBooleanParameterEnabled(visibility)) continue;
                }

                var component = new ExistingLintelComponentV3
                {
                    FamilyName = componentSymbol.FamilyName,
                    TypeName = componentSymbol.Name,
                    Order = result.Count
                };
                string offsetName = "Отступ от " + slot.ToString(CultureInfo.InvariantCulture)
                                    + " до " + (slot + 1).ToString(CultureInfo.InvariantCulture);
                double offset = GetLengthParameterMm(FindParameterByNormalizedName(symbol, offsetName));
                component.OffsetToNextMm = double.IsNaN(offset) ? 0 : offset;
                result.Add(component);
            }

            if (result.Count > 0)
                result[result.Count - 1].OffsetToNextMm = 0;
            return result;
        }

        private static bool IsBooleanParameterEnabled(Parameter parameter)
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

        private static double GetLengthParameterMm(Parameter parameter)
        {
            if (parameter == null) return double.NaN;
            if (parameter.StorageType == StorageType.Double)
                return parameter.AsDouble() * MillimetersPerFoot;
            if (parameter.StorageType == StorageType.Integer)
                return parameter.AsInteger();
            string text = parameter.AsString() ?? parameter.AsValueString();
            return double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out double value)
                   || double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
                ? value
                : double.NaN;
        }

        private static void ConfigureCompositeSymbol(
            FamilySymbol symbol,
            IList<LintelPlacementComponentRequestV3> components,
            IList<FamilySymbol> componentSymbols)
        {
            for (int index = 0; index < components.Count; index++)
            {
                int slot = index + 1;
                Parameter typeParameter = FindNestedTypeParameter(symbol, slot);
                if (typeParameter == null || typeParameter.IsReadOnly)
                    throw new InvalidOperationException(
                        "Не найден доступный параметр типа для " + slot + "ПР.");
                if (!typeParameter.Set(componentSymbols[index].Id))
                    throw new InvalidOperationException(
                        "Параметр «" + typeParameter.Definition?.Name
                        + "» не принял тип «" + componentSymbols[index].Name + "».");

                if (slot >= 2)
                {
                    Parameter visibility = FindParameterByNormalizedName(
                        symbol,
                        slot.ToString(CultureInfo.InvariantCulture) + "ПР.Видимость");
                    if (visibility == null || visibility.IsReadOnly)
                        throw new InvalidOperationException(
                            "Не найден параметр «" + slot + "ПР.Видимость».");
                    SetBooleanParameter(visibility, true);
                }

                if (index < components.Count - 1)
                {
                    string offsetName = "Отступ от "
                                        + slot.ToString(CultureInfo.InvariantCulture)
                                        + " до " + (slot + 1).ToString(CultureInfo.InvariantCulture);
                    Parameter offset = FindParameterByNormalizedName(symbol, offsetName);
                    if (offset == null || offset.IsReadOnly)
                        throw new InvalidOperationException(
                            "Не найден параметр «" + offsetName + "».");
                    SetLengthParameter(
                        offset,
                        components[index].WidthMm + components[index].GapAfterMm);
                }
            }

            foreach (Parameter visibility in symbol.Parameters.Cast<Parameter>()
                         .Where(parameter => TryGetVisibilitySlot(parameter.Definition?.Name, out int slot)
                                             && slot > components.Count))
            {
                if (!visibility.IsReadOnly)
                    SetBooleanParameter(visibility, false);
            }
        }

        private static Parameter FindNestedTypeParameter(FamilySymbol symbol, int slot)
        {
            string prefix = slot.ToString(CultureInfo.InvariantCulture) + "ПР";
            string[] preferredNames =
            {
                prefix + ".Тип",
                prefix + ".Типоразмер",
                prefix + ".Семейство и типоразмер",
                prefix
            };
            List<Parameter> candidates = symbol.Parameters.Cast<Parameter>()
                .Where(parameter => parameter.StorageType == StorageType.ElementId)
                .Where(parameter =>
                {
                    string name = (parameter.Definition?.Name ?? string.Empty)
                        .Replace(" ", string.Empty);
                    return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                           || name.IndexOf("ПР", StringComparison.OrdinalIgnoreCase) >= 0
                           && TryGetFirstInteger(name, out int number)
                           && number == slot;
                })
                .Where(parameter => (parameter.Definition?.Name ?? string.Empty)
                    .IndexOf("Видимость", StringComparison.OrdinalIgnoreCase) < 0)
                .ToList();
            if (candidates.Count > 0)
            {
                return candidates
                    .OrderBy(parameter =>
                    {
                        string name = (parameter.Definition?.Name ?? string.Empty).Replace(" ", string.Empty);
                        int preferredIndex = Array.FindIndex(preferredNames, preferred =>
                            string.Equals(name, preferred.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase));
                        return preferredIndex < 0 ? preferredNames.Length : preferredIndex;
                    })
                    .ThenBy(parameter => parameter.Definition?.Name, StringComparer.OrdinalIgnoreCase)
                    .First();
            }

            return symbol.Parameters.Cast<Parameter>()
                .Where(parameter => parameter.StorageType == StorageType.ElementId)
                .Where(parameter => !parameter.IsReadOnly)
                .Where(parameter => IsNestedFamilyTypeParameter(symbol, parameter))
                .Where(parameter => TryGetFirstInteger(parameter.Definition?.Name, out int number)
                                    && number == slot)
                .OrderBy(parameter => parameter.Definition?.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static bool IsNestedFamilyTypeParameter(FamilySymbol owner, Parameter parameter)
        {
            ElementId valueId = parameter?.AsElementId();
            if (valueId != null
                && valueId != ElementId.InvalidElementId
                && owner.Document.GetElement(valueId) is FamilySymbol)
                return true;

            string name = parameter?.Definition?.Name ?? string.Empty;
            return name.IndexOf("перемыч", StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("тип", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Parameter FindParameterByNormalizedName(Element element, string parameterName)
        {
            Parameter exact = element?.LookupParameter(parameterName);
            if (exact != null) return exact;
            string normalized = NormalizeParameterName(parameterName);
            return element?.Parameters.Cast<Parameter>().FirstOrDefault(parameter =>
                string.Equals(
                    NormalizeParameterName(parameter.Definition?.Name),
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeParameterName(string name)
        {
            return new string((name ?? string.Empty)
                .Where(character => !char.IsWhiteSpace(character))
                .ToArray());
        }

        private static bool TryGetFirstInteger(string text, out int value)
        {
            value = 0;
            string digits = new string((text ?? string.Empty)
                .SkipWhile(character => !char.IsDigit(character))
                .TakeWhile(char.IsDigit)
                .ToArray());
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static int CountAvailableSlots(FamilySymbol symbol)
        {
            int nestedSlots = Enumerable.Range(1, 16)
                .Count(slot => FindNestedTypeParameter(symbol, slot) != null);
            int visibilitySlots = symbol.Parameters.Cast<Parameter>()
                .Count(parameter => TryGetVisibilitySlot(parameter.Definition?.Name, out int ignored));
            return nestedSlots * 100 + visibilitySlots;
        }

        private static bool TryGetVisibilitySlot(string name, out int slot)
        {
            slot = 0;
            const string suffix = "ПР.Видимость";
            string normalized = (name ?? string.Empty).Replace(" ", string.Empty);
            if (!normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
            string prefix = normalized.Substring(0, normalized.Length - suffix.Length);
            return int.TryParse(prefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot);
        }

        private static void SetBooleanParameter(Parameter parameter, bool value)
        {
            bool isSet;
            switch (parameter.StorageType)
            {
                case StorageType.Integer:
                    isSet = parameter.Set(value ? 1 : 0);
                    break;
                case StorageType.Double:
                    isSet = parameter.Set(value ? 1.0 : 0.0);
                    break;
                case StorageType.String:
                    isSet = parameter.Set(value ? "1" : "0");
                    break;
                default:
                    throw new InvalidOperationException(
                        "Параметр «" + parameter.Definition?.Name + "» имеет неподдерживаемый тип.");
            }
            if (!isSet)
                throw new InvalidOperationException(
                    "Не удалось записать параметр «" + parameter.Definition?.Name + "».");
        }

        private static void SetLengthParameter(Parameter parameter, double millimeters)
        {
            bool isSet;
            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    isSet = parameter.Set(millimeters / MillimetersPerFoot);
                    break;
                case StorageType.Integer:
                    isSet = parameter.Set((int)Math.Round(millimeters));
                    break;
                case StorageType.String:
                    isSet = parameter.Set(millimeters.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new InvalidOperationException(
                        "Параметр «" + parameter.Definition?.Name + "» имеет неподдерживаемый тип.");
            }
            if (!isSet)
                throw new InvalidOperationException(
                    "Не удалось записать параметр «" + parameter.Definition?.Name + "».");
        }

        private static PlacedLintelDataV3 PlaceLintel(
            Document document,
            FamilySymbol symbol,
            string wallTypeName,
            OpeningPlacementTargetV3 target,
            IList<double> nonNegativeLevelElevations)
        {
            Wall wall = document.GetElement(target.WallId) as Wall;
            if (wall == null)
                throw new InvalidOperationException("Не найдена стена-основа проёма.");

            GetPlacementLevelAndOffset(document, target, wall, out Level level, out double topOffset);
            XYZ point = new XYZ(target.Location.X, target.Location.Y, topOffset);
            FamilyInstance lintel = document.Create.NewFamilyInstance(
                point,
                symbol,
                level,
                Autodesk.Revit.DB.Structure.StructuralType.NonStructural) as FamilyInstance;
            if (lintel == null)
                throw new InvalidOperationException("Revit не создал экземпляр перемычки.");

            XYZ baseOrientation = target.SupportType == 1
                ? NormalizeInPlan(target.SupportDirection)
                : null;
            baseOrientation = baseOrientation
                              ?? NormalizeInPlan(target.WallOrientation)
                              ?? NormalizeInPlan(wall.Orientation)
                              ?? XYZ.BasisX;
            XYZ facingOrientation = NormalizeInPlan(lintel.FacingOrientation) ?? XYZ.BasisX;
            if (!baseOrientation.IsAlmostEqualTo(facingOrientation))
            {
                LocationPoint location = lintel.Location as LocationPoint;
                if (location == null)
                    throw new InvalidOperationException("У перемычки отсутствует точка вставки.");
                Line axis = Line.CreateBound(location.Point, location.Point + XYZ.BasisZ);
                double targetAngle = baseOrientation.AngleOnPlaneTo(XYZ.BasisX, XYZ.BasisZ);
                double currentAngle = facingOrientation.AngleOnPlaneTo(XYZ.BasisX, XYZ.BasisZ);
                ElementTransformUtils.RotateElement(
                    document,
                    lintel.Id,
                    axis,
                    currentAngle - targetAngle);
            }

            ElementTransformUtils.MoveElement(
                document,
                lintel.Id,
                baseOrientation * (wall.Width / 2.0));
            SetRequiredString(lintel.LookupParameter("ADSK_Группирование"), "ПР");

            int floorNumber = level.Elevation >= 0
                ? nonNegativeLevelElevations.IndexOf(level.Elevation) + 1
                : -1;
            SetValueStringIfWritable(
                lintel.LookupParameter("ZH_Этаж_Числовой"),
                floorNumber.ToString(CultureInfo.InvariantCulture));
            SetValueStringIfWritable(lintel.LookupParameter("Видимость.Глубина"), "2000");

            return new PlacedLintelDataV3
            {
                Instance = lintel,
                Wall = wall,
                WallTypeName = wallTypeName
            };
        }

        private static void GetPlacementLevelAndOffset(
            Document document,
            OpeningPlacementTargetV3 target,
            Wall wall,
            out Level level,
            out double topOffset)
        {
            level = null;
            topOffset = double.MinValue;
            double bestWorldTop = double.MinValue;
            foreach (ElementId openingId in target.OpeningIds)
            {
                Element opening = document.GetElement(openingId);
                Level openingLevel = document.GetElement(opening?.LevelId) as Level;
                if (opening == null || openingLevel == null) continue;

                double candidateOffset;
                if (opening is Wall openingWall
                    && openingWall.Location is LocationCurve wallLocation)
                {
                    double height = opening.LookupParameter("Неприсоединенная высота")?.AsDouble() ?? 0;
                    double bottomOffset = opening.LookupParameter("Смещение снизу")?.AsDouble() ?? 0;
                    candidateOffset = wallLocation.Curve.GetEndPoint(0).Z
                                      - openingLevel.ProjectElevation
                                      + height + bottomOffset;
                }
                else if (opening.Location is LocationPoint locationPoint)
                {
                    double height = opening.LookupParameter("ADSK_Размер_Высота")?.AsDouble() ?? 0;
                    if (height <= 0)
                    {
                        BoundingBoxXYZ box = opening.get_BoundingBox(null);
                        if (box != null)
                            height = box.Max.Z - box.Min.Z;
                    }
                    candidateOffset = locationPoint.Point.Z
                                      - openingLevel.ProjectElevation
                                      + height;
                }
                else
                {
                    continue;
                }

                double worldTop = candidateOffset + openingLevel.ProjectElevation;
                if (worldTop <= bestWorldTop) continue;
                bestWorldTop = worldTop;
                topOffset = candidateOffset;
                level = openingLevel;
            }

            if (level != null) return;
            level = document.GetElement(target.LevelId) as Level
                    ?? document.GetElement(wall.LevelId) as Level;
            if (level == null)
                throw new InvalidOperationException("Не найден уровень проёма.");
            topOffset = target.TopElevation - level.ProjectElevation;
        }

        private static void ApplyBaseWallType(
            Document document,
            FamilyInstance lintel,
            Wall wall,
            string wallTypeName)
        {
            string effectiveWallTypeName = string.IsNullOrWhiteSpace(wallTypeName)
                ? wall.WallType?.Name ?? string.Empty
                : wallTypeName;
            string baseType = effectiveWallTypeName.IndexOf(
                "_НСЩ_",
                StringComparison.OrdinalIgnoreCase) >= 0
                ? "Каркас"
                : "Перегородка";
            Parameter constructionMarkParameter = wall.LookupParameter("ZH_Марка КС");
            string constructionMark = constructionMarkParameter?.StorageType == StorageType.String
                ? constructionMarkParameter.AsString()
                : constructionMarkParameter?.AsValueString();
            string baseTypeValue = string.IsNullOrWhiteSpace(constructionMark)
                ? baseType
                : baseType + "_" + constructionMark.Trim().Trim('_');

            foreach (ElementId componentId in lintel.GetSubComponentIds())
            {
                Parameter parameter = document.GetElement(componentId)
                    ?.LookupParameter("ZH_Тип_Основы_Стена");
                if (parameter != null
                    && !parameter.IsReadOnly
                    && parameter.StorageType == StorageType.String)
                    parameter.Set(baseTypeValue);
            }
        }

        private static void SetRequiredString(Parameter parameter, string value)
        {
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
                throw new InvalidOperationException(
                    "У составной перемычки отсутствует строковый параметр «ADSK_Группирование». ");
            parameter.Set(value);
        }

        private static void SetValueStringIfWritable(Parameter parameter, string value)
        {
            if (parameter == null || parameter.IsReadOnly) return;
            if (parameter.StorageType == StorageType.String)
                parameter.Set(value);
            else
                parameter.SetValueString(value);
        }

        private static XYZ NormalizeInPlan(XYZ value)
        {
            if (value == null) return null;
            var planar = new XYZ(value.X, value.Y, 0);
            return planar.GetLength() < 1e-9 ? null : planar.Normalize();
        }
    }

    internal static class LintelTypeReplacementEngineV3
    {
        private const double MillimetersPerFoot = 304.8;

        public static LintelTypeReplacementResultV3 Execute(
            Document document,
            LintelTypeReplacementRequestV3 request)
        {
            var result = new LintelTypeReplacementResultV3
            {
                FamilyName = request?.FamilyName,
                TypeName = request?.TypeName
            };
            if (document == null || request?.TypeId == null || request.LintelIds.Count == 0)
            {
                result.FatalError = "Не сформировано задание на замену типа перемычек.";
                return result;
            }

            FamilySymbol targetSymbol = document.GetElement(request.TypeId) as FamilySymbol;
            if (targetSymbol == null || !IsCompositeLintelSymbol(targetSymbol))
            {
                result.FatalError = "Выбранный тип перемычки не найден или не является составной перемычкой.";
                return result;
            }
            result.FamilyName = targetSymbol.FamilyName;
            result.TypeName = targetSymbol.Name;

            using (var transaction = new Transaction(document, "Замена типа перемычек"))
            {
                transaction.Start();
                try
                {
                    if (!targetSymbol.IsActive)
                    {
                        targetSymbol.Activate();
                        document.Regenerate();
                    }

                    foreach (ElementId lintelId in request.LintelIds
                                 .GroupBy(id => id.Value)
                                 .Select(group => group.First()))
                    {
                        using (var subTransaction = new SubTransaction(document))
                        {
                            subTransaction.Start();
                            try
                            {
                                FamilyInstance lintel = document.GetElement(lintelId) as FamilyInstance;
                                if (lintel == null || lintel.SuperComponent != null)
                                    throw new InvalidOperationException("Элемент не является экземпляром составной перемычки.");

                                ElementId resultId = lintel.Id;
                                if (lintel.Symbol?.Id.Value != targetSymbol.Id.Value)
                                {
                                    ElementId changedId = lintel.ChangeTypeId(targetSymbol.Id);
                                    if (changedId != null && changedId != ElementId.InvalidElementId)
                                        resultId = changedId;
                                }

                                subTransaction.Commit();
                                result.ChangedItems.Add(new LintelTypeReplacementItemResultV3
                                {
                                    OriginalId = lintelId,
                                    ResultId = resultId
                                });
                            }
                            catch (Exception exception)
                            {
                                subTransaction.RollBack();
                                result.Errors.Add("Перемычка ID " + lintelId.Value.ToString(CultureInfo.InvariantCulture)
                                                  + ": " + exception.Message);
                            }
                        }
                    }

                    if (result.ChangedItems.Count > 0)
                    {
                        document.Regenerate();
                        ElementId representativeId = result.ChangedItems.First().ResultId;
                        FamilyInstance representative = document.GetElement(representativeId) as FamilyInstance;
                        result.Components.AddRange(ReadComponents(document, representative));
                    }
                    transaction.Commit();
                }
                catch (Exception exception)
                {
                    transaction.RollBack();
                    result.ChangedItems.Clear();
                    result.Components.Clear();
                    result.FatalError = exception.Message;
                }
            }
            return result;
        }

        private static bool IsCompositeLintelSymbol(FamilySymbol symbol)
        {
            return symbol != null
                   && string.Equals(
                       symbol.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString(),
                       "Перемычки составные",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static List<ExistingLintelComponentV3> ReadComponents(
            Document document,
            FamilyInstance lintel)
        {
            var result = new List<ExistingLintelComponentV3>();
            if (document == null || lintel?.Symbol == null) return result;

            XYZ origin = (lintel.Location as LocationPoint)?.Point;
            XYZ direction = NormalizeInPlan(lintel.FacingOrientation) ?? XYZ.BasisX;
            int fallbackOrder = 0;
            foreach (ElementId componentId in lintel.GetSubComponentIds())
            {
                FamilyInstance component = document.GetElement(componentId) as FamilyInstance;
                if (component?.Symbol == null) continue;
                XYZ point = (component.Location as LocationPoint)?.Point;
                double order = origin != null && point != null
                    ? (point - origin).DotProduct(direction)
                    : fallbackOrder;
                result.Add(new ExistingLintelComponentV3
                {
                    FamilyName = component.Symbol.FamilyName,
                    TypeName = component.Symbol.Name,
                    Order = order
                });
                fallbackOrder++;
            }

            result = result.OrderBy(component => component.Order).ToList();
            int visibleCount = GetVisibleComponentCount(lintel.Symbol, result.Count);
            result = result.Take(visibleCount).ToList();
            for (int index = 0; index < result.Count; index++)
            {
                result[index].Order = index;
                if (index < result.Count - 1)
                {
                    result[index].OffsetToNextMm = GetLengthParameterMm(
                        lintel.Symbol.LookupParameter(
                            "Отступ от " + (index + 1).ToString(CultureInfo.InvariantCulture)
                            + " до " + (index + 2).ToString(CultureInfo.InvariantCulture)));
                }
            }
            return result;
        }

        private static int GetVisibleComponentCount(FamilySymbol symbol, int availableCount)
        {
            if (symbol == null || availableCount <= 0) return 0;
            int visibleCount = 1;
            bool hasVisibilityParameters = false;
            for (int slot = 2; slot <= Math.Max(availableCount, 16); slot++)
            {
                Parameter parameter = symbol.LookupParameter(
                    slot.ToString(CultureInfo.InvariantCulture) + "ПР.Видимость");
                if (parameter == null) continue;
                hasVisibilityParameters = true;
                if (IsEnabled(parameter)) visibleCount++;
            }
            return hasVisibilityParameters ? Math.Min(availableCount, visibleCount) : availableCount;
        }

        private static bool IsEnabled(Parameter parameter)
        {
            if (parameter == null) return false;
            if (parameter.StorageType == StorageType.Integer) return parameter.AsInteger() != 0;
            if (parameter.StorageType == StorageType.Double) return Math.Abs(parameter.AsDouble()) > 1e-9;
            string value = (parameter.AsString() ?? string.Empty).Trim();
            return value == "1"
                   || string.Equals(value, "Да", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
        }

        private static double GetLengthParameterMm(Parameter parameter)
        {
            if (parameter == null) return 0;
            if (parameter.StorageType == StorageType.Double)
                return parameter.AsDouble() * MillimetersPerFoot;
            if (parameter.StorageType == StorageType.Integer)
                return parameter.AsInteger();
            string value = parameter.AsString() ?? parameter.AsValueString();
            return double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out double parsed)
                   || double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0;
        }

        private static XYZ NormalizeInPlan(XYZ value)
        {
            if (value == null) return null;
            var planar = new XYZ(value.X, value.Y, 0);
            return planar.GetLength() < 1e-9 ? null : planar.Normalize();
        }
    }

    public sealed class LintelTypeReplacementHandlerV3 : IExternalEventHandler
    {
        private readonly object _sync = new object();
        private readonly Document _document;
        private readonly LintelOpeningWorkspaceV3 _workspace;
        private LintelTypeReplacementRequestV3 _pendingRequest;

        public LintelTypeReplacementHandlerV3(
            Document document,
            LintelOpeningWorkspaceV3 workspace)
        {
            _document = document;
            _workspace = workspace;
        }

        internal void Request(LintelTypeReplacementRequestV3 request)
        {
            lock (_sync)
                _pendingRequest = request;
        }

        public void Execute(UIApplication application)
        {
            LintelTypeReplacementRequestV3 request;
            lock (_sync)
            {
                request = _pendingRequest;
                _pendingRequest = null;
            }
            if (request == null) return;

            LintelTypeReplacementResultV3 result;
            try
            {
                result = LintelTypeReplacementEngineV3.Execute(_document, request);
            }
            catch (Exception exception)
            {
                result = new LintelTypeReplacementResultV3
                {
                    FamilyName = request.FamilyName,
                    TypeName = request.TypeName,
                    FatalError = exception.Message
                };
            }

            try
            {
                _workspace.ApplyLintelTypeReplacementResult(result);
            }
            catch (Exception exception)
            {
                result.Errors.Add("Ошибка обновления окна: " + exception.Message);
                _workspace.CancelLintelTypeReplacement("Типы изменены, но окно не удалось обновить: "
                                                       + exception.Message);
            }
            string journalMessage = "Заменено типов перемычек: "
                                    + result.ChangedItems.Count.ToString(CultureInfo.InvariantCulture)
                                    + ". Ошибок: "
                                    + (result.Errors.Count + (string.IsNullOrWhiteSpace(result.FatalError) ? 0 : 1))
                                        .ToString(CultureInfo.InvariantCulture)
                                    + ".";
            application.Application.WriteJournalComment("[LintelCreator v3] " + journalMessage, false);
            if (result.ChangedItems.Count == 0
                || result.Errors.Count > 0
                || !string.IsNullOrWhiteSpace(result.FatalError))
            {
                var messages = new List<string> { journalMessage };
                if (!string.IsNullOrWhiteSpace(result.FatalError)) messages.Add(result.FatalError);
                messages.AddRange(result.Errors);
                TaskDialog.Show("Замена типа перемычек", string.Join(Environment.NewLine, messages));
            }
        }

        public string GetName()
        {
            return "Замена типов существующих перемычек v3";
        }
    }

    public sealed class LintelPlacementHandlerV3 : IExternalEventHandler
    {
        private readonly object _sync = new object();
        private readonly Document _document;
        private readonly LintelOpeningWorkspaceV3 _workspace;
        private LintelPlacementRequestV3 _pendingRequest;

        public LintelPlacementHandlerV3(
            Document document,
            LintelOpeningWorkspaceV3 workspace)
        {
            _document = document;
            _workspace = workspace;
        }

        internal void Request(LintelPlacementRequestV3 request)
        {
            lock (_sync)
                _pendingRequest = request;
        }

        public void Execute(UIApplication application)
        {
            LintelPlacementRequestV3 request;
            lock (_sync)
            {
                request = _pendingRequest;
                _pendingRequest = null;
            }
            if (request == null) return;

            LintelPlacementResultV3 result;

                try
                {
                    result = LintelPlacementEngineV3.Execute(_document, request);
                }
                catch (Exception exception)
                {
                    result = new LintelPlacementResultV3 { FatalError = exception.Message };
                }

            string resultMessage = BuildResultMessage(result);
            application.Application.WriteJournalComment(
                "[LintelCreator v3] " + resultMessage.Replace(Environment.NewLine, " | "),
                false);
            try
            {
                _workspace.ApplyLintelPlacementResult(result);
            }
            catch (Exception exception)
            {
                resultMessage += Environment.NewLine
                                 + "Ошибка обновления окна: " + exception.Message;
            }

            try
            {
                LintelPlacementReportWindowV3.ShowReport(resultMessage);
            }
            catch (Exception exception)
            {
                TaskDialog.Show(
                    "Отчёт о простановке перемычек",
                    "Не удалось открыть окно отчёта: " + exception.Message
                    + Environment.NewLine + "Краткий итог: "
                    + (result?.Groups.Count(group => group.IsSuccess) ?? 0)
                    + " групп обработано.");
            }
        }

        private static string BuildResultMessage(LintelPlacementResultV3 result)
        {
            int successfulGroups = result?.Groups.Count(group => group.IsSuccess) ?? 0;
            int createdInstances = result?.Groups
                .Where(group => group.IsSuccess)
                .Sum(group => group.CreatedLintelIds.Count) ?? 0;
            var lines = new List<string>
            {
                "Успешно обработано групп: " + successfulGroups + ".",
                "Создано перемычек: " + createdInstances + "."
            };
            if (!string.IsNullOrWhiteSpace(result?.FatalError))
                lines.Add("Ошибка: " + result.FatalError);
            lines.AddRange(result?.Groups
                               .Where(group => !group.IsSuccess && !string.IsNullOrWhiteSpace(group.Error))
                               .Select(group => "Ошибка: " + group.Error)
                           ?? Enumerable.Empty<string>());

            List<IGrouping<string, LintelPlacementGroupResultV3>> conflicts = result?.Groups
                .Where(group => group.HasTypeNameConflict)
                .GroupBy(group =>
                    (group.RequestedTypeName ?? string.Empty) + "\u001f"
                    + (group.TypeName ?? string.Empty) + "\u001f"
                    + (group.TypeNameConflictAction ?? string.Empty) + "\u001f"
                    + (group.TypeNameConflictDifferences ?? string.Empty),
                    StringComparer.Ordinal)
                .ToList() ?? new List<IGrouping<string, LintelPlacementGroupResultV3>>();
            lines.Add(string.Empty);
            lines.Add("Совпадения имён и различия состава:");
            if (conflicts.Count == 0)
            {
                lines.Add("Не обнаружены.");
            }
            else
            {
                foreach (IGrouping<string, LintelPlacementGroupResultV3> conflict in conflicts)
                {
                    LintelPlacementGroupResultV3 item = conflict.First();
                    string actualName = string.Equals(
                        item.RequestedTypeName,
                        item.TypeName,
                        StringComparison.Ordinal)
                        ? item.TypeName
                        : item.RequestedTypeName + " → " + item.TypeName;
                    lines.Add("• " + actualName
                              + (conflict.Count() > 1 ? " · групп: " + conflict.Count() : string.Empty));
                    lines.Add("  Отличия: " + item.TypeNameConflictDifferences + ".");
                    lines.Add("  Действие: " + item.TypeNameConflictAction);
                }
            }
            return string.Join(Environment.NewLine, lines);
        }

        public string GetName()
        {
            return "Создание типов и размещение перемычек v3";
        }
    }

    public sealed class OpeningReloadHandlerV3 : IExternalEventHandler
    {
        private readonly object _sync = new object();
        private readonly LintelOpeningWorkspaceV3 _workspace;
        private Action<int, int> _progress;
        private Action<Exception> _completed;

        public OpeningReloadHandlerV3(LintelOpeningWorkspaceV3 workspace)
        {
            _workspace = workspace;
        }

        internal void Request(Action<int, int> progress, Action<Exception> completed)
        {
            lock (_sync)
            {
                _progress = progress;
                _completed = completed;
            }
        }

        public void Execute(UIApplication application)
        {
            Action<int, int> progress;
            Action<Exception> completed;
            lock (_sync)
            {
                progress = _progress;
                completed = _completed;
                _progress = null;
                _completed = null;
            }

            Exception error = null;
            try
            {
                _workspace.Reload(progress);
            }
            catch (Exception exception)
            {
                error = exception;
            }
            completed?.Invoke(error);
        }

        public string GetName()
        {
            return "Повторный сбор проёмов v3";
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
