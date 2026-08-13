using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FerrumAddinDev.LintelCreator_v2;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FerrumAddinDev.LintelCreator_v3
{
    public partial class LintelCreatorForm_v3 : Window
    {
        private const string SearchPlaceholder = "Поиск по ID или типу";
        private readonly LintelActionHandlerV3 _actionHandler;
        private readonly ExternalEvent _actionEvent;
        private readonly LintelUtilityEventsV3 _utilityEvents;
        private bool _syncingSettings;

        public LintelWorkspaceV3 Workspace { get; }

        public LintelCreatorForm_v3(
            LintelWorkspaceV3 workspace,
            LintelActionHandlerV3 actionHandler,
            ExternalEvent actionEvent,
            LintelUtilityEventsV3 utilityEvents)
        {
            Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _actionHandler = actionHandler ?? throw new ArgumentNullException(nameof(actionHandler));
            _actionEvent = actionEvent ?? throw new ArgumentNullException(nameof(actionEvent));
            _utilityEvents = utilityEvents ?? throw new ArgumentNullException(nameof(utilityEvents));

            InitializeComponent();
            DataContext = Workspace;
            Workspace.PropertyChanged += Workspace_PropertyChanged;
            Closed += Form_Closed;
            SyncSettingsControls();
            ApplyOpeningFilter();
        }

        private void Workspace_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LintelWorkspaceV3.OpeningGroups)
                || e.PropertyName == nameof(LintelWorkspaceV3.ExistingOpeningGroups))
                ApplyOpeningFilter();
            if (e.PropertyName == nameof(LintelWorkspaceV3.SelectedWall))
                SyncSettingsControls();
        }

        private void Form_Closed(object sender, EventArgs e)
        {
            Workspace.PropertyChanged -= Workspace_PropertyChanged;
        }

        private void OpeningWall_Checked(object sender, RoutedEventArgs e)
        {
            SelectWall((sender as FrameworkElement)?.DataContext as OpeningWallGroupV3, true);
        }

        private void ExistingWall_Checked(object sender, RoutedEventArgs e)
        {
            SelectWall((sender as FrameworkElement)?.DataContext as OpeningWallGroupV3, true);
        }

        private void OpeningGroup_Click(object sender, RoutedEventArgs e)
        {
            OpeningTypeGroupV3 group = (sender as FrameworkElement)?.DataContext as OpeningTypeGroupV3;
            OpeningWallGroupV3 first = group?.Walls.FirstOrDefault();
            if (first != null) SelectWall(first, false);
            RequestRevitSelection(group?.Walls.SelectMany(x => x.Openings).Select(x => x.OpeningId));
            e.Handled = true;
        }

        private void OpeningId_Click(object sender, RoutedEventArgs e)
        {
            OpeningRecordV3 opening = (sender as FrameworkElement)?.DataContext as OpeningRecordV3;
            if (opening == null) return;
            OpeningWallGroupV3 wall = Workspace.OpeningGroups.SelectMany(x => x.Walls)
                .Concat(Workspace.ExistingOpeningGroups.SelectMany(x => x.Walls))
                .FirstOrDefault(x => x.Openings.Contains(opening));
            if (wall != null) SelectWall(wall, false);
            RequestRevitSelection(new[] { opening.OpeningId });
            e.Handled = true;
        }

        private void OpeningSelection_Click(object sender, RoutedEventArgs e)
        {
            Workspace?.NotifySelectionChanged();
        }

        private void SelectWall(OpeningWallGroupV3 wall, bool selectInRevit = false)
        {
            if (wall == null || Workspace == null) return;
            Workspace.SelectedWall = wall;
            SyncSettingsControls();
            if (selectInRevit)
                RequestRevitSelection(wall.Openings.Select(x => x.OpeningId));
        }

        private void RequestRevitSelection(IEnumerable<ElementId> ids)
        {
            List<ElementId> validIds = (ids ?? Enumerable.Empty<ElementId>())
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .Distinct()
                .ToList();
            if (!validIds.Any()) return;
            RaiseAction(new LintelActionRequestV3
            {
                Kind = LintelActionKindV3.SelectElements,
                SelectedElementIds = validIds
            });
        }

        private void SyncSettingsControls()
        {
            if (Workspace == null || Masonry65Radio == null) return;
            _syncingSettings = true;
            try
            {
                Masonry65Radio.IsChecked = Workspace.MasonryMode == "65";
                Masonry88Radio.IsChecked = Workspace.MasonryMode == "88";
                PartitionsRadio.IsChecked = Workspace.MasonryMode == "Перегородки";
                MetalRadio.IsChecked = Workspace.MaterialMode == "Металлическая";
                ConcreteRadio.IsChecked = Workspace.MaterialMode == "Железобетонная";
            }
            finally
            {
                _syncingSettings = false;
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Workspace.Reload();
            ApplyOpeningFilter();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyOpeningFilter();
        }

        private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (SearchBox.Text == SearchPlaceholder) SearchBox.Text = string.Empty;
            SearchBox.Foreground = Brushes.Black;
        }

        private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) return;
            SearchBox.Text = SearchPlaceholder;
            SearchBox.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(152, 162, 179));
        }

        private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyOpeningFilter();
        }

        private void SelectAllCalculated_Click(object sender, RoutedEventArgs e)
        {
            Workspace?.SelectAllCalculated(SelectAllCalculatedCheckBox.IsChecked == true);
        }

        private void GroupingCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Workspace == null || GroupingCombo?.SelectedItem == null) return;
            string mode = (GroupingCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(mode) && mode != Workspace.GroupingMode)
            {
                Workspace.ChangeGrouping(mode);
                ApplyOpeningFilter();
            }
        }

        private void ApplyOpeningFilter()
        {
            if (Workspace == null || SearchBox == null || StatusFilter == null) return;
            ICollectionView openingsView = CollectionViewSource.GetDefaultView(Workspace.OpeningGroups);
            if (openingsView != null)
            {
                openingsView.Filter = item => MatchesGroup(item as OpeningTypeGroupV3, false);
                openingsView.Refresh();
            }

            ICollectionView existingView = CollectionViewSource.GetDefaultView(Workspace.ExistingOpeningGroups);
            if (existingView != null)
            {
                existingView.Filter = item => MatchesGroup(item as OpeningTypeGroupV3, true);
                existingView.Refresh();
            }
        }

        private bool MatchesGroup(OpeningTypeGroupV3 group, bool existing)
        {
            if (group == null) return false;
            string query = (SearchBox.Text ?? string.Empty).Trim();
            if (query == SearchPlaceholder) query = string.Empty;
            bool matchesText = query.Length == 0
                || Contains(group.FamilyName, query)
                || Contains(group.TypeName, query)
                || group.Walls.Any(w => Contains(w.WallTypeName, query)
                                        || w.Openings.Any(o => Contains(o.IdText, query)));
            int status = StatusFilter.SelectedIndex;
            if (!matchesText) return false;
            if (existing)
            {
                if (status == 0) return true;
                if (status == 4) return group.Walls.Any(w => w.NeedsReplacement);
                return false;
            }

            if (status == 1) return group.Walls.Any(w => w.Variants.Any() && w.CanPlace);
            if (status == 2) return group.Walls.Any(w => w.Variants.Any() && !w.CanPlace);
            if (status == 3) return group.Walls.Any(w => !w.Variants.Any());
            if (status == 4) return false;
            return true;
        }

        private static bool Contains(string value, string query)
        {
            return (value ?? string.Empty).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void OpeningsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, sender) || Workspace == null) return;
            IEnumerable<OpeningWallGroupV3> source = OpeningsTabs.SelectedIndex == 1
                ? Workspace.ExistingOpeningGroups.SelectMany(x => x.Walls)
                : Workspace.OpeningGroups.SelectMany(x => x.Walls);
            OpeningWallGroupV3 first = source.FirstOrDefault();
            if (first != null) SelectWall(first);
        }

        private void Masonry_Checked(object sender, RoutedEventArgs e)
        {
            if (Workspace == null || _syncingSettings) return;
            Workspace.MasonryMode = Convert.ToString((sender as FrameworkElement)?.Tag);
            Workspace.RecalculateSelected();
        }

        private void Material_Checked(object sender, RoutedEventArgs e)
        {
            if (Workspace == null || _syncingSettings) return;
            Workspace.MaterialMode = Convert.ToString((sender as FrameworkElement)?.Tag);
            Workspace.RecalculateSelected();
        }

        private void Recalculate_Click(object sender, RoutedEventArgs e)
        {
            Workspace.RecalculateSelected();
            ApplyOpeningFilter();
        }

        private void Variants_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Workspace == null) return;
            Workspace.NotifyVariantEdited();
        }

        private void PieceType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Workspace?.NotifyVariantEdited();
        }

        private void PieceValue_LostFocus(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            Workspace.NotifyVariantEdited();
        }

        private void Tolerance_LostFocus(object sender, RoutedEventArgs e)
        {
            Workspace.RecalculateSelected();
        }

        private void Reverse_Click(object sender, RoutedEventArgs e)
        {
            LintelVariantV3 variant = Workspace.SelectedVariant;
            if (variant == null || variant.Pieces.Count < 2) return;
            List<LintelPieceV3> reversed = variant.Pieces.Reverse().ToList();
            variant.Pieces.Clear();
            foreach (LintelPieceV3 piece in reversed) variant.Pieces.Add(piece);
            Workspace.NotifyVariantEdited();
        }

        private void ResetVariant_Click(object sender, RoutedEventArgs e)
        {
            Workspace.RecalculateSelected();
        }

        private void AddPiece_Click(object sender, RoutedEventArgs e)
        {
            if (!Workspace.TryAddPieceToSelectedVariant(out string error))
                MessageBox.Show(this, error, "Перемычки v3", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void MovePieceUp_Click(object sender, RoutedEventArgs e)
        {
            MovePiece((sender as FrameworkElement)?.Tag as LintelPieceV3, -1);
        }

        private void MovePieceDown_Click(object sender, RoutedEventArgs e)
        {
            MovePiece((sender as FrameworkElement)?.Tag as LintelPieceV3, 1);
        }

        private void MovePiece(LintelPieceV3 piece, int offset)
        {
            LintelVariantV3 variant = Workspace.SelectedVariant;
            if (piece == null || variant == null) return;
            int index = variant.Pieces.IndexOf(piece);
            int target = index + offset;
            if (index < 0 || target < 0 || target >= variant.Pieces.Count) return;
            variant.Pieces.Move(index, target);
            Workspace.NotifyVariantEdited();
        }

        private void DeletePiece_Click(object sender, RoutedEventArgs e)
        {
            LintelPieceV3 piece = (sender as FrameworkElement)?.Tag as LintelPieceV3;
            if (piece == null || Workspace.SelectedVariant == null) return;
            Workspace.SelectedVariant.Pieces.Remove(piece);
            Workspace.NotifyVariantEdited();
        }

        private void SaveVariant_Click(object sender, RoutedEventArgs e)
        {
            Workspace.NotifyVariantEdited();
            Workspace.LastMessage = Workspace.SelectedVariant == null
                ? "Вариант не выбран."
                : Workspace.SelectedVariant.IsValid
                    ? "Изменения варианта сохранены в текущем сеансе."
                    : "Вариант сохранён, но его ширина выходит за установленный допуск.";
        }

        private void Place_Click(object sender, RoutedEventArgs e)
        {
            List<LintelPlacementRequestV3> placements = Workspace.CreatePlacementRequests(false);
            if (!placements.Any())
            {
                MessageBox.Show(this, "Нет выбранных групп с допустимым вариантом раскладки.", "Перемычки v3", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RaiseAction(new LintelActionRequestV3
            {
                Kind = LintelActionKindV3.Place,
                Placements = placements
            });
        }

        private void ChangeExistingType_Click(object sender, RoutedEventArgs e)
        {
            OpeningWallGroupV3 wall = GetSelectedExistingWall();
            if (wall == null || wall.SelectedExistingType == null)
            {
                MessageBox.Show(this, "Выберите группу проёмов и новый существующий тип перемычки.", "Перемычки v3", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RaiseAction(new LintelActionRequestV3
            {
                Kind = LintelActionKindV3.ChangeType,
                ExistingLintelIds = GetExistingLintelIds(wall, ApplyAllExistingCheckBox.IsChecked == true),
                TargetExistingType = wall.SelectedExistingType
            });
        }

        private void CalculateReplacement_Click(object sender, RoutedEventArgs e)
        {
            OpeningWallGroupV3 wall = GetSelectedExistingWall();
            if (wall == null) return;
            Workspace.RecalculateSelected();
            Workspace.LastMessage = "Для выбранных существующих перемычек рассчитан новый вариант замены.";
        }

        private void ReplaceExisting_Click(object sender, RoutedEventArgs e)
        {
            OpeningWallGroupV3 wall = GetSelectedExistingWall();
            if (wall?.SelectedVariant == null || !wall.SelectedVariant.IsValid)
            {
                MessageBox.Show(this, "Для выбранной группы нет допустимого рассчитанного варианта.", "Перемычки v3", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RaiseAction(new LintelActionRequestV3
            {
                Kind = LintelActionKindV3.Replace,
                Placements = new List<LintelPlacementRequestV3>
                {
                    new LintelPlacementRequestV3
                    {
                        WallGroup = GetExistingActionWall(wall),
                        Variant = wall.SelectedVariant.Clone(),
                        ReplaceExisting = true
                    }
                }
            });
        }

        private void DeleteExisting_Click(object sender, RoutedEventArgs e)
        {
            OpeningWallGroupV3 wall = GetSelectedExistingWall();
            List<ElementId> ids = GetExistingLintelIds(wall, ApplyAllExistingCheckBox.IsChecked == true);
            if (!ids.Any()) return;
            MessageBoxResult answer = MessageBox.Show(this, $"Удалить перемычки ({ids.Count} шт.) у выбранной группы проёмов?", "Перемычки v3", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            RaiseAction(new LintelActionRequestV3 { Kind = LintelActionKindV3.Delete, ExistingLintelIds = ids });
        }

        private OpeningWallGroupV3 GetSelectedExistingWall()
        {
            OpeningWallGroupV3 wall = Workspace.SelectedWall;
            return wall != null && wall.HasExistingLintels ? wall : null;
        }

        private static List<ElementId> GetExistingLintelIds(OpeningWallGroupV3 wall, bool applyAll)
        {
            IEnumerable<OpeningRecordV3> openings = wall?.Openings ?? Enumerable.Empty<OpeningRecordV3>();
            if (!applyAll) openings = openings.Take(1);
            return openings.SelectMany(x => x.ExistingLintelIds).Distinct().ToList();
        }

        private OpeningWallGroupV3 GetExistingActionWall(OpeningWallGroupV3 source)
        {
            if (ApplyAllExistingCheckBox.IsChecked == true) return source;
            var result = new OpeningWallGroupV3
            {
                Parent = source.Parent,
                WallTypeId = source.WallTypeId,
                WallTypeName = source.WallTypeName,
                WallWidthMm = source.WallWidthMm,
                CurrentLintelTypeName = source.CurrentLintelTypeName,
                SelectedVariant = source.SelectedVariant,
                MasonryMode = source.MasonryMode,
                MaterialMode = source.MaterialMode,
                ToleranceMm = source.ToleranceMm
            };
            OpeningRecordV3 first = source.Openings.FirstOrDefault();
            if (first != null) result.Openings.Add(first);
            return result;
        }

        private void RaiseAction(LintelActionRequestV3 request)
        {
            _actionHandler.PendingRequest = request;
            ExternalEventRequest result = _actionEvent.Raise();
            if (result != ExternalEventRequest.Accepted)
                Workspace.LastMessage = "Revit занят: запрос не принят. Повторите действие после завершения текущей операции.";
        }

        private void Numerate_Click(object sender, RoutedEventArgs e)
        {
            SetSplitOption();
            _utilityEvents.Numerate.Raise();
        }

        private void NumerateNested_Click(object sender, RoutedEventArgs e)
        {
            SetSplitOption();
            _utilityEvents.NumerateNested.Raise();
        }

        private void SetBaseType_Click(object sender, RoutedEventArgs e)
        {
            _utilityEvents.SetBaseType.Raise();
        }

        private void CreateSections_Click(object sender, RoutedEventArgs e)
        {
            _utilityEvents.CreateSections.Raise();
        }

        private void TagLintels_Click(object sender, RoutedEventArgs e)
        {
            _utilityEvents.TagLintels.Raise();
        }

        private void PlaceSections_Click(object sender, RoutedEventArgs e)
        {
            _utilityEvents.PlaceSections.Raise();
        }

        private void SetSplitOption()
        {
            LintelCreatorForm_v2.check = SplitCheckBox.IsChecked == true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
