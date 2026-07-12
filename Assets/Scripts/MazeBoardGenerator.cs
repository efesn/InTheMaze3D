using System;
using System.Collections.Generic;
using UnityEngine;

public class MazeBoardGenerator : MonoBehaviour
{
    public enum CellKind
    {
        Empty,
        MainPath,
        FalsePath,
        Intersection
    }

    [Serializable]
    public class CellRecord
    {
        public int column;
        public int row;
        public float worldX;
        public float worldZ;
        public CellKind kind;
        public string colorName;
        public bool isIntersection;
    }

    private struct CellCoord : IEquatable<CellCoord>
    {
        public int Column;
        public int Row;

        public CellCoord(int column, int row)
        {
            Column = column;
            Row = row;
        }

        public bool Equals(CellCoord other)
        {
            return Column == other.Column && Row == other.Row;
        }

        public override bool Equals(object obj)
        {
            return obj is CellCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Column * 397) ^ Row;
        }
    }

    [Header("Board Size")]
    [SerializeField] private int columns = 14;
    [SerializeField] private int rows = 12;
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private float cellHeight = 0.08f;
    [SerializeField] private float visualGap = 0.04f;

    [Header("Path Generation")]
    [SerializeField] private int randomSeed = 0;
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int mainWaypointCount = 5;
    [SerializeField] private int falsePathCount = 5;
    [SerializeField] private int minimumFalsePathSegments = 3;
    [SerializeField] private int maximumFalsePathSegments = 7;
    [SerializeField] private int minimumFalsePathSegmentLength = 2;
    [SerializeField] private int maximumFalsePathSegmentLength = 5;

    [Header("Rendering")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool regenerateInEditor = true;
    [SerializeField] private string generatedRootName = "Generated Maze Board";

    public string Status { get; private set; } = "not generated";
    public IReadOnlyList<CellRecord> CellTable => cellTable;

    private readonly List<CellRecord> cellTable = new List<CellRecord>();
    private readonly Dictionary<CellCoord, CellRecord> recordsByCoord = new Dictionary<CellCoord, CellRecord>();
    private readonly List<CellCoord> mainPathCells = new List<CellCoord>();
    private readonly List<CellCoord> falsePathCells = new List<CellCoord>();
    private readonly Color[] falsePathColors =
    {
        Color.yellow,
        new Color(0f, 0.75f, 1f),
        Color.magenta,
        Color.green,
        new Color(0.58f, 0f, 0.83f),
        new Color(1f, 0.41f, 0.71f)
    };

    private readonly string[] falsePathColorNames =
    {
        "Yellow",
        "Azure",
        "Magenta",
        "Lime",
        "Violet",
        "Pink"
    };

    private System.Random random;
    private Transform generatedRoot;

    private void Start()
    {
        if (generateOnStart)
        {
            GenerateBoard();
        }
    }

    [ContextMenu("Generate Board")]
    public void GenerateBoard()
    {
        ClampParameters();
        ClearGeneratedBoard();
        PrepareRandom();
        CreateEmptyCellTable();
        RouteMainPath();
        RouteFalsePaths();
        RenderBoard();
        Status = "board ready";
        Debug.Log(Status);
    }

    public List<CellRecord> GetCellTable()
    {
        return new List<CellRecord>(cellTable);
    }

    private void ClampParameters()
    {
        columns = Mathf.Max(2, columns);
        rows = Mathf.Max(2, rows);
        cellSize = Mathf.Max(0.1f, cellSize);
        cellHeight = Mathf.Max(0.01f, cellHeight);
        visualGap = Mathf.Clamp(visualGap, 0f, cellSize * 0.4f);
        mainWaypointCount = Mathf.Max(0, mainWaypointCount);
        falsePathCount = Mathf.Max(0, falsePathCount);
        minimumFalsePathSegments = Mathf.Max(1, minimumFalsePathSegments);
        maximumFalsePathSegments = Mathf.Max(minimumFalsePathSegments, maximumFalsePathSegments);
        minimumFalsePathSegmentLength = Mathf.Max(1, minimumFalsePathSegmentLength);
        maximumFalsePathSegmentLength = Mathf.Max(minimumFalsePathSegmentLength, maximumFalsePathSegmentLength);
    }

    private void PrepareRandom()
    {
        int seed = useRandomSeed ? Environment.TickCount : randomSeed;
        random = new System.Random(seed);
    }

    private void CreateEmptyCellTable()
    {
        cellTable.Clear();
        recordsByCoord.Clear();

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                CellCoord coord = new CellCoord(column, row);
                Vector3 world = GridToWorld(coord);
                CellRecord record = new CellRecord
                {
                    column = column,
                    row = row,
                    worldX = world.x,
                    worldZ = world.z,
                    kind = CellKind.Empty,
                    colorName = "Green",
                    isIntersection = false
                };

                cellTable.Add(record);
                recordsByCoord.Add(coord, record);
            }
        }
    }

    private void RouteMainPath()
    {
        mainPathCells.Clear();
        CellCoord current = new CellCoord(0, rows / 2);
        CellCoord exit = new CellCoord(columns - 1, rows / 2);
        List<CellCoord> waypoints = SelectInteriorWaypoints(mainWaypointCount);

        MarkMainPathCell(current);

        while (waypoints.Count > 0)
        {
            CellCoord target = FindNearestCell(current, waypoints);
            AddRoute(current, target, true, 0);
            current = target;
            waypoints.Remove(target);
        }

        AddRoute(current, exit, true, 0);
    }

    private List<CellCoord> SelectInteriorWaypoints(int count)
    {
        List<CellCoord> waypoints = new List<CellCoord>();
        int safetyLimit = columns * rows * 4;

        while (waypoints.Count < count && safetyLimit > 0)
        {
            int column = random.Next(1, columns - 1);
            int row = random.Next(1, rows - 1);
            CellCoord candidate = new CellCoord(column, row);

            if (!waypoints.Contains(candidate))
            {
                waypoints.Add(candidate);
            }

            safetyLimit--;
        }

        return waypoints;
    }

    private CellCoord FindNearestCell(CellCoord from, List<CellCoord> candidates)
    {
        CellCoord nearest = candidates[0];
        int nearestDistance = ManhattanDistance(from, nearest);

        for (int i = 1; i < candidates.Count; i++)
        {
            int distance = ManhattanDistance(from, candidates[i]);
            if (distance < nearestDistance)
            {
                nearest = candidates[i];
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private int ManhattanDistance(CellCoord a, CellCoord b)
    {
        return Mathf.Abs(a.Column - b.Column) + Mathf.Abs(a.Row - b.Row);
    }

    private void RouteFalsePaths()
    {
        falsePathCells.Clear();

        for (int pathIndex = 0; pathIndex < falsePathCount; pathIndex++)
        {
            CellCoord current = new CellCoord(random.Next(0, columns), random.Next(0, rows));
            MarkFalsePathCell(current, pathIndex);

            int segmentCount = random.Next(minimumFalsePathSegments, maximumFalsePathSegments + 1);

            for (int segment = 0; segment < segmentCount; segment++)
            {
                Vector2Int direction = GetRandomDirection();
                int length = random.Next(minimumFalsePathSegmentLength, maximumFalsePathSegmentLength + 1);

                for (int step = 0; step < length; step++)
                {
                    CellCoord next = new CellCoord(current.Column + direction.x, current.Row + direction.y);

                    if (!IsInsideBoard(next))
                    {
                        break;
                    }

                    current = next;
                    MarkFalsePathCell(current, pathIndex);
                }
            }
        }
    }

    private Vector2Int GetRandomDirection()
    {
        int value = random.Next(0, 4);

        if (value == 0)
        {
            return new Vector2Int(1, 0);
        }

        if (value == 1)
        {
            return new Vector2Int(-1, 0);
        }

        if (value == 2)
        {
            return new Vector2Int(0, 1);
        }

        return new Vector2Int(0, -1);
    }

    private void AddRoute(CellCoord start, CellCoord target, bool isMainPath, int falsePathIndex)
    {
        List<CellCoord> routePoints = BuildOneOrThreeSegmentRoute(start, target);

        for (int i = 0; i < routePoints.Count - 1; i++)
        {
            AddStraightSegment(routePoints[i], routePoints[i + 1], isMainPath, falsePathIndex);
        }
    }

    private List<CellCoord> BuildOneOrThreeSegmentRoute(CellCoord start, CellCoord target)
    {
        List<CellCoord> points = new List<CellCoord> { start };

        if (start.Column == target.Column || start.Row == target.Row)
        {
            points.Add(target);
            return points;
        }

        bool horizontalFirst = random.Next(0, 2) == 0;

        if (horizontalFirst)
        {
            int middleColumn = random.Next(Mathf.Min(start.Column, target.Column), Mathf.Max(start.Column, target.Column) + 1);
            points.Add(new CellCoord(middleColumn, start.Row));
            points.Add(new CellCoord(middleColumn, target.Row));
        }
        else
        {
            int middleRow = random.Next(Mathf.Min(start.Row, target.Row), Mathf.Max(start.Row, target.Row) + 1);
            points.Add(new CellCoord(start.Column, middleRow));
            points.Add(new CellCoord(target.Column, middleRow));
        }

        points.Add(target);
        return points;
    }

    private void AddStraightSegment(CellCoord start, CellCoord target, bool isMainPath, int falsePathIndex)
    {
        int columnStep = Math.Sign(target.Column - start.Column);
        int rowStep = Math.Sign(target.Row - start.Row);
        CellCoord current = start;

        while (!current.Equals(target))
        {
            current = new CellCoord(current.Column + columnStep, current.Row + rowStep);

            if (!IsInsideBoard(current))
            {
                break;
            }

            if (isMainPath)
            {
                MarkMainPathCell(current);
            }
            else
            {
                MarkFalsePathCell(current, falsePathIndex);
            }
        }
    }

    private void MarkMainPathCell(CellCoord coord)
    {
        if (!IsInsideBoard(coord))
        {
            return;
        }

        CellRecord record = recordsByCoord[coord];

        if (record.kind == CellKind.FalsePath)
        {
            record.kind = CellKind.Intersection;
            record.isIntersection = true;
            record.colorName = "Intersection";
        }
        else if (record.kind != CellKind.Intersection)
        {
            record.kind = CellKind.MainPath;
            record.colorName = "Red";
        }

        if (!mainPathCells.Contains(coord))
        {
            mainPathCells.Add(coord);
        }
    }

    private void MarkFalsePathCell(CellCoord coord, int pathIndex)
    {
        if (!IsInsideBoard(coord))
        {
            return;
        }

        CellRecord record = recordsByCoord[coord];

        if (record.kind == CellKind.MainPath || record.kind == CellKind.FalsePath || record.kind == CellKind.Intersection)
        {
            record.kind = CellKind.Intersection;
            record.isIntersection = true;
            record.colorName = "Intersection";
        }
        else
        {
            record.kind = CellKind.FalsePath;
            record.colorName = falsePathColorNames[pathIndex % falsePathColorNames.Length];
        }

        if (!falsePathCells.Contains(coord))
        {
            falsePathCells.Add(coord);
        }
    }

    private bool IsInsideBoard(CellCoord coord)
    {
        return coord.Column >= 0 && coord.Column < columns && coord.Row >= 0 && coord.Row < rows;
    }

    private void RenderBoard()
    {
        GameObject rootObject = new GameObject(generatedRootName);
        rootObject.transform.SetParent(transform, false);
        generatedRoot = rootObject.transform;

        foreach (CellRecord record in cellTable)
        {
            GameObject cell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cell.name = $"Cell [{record.column}, {record.row}] {record.kind}";
            cell.transform.SetParent(generatedRoot, false);
            cell.transform.position = new Vector3(record.worldX, 0f, record.worldZ);
            cell.transform.localScale = new Vector3(cellSize - visualGap, cellHeight, cellSize - visualGap);

            Renderer renderer = cell.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateMaterial(GetColorForRecord(record));
        }
    }

    private Color GetColorForRecord(CellRecord record)
    {
        if (record.kind == CellKind.MainPath)
        {
            return Color.red;
        }

        if (record.kind == CellKind.Intersection)
        {
            return Color.white;
        }

        if (record.kind == CellKind.FalsePath)
        {
            for (int i = 0; i < falsePathColorNames.Length; i++)
            {
                if (record.colorName == falsePathColorNames[i])
                {
                    return falsePathColors[i];
                }
            }
        }

        return Color.green;
    }

    private Material CreateMaterial(Color color)
    {
        Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = color;
        return material;
    }

    private Vector3 GridToWorld(CellCoord coord)
    {
        float originX = -((columns - 1) * cellSize) * 0.5f;
        float originZ = -((rows - 1) * cellSize) * 0.5f;
        float x = originX + coord.Column * cellSize;
        float z = originZ + coord.Row * cellSize;
        return new Vector3(x, 0f, z);
    }

    private void ClearGeneratedBoard()
    {
        Transform existing = transform.Find(generatedRootName);

        if (existing == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(existing.gameObject);
        }
        else
        {
            DestroyImmediate(existing.gameObject);
        }
    }

    private void OnValidate()
    {
        ClampParameters();

        if (!Application.isPlaying && regenerateInEditor && generatedRoot != null)
        {
            GenerateBoard();
        }
    }
}
