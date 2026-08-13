using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FerrumAddinDev.LintelCreator_v2;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace FerrumAddinDev.LintelCreator_v3
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class CommandLintelCreator_v3 : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                var workspace = new LintelWorkspaceV3(doc, uidoc.Selection);
                if (!workspace.CompositeTypes.Any())
                {
                    message = "В проекте не найдено составное семейство перемычек с параметром типа «Модель» = «Перемычки составные».";
                    return Result.Cancelled;
                }

                if (!workspace.OpeningGroups.Any() && !workspace.ExistingOpeningGroups.Any())
                {
                    message = "В активном виде или текущем выборе не найдены дверные, оконные проёмы и витражи со стеной-основой.";
                    return Result.Cancelled;
                }

                var actionHandler = new LintelActionHandlerV3(workspace);
                ExternalEvent actionEvent = ExternalEvent.Create(actionHandler);

                var utilityEvents = new LintelUtilityEventsV3
                {
                    Numerate = ExternalEvent.Create(new LintelNumerate()),
                    NumerateNested = ExternalEvent.Create(new NestedElementsNumbering()),
                    SetBaseType = ExternalEvent.Create(new SetLintelBaseType()),
                    CreateSections = ExternalEvent.Create(new CreateSectionsForLintels()),
                    TagLintels = ExternalEvent.Create(new TagLintels()),
                    PlaceSections = ExternalEvent.Create(new PlaceSections())
                };

                var form = new LintelCreatorForm_v3(workspace, actionHandler, actionEvent, utilityEvents);
                form.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    public sealed class LintelUtilityEventsV3
    {
        public ExternalEvent Numerate { get; set; }
        public ExternalEvent NumerateNested { get; set; }
        public ExternalEvent SetBaseType { get; set; }
        public ExternalEvent CreateSections { get; set; }
        public ExternalEvent TagLintels { get; set; }
        public ExternalEvent PlaceSections { get; set; }
    }

    public abstract class NotifyObjectV3 : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class OpeningRecordV3
    {
        public ElementId OpeningId { get; set; }
        public ElementId WallId { get; set; }
        public ElementId WallTypeId { get; set; }
        public ElementId LevelId { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string CategoryName { get; set; }
        public string WallTypeName { get; set; }
        public double OpeningWidthMm { get; set; }
        public double OpeningHeightMm { get; set; }
        public double WallWidthMm { get; set; }
        public XYZ Location { get; set; }
        public double TopElevation { get; set; }
        public XYZ WallOrientation { get; set; }
        public XYZ BearingDirection { get; set; }
        public int SupportType { get; set; }
        public double RequiredBearingWidthMm { get; set; }
        public List<ElementId> ExistingLintelIds { get; set; } = new List<ElementId>();

        public string IdText => OpeningId == null ? string.Empty : OpeningId.Value.ToString(CultureInfo.InvariantCulture);
        public string IdButtonText => "ID " + IdText;
    }

    internal sealed class SupportGeometryV3
    {
        public BoundingBoxXYZ Box { get; set; }
    }

    internal sealed class ExistingLintelGeometryV3
    {
        public ElementId Id { get; set; }
        public BoundingBoxXYZ Box { get; set; }
    }

    public sealed class OpeningTypeGroupV3 : NotifyObjectV3
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public ObservableCollection<OpeningWallGroupV3> Walls { get; } = new ObservableCollection<OpeningWallGroupV3>();
        public string DisplayName => FamilyName + " : " + TypeName;
        public int Count => Walls.Sum(x => x.Count);

        public bool IsSelected
        {
            get
            {
                List<OpeningWallGroupV3> available = Walls.Where(x => x.CanPlace).ToList();
                return available.Any() && available.All(x => x.IsSelected);
            }
            set
            {
                foreach (OpeningWallGroupV3 wall in Walls)
                    wall.IsSelected = value && wall.CanPlace;
                RaisePropertyChanged(nameof(IsSelected));
            }
        }

        public void NotifySelectionStateChanged()
        {
            RaisePropertyChanged(nameof(IsSelected));
        }
    }

    public sealed class OpeningWallGroupV3 : NotifyObjectV3
    {
        private bool _isSelected = true;
        private ObservableCollection<LintelVariantV3> _variants = new ObservableCollection<LintelVariantV3>();
        private LintelVariantV3 _selectedVariant;
        private string _statusText;
        private bool _isCurrent;

        public OpeningTypeGroupV3 Parent { get; set; }
        public ElementId WallTypeId { get; set; }
        public string WallTypeName { get; set; }
        public double WallWidthMm { get; set; }
        public double RequiredLengthMm { get; set; }
        public List<OpeningRecordV3> Openings { get; } = new List<OpeningRecordV3>();
        public bool HasExistingLintels => Openings.Any(x => x.ExistingLintelIds.Any());
        public int Count => Openings.Count;
        public string DisplayName => $"{WallTypeName} · {Math.Round(WallWidthMm)} мм";
        public string CountText => Count + " экз.";
        public string OpeningIdsText => "Проёмы: " + string.Join(", ", Openings.Select(x => x.IdText));
        public string ExistingLintelIdsText => "ID перемычек: " + string.Join(", ", Openings.SelectMany(x => x.ExistingLintelIds).Distinct().Select(x => x.Value));
        public string CurrentLintelTypeName { get; set; }
        public bool NeedsReplacement => (CurrentLintelTypeName ?? string.Empty)
            .IndexOf("Тестовый вариант", StringComparison.OrdinalIgnoreCase) >= 0;
        public string ExistingStateText => NeedsReplacement
            ? "⚠ Не подобрано — требуется расчёт или замена"
            : "Перемычка установлена";
        public ObservableCollection<FamilySymbol> ExistingTypeChoices { get; set; }
        public FamilySymbol SelectedExistingType { get; set; }
        public string MasonryMode { get; set; } = "65";
        public string MaterialMode { get; set; } = "Железобетонная";
        public double ToleranceMm { get; set; } = 20;
        public bool CanPlace => SelectedVariant != null && SelectedVariant.IsValid;

        public double OpeningWidthMm => Openings.Count == 0 ? 0 : Openings.Max(x => x.OpeningWidthMm);
        public double OpeningHeightMm => Openings.Count == 0 ? 0 : Openings.Max(x => x.OpeningHeightMm);
        public int SupportType => Openings.Count == 0 ? 0 : Openings.Max(x => x.SupportType);
        public double RequiredBearingWidthMm => Openings.Count == 0 ? 0 : Openings.Max(x => x.RequiredBearingWidthMm);
        public XYZ BearingDirection => Openings.FirstOrDefault(x => !x.BearingDirection.IsZeroLength())?.BearingDirection ?? XYZ.Zero;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                RaisePropertyChanged(nameof(IsSelected));
                Parent?.NotifySelectionStateChanged();
            }
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                _isCurrent = value;
                RaisePropertyChanged(nameof(IsCurrent));
            }
        }

        public ObservableCollection<LintelVariantV3> Variants
        {
            get => _variants;
            set
            {
                _variants = value ?? new ObservableCollection<LintelVariantV3>();
                RaisePropertyChanged(nameof(Variants));
                RaisePropertyChanged(nameof(VariantsCountText));
            }
        }

        public LintelVariantV3 SelectedVariant
        {
            get => _selectedVariant;
            set
            {
                _selectedVariant = value;
                RaisePropertyChanged(nameof(SelectedVariant));
                RaisePropertyChanged(nameof(CanPlace));
                RaisePropertyChanged(nameof(RequiredLengthText));
                RaisePropertyChanged(nameof(ManualHintText));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                RaisePropertyChanged(nameof(StatusText));
            }
        }

        public string VariantsCountText => Variants.Count == 0 ? StatusText : $"Найдено вариантов: {Variants.Count}";
        public string RequiredLengthText => $"≥ {Math.Round(SelectedVariant?.RequiredLengthMm ?? RequiredLengthMm)} мм";
        public string BearingSideText => SupportType == 1 ? "Одна сторона" : SupportType == 2 ? "Две стороны" : "Не определена";
        public string BearingWidthText => SupportType == 0 ? "0 мм" : $"{Math.Round(RequiredBearingWidthMm)} мм";
        public string BearingLayoutText => SupportType == 1
            ? "Несущая сторона →"
            : SupportType == 2 ? "Несущие стороны с обоих краёв" : "Несущая сторона не определена";
        public string ManualHintText => SelectedVariant == null
            ? "Автоматический вариант не найден. Создайте комплект вручную кнопкой справа."
            : SelectedVariant.ValidationText;

        public void NotifyCalculated()
        {
            RaisePropertyChanged(nameof(VariantsCountText));
            RaisePropertyChanged(nameof(RequiredLengthText));
            RaisePropertyChanged(nameof(BearingSideText));
            RaisePropertyChanged(nameof(BearingWidthText));
            RaisePropertyChanged(nameof(CanPlace));
            RaisePropertyChanged(nameof(ManualHintText));
            Parent?.NotifySelectionStateChanged();
        }
    }

    public sealed class LintelCatalogItemV3
    {
        public FamilySymbol Symbol { get; set; }
        public string DisplayName { get; set; }
        public string Mark { get; set; }
        public double LengthMm { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public bool IsBearing { get; set; }
        public bool IsMetal { get; set; }
        public int MasonryHeight { get; set; }
        public double MinimumOpeningWidthMm { get; set; }
        public double MaximumOpeningWidthMm { get; set; }
        public double MinimumBearingMm { get; set; }
        public string LoadCategory { get; set; }
        public int Priority { get; set; }
        public bool IsAutoAllowed { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public sealed class LintelCatalogFileV3
    {
        public int SchemaVersion { get; set; }
        public LintelCatalogDefaultsV3 Defaults { get; set; } = new LintelCatalogDefaultsV3();
        public List<LintelCatalogEntryV3> Items { get; set; } = new List<LintelCatalogEntryV3>();
    }

    public sealed class LintelCatalogDefaultsV3
    {
        public string FamilyName { get; set; }
        public bool IsBearing { get; set; }
        public string Material { get; set; }
        public int MasonryHeightMm { get; set; }
        public double MinimumBearingMm { get; set; }
        public string LoadCategory { get; set; }
        public int Priority { get; set; }
        public bool AutoAllowed { get; set; } = true;
    }

    public sealed class LintelCatalogEntryV3
    {
        public bool Enabled { get; set; } = true;
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string Code { get; set; }
        public double LengthMm { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public bool? IsBearing { get; set; }
        public string Material { get; set; }
        public int? MasonryHeightMm { get; set; }
        public double MinimumOpeningWidthMm { get; set; }
        public double MaximumOpeningWidthMm { get; set; }
        public double? MinimumBearingMm { get; set; }
        public string LoadCategory { get; set; }
        public int? Priority { get; set; }
        public bool? AutoAllowed { get; set; }
    }

    public sealed class NestedTypeChoiceV3
    {
        public ElementId Id { get; set; }
        public string DisplayName { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public sealed class LintelPieceV3 : NotifyObjectV3
    {
        private LintelCatalogItemV3 _selectedType;
        private string _role;
        private double _lengthMm;
        private double _widthMm;
        private double _gapMm;

        public int Number { get; set; }
        public ObservableCollection<LintelCatalogItemV3> AvailableTypes { get; set; }

        public LintelCatalogItemV3 SelectedType
        {
            get => _selectedType;
            set
            {
                _selectedType = value;
                if (value != null)
                {
                    _lengthMm = value.LengthMm;
                    _widthMm = value.WidthMm;
                }
                RaisePropertyChanged(nameof(SelectedType));
                RaisePropertyChanged(nameof(LengthMm));
                RaisePropertyChanged(nameof(WidthMm));
                RaisePropertyChanged(nameof(DisplayText));
            }
        }

        public string Role
        {
            get => _role;
            set { _role = value; RaisePropertyChanged(nameof(Role)); RaisePropertyChanged(nameof(DisplayText)); }
        }

        public double LengthMm
        {
            get => _lengthMm;
            set { _lengthMm = value; RaisePropertyChanged(nameof(LengthMm)); }
        }

        public double WidthMm
        {
            get => _widthMm;
            set { _widthMm = value; RaisePropertyChanged(nameof(WidthMm)); }
        }

        public double GapMm
        {
            get => _gapMm;
            set { _gapMm = value; RaisePropertyChanged(nameof(GapMm)); }
        }

        public string DisplayText => (Role == "Несущая" ? "Н " : string.Empty) + Math.Round(WidthMm) + " · " + (SelectedType?.Mark ?? "Не выбрано");

        public LintelPieceV3 Clone()
        {
            return new LintelPieceV3
            {
                Number = Number,
                AvailableTypes = AvailableTypes,
                SelectedType = SelectedType,
                Role = Role,
                LengthMm = LengthMm,
                WidthMm = WidthMm,
                GapMm = GapMm
            };
        }
    }

    public sealed class LintelVariantV3 : NotifyObjectV3
    {
        private string _typeName;
        private bool _isExistingType;
        private ElementId _existingTypeId;
        public int Number { get; set; }
        public double WallWidthMm { get; set; }
        public double OpeningWidthMm { get; set; }
        public double RequiredLengthMm { get; set; }
        public double ToleranceMm { get; set; }
        public int SupportType { get; set; }
        public double RequiredBearingWidthMm { get; set; }
        public bool IsRecommended { get; set; }
        public bool IsManual { get; set; }
        public bool IsExistingType
        {
            get => _isExistingType;
            set
            {
                _isExistingType = value;
                RaisePropertyChanged(nameof(IsExistingType));
                RaisePropertyChanged(nameof(TypeStatusText));
            }
        }
        public ElementId ExistingTypeId
        {
            get => _existingTypeId;
            set { _existingTypeId = value; RaisePropertyChanged(nameof(ExistingTypeId)); }
        }
        public NestedTypeChoiceV3 LeftSupportPad { get; set; }
        public NestedTypeChoiceV3 RightSupportPad { get; set; }
        public NestedTypeChoiceV3 LeftAngle { get; set; }
        public NestedTypeChoiceV3 RightAngle { get; set; }
        public NestedTypeChoiceV3 Strip { get; set; }
        public ObservableCollection<LintelPieceV3> Pieces { get; } = new ObservableCollection<LintelPieceV3>();

        public string TypeName
        {
            get => _typeName;
            set { _typeName = value; RaisePropertyChanged(nameof(TypeName)); }
        }

        public double TotalWidthMm => Pieces.Sum(x => x.WidthMm) + Pieces.Take(Math.Max(0, Pieces.Count - 1)).Sum(x => x.GapMm);
        public double DeltaMm => TotalWidthMm - WallWidthMm;
        public bool IsLengthValid => Pieces.Any() && Pieces.All(x => x.SelectedType != null
            && x.LengthMm + 0.1 >= OpeningWidthMm + 2 * x.SelectedType.MinimumBearingMm);
        public bool IsBearingLayoutValid
        {
            get
            {
                if (SupportType == 0 || RequiredBearingWidthMm <= 0) return true;

                double leftWidth = 0;
                int leftCount = 0;
                foreach (LintelPieceV3 piece in Pieces)
                {
                    if (!string.Equals(piece.Role, "Несущая", StringComparison.OrdinalIgnoreCase)) break;
                    leftWidth += piece.WidthMm;
                    leftCount++;
                }
                if (leftWidth + 0.1 < RequiredBearingWidthMm) return false;
                if (SupportType != 2) return true;

                double rightWidth = 0;
                int rightCount = 0;
                for (int index = Pieces.Count - 1; index >= 0; index--)
                {
                    if (!string.Equals(Pieces[index].Role, "Несущая", StringComparison.OrdinalIgnoreCase)) break;
                    rightWidth += Pieces[index].WidthMm;
                    rightCount++;
                }
                return rightWidth + 0.1 >= RequiredBearingWidthMm
                       && leftCount + rightCount <= Pieces.Count;
            }
        }
        public bool IsValid => Pieces.Any() && Pieces.Count <= 7 && Math.Abs(DeltaMm) <= ToleranceMm
                               && Pieces.All(x => x.SelectedType != null)
                               && Pieces.All(x => !string.Equals(x.Role, "Несущая", StringComparison.OrdinalIgnoreCase)
                                                  || x.SelectedType.IsBearing)
                               && IsLengthValid
                               && IsBearingLayoutValid;
        public string HeaderText => IsManual
            ? "Ручной вариант"
            : IsRecommended ? $"Вариант {Number} · рекомендуемый" : $"Вариант {Number}";
        public string SummaryText => $"{Pieces.Count} элем. · {Pieces.Select(x => x.SelectedType?.Mark).Where(x => x != null).Distinct().Count()} марок";
        public string DeltaText => $"Δ {DeltaMm:+0;-0;0} мм";
        public string PiecesText => string.Join("  +  ", Pieces.Select(x => x.DisplayText));
        public string TypeStatusText => IsExistingType ? "Тип существует в проекте" : "Тип будет создан";
        public string ValidationText
        {
            get
            {
                if (!Pieces.Any()) return "Добавьте хотя бы одну единичную перемычку.";
                if (Pieces.Count > 7) return "Допустимо не более семи элементов.";
                if (Pieces.Any(x => x.SelectedType == null)) return "Для всех строк необходимо выбрать тип.";
                if (Pieces.Any(x => string.Equals(x.Role, "Несущая", StringComparison.OrdinalIgnoreCase)
                                    && !x.SelectedType.IsBearing))
                    return "Для несущей позиции выбран тип, не разрешённый как несущий в JSON.";
                if (!IsLengthValid) return "Есть элементы короче требуемой длины с учётом опирания из JSON.";
                if (!IsBearingLayoutValid)
                    return SupportType == 2
                        ? $"Несущие элементы должны начинать и завершать комплект, закрывая по {RequiredBearingWidthMm:0} мм с каждой стороны."
                        : $"Несущие элементы должны быть первыми со стороны нагрузки и закрывать {RequiredBearingWidthMm:0} мм стены.";
                if (Math.Abs(DeltaMm) > ToleranceMm) return $"Ширина комплекта отличается от стены на {Math.Abs(DeltaMm):0} мм.";
                return "Комплект допустим и готов к созданию типа.";
            }
        }

        public void Refresh()
        {
            for (int i = 0; i < Pieces.Count; i++)
                Pieces[i].Number = i + 1;
            RequiredLengthMm = Pieces.Any(x => x.SelectedType != null)
                ? Pieces.Where(x => x.SelectedType != null)
                    .Max(x => OpeningWidthMm + 2 * x.SelectedType.MinimumBearingMm)
                : OpeningWidthMm;
            RaisePropertyChanged(nameof(RequiredLengthMm));
            RaisePropertyChanged(nameof(TotalWidthMm));
            RaisePropertyChanged(nameof(DeltaMm));
            RaisePropertyChanged(nameof(IsLengthValid));
            RaisePropertyChanged(nameof(IsBearingLayoutValid));
            RaisePropertyChanged(nameof(IsValid));
            RaisePropertyChanged(nameof(SummaryText));
            RaisePropertyChanged(nameof(DeltaText));
            RaisePropertyChanged(nameof(PiecesText));
            RaisePropertyChanged(nameof(ValidationText));
        }

        public LintelVariantV3 Clone()
        {
            var copy = new LintelVariantV3
            {
                Number = Number,
                WallWidthMm = WallWidthMm,
                OpeningWidthMm = OpeningWidthMm,
                RequiredLengthMm = RequiredLengthMm,
                ToleranceMm = ToleranceMm,
                SupportType = SupportType,
                RequiredBearingWidthMm = RequiredBearingWidthMm,
                IsRecommended = IsRecommended,
                IsManual = IsManual,
                IsExistingType = IsExistingType,
                ExistingTypeId = ExistingTypeId,
                LeftSupportPad = LeftSupportPad,
                RightSupportPad = RightSupportPad,
                LeftAngle = LeftAngle,
                RightAngle = RightAngle,
                Strip = Strip,
                TypeName = TypeName
            };
            foreach (LintelPieceV3 piece in Pieces)
                copy.Pieces.Add(piece.Clone());
            copy.Refresh();
            return copy;
        }
    }

    public sealed class LintelWorkspaceV3 : NotifyObjectV3
    {
        private readonly Document _doc;
        private readonly Selection _selection;
        private readonly Dictionary<string, List<LintelVariantV3>> _variantCache = new Dictionary<string, List<LintelVariantV3>>();
        private List<OpeningRecordV3> _records = new List<OpeningRecordV3>();
        private OpeningWallGroupV3 _selectedWall;
        private LintelVariantV3 _selectedVariant;
        private string _masonryMode = "65";
        private string _materialMode = "Железобетонная";
        private double _toleranceMm = 20;
        private string _lastMessage;
        private string _catalogStatusText;
        private FamilySymbol _baseCompositeType;
        private string _groupingMode = "По типу проёма";
        private string _calculationDurationText;

        public ObservableCollection<OpeningTypeGroupV3> OpeningGroups { get; private set; } = new ObservableCollection<OpeningTypeGroupV3>();
        public ObservableCollection<OpeningTypeGroupV3> ExistingOpeningGroups { get; private set; } = new ObservableCollection<OpeningTypeGroupV3>();
        public ObservableCollection<LintelCatalogItemV3> Catalog { get; private set; } = new ObservableCollection<LintelCatalogItemV3>();
        public ObservableCollection<FamilySymbol> CompositeTypes { get; private set; } = new ObservableCollection<FamilySymbol>();
        public ObservableCollection<NestedTypeChoiceV3> SupportPadChoices { get; private set; } = new ObservableCollection<NestedTypeChoiceV3>();
        public ObservableCollection<NestedTypeChoiceV3> AngleChoices { get; private set; } = new ObservableCollection<NestedTypeChoiceV3>();
        public ObservableCollection<NestedTypeChoiceV3> StripChoices { get; private set; } = new ObservableCollection<NestedTypeChoiceV3>();
        public FamilySymbol BaseCompositeType
        {
            get => _baseCompositeType;
            private set { _baseCompositeType = value; RaisePropertyChanged(nameof(BaseCompositeType)); }
        }
        public ObservableCollection<string> Roles { get; } = new ObservableCollection<string> { "Несущая", "Ненесущая" };

        public string GroupingMode
        {
            get => _groupingMode;
            set { _groupingMode = value ?? "По типу проёма"; RaisePropertyChanged(nameof(GroupingMode)); }
        }

        public string CalculationDurationText
        {
            get => _calculationDurationText;
            private set { _calculationDurationText = value; RaisePropertyChanged(nameof(CalculationDurationText)); }
        }

        public LintelWorkspaceV3(Document doc, Selection selection)
        {
            _doc = doc;
            _selection = selection;
            Reload();
        }

        public OpeningWallGroupV3 SelectedWall
        {
            get => _selectedWall;
            set
            {
                if (!ReferenceEquals(_selectedWall, value) && _selectedWall != null)
                    _selectedWall.IsCurrent = false;
                _selectedWall = value;
                if (value != null)
                {
                    value.IsCurrent = true;
                    _masonryMode = value.MasonryMode;
                    _materialMode = value.MaterialMode;
                    _toleranceMm = value.ToleranceMm;
                    RaisePropertyChanged(nameof(MasonryMode));
                    RaisePropertyChanged(nameof(MaterialMode));
                    RaisePropertyChanged(nameof(ToleranceMm));
                    if (!value.Variants.Any())
                        CalculateVariants(value);
                    SelectedVariant = value.SelectedVariant;
                }
                RaisePropertyChanged(nameof(SelectedWall));
                RaisePropertyChanged(nameof(SelectedExistingCountText));
            }
        }

        public LintelVariantV3 SelectedVariant
        {
            get => _selectedVariant;
            set
            {
                _selectedVariant = value;
                if (SelectedWall != null)
                    SelectedWall.SelectedVariant = value;
                RaisePropertyChanged(nameof(SelectedVariant));
                RaisePropertyChanged(nameof(CanEditVariant));
                RaiseSummaryProperties();
            }
        }

        public string MasonryMode
        {
            get => _masonryMode;
            set
            {
                _masonryMode = value;
                if (SelectedWall != null) SelectedWall.MasonryMode = value;
                RaisePropertyChanged(nameof(MasonryMode));
            }
        }

        public string MaterialMode
        {
            get => _materialMode;
            set
            {
                _materialMode = value;
                if (SelectedWall != null) SelectedWall.MaterialMode = value;
                RaisePropertyChanged(nameof(MaterialMode));
            }
        }

        public double ToleranceMm
        {
            get => _toleranceMm;
            set
            {
                _toleranceMm = Math.Max(0, value);
                if (SelectedWall != null) SelectedWall.ToleranceMm = _toleranceMm;
                RaisePropertyChanged(nameof(ToleranceMm));
            }
        }

        public string LastMessage
        {
            get => _lastMessage;
            set { _lastMessage = value; RaisePropertyChanged(nameof(LastMessage)); }
        }

        public string CatalogStatusText
        {
            get => _catalogStatusText;
            private set { _catalogStatusText = value; RaisePropertyChanged(nameof(CatalogStatusText)); }
        }

        public bool CanEditVariant => SelectedVariant != null;
        public int OpeningsCount => OpeningGroups.SelectMany(x => x.Walls).Sum(x => x.Count) + ExistingOpeningGroups.SelectMany(x => x.Walls).Sum(x => x.Count);
        public int WithoutLintelCount => OpeningGroups.SelectMany(x => x.Walls).Sum(x => x.Count);
        public int ExistingCount => ExistingOpeningGroups.SelectMany(x => x.Walls).Sum(x => x.Count);
        public int NeedsReplacementCount => ExistingOpeningGroups.SelectMany(x => x.Walls).Where(x => x.NeedsReplacement).Sum(x => x.Count);
        public int SelectedPlacementCount => OpeningGroups.SelectMany(x => x.Walls).Where(x => x.IsSelected && x.CanPlace).Sum(x => x.Count);
        public int CalculatedCount => OpeningGroups.SelectMany(x => x.Walls).Where(x => x.Variants.Any() && x.CanPlace).Sum(x => x.Count);
        public int ReviewCount => OpeningGroups.SelectMany(x => x.Walls).Where(x => x.Variants.Any() && !x.CanPlace).Sum(x => x.Count);
        public int NoSolutionCount => OpeningGroups.SelectMany(x => x.Walls).Where(x => !x.Variants.Any()).Sum(x => x.Count);
        public string HeaderSummary => $"Модель собрана · {OpeningsCount} проёмов";
        public string OpeningsSummary => $"Без перемычек: {WithoutLintelCount} · с перемычками: {ExistingCount} · не подобрано: {NeedsReplacementCount}";
        public string PlacementSummary => $"К простановке выбрано: {SelectedPlacementCount}";
        public string OpeningStatusSummary => $"{CalculatedCount} рассчитано · {ReviewCount} требуют проверки · {NoSolutionCount} без решения";
        public string SelectedCountText => $"{SelectedPlacementCount} из {WithoutLintelCount}";
        public string ManualReviewText => $"⚠ {ReviewCount + NoSolutionCount + NeedsReplacementCount} проёмов требуют ручной проверки";
        public string TypeCreationSummary
        {
            get
            {
                List<LintelVariantV3> variants = OpeningGroups.SelectMany(x => x.Walls)
                    .Where(x => x.IsSelected && x.CanPlace)
                    .Select(x => x.SelectedVariant)
                    .Where(x => x != null)
                    .GroupBy(x => x.TypeName ?? string.Empty)
                    .Select(x => x.First())
                    .ToList();
                return $"Будет создано {variants.Count(x => !x.IsExistingType)} новых типов, "
                       + $"{variants.Count(x => x.IsExistingType)} существующих типов будут переиспользованы";
            }
        }
        public string PlaceButtonText => $"Создать типы и поставить {SelectedPlacementCount} перемычек";
        public string SelectedExistingCountText => SelectedWall != null && SelectedWall.HasExistingLintels
            ? $"Выбрано: {SelectedWall.Count} экземпляров"
            : "Выберите стену с существующими перемычками";

        public void Reload()
        {
            var stopwatch = Stopwatch.StartNew();
            _variantCache.Clear();
            CompositeTypes = new ObservableCollection<FamilySymbol>(CollectCompositeTypes(_doc));
            BaseCompositeType = BaseCompositeType != null
                ? CompositeTypes.FirstOrDefault(x => x.Id == BaseCompositeType.Id) ?? CompositeTypes.FirstOrDefault()
                : CompositeTypes.FirstOrDefault();
            Catalog = new ObservableCollection<LintelCatalogItemV3>(CollectCatalog(_doc, out string catalogStatus));
            CatalogStatusText = catalogStatus;
            SupportPadChoices = new ObservableCollection<NestedTypeChoiceV3>(CollectNestedTypeChoices(_doc, CompositeTypes, "ОП_"));
            AngleChoices = new ObservableCollection<NestedTypeChoiceV3>(CollectNestedTypeChoices(_doc, CompositeTypes, "УГ_"));
            StripChoices = new ObservableCollection<NestedTypeChoiceV3>(CollectNestedTypeChoices(_doc, CompositeTypes, "Планка"));

            _records = CollectOpenings(_doc, _selection);
            OpeningGroups = BuildGroups(_records.Where(x => !x.ExistingLintelIds.Any()), false, GroupingMode);
            ExistingOpeningGroups = BuildGroups(_records.Where(x => x.ExistingLintelIds.Any()), true, GroupingMode);

            foreach (OpeningWallGroupV3 wall in OpeningGroups.SelectMany(x => x.Walls))
                CalculateVariants(wall);
            foreach (OpeningWallGroupV3 wall in ExistingOpeningGroups.SelectMany(x => x.Walls))
                CalculateVariants(wall);

            SelectedWall = OpeningGroups.SelectMany(x => x.Walls).FirstOrDefault()
                           ?? ExistingOpeningGroups.SelectMany(x => x.Walls).FirstOrDefault();

            RaisePropertyChanged(nameof(CompositeTypes));
            RaisePropertyChanged(nameof(Catalog));
            RaisePropertyChanged(nameof(CatalogStatusText));
            RaisePropertyChanged(nameof(SupportPadChoices));
            RaisePropertyChanged(nameof(AngleChoices));
            RaisePropertyChanged(nameof(StripChoices));
            RaisePropertyChanged(nameof(OpeningGroups));
            RaisePropertyChanged(nameof(ExistingOpeningGroups));
            RaiseSummaryProperties();
            stopwatch.Stop();
            CalculationDurationText = $"Сбор и расчёт: {stopwatch.Elapsed.TotalSeconds:0.0} с";
        }

        public void RecalculateSelected()
        {
            if (SelectedWall == null) return;
            CalculateVariants(SelectedWall);
            SelectedVariant = SelectedWall.SelectedVariant;
        }

        public bool TryAddPieceToSelectedVariant(out string error)
        {
            error = null;
            if (SelectedWall == null)
            {
                error = "Сначала выберите группу проёмов слева.";
                return false;
            }

            List<LintelCatalogItemV3> choices = GetManualCatalogChoices(SelectedWall);
            if (!choices.Any())
            {
                error = "Для текущих настроек в JSON нет типов, найденных в модели Revit.";
                return false;
            }

            LintelVariantV3 variant = SelectedVariant;
            if (variant == null)
            {
                variant = new LintelVariantV3
                {
                    Number = 1,
                    WallWidthMm = SelectedWall.WallWidthMm,
                    OpeningWidthMm = SelectedWall.OpeningWidthMm,
                    RequiredLengthMm = SelectedWall.OpeningWidthMm,
                    ToleranceMm = SelectedWall.ToleranceMm,
                    SupportType = SelectedWall.SupportType,
                    RequiredBearingWidthMm = GetEffectiveRequiredBearingWidth(SelectedWall),
                    IsManual = true,
                    LeftSupportPad = GetDefaultNestedChoice(BaseCompositeType, SupportPadChoices, "ОП_левая", SelectedWall.WallTypeName),
                    RightSupportPad = GetDefaultNestedChoice(BaseCompositeType, SupportPadChoices, "ОП_правая", SelectedWall.WallTypeName),
                    LeftAngle = GetDefaultNestedChoice(BaseCompositeType, AngleChoices, "УГ_левая", SelectedWall.WallTypeName),
                    RightAngle = GetDefaultNestedChoice(BaseCompositeType, AngleChoices, "УГ_правая", SelectedWall.WallTypeName),
                    Strip = GetDefaultNestedChoice(BaseCompositeType, StripChoices, "Планка", SelectedWall.WallTypeName)
                };
                SelectedWall.Variants.Add(variant);
                SelectedWall.SelectedVariant = variant;
                SelectedVariant = variant;
            }

            if (variant.Pieces.Count >= 7)
            {
                error = "Семейство поддерживает не более семи вложенных перемычек.";
                return false;
            }

            bool needBearing = SelectedWall.SupportType > 0
                               && !variant.Pieces.Any(x => string.Equals(x.Role, "Несущая", StringComparison.OrdinalIgnoreCase));
            LintelCatalogItemV3 selected = choices
                .Where(x => !needBearing || x.IsBearing)
                .OrderByDescending(x => x.LengthMm + 0.1 >= SelectedWall.OpeningWidthMm + 2 * x.MinimumBearingMm)
                .ThenByDescending(x => x.Priority)
                .ThenByDescending(x => x.WidthMm)
                .FirstOrDefault()
                ?? choices.First();

            variant.Pieces.Add(new LintelPieceV3
            {
                Number = variant.Pieces.Count + 1,
                AvailableTypes = new ObservableCollection<LintelCatalogItemV3>(choices),
                SelectedType = selected,
                Role = needBearing && selected.IsBearing ? "Несущая" : "Ненесущая",
                GapMm = 0
            });
            NotifyVariantEdited();
            LastMessage = variant.IsManual
                ? "Создан или дополнен ручной вариант. Проверьте длину и суммарную ширину комплекта."
                : "В расчётный вариант добавлена единичная перемычка.";
            return true;
        }

        public List<LintelCatalogItemV3> GetManualCatalogChoices(OpeningWallGroupV3 wall)
        {
            if (wall == null) return new List<LintelCatalogItemV3>();
            bool metal = string.Equals(wall.MaterialMode, "Металлическая", StringComparison.OrdinalIgnoreCase);
            bool partitions = string.Equals(wall.MasonryMode, "Перегородки", StringComparison.OrdinalIgnoreCase);
            int masonry = int.TryParse(wall.MasonryMode, out int parsed) ? parsed : 0;
            return Catalog
                .Where(x => x.IsMetal == metal)
                .Where(x => partitions ? x.MasonryHeight == 0 : masonry == 0 || x.MasonryHeight == 0 || x.MasonryHeight == masonry)
                .OrderByDescending(x => x.LengthMm + 0.1 >= wall.OpeningWidthMm + 2 * x.MinimumBearingMm)
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => x.LengthMm)
                .ThenByDescending(x => x.WidthMm)
                .ToList();
        }

        private static double GetEffectiveRequiredBearingWidth(OpeningWallGroupV3 wall)
        {
            if (wall == null || wall.SupportType == 0) return 0;
            double required = wall.RequiredBearingWidthMm > 0
                ? wall.RequiredBearingWidthMm
                : Math.Min(wall.WallWidthMm, 160);
            return wall.SupportType == 2
                ? Math.Min(required, wall.WallWidthMm / 2.0)
                : Math.Min(required, wall.WallWidthMm);
        }

        public void RecalculateAll()
        {
            foreach (OpeningWallGroupV3 wall in OpeningGroups.SelectMany(x => x.Walls)
                         .Concat(ExistingOpeningGroups.SelectMany(x => x.Walls)))
                CalculateVariants(wall);
            SelectedVariant = SelectedWall?.SelectedVariant;
            RaiseSummaryProperties();
        }

        public List<LintelPlacementRequestV3> CreatePlacementRequests(bool includeExisting)
        {
            IEnumerable<OpeningWallGroupV3> walls = OpeningGroups.SelectMany(x => x.Walls);
            if (includeExisting)
                walls = walls.Concat(ExistingOpeningGroups.SelectMany(x => x.Walls));

            return walls
                .Where(x => x.IsSelected && x.SelectedVariant != null && x.SelectedVariant.IsValid)
                .Select(x => new LintelPlacementRequestV3
                {
                    WallGroup = x,
                    Variant = x.SelectedVariant.Clone(),
                    ReplaceExisting = x.HasExistingLintels
                })
                .ToList();
        }

        public void NotifyVariantEdited()
        {
            if (SelectedVariant != null && SelectedWall != null)
                SelectedVariant.TypeName = LintelCombinationEngineV3.BuildTypeName(SelectedWall.MasonryMode, SelectedVariant);
            SelectedVariant?.Refresh();
            if (SelectedVariant?.IsManual == true && SelectedWall?.CanPlace == true)
                SelectedWall.IsSelected = true;
            RefreshSelectedTypeStatus();
            SelectedWall?.NotifyCalculated();
            RaisePropertyChanged(nameof(SelectedVariant));
            RaiseSummaryProperties();
        }

        public void RefreshSelectedTypeStatus()
        {
            if (SelectedVariant == null) return;
            FamilySymbol existing = CompositeTypes.FirstOrDefault(x =>
                string.Equals(x.Name, SelectedVariant.TypeName, StringComparison.OrdinalIgnoreCase));
            SelectedVariant.IsExistingType = existing != null;
            SelectedVariant.ExistingTypeId = existing?.Id;
            RaiseSummaryProperties();
        }

        public void NotifySelectionChanged()
        {
            RaiseSummaryProperties();
        }

        public void SelectAllCalculated(bool selected)
        {
            foreach (OpeningWallGroupV3 wall in OpeningGroups.SelectMany(x => x.Walls))
                wall.IsSelected = selected && wall.CanPlace;
            RaiseSummaryProperties();
        }

        public void ChangeGrouping(string mode)
        {
            GroupingMode = mode;
            OpeningGroups = BuildGroups(_records.Where(x => !x.ExistingLintelIds.Any()), false, GroupingMode);
            ExistingOpeningGroups = BuildGroups(_records.Where(x => x.ExistingLintelIds.Any()), true, GroupingMode);
            foreach (OpeningWallGroupV3 wall in OpeningGroups.SelectMany(x => x.Walls)
                         .Concat(ExistingOpeningGroups.SelectMany(x => x.Walls)))
                CalculateVariants(wall);
            SelectedWall = OpeningGroups.SelectMany(x => x.Walls).FirstOrDefault()
                           ?? ExistingOpeningGroups.SelectMany(x => x.Walls).FirstOrDefault();
            RaisePropertyChanged(nameof(OpeningGroups));
            RaisePropertyChanged(nameof(ExistingOpeningGroups));
            RaiseSummaryProperties();
        }

        private void RaiseSummaryProperties()
        {
            RaisePropertyChanged(nameof(HeaderSummary));
            RaisePropertyChanged(nameof(OpeningsSummary));
            RaisePropertyChanged(nameof(NeedsReplacementCount));
            RaisePropertyChanged(nameof(PlacementSummary));
            RaisePropertyChanged(nameof(OpeningStatusSummary));
            RaisePropertyChanged(nameof(SelectedCountText));
            RaisePropertyChanged(nameof(ManualReviewText));
            RaisePropertyChanged(nameof(TypeCreationSummary));
            RaisePropertyChanged(nameof(PlaceButtonText));
        }

        private void CalculateVariants(OpeningWallGroupV3 wall)
        {
            List<LintelCatalogItemV3> manualChoices = GetManualCatalogChoices(wall);
            wall.RequiredLengthMm = manualChoices.Any()
                ? manualChoices.Min(x => LintelCombinationEngineV3.GetRequiredLength(wall.OpeningWidthMm, x))
                : wall.OpeningWidthMm;
            string cacheKey = string.Join("|",
                Math.Round(wall.WallWidthMm, 1),
                Math.Round(wall.OpeningWidthMm, 1),
                wall.SupportType,
                Math.Round(wall.RequiredBearingWidthMm, 1),
                wall.MasonryMode,
                wall.MaterialMode,
                Math.Round(wall.ToleranceMm, 1));

            if (!_variantCache.TryGetValue(cacheKey, out List<LintelVariantV3> cached))
            {
                cached = LintelCombinationEngineV3.Calculate(
                    wall,
                    Catalog,
                    wall.MasonryMode,
                    wall.MaterialMode,
                    wall.ToleranceMm,
                    CompositeTypes);
                _variantCache[cacheKey] = cached.Select(x => x.Clone()).ToList();
            }
            else
            {
                cached = cached.Select(x => x.Clone()).ToList();
            }

            foreach (LintelVariantV3 variant in cached)
            {
                if (variant.LeftSupportPad == null)
                    variant.LeftSupportPad = GetDefaultNestedChoice(BaseCompositeType, SupportPadChoices, "ОП_левая", wall.WallTypeName);
                if (variant.RightSupportPad == null)
                    variant.RightSupportPad = GetDefaultNestedChoice(BaseCompositeType, SupportPadChoices, "ОП_правая", wall.WallTypeName);
                if (variant.LeftAngle == null)
                    variant.LeftAngle = GetDefaultNestedChoice(BaseCompositeType, AngleChoices, "УГ_левая", wall.WallTypeName);
                if (variant.RightAngle == null)
                    variant.RightAngle = GetDefaultNestedChoice(BaseCompositeType, AngleChoices, "УГ_правая", wall.WallTypeName);
                if (variant.Strip == null)
                    variant.Strip = GetDefaultNestedChoice(BaseCompositeType, StripChoices, "Планка", wall.WallTypeName);
            }

            wall.Variants = new ObservableCollection<LintelVariantV3>(cached);
            wall.SelectedVariant = wall.Variants.FirstOrDefault();
            wall.StatusText = wall.Variants.Any()
                ? "Рассчитано"
                : $"Нет решения: {wall.OpeningIdsText}; стена {Math.Round(wall.WallWidthMm)} мм; "
                  + $"проём {Math.Round(wall.OpeningWidthMm)} мм; требуемая длина ≥ {Math.Round(wall.RequiredLengthMm)} мм; "
                  + $"несущая сторона — {wall.BearingSideText}; "
                  + (manualChoices.Any()
                      ? "в каталоге отсутствует допустимая комбинация по толщине и несущей зоне."
                      : "для выбранных фильтров в JSON нет доступных типов, найденных в модели.");
            wall.IsSelected = wall.CanPlace;
            wall.NotifyCalculated();
        }

        private ObservableCollection<OpeningTypeGroupV3> BuildGroups(IEnumerable<OpeningRecordV3> records, bool existing, string groupingMode)
        {
            var result = new ObservableCollection<OpeningTypeGroupV3>();
            Func<OpeningRecordV3, string> groupKey = groupingMode == "По категории"
                ? new Func<OpeningRecordV3, string>(x => (x.CategoryName ?? "Проёмы") + "\u001f" + x.FamilyName + "\u001f" + x.TypeName)
                : groupingMode == "Без группировки"
                    ? new Func<OpeningRecordV3, string>(x => x.OpeningId.Value.ToString(CultureInfo.InvariantCulture))
                    : new Func<OpeningRecordV3, string>(x => x.FamilyName + "\u001f" + x.TypeName);

            foreach (var typeGroup in records.GroupBy(groupKey).OrderBy(x => x.Key))
            {
                OpeningRecordV3 first = typeGroup.First();
                var group = groupingMode == "По категории"
                    ? new OpeningTypeGroupV3 { FamilyName = first.CategoryName, TypeName = first.FamilyName + " : " + first.TypeName }
                    : groupingMode == "Без группировки"
                        ? new OpeningTypeGroupV3 { FamilyName = first.FamilyName, TypeName = first.TypeName + " · ID " + first.IdText }
                        : new OpeningTypeGroupV3 { FamilyName = first.FamilyName, TypeName = first.TypeName };

                foreach (var wallGroup in typeGroup.GroupBy(x => x.WallTypeId.Value).OrderBy(x => x.First().WallTypeName))
                {
                    OpeningRecordV3 firstWall = wallGroup.First();
                    var wall = new OpeningWallGroupV3
                    {
                        Parent = group,
                        WallTypeId = firstWall.WallTypeId,
                        WallTypeName = firstWall.WallTypeName,
                        WallWidthMm = firstWall.WallWidthMm,
                        ExistingTypeChoices = CompositeTypes,
                        CurrentLintelTypeName = existing ? GetCurrentLintelTypeName(_doc, wallGroup.SelectMany(x => x.ExistingLintelIds)) : null
                    };
                    wall.Openings.AddRange(wallGroup);
                    wall.SelectedExistingType = CompositeTypes.FirstOrDefault(x => x.Name == wall.CurrentLintelTypeName) ?? CompositeTypes.FirstOrDefault();
                    group.Walls.Add(wall);
                }
                result.Add(group);
            }
            return result;
        }

        private static List<NestedTypeChoiceV3> CollectNestedTypeChoices(
            Document doc,
            IEnumerable<FamilySymbol> compositeTypes,
            string parameterPrefix)
        {
            var ids = new HashSet<long>();
            foreach (FamilySymbol symbol in compositeTypes)
            {
                foreach (Parameter parameter in symbol.Parameters.Cast<Parameter>())
                {
                    if (parameter.StorageType != StorageType.ElementId
                        || !parameter.Definition.Name.StartsWith(parameterPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    ElementId id = parameter.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId) ids.Add(id.Value);
                }
            }

            var result = new List<NestedTypeChoiceV3>
            {
                new NestedTypeChoiceV3 { Id = ElementId.InvalidElementId, DisplayName = "<Нет>" }
            };
            result.AddRange(ids.Select(id => doc.GetElement(new ElementId(id)) as FamilySymbol)
                .Where(x => x != null)
                .OrderBy(x => x.FamilyName)
                .ThenBy(x => x.Name)
                .Select(x => new NestedTypeChoiceV3
                {
                    Id = x.Id,
                    DisplayName = x.FamilyName + " : " + x.Name
                }));
            return result;
        }

        private static NestedTypeChoiceV3 GetDefaultNestedChoice(
            FamilySymbol baseType,
            IEnumerable<NestedTypeChoiceV3> choices,
            string parameterPrefix,
            string wallTypeName)
        {
            List<NestedTypeChoiceV3> list = choices.ToList();
            if (baseType == null) return list.FirstOrDefault();
            string context = (wallTypeName ?? string.Empty).IndexOf("НСЩ", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Каркас несущий"
                : "Перегородка";
            Parameter parameter = baseType.Parameters.Cast<Parameter>()
                .Where(x => x.StorageType == StorageType.ElementId)
                .Where(x => x.Definition.Name.StartsWith(parameterPrefix, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(x => x.Definition.Name.IndexOf(context, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? baseType.Parameters.Cast<Parameter>()
                    .FirstOrDefault(x => x.StorageType == StorageType.ElementId
                                         && x.Definition.Name.StartsWith(parameterPrefix, StringComparison.OrdinalIgnoreCase));
            ElementId selectedId = parameter?.AsElementId() ?? ElementId.InvalidElementId;
            return list.FirstOrDefault(x => x.Id.Value == selectedId.Value) ?? list.FirstOrDefault();
        }

        private static string GetCurrentLintelTypeName(Document doc, IEnumerable<ElementId> ids)
        {
            List<string> names = ids.Distinct()
                .Select(id => doc.GetElement(id) as FamilyInstance)
                .Where(x => x?.Symbol != null)
                .Select(x => x.Symbol.Name)
                .Distinct()
                .ToList();
            return names.Count == 0 ? "Не определён" : string.Join(", ", names);
        }

        private static List<FamilySymbol> CollectCompositeTypes(Document doc)
        {
            Family family = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(x => x.FamilyCategory != null
                            && x.FamilyCategory.Id.Value == (int)BuiltInCategory.OST_StructuralFraming)
                .Where(x => x.GetFamilySymbolIds() != null && x.GetFamilySymbolIds().Count > 0)
                .Where(x =>
                {
                    FamilySymbol firstType = doc.GetElement(x.GetFamilySymbolIds().First()) as FamilySymbol;
                    string model = firstType?.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString();
                    return string.Equals(model, "Перемычки составные", StringComparison.Ordinal);
                })
                .OrderBy(x => x.Name, new AlphanumComparatorFastString())
                .FirstOrDefault();

            if (family == null) return new List<FamilySymbol>();
            return family.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Where(x => x != null)
                .OrderBy(x => x.Name, new AlphanumComparatorFastString())
                .ToList();
        }

        private static List<LintelCatalogItemV3> CollectCatalog(Document doc, out string status)
        {
            string path = ResolveCatalogPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                status = "JSON-каталог одиночных перемычек не найден: LintelUnitCatalog_v3.json";
                return new List<LintelCatalogItemV3>();
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                LintelCatalogFileV3 file = JsonSerializer.Deserialize<LintelCatalogFileV3>(json, options)
                                           ?? new LintelCatalogFileV3();
                LintelCatalogDefaultsV3 defaults = file.Defaults ?? new LintelCatalogDefaultsV3();

                List<FamilySymbol> symbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .ToList();
                var result = new List<LintelCatalogItemV3>();
                var unresolved = new List<string>();
                int disabled = 0;

                foreach (LintelCatalogEntryV3 entry in file.Items ?? new List<LintelCatalogEntryV3>())
                {
                    if (!entry.Enabled)
                    {
                        disabled++;
                        continue;
                    }
                    string familyName = string.IsNullOrWhiteSpace(entry.FamilyName)
                        ? defaults.FamilyName
                        : entry.FamilyName.Trim();
                    if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(entry.TypeName))
                    {
                        unresolved.Add("запись без familyName/typeName");
                        continue;
                    }

                    FamilySymbol symbol = symbols.FirstOrDefault(x =>
                        string.Equals(x.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Name, entry.TypeName.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (symbol == null)
                    {
                        unresolved.Add(familyName + " : " + entry.TypeName);
                        continue;
                    }

                    double length = entry.LengthMm > 0
                        ? entry.LengthMm
                        : GetLengthMm(symbol, "ADSK_Размер_Длина", "Длина", "Length");
                    double width = entry.WidthMm > 0
                        ? entry.WidthMm
                        : GetLengthMm(symbol, "ADSK_Размер_Ширина", "Ширина", "Width");
                    double height = entry.HeightMm > 0
                        ? entry.HeightMm
                        : GetLengthMm(symbol, "ADSK_Размер_Высота", "Высота", "Height");
                    if (length <= 0 || width <= 0)
                    {
                        unresolved.Add(familyName + " : " + entry.TypeName + " (нет длины/ширины)");
                        continue;
                    }

                    string material = string.IsNullOrWhiteSpace(entry.Material) ? defaults.Material ?? string.Empty : entry.Material;
                    bool isMetal = material.IndexOf("металл", StringComparison.OrdinalIgnoreCase) >= 0
                                   || material.IndexOf("сталь", StringComparison.OrdinalIgnoreCase) >= 0;
                    result.Add(new LintelCatalogItemV3
                    {
                        Symbol = symbol,
                        DisplayName = symbol.FamilyName + " : " + symbol.Name,
                        Mark = string.IsNullOrWhiteSpace(entry.Code) ? symbol.Name : entry.Code.Trim(),
                        LengthMm = length,
                        WidthMm = width,
                        HeightMm = height,
                        IsBearing = entry.IsBearing ?? defaults.IsBearing,
                        IsMetal = isMetal,
                        MasonryHeight = entry.MasonryHeightMm ?? defaults.MasonryHeightMm,
                        MinimumOpeningWidthMm = entry.MinimumOpeningWidthMm,
                        MaximumOpeningWidthMm = entry.MaximumOpeningWidthMm,
                        MinimumBearingMm = Math.Max(0, entry.MinimumBearingMm ?? defaults.MinimumBearingMm),
                        LoadCategory = string.IsNullOrWhiteSpace(entry.LoadCategory) ? defaults.LoadCategory ?? string.Empty : entry.LoadCategory,
                        Priority = entry.Priority ?? defaults.Priority,
                        IsAutoAllowed = entry.AutoAllowed ?? defaults.AutoAllowed
                    });
                }

                status = $"JSON-каталог: {result.Count} активных типов";
                if (disabled > 0) status += $", {disabled} отключено";
                if (unresolved.Count > 0)
                {
                    status += $", {unresolved.Count} не найдено в проекте";
                    status += ". Первые: " + string.Join("; ", unresolved.Take(3));
                }
                return result
                    .OrderByDescending(x => x.Priority)
                    .ThenBy(x => x.LengthMm)
                    .ThenByDescending(x => x.WidthMm)
                    .ToList();
            }
            catch (Exception ex)
            {
                status = "Ошибка чтения JSON-каталога: " + ex.Message;
                return new List<LintelCatalogItemV3>();
            }
        }

        private static string ResolveCatalogPath()
        {
            const string fileName = "LintelUnitCatalog_v3.json";
            string assemblyDirectory = Path.GetDirectoryName(typeof(CommandLintelCreator_v3).Assembly.Location) ?? string.Empty;
            var candidates = new List<string>
            {
                Path.Combine(assemblyDirectory, fileName),
                Path.Combine(assemblyDirectory, "LintelCreator - v2", fileName)
            };
            try
            {
                candidates.Add(Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "LintelCreator - v2", fileName)));
            }
            catch
            {
                // Для установленной сборки достаточно файла рядом с DLL.
            }
            return candidates.FirstOrDefault(File.Exists) ?? candidates.First();
        }

        private static string GetStringValue(Element element, params string[] names)
        {
            if (element == null) return string.Empty;
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                string value = parameter?.AsString() ?? parameter?.AsValueString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return string.Empty;
        }

        private static bool? GetBooleanValue(Element element, params string[] names)
        {
            if (element == null) return null;
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null) continue;
                if (parameter.StorageType == StorageType.Integer) return parameter.AsInteger() != 0;
                string value = parameter.AsString() ?? parameter.AsValueString();
                if (string.Equals(value, "Да", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "True", StringComparison.OrdinalIgnoreCase)
                    || value == "1") return true;
                if (string.Equals(value, "Нет", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "False", StringComparison.OrdinalIgnoreCase)
                    || value == "0") return false;
            }
            return null;
        }

        private static List<OpeningRecordV3> CollectOpenings(Document doc, Selection selection)
        {
            List<Element> openings = selection.GetElementIds().Select(doc.GetElement).Where(IsSupportedOpening).ToList();
            if (!openings.Any())
            {
                openings.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Where(IsSupportedOpening));
            }

            List<Wall> nonCurtainWalls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .OfType<Wall>()
                .Where(x => x.WallType.Kind != WallKind.Curtain)
                .ToList();

            var insertHosts = new Dictionary<long, Wall>();
            HashSet<long> curtainIds = new HashSet<long>(openings.OfType<Wall>().Select(x => x.Id.Value));
            if (curtainIds.Any())
            {
                foreach (Wall candidate in nonCurtainWalls)
                {
                    foreach (ElementId insertId in candidate.FindInserts(false, false, true, false))
                    {
                        if (curtainIds.Contains(insertId.Value) && !insertHosts.ContainsKey(insertId.Value))
                            insertHosts[insertId.Value] = candidate;
                    }
                }
            }

            List<SupportGeometryV3> supportElements = CollectSupportElements(doc, openings);
            List<ExistingLintelGeometryV3> existingLintels = CollectExistingLintels(doc, openings);
            var result = new List<OpeningRecordV3>();
            foreach (Element opening in openings.Distinct(new ElementIdComparerV3()))
            {
                BoundingBoxXYZ box = opening.get_BoundingBox(null);
                if (box == null) continue;
                Wall wall = FindHostWall(doc, opening, box, insertHosts);
                if (wall == null || wall.WallType.Kind == WallKind.Curtain) continue;

                XYZ location = GetLocation(opening, wall, box);
                double width = GetOpeningWidthMm(opening, box);
                double height = GetOpeningHeightMm(opening, box);
                DetectSupport(wall, location, box.Max.Z, width, supportElements,
                    out int supportType, out XYZ supportDirection, out double requiredBearingWidthMm);

                result.Add(new OpeningRecordV3
                {
                    OpeningId = opening.Id,
                    WallId = wall.Id,
                    WallTypeId = wall.GetTypeId(),
                    LevelId = opening.LevelId != null && opening.LevelId != ElementId.InvalidElementId ? opening.LevelId : wall.LevelId,
                    FamilyName = opening is FamilyInstance fi ? fi.Symbol.FamilyName : "Витраж",
                    TypeName = opening is FamilyInstance fi2 ? fi2.Symbol.Name : opening.Name,
                    CategoryName = opening.Category?.Name ?? "Проёмы",
                    WallTypeName = wall.WallType.Name,
                    OpeningWidthMm = width,
                    OpeningHeightMm = height,
                    WallWidthMm = wall.Width * 304.8,
                    Location = location,
                    TopElevation = box.Max.Z,
                    WallOrientation = opening is FamilyInstance openingInstance ? openingInstance.FacingOrientation : wall.Orientation,
                    BearingDirection = supportDirection,
                    SupportType = supportType,
                    RequiredBearingWidthMm = requiredBearingWidthMm,
                    ExistingLintelIds = FindExistingLintels(existingLintels, box)
                });
            }
            return result;
        }

        private static bool IsSupportedOpening(Element element)
        {
            if (element == null || element.Category == null) return false;
            long category = element.Category.Id.Value;
            if (category == (long)BuiltInCategory.OST_Doors || category == (long)BuiltInCategory.OST_Windows)
                return element is FamilyInstance fi && fi.SuperComponent == null;
            return category == (long)BuiltInCategory.OST_Walls
                   && element is Wall wall
                   && wall.WallType.Kind == WallKind.Curtain;
        }

        private static Wall FindHostWall(Document doc, Element opening, BoundingBoxXYZ box, IDictionary<long, Wall> insertHosts)
        {
            if (opening is FamilyInstance instance && instance.Host is Wall directHost)
                return directHost;

            if (insertHosts.TryGetValue(opening.Id.Value, out Wall insertHost)) return insertHost;

            double expansion = 200 / 304.8;
            var outline = new Outline(
                new XYZ(box.Min.X - expansion, box.Min.Y - expansion, box.Min.Z - expansion),
                new XYZ(box.Max.X + expansion, box.Max.Y + expansion, box.Max.Z + expansion));
            XYZ center = (box.Min + box.Max) / 2.0;

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .OfType<Wall>()
                .Where(w => w.WallType.Kind != WallKind.Curtain)
                .Select(w => new { Wall = w, Distance = DistanceToWallCurve(w, center) })
                .Where(x => x.Distance < expansion + x.Wall.Width / 2.0)
                .OrderBy(x => x.Distance)
                .Select(x => x.Wall)
                .FirstOrDefault();
        }

        private static double DistanceToWallCurve(Wall wall, XYZ point)
        {
            Curve curve = (wall.Location as LocationCurve)?.Curve;
            if (curve == null) return double.MaxValue;
            IntersectionResult projection = curve.Project(new XYZ(point.X, point.Y, curve.GetEndPoint(0).Z));
            return projection == null ? double.MaxValue : projection.XYZPoint.DistanceTo(new XYZ(point.X, point.Y, projection.XYZPoint.Z));
        }

        private static List<SupportGeometryV3> CollectSupportElements(Document doc, IEnumerable<Element> openings)
        {
            List<BoundingBoxXYZ> openingBoxes = openings.Select(x => x.get_BoundingBox(null)).Where(x => x != null).ToList();
            var typeCodeCache = new Dictionary<long, double>();
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType();

            if (openingBoxes.Any())
            {
                double horizontal = 1000 / 304.8;
                double vertical = 1200 / 304.8;
                XYZ minimum = new XYZ(
                    openingBoxes.Min(x => x.Min.X) - horizontal,
                    openingBoxes.Min(x => x.Min.Y) - horizontal,
                    openingBoxes.Min(x => x.Max.Z) - 100 / 304.8);
                XYZ maximum = new XYZ(
                    openingBoxes.Max(x => x.Max.X) + horizontal,
                    openingBoxes.Max(x => x.Max.Y) + horizontal,
                    openingBoxes.Max(x => x.Max.Z) + vertical);
                collector = collector.WherePasses(new BoundingBoxIntersectsFilter(new Outline(minimum, maximum)));
            }

            return collector
                .Where(element =>
                {
                    ElementId typeId = element.GetTypeId();
                    if (!typeCodeCache.TryGetValue(typeId.Value, out double code))
                    {
                        code = GetDoubleValue(doc.GetElement(typeId), "ZH_Код_Тип_Число", "ZH_Код_Тип");
                        typeCodeCache[typeId.Value] = code;
                    }
                    if (Math.Abs(code) < 1e-9)
                        code = GetDoubleValue(element, "ZH_Код_Тип_Число", "ZH_Код_Тип");
                    return (code >= 311 && code < 312) || (code >= 317 && code < 318);
                })
                .Select(element => new SupportGeometryV3 { Box = element.get_BoundingBox(null) })
                .Where(x => x.Box != null)
                .ToList();
        }

        private static void DetectSupport(
            Wall wall,
            XYZ location,
            double top,
            double openingWidthMm,
            IEnumerable<SupportGeometryV3> supports,
            out int supportType,
            out XYZ direction,
            out double requiredBearingWidthMm)
        {
            XYZ normal = wall.Orientation.Normalize();
            XYZ along = normal.CrossProduct(XYZ.BasisZ).Normalize();
            XYZ center = new XYZ(location.X, location.Y, top);
            double centerNormal = center.DotProduct(normal);
            double centerAlong = center.DotProduct(along);
            double wallMin = centerNormal - wall.Width / 2.0;
            double wallMax = centerNormal + wall.Width / 2.0;
            double openingHalfWidth = Math.Max(350, openingWidthMm / 2.0) / 304.8;
            bool first = false;
            bool second = false;
            double firstDepth = 0;
            double secondDepth = 0;

            foreach (SupportGeometryV3 support in supports)
            {
                BoundingBoxXYZ box = support.Box;
                if (box == null || box.Max.Z < top - 50 / 304.8 || box.Min.Z > top + 1000 / 304.8) continue;

                GetProjectedRange(box, along, out double alongMin, out double alongMax);
                if (centerAlong + openingHalfWidth < alongMin || centerAlong - openingHalfWidth > alongMax) continue;

                GetProjectedRange(box, normal, out double supportMin, out double supportMax);
                double overlapMin = Math.Max(wallMin, supportMin);
                double overlapMax = Math.Min(wallMax, supportMax);
                if (overlapMax <= overlapMin + 1e-6) continue;

                if (overlapMin < centerNormal - 1e-6)
                {
                    first = true;
                    firstDepth = Math.Max(firstDepth, (Math.Min(overlapMax, centerNormal) - overlapMin) * 304.8);
                }
                if (overlapMax > centerNormal + 1e-6)
                {
                    second = true;
                    secondDepth = Math.Max(secondDepth, (overlapMax - Math.Max(overlapMin, centerNormal)) * 304.8);
                }
            }

            supportType = first && second ? 2 : first || second ? 1 : 0;
            direction = supportType == 1 ? (first ? -normal : normal) : XYZ.Zero;
            requiredBearingWidthMm = supportType == 0 ? 0 : Math.Max(firstDepth, secondDepth);
            if (supportType > 0 && requiredBearingWidthMm < 1)
                requiredBearingWidthMm = Math.Min(wall.Width * 304.8, 160);
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

        private static List<ExistingLintelGeometryV3> CollectExistingLintels(Document doc, IEnumerable<Element> openings)
        {
            List<BoundingBoxXYZ> openingBoxes = openings
                .Select(x => x.get_BoundingBox(null))
                .Where(x => x != null)
                .ToList();
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType();

            if (openingBoxes.Any())
            {
                double horizontal = 300 / 304.8;
                double lower = 200 / 304.8;
                double upper = 300 / 304.8;
                var outline = new Outline(
                    new XYZ(
                        openingBoxes.Min(x => x.Min.X) - horizontal,
                        openingBoxes.Min(x => x.Min.Y) - horizontal,
                        openingBoxes.Min(x => x.Max.Z) - lower),
                    new XYZ(
                        openingBoxes.Max(x => x.Max.X) + horizontal,
                        openingBoxes.Max(x => x.Max.Y) + horizontal,
                        openingBoxes.Max(x => x.Max.Z) + upper));
                collector = collector.WherePasses(new BoundingBoxIntersectsFilter(outline));
            }

            return collector
                .OfType<FamilyInstance>()
                .Where(instance => instance.SuperComponent == null && IsLintel(instance))
                .Select(instance => new ExistingLintelGeometryV3
                {
                    Id = instance.Id,
                    Box = instance.get_BoundingBox(null)
                })
                .Where(x => x.Box != null)
                .ToList();
        }

        private static List<ElementId> FindExistingLintels(
            IEnumerable<ExistingLintelGeometryV3> lintels,
            BoundingBoxXYZ box)
        {
            XYZ center = (box.Min + box.Max) / 2.0;
            XYZ minimum = new XYZ(center.X - 250 / 304.8, center.Y - 250 / 304.8, box.Max.Z - 150 / 304.8);
            XYZ maximum = new XYZ(center.X + 250 / 304.8, center.Y + 250 / 304.8, box.Max.Z + 250 / 304.8);
            return (lintels ?? Enumerable.Empty<ExistingLintelGeometryV3>())
                .Where(x => BoxesIntersect(x.Box, minimum, maximum))
                .Select(x => x.Id)
                .Distinct()
                .ToList();
        }

        private static bool BoxesIntersect(BoundingBoxXYZ box, XYZ minimum, XYZ maximum)
        {
            return box != null
                   && box.Max.X >= minimum.X && box.Min.X <= maximum.X
                   && box.Max.Y >= minimum.Y && box.Min.Y <= maximum.Y
                   && box.Max.Z >= minimum.Z && box.Min.Z <= maximum.Z;
        }

        private static bool IsLintel(FamilyInstance instance)
        {
            string grouping = instance.LookupParameter("ADSK_Группирование")?.AsString();
            string keyNote = instance.Symbol?.LookupParameter("Ключевая пометка")?.AsString();
            string model = instance.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString();
            return string.Equals(grouping, "ПР", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(keyNote, "ПР", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(model, "Перемычки составные", StringComparison.OrdinalIgnoreCase);
        }

        private static XYZ GetLocation(Element opening, Wall hostWall, BoundingBoxXYZ box)
        {
            if (opening.Location is LocationPoint point) return point.Point;
            if (opening.Location is LocationCurve curve)
            {
                XYZ source = (curve.Curve.GetEndPoint(0) + curve.Curve.GetEndPoint(1)) / 2.0;
                Curve hostCurve = (hostWall.Location as LocationCurve)?.Curve;
                IntersectionResult projection = hostCurve?.Project(source);
                return projection?.XYZPoint ?? source;
            }
            return (box.Min + box.Max) / 2.0;
        }

        private static double GetOpeningWidthMm(Element opening, BoundingBoxXYZ box)
        {
            double value = GetLengthMm(opening, "ADSK_Размер_Ширина", "Ширина", "Длина");
            if (value > 0) return value;
            XYZ size = box.Max - box.Min;
            return Math.Max(size.X, size.Y) * 304.8;
        }

        private static double GetOpeningHeightMm(Element opening, BoundingBoxXYZ box)
        {
            double value = GetLengthMm(opening, "ADSK_Размер_Высота", "Высота", "Неприсоединенная высота");
            return value > 0 ? value : (box.Max.Z - box.Min.Z) * 304.8;
        }

        private static double GetLengthMm(Element element, params string[] names)
        {
            if (element == null) return 0;
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null && element is FamilyInstance instance)
                    parameter = instance.Symbol.LookupParameter(name);
                if (parameter == null) continue;
                if (parameter.StorageType == StorageType.Double && parameter.AsDouble() > 0)
                    return parameter.AsDouble() * 304.8;
                if (double.TryParse(parameter.AsValueString(), NumberStyles.Any, CultureInfo.CurrentCulture, out double value) && value > 0)
                    return value;
            }
            return 0;
        }

        private static int GetInteger(Element element, params string[] names)
        {
            if (element == null) return 0;
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null) continue;
                if (parameter.StorageType == StorageType.Integer) return parameter.AsInteger();
                if (int.TryParse(parameter.AsValueString(), out int value)) return value;
            }
            return 0;
        }

        private static double GetDoubleValue(Element element, params string[] names)
        {
            if (element == null) return 0;
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null) continue;
                if (parameter.StorageType == StorageType.Double) return parameter.AsDouble();
                string text = parameter.AsValueString() ?? parameter.AsString();
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out double value)) return value;
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return value;
            }
            return 0;
        }
    }

    public static class LintelCombinationEngineV3
    {
        public static List<LintelVariantV3> Calculate(
            OpeningWallGroupV3 wall,
            IEnumerable<LintelCatalogItemV3> fullCatalog,
            string masonryMode,
            string materialMode,
            double toleranceMm,
            IEnumerable<FamilySymbol> existingCompositeTypes)
        {
            bool metal = string.Equals(materialMode, "Металлическая", StringComparison.OrdinalIgnoreCase);
            int masonry = int.TryParse(masonryMode, out int parsed) ? parsed : 0;
            bool partitions = string.Equals(masonryMode, "Перегородки", StringComparison.OrdinalIgnoreCase);

            List<LintelCatalogItemV3> editorChoices = fullCatalog
                .Where(x => x.IsMetal == metal)
                .Where(x => partitions ? x.MasonryHeight == 0 : masonry == 0 || x.MasonryHeight == 0 || x.MasonryHeight == masonry)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.LengthMm)
                .ThenByDescending(x => x.WidthMm)
                .ToList();

            List<LintelCatalogItemV3> filtered = editorChoices
                .Where(x => x.IsAutoAllowed)
                .Where(x => x.MinimumOpeningWidthMm <= 0 || wall.OpeningWidthMm + 0.1 >= x.MinimumOpeningWidthMm)
                .Where(x => x.MaximumOpeningWidthMm <= 0 || wall.OpeningWidthMm - 0.1 <= x.MaximumOpeningWidthMm)
                .Where(x => x.LengthMm + 0.1 >= GetRequiredLength(wall.OpeningWidthMm, x))
                .OrderBy(x => x.LengthMm)
                .ThenByDescending(x => x.Priority)
                .ThenByDescending(x => x.WidthMm)
                .GroupBy(x => Math.Round(x.WidthMm) + "|" + x.IsBearing)
                .Select(x => x.First())
                .Take(18)
                .OrderByDescending(x => x.WidthMm)
                .ToList();

            if (!filtered.Any()) return new List<LintelVariantV3>();

            double requiredBearingWidth = wall.SupportType == 0
                ? 0
                : wall.RequiredBearingWidthMm > 0
                    ? wall.RequiredBearingWidthMm
                    : Math.Min(wall.WallWidthMm, 160);
            if (wall.SupportType == 2)
                requiredBearingWidth = Math.Min(requiredBearingWidth, wall.WallWidthMm / 2.0);
            else
                requiredBearingWidth = Math.Min(requiredBearingWidth, wall.WallWidthMm);

            List<List<LintelCatalogItemV3>> combinations = FindCombinations(
                filtered,
                wall.WallWidthMm,
                toleranceMm,
                wall.SupportType,
                requiredBearingWidth);

            var ranked = combinations
                .Where(items => CanArrangeForBearing(items, wall.SupportType, requiredBearingWidth))
                .Select(items => new
                {
                    Items = items,
                    Delta = Math.Abs(items.Sum(x => x.WidthMm) - wall.WallWidthMm),
                    Count = items.Count,
                    Marks = items.Select(x => x.Mark).Distinct().Count(),
                    ExcessLength = items.Sum(x => x.LengthMm - GetRequiredLength(wall.OpeningWidthMm, x)),
                    Priority = items.Sum(x => x.Priority)
                })
                .OrderBy(x => x.Delta)
                .ThenBy(x => x.Count)
                .ThenBy(x => x.Marks)
                .ThenBy(x => x.ExcessLength)
                .ThenByDescending(x => x.Priority)
                .Take(5)
                .ToList();

            var variants = new List<LintelVariantV3>();
            int number = 1;
            foreach (var candidate in ranked)
            {
                var variant = new LintelVariantV3
                {
                    Number = number,
                    WallWidthMm = wall.WallWidthMm,
                    OpeningWidthMm = wall.OpeningWidthMm,
                    RequiredLengthMm = candidate.Items.Max(x => GetRequiredLength(wall.OpeningWidthMm, x)),
                    ToleranceMm = toleranceMm,
                    SupportType = wall.SupportType,
                    RequiredBearingWidthMm = requiredBearingWidth,
                    IsRecommended = number == 1
                };

                List<(LintelCatalogItemV3 Item, bool IsBearing)> arranged = ArrangeForBearing(candidate.Items, wall.SupportType, requiredBearingWidth);
                foreach ((LintelCatalogItemV3 item, bool isBearing) in arranged)
                {
                    variant.Pieces.Add(new LintelPieceV3
                    {
                        AvailableTypes = new ObservableCollection<LintelCatalogItemV3>(editorChoices),
                        SelectedType = item,
                        Role = isBearing ? "Несущая" : "Ненесущая",
                        GapMm = 0
                    });
                }

                variant.TypeName = BuildTypeName(masonryMode, variant);
                FamilySymbol existing = existingCompositeTypes.FirstOrDefault(x =>
                    string.Equals(x.Name, variant.TypeName, StringComparison.OrdinalIgnoreCase));
                variant.IsExistingType = existing != null;
                variant.ExistingTypeId = existing?.Id;
                variant.Refresh();
                variants.Add(variant);
                number++;
            }
            return variants;
        }

        public static double GetRequiredLength(double openingWidthMm, LintelCatalogItemV3 item)
        {
            return openingWidthMm + 2 * Math.Max(0, item?.MinimumBearingMm ?? 0);
        }

        private static List<List<LintelCatalogItemV3>> FindCombinations(
            IList<LintelCatalogItemV3> items,
            double target,
            double tolerance,
            int supportType,
            double requiredBearingWidth)
        {
            const int maxElements = 7;
            const int statesPerBucket = 6;
            const int resultLimit = 300;
            double maximum = target + tolerance + 0.1;
            var frontier = new List<CombinationStateV3>
            {
                new CombinationStateV3 { StartIndex = 0, Width = 0 }
            };
            var results = new Dictionary<string, CombinationStateV3>();

            for (int depth = 1; depth <= maxElements && frontier.Count > 0; depth++)
            {
                var buckets = new Dictionary<string, List<CombinationStateV3>>();
                foreach (CombinationStateV3 state in frontier)
                {
                    for (int index = state.StartIndex; index < items.Count; index++)
                    {
                        double nextWidth = state.Width + items[index].WidthMm;
                        if (nextWidth > maximum) continue;

                        var next = state.Add(index, nextWidth);
                        if (Math.Abs(nextWidth - target) <= tolerance + 0.1)
                        {
                            string signature = string.Join(",", next.Indices);
                            if (!results.ContainsKey(signature)) results[signature] = next;
                        }

                        if (depth == maxElements || nextWidth >= maximum) continue;
                        int widthKey = (int)Math.Round(nextWidth * 10.0);
                        int bearingWidthKey = (int)Math.Round(next.Indices.Where(i => items[i].IsBearing).Sum(i => items[i].WidthMm) * 10.0);
                        int bearingCount = next.Indices.Count(i => items[i].IsBearing);
                        string bucketKey = index + "|" + widthKey + "|" + bearingWidthKey + "|" + bearingCount;
                        if (!buckets.TryGetValue(bucketKey, out List<CombinationStateV3> bucket))
                        {
                            bucket = new List<CombinationStateV3>();
                            buckets[bucketKey] = bucket;
                        }
                        bucket.Add(next);
                        if (bucket.Count > statesPerBucket)
                        {
                            bucket.Sort((left, right) => ComparePartialStates(left, right, items));
                            bucket.RemoveRange(statesPerBucket, bucket.Count - statesPerBucket);
                        }
                    }
                }

                frontier = buckets.Values.SelectMany(x => x).ToList();
            }

            return results.Values
                .Select(state => state.Indices.Select(index => items[index]).ToList())
                .Where(x => CanArrangeForBearing(x, supportType, requiredBearingWidth))
                .OrderBy(x => Math.Abs(x.Sum(item => item.WidthMm) - target))
                .ThenBy(x => x.Count)
                .ThenBy(x => x.Select(item => item.Mark).Distinct().Count())
                .ThenByDescending(x => x.Sum(item => item.Priority))
                .Take(resultLimit)
                .ToList();
        }

        private static int ComparePartialStates(
            CombinationStateV3 left,
            CombinationStateV3 right,
            IList<LintelCatalogItemV3> items)
        {
            int comparison = left.Indices.Select(index => items[index].Mark).Distinct().Count()
                .CompareTo(right.Indices.Select(index => items[index].Mark).Distinct().Count());
            if (comparison != 0) return comparison;
            comparison = left.Indices.Sum(index => items[index].LengthMm)
                .CompareTo(right.Indices.Sum(index => items[index].LengthMm));
            if (comparison != 0) return comparison;
            return right.Indices.Sum(index => items[index].Priority)
                .CompareTo(left.Indices.Sum(index => items[index].Priority));
        }

        private sealed class CombinationStateV3
        {
            public int StartIndex { get; set; }
            public double Width { get; set; }
            public List<int> Indices { get; } = new List<int>();

            public CombinationStateV3 Add(int index, double width)
            {
                var next = new CombinationStateV3 { StartIndex = index, Width = width };
                next.Indices.AddRange(Indices);
                next.Indices.Add(index);
                return next;
            }
        }

        private static bool CanArrangeForBearing(IList<LintelCatalogItemV3> items, int supportType, double requiredBearingWidth)
        {
            return ArrangeForBearing(items, supportType, requiredBearingWidth) != null;
        }

        private static List<(LintelCatalogItemV3 Item, bool IsBearing)> ArrangeForBearing(
            IList<LintelCatalogItemV3> items,
            int supportType,
            double requiredBearingWidth)
        {
            var indexed = items.Select((item, index) => (Item: item, Index: index)).ToList();
            if (supportType == 0 || requiredBearingWidth <= 0)
                return indexed.OrderByDescending(x => x.Item.WidthMm).Select(x => (x.Item, false)).ToList();

            var bearingCandidates = indexed.Where(x => x.Item.IsBearing)
                .OrderByDescending(x => x.Item.WidthMm)
                .ThenBy(x => x.Item.LengthMm)
                .ToList();
            var left = TakeBearingZone(bearingCandidates, requiredBearingWidth);
            if (left == null) return null;

            var used = new HashSet<int>(left.Select(x => x.Index));
            var right = new List<(LintelCatalogItemV3 Item, int Index)>();
            if (supportType == 2)
            {
                right = TakeBearingZone(bearingCandidates.Where(x => !used.Contains(x.Index)).ToList(), requiredBearingWidth);
                if (right == null) return null;
                foreach (var item in right) used.Add(item.Index);
            }

            var result = new List<(LintelCatalogItemV3 Item, bool IsBearing)>();
            result.AddRange(left.Select(x => (x.Item, true)));
            result.AddRange(indexed.Where(x => !used.Contains(x.Index)).OrderByDescending(x => x.Item.WidthMm).Select(x => (x.Item, false)));
            if (supportType == 2)
                result.AddRange(right.OrderBy(x => x.Item.WidthMm).Select(x => (x.Item, true)));
            return result;
        }

        private static List<(LintelCatalogItemV3 Item, int Index)> TakeBearingZone(
            IList<(LintelCatalogItemV3 Item, int Index)> candidates,
            double requiredWidth)
        {
            var selected = new List<(LintelCatalogItemV3 Item, int Index)>();
            double width = 0;
            foreach (var candidate in candidates)
            {
                selected.Add(candidate);
                width += candidate.Item.WidthMm;
                if (width + 0.1 >= requiredWidth) return selected;
            }
            return null;
        }

        public static string BuildTypeName(string masonryMode, LintelVariantV3 variant)
        {
            string masonry = string.Equals(masonryMode, "Перегородки", StringComparison.OrdinalIgnoreCase)
                ? "П"
                : masonryMode;
            double wallWidth = Math.Round(variant.WallWidthMm);
            if (wallWidth == 400) wallWidth = 380;
            if (wallWidth == 500) wallWidth = 510;
            if (wallWidth == 600) wallWidth = 640;

            string pieces = string.Join("_", variant.Pieces.Select(x =>
            {
                string token = Regex.Replace(x.SelectedType?.Mark ?? Math.Round(x.WidthMm).ToString(CultureInfo.InvariantCulture), @"[^\p{L}\p{Nd}-]+", string.Empty);
                if (string.IsNullOrWhiteSpace(token)) token = Math.Round(x.WidthMm).ToString(CultureInfo.InvariantCulture);
                return string.Equals(x.Role, "Несущая", StringComparison.OrdinalIgnoreCase)
                    ? token.ToUpperInvariant()
                    : token.ToLowerInvariant();
            }));
            string name = $"{masonry}_{wallWidth:0}_{Math.Round(variant.OpeningWidthMm):0}_{pieces}";
            return name.Length <= 120 ? name : name.Substring(0, 120);
        }
    }

    public enum LintelActionKindV3
    {
        SelectElements,
        Place,
        ChangeType,
        Replace,
        Delete
    }

    public sealed class LintelPlacementRequestV3
    {
        public OpeningWallGroupV3 WallGroup { get; set; }
        public LintelVariantV3 Variant { get; set; }
        public bool ReplaceExisting { get; set; }
    }

    public sealed class LintelActionRequestV3
    {
        public LintelActionKindV3 Kind { get; set; }
        public List<LintelPlacementRequestV3> Placements { get; set; } = new List<LintelPlacementRequestV3>();
        public List<ElementId> ExistingLintelIds { get; set; } = new List<ElementId>();
        public List<ElementId> SelectedElementIds { get; set; } = new List<ElementId>();
        public FamilySymbol TargetExistingType { get; set; }
    }

    public sealed class LintelActionHandlerV3 : IExternalEventHandler
    {
        private readonly LintelWorkspaceV3 _workspace;
        public LintelActionRequestV3 PendingRequest { get; set; }

        public LintelActionHandlerV3(LintelWorkspaceV3 workspace)
        {
            _workspace = workspace;
        }

        public void Execute(UIApplication app)
        {
            LintelActionRequestV3 request = PendingRequest;
            PendingRequest = null;
            if (request == null) return;

            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;
            if (request.Kind == LintelActionKindV3.SelectElements)
            {
                List<ElementId> ids = request.SelectedElementIds
                    .Where(x => x != null && x != ElementId.InvalidElementId && doc.GetElement(x) != null)
                    .Distinct()
                    .ToList();
                uidoc.Selection.SetElementIds(ids);
                uidoc.RefreshActiveView();
                _workspace.LastMessage = ids.Any()
                    ? $"В Revit выбрано элементов: {ids.Count}."
                    : "Элементы выбранного узла больше не существуют в модели.";
                return;
            }

            int changed = 0;
            var errors = new List<string>();

            using (var transaction = new Transaction(doc, GetTransactionName(request.Kind)))
            {
                transaction.Start();
                try
                {
                    if (request.Kind == LintelActionKindV3.ChangeType)
                    {
                        if (request.TargetExistingType == null)
                            throw new InvalidOperationException("Не выбран новый тип перемычки.");
                        if (!request.TargetExistingType.IsActive)
                        {
                            request.TargetExistingType.Activate();
                            doc.Regenerate();
                        }
                        foreach (ElementId id in request.ExistingLintelIds.Distinct())
                        {
                            if (doc.GetElement(id) is FamilyInstance instance)
                            {
                                instance.Symbol = request.TargetExistingType;
                                changed++;
                            }
                        }
                    }
                    else if (request.Kind == LintelActionKindV3.Delete)
                    {
                        foreach (ElementId id in request.ExistingLintelIds.Distinct())
                        {
                            if (doc.GetElement(id) != null)
                            {
                                doc.Delete(id);
                                changed++;
                            }
                        }
                    }
                    else
                    {
                        var typeCache = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
                        foreach (LintelPlacementRequestV3 placement in request.Placements)
                        {
                            try
                            {
                                FamilySymbol type = GetOrCreateCompositeType(doc, _workspace.BaseCompositeType, placement.WallGroup, placement.Variant, typeCache);
                                if (!type.IsActive)
                                {
                                    type.Activate();
                                    doc.Regenerate();
                                }

                                foreach (OpeningRecordV3 opening in placement.WallGroup.Openings)
                                {
                                    if (request.Kind == LintelActionKindV3.Replace || placement.ReplaceExisting)
                                    {
                                        foreach (ElementId existingId in opening.ExistingLintelIds.Distinct())
                                            if (doc.GetElement(existingId) != null) doc.Delete(existingId);
                                    }
                                    PlaceLintel(doc, opening, type);
                                    changed++;
                                }
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"{placement.WallGroup.DisplayName}: {ex.Message}");
                            }
                        }
                    }

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.RollBack();
                    TaskDialog.Show("Перемычки v3", ex.Message);
                    return;
                }
            }

            _workspace.LastMessage = errors.Any()
                ? $"Обработано: {changed}. Ошибки:\n" + string.Join("\n", errors)
                : $"Операция выполнена. Обработано элементов: {changed}.";
            _workspace.Reload();
            TaskDialog.Show("Перемычки v3", _workspace.LastMessage);
        }

        public string GetName()
        {
            return "Создание и изменение перемычек v3";
        }

        private static string GetTransactionName(LintelActionKindV3 kind)
        {
            if (kind == LintelActionKindV3.ChangeType) return "Изменение типа перемычек";
            if (kind == LintelActionKindV3.Delete) return "Удаление перемычек";
            if (kind == LintelActionKindV3.Replace) return "Замена перемычек";
            return "Создание перемычек v3";
        }

        private static FamilySymbol GetOrCreateCompositeType(
            Document doc,
            FamilySymbol baseType,
            OpeningWallGroupV3 wall,
            LintelVariantV3 variant,
            IDictionary<string, FamilySymbol> cache)
        {
            if (baseType == null) throw new InvalidOperationException("В единственном составном семействе не найден тип-шаблон.");
            string typeName = string.IsNullOrWhiteSpace(variant.TypeName)
                ? LintelCombinationEngineV3.BuildTypeName(wall.MasonryMode, variant)
                : variant.TypeName.Trim();

            if (cache.TryGetValue(typeName, out FamilySymbol cached)) return cached;
            FamilySymbol symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => x.Family.Id == baseType.Family.Id
                                     && string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));

            if (symbol != null)
            {
                if (!VariantMatchesType(symbol, wall, variant))
                    throw new InvalidOperationException($"Тип «{typeName}» уже существует, но его состав отличается от рассчитанного варианта.");
                cache[typeName] = symbol;
                return symbol;
            }

            symbol = baseType.Duplicate(typeName) as FamilySymbol;
            if (symbol == null) throw new InvalidOperationException("Не удалось создать тип " + typeName + ".");

            ApplyVariantToType(symbol, wall, variant);
            cache[typeName] = symbol;
            return symbol;
        }

        private static void ApplyVariantToType(FamilySymbol symbol, OpeningWallGroupV3 wall, LintelVariantV3 variant)
        {
            string context = wall.WallTypeName.IndexOf("НСЩ", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Каркас несущий"
                : "Перегородка";

            bool firstSlotSet = false;
            for (int i = 1; i <= 7; i++)
            {
                Parameter slot = FindSlotParameter(symbol, i, context);
                if (slot == null || slot.IsReadOnly) continue;
                if (i <= variant.Pieces.Count && variant.Pieces[i - 1].SelectedType != null)
                {
                    slot.Set(variant.Pieces[i - 1].SelectedType.Symbol.Id);
                    if (i == 1) firstSlotSet = true;
                }
                else
                {
                    try { slot.Set(ElementId.InvalidElementId); } catch { }
                }
            }

            if (!firstSlotSet)
                throw new InvalidOperationException("В базовом типе не найден доступный параметр «1ПР…» для контекста «" + context + "».");

            for (int i = 1; i <= 6; i++)
            {
                double centerDistance = 0;
                if (i < variant.Pieces.Count)
                {
                    centerDistance = variant.Pieces[i - 1].WidthMm / 2.0
                                     + variant.Pieces[i - 1].GapMm
                                     + variant.Pieces[i].WidthMm / 2.0;
                }
                SetLength(symbol, centerDistance, $"Отступ от {i} до {i + 1}");
            }

            SetNestedTypeParameter(symbol, "ОП_левая", context, variant.LeftSupportPad);
            SetNestedTypeParameter(symbol, "ОП_правая", context, variant.RightSupportPad);
            SetNestedTypeParameter(symbol, "УГ_левая", context, variant.LeftAngle);
            SetNestedTypeParameter(symbol, "УГ_правая", context, variant.RightAngle);
            SetNestedTypeParameter(symbol, "Планка", context, variant.Strip);

            LintelPieceV3 first = variant.Pieces.First();
            double bearing = Math.Max(0, (first.LengthMm - variant.OpeningWidthMm) / 2.0);
            SetLength(symbol, variant.OpeningWidthMm, "ADSK_Размер_Длина");
            SetLength(symbol, first.LengthMm, "Длина главной(первой) перемычки");
            SetLength(symbol, first.SelectedType?.HeightMm ?? 0, "Высота 1 перемычки");
            SetLength(symbol, bearing, "Мин. длина опирания ОП_левая");
            SetLength(symbol, bearing, "Мин. длина опирания ОП_правая");
        }

        private static bool VariantMatchesType(FamilySymbol symbol, OpeningWallGroupV3 wall, LintelVariantV3 variant)
        {
            string context = wall.WallTypeName.IndexOf("НСЩ", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Каркас несущий"
                : "Перегородка";
            for (int i = 1; i <= 7; i++)
            {
                Parameter slot = FindSlotParameter(symbol, i, context);
                if (slot == null) return false;
                ElementId expected = i <= variant.Pieces.Count
                    ? variant.Pieces[i - 1].SelectedType?.Symbol?.Id ?? ElementId.InvalidElementId
                    : ElementId.InvalidElementId;
                ElementId actual = slot.AsElementId() ?? ElementId.InvalidElementId;
                if (actual.Value != expected.Value) return false;
            }

            for (int i = 1; i < variant.Pieces.Count; i++)
            {
                double expectedMm = variant.Pieces[i - 1].WidthMm / 2.0
                                    + variant.Pieces[i - 1].GapMm
                                    + variant.Pieces[i].WidthMm / 2.0;
                Parameter offset = symbol.LookupParameter($"Отступ от {i} до {i + 1}");
                if (offset != null && offset.StorageType == StorageType.Double
                    && Math.Abs(offset.AsDouble() * 304.8 - expectedMm) > 0.5)
                    return false;
            }
            if (!NestedTypeParameterMatches(symbol, "ОП_левая", context, variant.LeftSupportPad)) return false;
            if (!NestedTypeParameterMatches(symbol, "ОП_правая", context, variant.RightSupportPad)) return false;
            if (!NestedTypeParameterMatches(symbol, "УГ_левая", context, variant.LeftAngle)) return false;
            if (!NestedTypeParameterMatches(symbol, "УГ_правая", context, variant.RightAngle)) return false;
            if (!NestedTypeParameterMatches(symbol, "Планка", context, variant.Strip)) return false;
            return true;
        }

        private static void SetNestedTypeParameter(
            FamilySymbol symbol,
            string prefix,
            string context,
            NestedTypeChoiceV3 choice)
        {
            if (choice == null) return;
            Parameter parameter = FindContextElementParameter(symbol, prefix, context);
            if (parameter != null && !parameter.IsReadOnly)
                parameter.Set(choice.Id ?? ElementId.InvalidElementId);
        }

        private static bool NestedTypeParameterMatches(
            FamilySymbol symbol,
            string prefix,
            string context,
            NestedTypeChoiceV3 choice)
        {
            if (choice == null) return true;
            Parameter parameter = FindContextElementParameter(symbol, prefix, context);
            if (parameter == null) return choice.Id == null || choice.Id == ElementId.InvalidElementId;
            ElementId actual = parameter.AsElementId() ?? ElementId.InvalidElementId;
            ElementId expected = choice.Id ?? ElementId.InvalidElementId;
            return actual.Value == expected.Value;
        }

        private static Parameter FindContextElementParameter(FamilySymbol symbol, string prefix, string context)
        {
            List<Parameter> parameters = symbol.Parameters.Cast<Parameter>()
                .Where(x => x.StorageType == StorageType.ElementId)
                .Where(x => x.Definition.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return parameters.FirstOrDefault(x => x.Definition.Name.IndexOf(context, StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? parameters.FirstOrDefault();
        }

        private static Parameter FindSlotParameter(FamilySymbol symbol, int index, string context)
        {
            List<Parameter> parameters = symbol.Parameters.Cast<Parameter>()
                .Where(x => Regex.IsMatch(x.Definition.Name, "^" + index + "ПР", RegexOptions.IgnoreCase))
                .Where(x => x.StorageType == StorageType.ElementId)
                .ToList();
            return parameters.FirstOrDefault(x => x.Definition.Name.IndexOf(context, StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? parameters.FirstOrDefault();
        }

        private static void SetLength(Element element, double valueMm, params string[] names)
        {
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double) continue;
                parameter.Set(valueMm / 304.8);
                return;
            }
        }

        private static FamilyInstance PlaceLintel(Document doc, OpeningRecordV3 opening, FamilySymbol type)
        {
            Level level = doc.GetElement(opening.LevelId) as Level;
            if (level == null)
            {
                level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(x => Math.Abs(x.Elevation - opening.TopElevation)).FirstOrDefault();
            }
            if (level == null) throw new InvalidOperationException("Не найден уровень для проёма " + opening.IdText + ".");

            double relativeTop = opening.TopElevation - level.ProjectElevation;
            XYZ point = new XYZ(opening.Location.X, opening.Location.Y, relativeTop);
            FamilyInstance lintel = doc.Create.NewFamilyInstance(point, type, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
            if (lintel == null) throw new InvalidOperationException("Не удалось создать перемычку для проёма " + opening.IdText + ".");

            XYZ orientation = opening.SupportType == 1 && !opening.BearingDirection.IsZeroLength()
                ? opening.BearingDirection.Normalize()
                : opening.WallOrientation.Normalize();

            if (!orientation.IsAlmostEqualTo(lintel.FacingOrientation))
            {
                LocationPoint location = lintel.Location as LocationPoint;
                if (location != null)
                {
                    Line axis = Line.CreateBound(location.Point, location.Point + XYZ.BasisZ);
                    double desired = orientation.AngleOnPlaneTo(XYZ.BasisX, XYZ.BasisZ);
                    double actual = lintel.FacingOrientation.AngleOnPlaneTo(XYZ.BasisX, XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, lintel.Id, axis, actual - desired);
                }
            }

            ElementTransformUtils.MoveElement(doc, lintel.Id, orientation * (opening.WallWidthMm / 304.8) / 2.0);
            SetString(lintel, "ПР", "ADSK_Группирование");
            SetString(lintel, "2000", "Видимость.Глубина");
            SetFloorNumber(doc, lintel, level);
            doc.Regenerate();
            SetBaseTypeOnSubcomponents(doc, lintel, opening.WallTypeName);
            return lintel;
        }

        private static void SetFloorNumber(Document doc, FamilyInstance lintel, Level level)
        {
            List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(x => x.Elevation)
                .ToList();
            int floor = level.Elevation >= 0 ? levels.FindIndex(x => x.Id == level.Id) + 1 : -1;
            Parameter parameter = lintel.LookupParameter("ZH_Этаж_Числовой");
            if (parameter == null || parameter.IsReadOnly) return;
            if (parameter.StorageType == StorageType.Integer) parameter.Set(floor);
            else parameter.SetValueString(floor.ToString(CultureInfo.InvariantCulture));
        }

        private static void SetBaseTypeOnSubcomponents(Document doc, FamilyInstance lintel, string wallTypeName)
        {
            string value = (wallTypeName ?? string.Empty).IndexOf("НСЩ", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Каркас"
                : "Перегородка";
            foreach (ElementId id in lintel.GetSubComponentIds())
            {
                Element subElement = doc.GetElement(id);
                Parameter parameter = subElement?.LookupParameter("ZH_Тип_Основы_Стена")
                                      ?? subElement?.LookupParameter("ZH_Тип*Основы*Стена");
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                    parameter.Set(value);
            }
        }

        private static void SetString(Element element, string value, params string[] names)
        {
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null || parameter.IsReadOnly) continue;
                if (parameter.StorageType == StorageType.String) parameter.Set(value);
                else parameter.SetValueString(value);
                return;
            }
        }
    }

    internal sealed class ElementIdComparerV3 : IEqualityComparer<Element>
    {
        public bool Equals(Element x, Element y)
        {
            return x?.Id == y?.Id;
        }

        public int GetHashCode(Element obj)
        {
            return obj?.Id?.Value.GetHashCode() ?? 0;
        }
    }

    internal static class XyzExtensionsV3
    {
        public static bool IsZeroLength(this XYZ value)
        {
            return value == null || value.GetLength() < 1e-9;
        }
    }
}
