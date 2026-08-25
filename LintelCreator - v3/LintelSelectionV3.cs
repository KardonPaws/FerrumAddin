using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FerrumAddinDev.LintelCreator_v3
{
    public enum LintelMasonryTypeV3
    {
        Brick65 = 65,
        Brick88 = 88,
        Partition = 0
    }

    public enum LintelMaterialV3
    {
        ReinforcedConcrete,
        Metal
    }

    public sealed class LintelCatalogFileV3
    {
        public int ItemCount { get; set; }
        public List<LintelCatalogItemV3> Items { get; set; } = new List<LintelCatalogItemV3>();
    }

    public sealed class LintelCatalogItemV3
    {
        public string Mark { get; set; }
        public string Family { get; set; }
        public string TypeCode { get; set; }
        public int LengthMm { get; set; }
        public int WidthMm { get; set; }
        public int HeightMm { get; set; }
        public bool IsBearing { get; set; }
        public int MinimumOpeningWidthMm { get; set; }
        public int MaximumOpeningWidthMm { get; set; }
        public int MinimumBearingMm { get; set; }
        public double LoadCapacityKgfPerM { get; set; }
        public string LoadCategory { get; set; }
        public int Priority { get; set; }
        public bool AutoSelectionAllowed { get; set; }
        public string ProductCode { get; set; }
        public string Material { get; set; }
        public string StandardSeries { get; set; }
        public int Issue { get; set; }
        public int MasonryCourseHeightMm { get; set; }
        public double MassKg { get; set; }
        public string RevitFamilyName { get; set; }

        public string DisplayName => (string.IsNullOrWhiteSpace(RevitFamilyName)
                                         ? string.IsNullOrWhiteSpace(Family) ? "Перемычка" : Family
                                         : RevitFamilyName)
                                     + " : " + Mark;
    }

    public sealed class LintelSelectionRequestV3
    {
        public double OpeningWidthMm { get; set; }
        public double WallWidthMm { get; set; }
        public int SupportType { get; set; }
        public double RequiredBearingWidth1Mm { get; set; }
        public double RequiredBearingWidth2Mm { get; set; }
        public string ValidationError { get; set; }
        public int MasonryCourseHeightMm { get; set; }
        public LintelMaterialV3 Material { get; set; }
        public int MinimumBearingMm { get; set; }
        public int WallWidthToleranceMm { get; set; }
        public int MaximumVariants { get; set; } = 5;
    }

    public sealed class LintelLayoutSegmentV3
    {
        public string Mark { get; set; }
        public int WidthMm { get; set; }
        public double DisplayWidth { get; set; }
        public bool IsBearing { get; set; }
        public bool IsGap { get; set; }

        public string LabelText => IsGap
            ? "Δ " + WidthMm
            : (IsBearing ? "Н " : string.Empty) + WidthMm;
        public string ToolTipText => IsGap
            ? "Зазор " + WidthMm + " мм"
            : (IsBearing ? "Несущая перемычка" : "Рядовая перемычка")
              + " · " + Mark + " · ширина " + WidthMm + " мм";
    }

    public sealed class LintelSelectionVariantV3
    {
        public int Rank { get; set; }
        public string CompositionKey { get; set; }
        public string CompositionText { get; set; }
        public int TotalWidthMm { get; set; }
        public int SignedWidthDeltaMm { get; set; }
        public int WidthDeltaMm { get; set; }
        public int BearingWidthMm { get; set; }
        public int RequiredBearingWidthMm { get; set; }
        public int ElementCount { get; set; }
        public int DistinctMarkCount { get; set; }
        public int MinimumLengthMm { get; set; }
        public int MaximumLengthMm { get; set; }
        public int LengthExcessScore { get; set; }
        public int PriorityScore { get; set; }
        public int WallWidthToleranceMm { get; set; }
        public List<LintelLayoutSegmentV3> LayoutSegments { get; set; } = new List<LintelLayoutSegmentV3>();

        public bool IsExact => WidthDeltaMm == 0;
        public string RankText => "Вариант " + Rank;
        public string FitText => IsExact ? "Точно" : "В допуске";
        public string WidthSummaryText => "Ширина комплекта: " + TotalWidthMm + " мм · отклонение "
            + (SignedWidthDeltaMm > 0 ? "+" : string.Empty) + SignedWidthDeltaMm + " мм";
        public string LengthSummaryText => MinimumLengthMm == MaximumLengthMm
            ? "Длина элементов: " + MinimumLengthMm + " мм · " + ElementCount + " шт."
            : "Длины элементов: " + MinimumLengthMm + "–" + MaximumLengthMm + " мм · " + ElementCount + " шт.";
        public string BearingSummaryText => RequiredBearingWidthMm > 0
            ? "Несущая часть: " + BearingWidthMm + " мм · требуется ≥ " + RequiredBearingWidthMm + " мм"
            : "Несущая часть не требуется";
        public string LayoutDifferenceText => SignedWidthDeltaMm < 0
            ? "Зазор " + Math.Abs(SignedWidthDeltaMm) + " мм · допуск ±" + WallWidthToleranceMm + " мм"
            : SignedWidthDeltaMm > 0
                ? "Превышение " + SignedWidthDeltaMm + " мм · допуск ±" + WallWidthToleranceMm + " мм"
                : "Без зазора · допуск ±" + WallWidthToleranceMm + " мм";
    }

    public sealed class LintelSelectionResultV3
    {
        public List<LintelSelectionVariantV3> Variants { get; } = new List<LintelSelectionVariantV3>();
        public int EligibleItemCount { get; set; }
        public string Message { get; set; }
    }

    internal static class LintelCatalogLoaderV3
    {
        private const string ResourceName = "FerrumAddinDev.LintelCreator_v3.LintelUnitCatalog_v3.json";

        public static LintelCatalogFileV3 Load()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                string assemblyDirectory = Path.GetDirectoryName(assembly.Location) ?? string.Empty;
                string filePath = Path.Combine(assemblyDirectory, "LintelUnitCatalog_v3.json");
                if (File.Exists(filePath))
                    stream = File.OpenRead(filePath);
            }

            if (stream == null)
                throw new FileNotFoundException("Не найден каталог единичных перемычек LintelUnitCatalog_v3.json.");

            using (stream)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                LintelCatalogFileV3 catalog = JsonSerializer.Deserialize<LintelCatalogFileV3>(stream, options);
                if (catalog?.Items == null || catalog.Items.Count == 0)
                    throw new InvalidDataException("Каталог единичных перемычек пуст или имеет неверную структуру.");
                return catalog;
            }
        }
    }

    internal static class LintelSelectionEngineV3
    {
        public static LintelSelectionResultV3 Calculate(
            IEnumerable<LintelCatalogItemV3> catalog,
            LintelSelectionRequestV3 request)
        {
            var result = new LintelSelectionResultV3();
            if (catalog == null || request == null || request.OpeningWidthMm <= 0 || request.WallWidthMm <= 0)
            {
                result.Message = "Недостаточно данных для подбора комплекта.";
                return result;
            }
            if (!string.IsNullOrWhiteSpace(request.ValidationError))
            {
                result.Message = request.ValidationError;
                return result;
            }

            string materialCode = request.Material == LintelMaterialV3.Metal
                ? "metal"
                : "reinforcedConcrete";

            List<LintelCatalogItemV3> eligible = catalog
                .Where(item => item != null
                               && item.AutoSelectionAllowed
                               && item.WidthMm > 0
                               && string.Equals(item.Material, materialCode, StringComparison.OrdinalIgnoreCase)
                               && item.MasonryCourseHeightMm == request.MasonryCourseHeightMm
                               && request.OpeningWidthMm + 0.5 >= item.MinimumOpeningWidthMm
                               && (item.MaximumOpeningWidthMm <= 0 || request.OpeningWidthMm <= item.MaximumOpeningWidthMm + 0.5)
                               && item.LengthMm + 0.5 >= GetRequiredLength(request, item))
                .ToList();

            result.EligibleItemCount = eligible.Count;
            if (eligible.Count == 0)
            {
                result.Message = BuildNoCandidatesMessage(request);
                return result;
            }

            int requiredBearingWidth = GetRequiredBearingWidth(request);
            if (requiredBearingWidth > 0 && eligible.All(item => !item.IsBearing))
            {
                result.Message = "Подходящие по длине элементы найдены, но среди них нет несущих перемычек для требуемой зоны.";
                return result;
            }

            List<LintelCatalogItemV3> representatives = eligible
                .GroupBy(item => item.WidthMm + "|" + item.IsBearing)
                .Select(group => group
                    .OrderBy(item => GetLengthExcess(request, item))
                    .ThenByDescending(item => item.Priority)
                    .ThenBy(item => item.IsBearing ? item.LoadCapacityKgfPerM : 0)
                    .ThenBy(item => item.Mark, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderBy(item => item.WidthMm)
                .ThenByDescending(item => item.IsBearing)
                .ToList();

            var search = new LintelCombinationSearchV3(request, representatives, requiredBearingWidth);
            List<LintelSelectionVariantV3> calculatedVariants = search.Run();
            int minimumBearingWidth = calculatedVariants.Count == 0
                ? 0
                : calculatedVariants.Min(variant => variant.BearingWidthMm);
            List<LintelSelectionVariantV3> variants = calculatedVariants
                .Where(variant => variant.BearingWidthMm == minimumBearingWidth)
                .OrderBy(variant => variant.WidthDeltaMm)
                .ThenBy(variant => variant.ElementCount)
                .ThenBy(variant => variant.DistinctMarkCount)
                .ThenBy(variant => variant.LengthExcessScore)
                .ThenByDescending(variant => variant.PriorityScore)
                .ThenBy(variant => variant.CompositionKey, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, request.MaximumVariants))
                .ToList();

            for (int index = 0; index < variants.Count; index++)
            {
                variants[index].Rank = index + 1;
                result.Variants.Add(variants[index]);
            }

            result.Message = result.Variants.Count > 0
                ? "Найдено вариантов: " + result.Variants.Count + ". Подходящих типов в каталоге: " + result.EligibleItemCount + "."
                : "Не удалось набрать толщину стены " + Math.Round(request.WallWidthMm)
                  + " мм с допуском ±" + request.WallWidthToleranceMm + " мм."
                  + (requiredBearingWidth > 0 ? " Требуемая несущая часть: " + requiredBearingWidth + " мм." : string.Empty);
            return result;
        }

        private static string BuildNoCandidatesMessage(LintelSelectionRequestV3 request)
        {
            if (request.Material == LintelMaterialV3.Metal)
                return "В каталоге нет металлических перемычек, соответствующих выбранным параметрам.";
            if (request.MasonryCourseHeightMm == 0)
                return "В каталоге нет перемычек для перегородок.";

            int requiredLength = (int)Math.Ceiling(request.OpeningWidthMm + 2.0 * request.MinimumBearingMm);
            return "Нет перемычек для кладки " + request.MasonryCourseHeightMm
                   + " мм и требуемой длины ≥ " + requiredLength + " мм.";
        }

        private static int GetRequiredBearingWidth(LintelSelectionRequestV3 request)
        {
            if (request.SupportType <= 0) return 0;
            double totalBearingWidth = Math.Max(0, request.RequiredBearingWidth1Mm)
                                       + Math.Max(0, request.RequiredBearingWidth2Mm);
            return (int)Math.Ceiling(Math.Min(request.WallWidthMm, totalBearingWidth));
        }

        internal static double GetRequiredLength(LintelSelectionRequestV3 request, LintelCatalogItemV3 item)
        {
            int bearing = Math.Max(request.MinimumBearingMm, item.MinimumBearingMm);
            return request.OpeningWidthMm + 2.0 * bearing;
        }

        internal static int GetLengthExcess(LintelSelectionRequestV3 request, LintelCatalogItemV3 item)
        {
            return Math.Max(0, (int)Math.Round(item.LengthMm - GetRequiredLength(request, item)));
        }
    }

    internal sealed class LintelCombinationSearchV3
    {
        private readonly LintelSelectionRequestV3 _request;
        private readonly List<LintelCatalogItemV3> _candidates;
        private readonly int _requiredBearingWidth;
        private readonly int _minimumTotalWidth;
        private readonly int _maximumTotalWidth;
        private readonly int _maximumElements;
        private readonly List<LintelSelectionVariantV3> _variants = new List<LintelSelectionVariantV3>();
        private readonly List<LintelCatalogItemV3> _current = new List<LintelCatalogItemV3>();

        public LintelCombinationSearchV3(
            LintelSelectionRequestV3 request,
            List<LintelCatalogItemV3> candidates,
            int requiredBearingWidth)
        {
            _request = request;
            _candidates = candidates ?? new List<LintelCatalogItemV3>();
            _requiredBearingWidth = requiredBearingWidth;
            _minimumTotalWidth = Math.Max(1, (int)Math.Ceiling(request.WallWidthMm - request.WallWidthToleranceMm));
            _maximumTotalWidth = Math.Max(_minimumTotalWidth, (int)Math.Floor(request.WallWidthMm + request.WallWidthToleranceMm));
            int minimumCandidateWidth = _candidates.Count == 0 ? 1 : _candidates.Min(item => item.WidthMm);
            _maximumElements = Math.Min(12, Math.Max(1, (int)Math.Ceiling((double)_maximumTotalWidth / minimumCandidateWidth)));
        }

        public List<LintelSelectionVariantV3> Run()
        {
            if (_candidates.Count == 0) return _variants;
            Explore(0, 0, 0);
            return _variants
                .GroupBy(variant => variant.CompositionKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private void Explore(int startIndex, int totalWidth, int bearingWidth)
        {
            if (_current.Count > 0
                && totalWidth >= _minimumTotalWidth
                && totalWidth <= _maximumTotalWidth
                && bearingWidth >= _requiredBearingWidth)
            {
                LintelSelectionVariantV3 variant = TryCreateVariant(totalWidth, bearingWidth);
                if (variant != null)
                    _variants.Add(variant);
            }

            if (_current.Count >= _maximumElements || totalWidth >= _maximumTotalWidth)
                return;

            for (int index = startIndex; index < _candidates.Count; index++)
            {
                LintelCatalogItemV3 item = _candidates[index];
                int nextWidth = totalWidth + item.WidthMm;
                if (nextWidth > _maximumTotalWidth) continue;

                _current.Add(item);
                Explore(index, nextWidth, bearingWidth + (item.IsBearing ? item.WidthMm : 0));
                _current.RemoveAt(_current.Count - 1);
            }
        }

        private LintelSelectionVariantV3 TryCreateVariant(int totalWidth, int bearingWidth)
        {
            List<LintelCatalogItemV3> bearing = _current
                .Where(item => item.IsBearing)
                .OrderByDescending(item => item.WidthMm)
                .ThenBy(item => item.Mark)
                .ToList();
            List<LintelCatalogItemV3> ordinary = _current
                .Where(item => !item.IsBearing)
                .OrderByDescending(item => item.WidthMm)
                .ThenBy(item => item.Mark)
                .ToList();
            List<LintelCatalogItemV3> ordered;
            int gapInsertionIndex;

            if (_request.SupportType == 2)
            {
                if (!TryArrangeTwoBearingSides(bearing, ordinary, out ordered, out gapInsertionIndex))
                    return null;
            }
            else if (_request.SupportType == 1)
            {
                bool bearingOnFirstSide = _request.RequiredBearingWidth1Mm > 0;
                ordered = bearingOnFirstSide
                    ? bearing.Concat(ordinary).ToList()
                    : ordinary.Concat(bearing).ToList();
                gapInsertionIndex = bearingOnFirstSide ? ordered.Count : 0;
            }
            else
            {
                ordered = ordinary.Concat(bearing).ToList();
                gapInsertionIndex = ordered.Count;
            }

            List<IGrouping<string, LintelCatalogItemV3>> markGroups = ordered
                .GroupBy(item => item.Mark, StringComparer.OrdinalIgnoreCase)
                .ToList();
            string composition = string.Join(" + ", markGroups.Select(group =>
                group.Count() > 1 ? group.Key + " × " + group.Count() : group.Key));
            string key = string.Join("|", markGroups
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Key + "x" + group.Count()));

            int roundedWallWidth = (int)Math.Round(_request.WallWidthMm);
            return new LintelSelectionVariantV3
            {
                CompositionKey = key,
                CompositionText = composition,
                TotalWidthMm = totalWidth,
                SignedWidthDeltaMm = totalWidth - roundedWallWidth,
                WidthDeltaMm = Math.Abs(totalWidth - roundedWallWidth),
                BearingWidthMm = bearingWidth,
                RequiredBearingWidthMm = _requiredBearingWidth,
                ElementCount = ordered.Count,
                DistinctMarkCount = markGroups.Count,
                MinimumLengthMm = ordered.Min(item => item.LengthMm),
                MaximumLengthMm = ordered.Max(item => item.LengthMm),
                LengthExcessScore = ordered.Sum(item => LintelSelectionEngineV3.GetLengthExcess(_request, item)),
                PriorityScore = ordered.Sum(item => item.Priority),
                WallWidthToleranceMm = _request.WallWidthToleranceMm,
                LayoutSegments = CreateLayoutSegments(ordered, totalWidth, roundedWallWidth, gapInsertionIndex)
            };
        }

        private bool TryArrangeTwoBearingSides(
            List<LintelCatalogItemV3> bearing,
            List<LintelCatalogItemV3> ordinary,
            out List<LintelCatalogItemV3> ordered,
            out int gapInsertionIndex)
        {
            ordered = null;
            gapInsertionIndex = 0;
            int requiredFirst = Math.Max(0, (int)Math.Ceiling(_request.RequiredBearingWidth1Mm));
            int requiredSecond = Math.Max(0, (int)Math.Ceiling(_request.RequiredBearingWidth2Mm));
            if (bearing.Count == 1
                && ordinary.Count == 0
                && bearing[0].WidthMm + 0.5 >= _request.WallWidthMm)
            {
                ordered = new List<LintelCatalogItemV3>(bearing);
                gapInsertionIndex = 1;
                return true;
            }
            if (bearing.Count == 0 || bearing.Count > 20) return false;

            int bestMask = -1;
            int bestMaximumExcess = int.MaxValue;
            int bestDifference = int.MaxValue;
            int totalBearing = bearing.Sum(item => item.WidthMm);
            int maskCount = 1 << bearing.Count;
            for (int mask = 0; mask < maskCount; mask++)
            {
                int firstWidth = 0;
                for (int index = 0; index < bearing.Count; index++)
                {
                    if ((mask & (1 << index)) != 0)
                        firstWidth += bearing[index].WidthMm;
                }

                int secondWidth = totalBearing - firstWidth;
                if (firstWidth < requiredFirst || secondWidth < requiredSecond) continue;
                int firstExcess = firstWidth - requiredFirst;
                int secondExcess = secondWidth - requiredSecond;
                int maximumExcess = Math.Max(firstExcess, secondExcess);
                int difference = Math.Abs(firstExcess - secondExcess);
                if (maximumExcess > bestMaximumExcess
                    || maximumExcess == bestMaximumExcess && difference >= bestDifference)
                    continue;

                bestMask = mask;
                bestMaximumExcess = maximumExcess;
                bestDifference = difference;
            }

            if (bestMask < 0) return false;
            var firstSide = new List<LintelCatalogItemV3>();
            var secondSide = new List<LintelCatalogItemV3>();
            for (int index = 0; index < bearing.Count; index++)
            {
                if ((bestMask & (1 << index)) != 0)
                    firstSide.Add(bearing[index]);
                else
                    secondSide.Add(bearing[index]);
            }

            ordered = firstSide.Concat(ordinary).Concat(secondSide).ToList();
            gapInsertionIndex = firstSide.Count + ordinary.Count;
            return true;
        }

        private static List<LintelLayoutSegmentV3> CreateLayoutSegments(
            IEnumerable<LintelCatalogItemV3> ordered,
            int totalWidth,
            int wallWidth,
            int gapInsertionIndex)
        {
            const double diagramWidth = 348.0;
            int referenceWidth = Math.Max(1, Math.Max(totalWidth, wallWidth));
            double scale = diagramWidth / referenceWidth;
            List<LintelCatalogItemV3> items = ordered.ToList();
            var result = new List<LintelLayoutSegmentV3>();
            int gap = Math.Max(0, wallWidth - totalWidth);
            for (int index = 0; index <= items.Count; index++)
            {
                if (gap > 0 && index == Math.Max(0, Math.Min(gapInsertionIndex, items.Count)))
                {
                    result.Add(new LintelLayoutSegmentV3
                    {
                        Mark = "Зазор",
                        WidthMm = gap,
                        DisplayWidth = Math.Max(1, gap * scale),
                        IsGap = true
                    });
                }
                if (index == items.Count) continue;
                LintelCatalogItemV3 item = items[index];
                result.Add(new LintelLayoutSegmentV3
                {
                    Mark = item.Mark,
                    WidthMm = item.WidthMm,
                    DisplayWidth = Math.Max(1, item.WidthMm * scale),
                    IsBearing = item.IsBearing
                });
            }
            return result;
        }
    }
}
