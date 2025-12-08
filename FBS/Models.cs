using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FerrumAddinDev.FBS
{
    // Информация о стене, необходимая для генерации раскладки
    public class WallInfo
    {
        public ElementId Id { get; set; }
        public Line line { get; set; }
        public XYZ StartPoint { get; set; }
        public XYZ EndPoint { get; set; }
        public XYZ Direction { get; set; }
        public double Length { get; set; }       // мм
        public double Thickness { get; set; }    // мм
        public double Height { get; set; }       // мм
        public double BaseElevation { get; set; } // координата Z
        public List<OpeningInfo> Openings { get; set; } = new List<OpeningInfo>();

        // Сторонние соседи (с сохранением информации для угловых соединений)
        public WallInfo LeftNeighbor { get; set; }
        public WallInfo RightNeighbor { get; set; }
        public bool LeftPriority { get; set; }
        public bool RightPriority { get; set; }
        public bool LeftNeighborAngleIsPerpendicular { get; set; }
        public bool RightNeighborAngleIsPerpendicular { get; set; }

        // Новый список всех соединённых стен
        public List<WallInfo> ConnectedWalls { get; set; } = new List<WallInfo>();
        public int RowOffset { get; set; }
        public List<double> coordZList { get; set; } = new List<double>();
        public bool first300 = false;
        public bool last300 = false;
        //04.08.25 - базовый уровень в перемычках
        public ElementId baseLevel { get; set; }
    }

    // Описание проёма (дверь/окно) вдоль стены, определённое стартовым и конечным расстоянием (в мм)
    public class OpeningInfo
    {
        public double Start { get; set; }
        public double End { get; set; }
        public double StartZ {  get; set; }
        public double EndZ { get; set; }
    }

    // Представляет полное решение раскладки (вариант) для всех выбранных стен
    public class LayoutVariant
    {
        public List<BlockPlacement> Blocks { get; set; } = new List<BlockPlacement>();
        public Dictionary<int, Dictionary<int, List<double>>> JointsByWall { get; set; } = new Dictionary<int, Dictionary<int, List<double>>>();
        public int TotalBlocks { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public bool IsPlaced { get; set; } = false;
    }

    // Представляет размещение одного блока в стене
    public class BlockPlacement
    {
        public WallInfo Wall { get; set; }
        public int Row { get; set; }
        public double ZCoord { get; set; }
        public double Length { get; set; }  // мм
        public double Start { get; set; }   // начало блока вдоль стены (мм)
        public double End { get; set; }     // конец блока вдоль стены (мм)
        public ElementId PlacedElementId { get; set; } = ElementId.InvalidElementId;
        public bool IsGapFill { get; set; } = false;
    }
}
