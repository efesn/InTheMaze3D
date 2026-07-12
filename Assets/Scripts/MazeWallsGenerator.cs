using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MazeWallsGenerator : MonoBehaviour
{
    private enum WallDirection
    {
        North,
        East,
        South,
        West
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

    private struct WallData
    {
        public CellCoord Cell;
        public WallDirection Direction;
        public Vector3 Position;
        public Vector3 Scale;
    }

    [Header("Board Source")]
    [SerializeField] private MazeBoardGenerator mazeBoardGenerator;
    [SerializeField] private bool findBoardGeneratorAutomatically = true;
    [SerializeField] private float boardReadyWaitTime = 3f;

    [Header("Wall Generation")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool removeRandomWalls = true;
    [SerializeField, Range(0f, 1f)] private float wallRemovalPercent = 0.2f;
    [SerializeField] private int randomSeed = 0;
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private string generatedRootName = "Generated Maze Walls";

    [Header("Wall Geometry")]
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private float wallWidth = 0.2f;
    [SerializeField] private float wallHeight = 1.2f;
    [SerializeField] private float wallVerticalOffset = 0f;

    [Header("Wall Physics")]
    [SerializeField] private float bounciness = 0.9f;
    [SerializeField] private float dynamicFriction = 0.2f;
    [SerializeField] private float staticFriction = 0.2f;

    public string Status { get; private set; } = "walls not generated";

    private readonly Color[] wallColors =
    {
        Color.yellow,
        new Color(0f, 0.75f, 1f),
        Color.magenta,
        Color.green,
        new Color(0.58f, 0f, 0.83f),
        new Color(1f, 0.41f, 0.71f),
        Color.green
    };

    private System.Random random;
    private Transform generatedRoot;
    private PhysicsMaterial wallPhysicsMaterial;

    private void Start()
    {
        if (generateOnStart)
        {
            StartCoroutine(GenerateWallsWhenBoardIsReady());
        }
    }

    [ContextMenu("Generate Walls")]
    public void GenerateWalls()
    {
        if (!ResolveBoardGenerator())
        {
            Debug.LogWarning("MazeWallsGenerator: MazeBoardGenerator was not found.");
            Status = "board not ready";
            return;
        }

        if (mazeBoardGenerator.Status != "board ready")
        {
            Debug.LogWarning("MazeWallsGenerator: board is not ready.");
            Status = "board not ready";
            return;
        }

        PrepareRandom();
        ClearGeneratedWalls();
        CreateWallPhysicsMaterial();

        Dictionary<CellCoord, MazeBoardGenerator.CellRecord> pathCells = BuildPathCellLookup();
        List<WallData> walls = BuildWallData(pathCells);

        if (removeRandomWalls)
        {
            RemoveRandomWallData(walls);
        }

        RenderWalls(walls);
        Status = "walls ready";
        Debug.Log(Status);
    }

    private IEnumerator GenerateWallsWhenBoardIsReady()
    {
        float elapsedTime = 0f;

        while (elapsedTime < boardReadyWaitTime)
        {
            if (ResolveBoardGenerator() && mazeBoardGenerator.Status == "board ready")
            {
                GenerateWalls();
                yield break;
            }

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        Debug.LogWarning("MazeWallsGenerator: board was not ready before timeout.");
        Status = "board not ready";
    }

    private bool ResolveBoardGenerator()
    {
        if (mazeBoardGenerator != null)
        {
            return true;
        }

        if (!findBoardGeneratorAutomatically)
        {
            return false;
        }

        mazeBoardGenerator = FindObjectOfType<MazeBoardGenerator>();
        return mazeBoardGenerator != null;
    }

    private void PrepareRandom()
    {
        int seed = useRandomSeed ? Environment.TickCount : randomSeed;
        random = new System.Random(seed);
    }

    private Dictionary<CellCoord, MazeBoardGenerator.CellRecord> BuildPathCellLookup()
    {
        Dictionary<CellCoord, MazeBoardGenerator.CellRecord> pathCells =
            new Dictionary<CellCoord, MazeBoardGenerator.CellRecord>();

        foreach (MazeBoardGenerator.CellRecord record in mazeBoardGenerator.CellTable)
        {
            if (record.kind == MazeBoardGenerator.CellKind.Empty)
            {
                continue;
            }

            CellCoord coord = new CellCoord(record.column, record.row);
            pathCells[coord] = record;
        }

        return pathCells;
    }

    private List<WallData> BuildWallData(Dictionary<CellCoord, MazeBoardGenerator.CellRecord> pathCells)
    {
        List<WallData> walls = new List<WallData>();
        HashSet<string> usedEdges = new HashSet<string>();

        foreach (KeyValuePair<CellCoord, MazeBoardGenerator.CellRecord> item in pathCells)
        {
            CellCoord cell = item.Key;
            MazeBoardGenerator.CellRecord record = item.Value;

            if (record.isIntersection)
            {
                continue;
            }

            AddWallIfOuterEdge(cell, WallDirection.North, pathCells, usedEdges, walls);
            AddWallIfOuterEdge(cell, WallDirection.East, pathCells, usedEdges, walls);
            AddWallIfOuterEdge(cell, WallDirection.South, pathCells, usedEdges, walls);
            AddWallIfOuterEdge(cell, WallDirection.West, pathCells, usedEdges, walls);
        }

        return walls;
    }

    private void AddWallIfOuterEdge(
        CellCoord cell,
        WallDirection direction,
        Dictionary<CellCoord, MazeBoardGenerator.CellRecord> pathCells,
        HashSet<string> usedEdges,
        List<WallData> walls)
    {
        CellCoord neighbor = GetNeighbor(cell, direction);

        if (pathCells.ContainsKey(neighbor))
        {
            return;
        }

        string edgeKey = BuildEdgeKey(cell, direction);

        if (usedEdges.Contains(edgeKey))
        {
            return;
        }

        usedEdges.Add(edgeKey);
        walls.Add(CreateWallData(cell, direction));
    }

    private CellCoord GetNeighbor(CellCoord cell, WallDirection direction)
    {
        if (direction == WallDirection.North)
        {
            return new CellCoord(cell.Column, cell.Row + 1);
        }

        if (direction == WallDirection.East)
        {
            return new CellCoord(cell.Column + 1, cell.Row);
        }

        if (direction == WallDirection.South)
        {
            return new CellCoord(cell.Column, cell.Row - 1);
        }

        return new CellCoord(cell.Column - 1, cell.Row);
    }

    private string BuildEdgeKey(CellCoord cell, WallDirection direction)
    {
        int columnA = cell.Column;
        int rowA = cell.Row;
        int columnB = cell.Column;
        int rowB = cell.Row;

        if (direction == WallDirection.North)
        {
            rowB += 1;
        }
        else if (direction == WallDirection.East)
        {
            columnB += 1;
        }
        else if (direction == WallDirection.South)
        {
            rowA -= 1;
        }
        else
        {
            columnA -= 1;
        }

        int minColumn = Mathf.Min(columnA, columnB);
        int maxColumn = Mathf.Max(columnA, columnB);
        int minRow = Mathf.Min(rowA, rowB);
        int maxRow = Mathf.Max(rowA, rowB);

        return $"{minColumn}:{minRow}:{maxColumn}:{maxRow}";
    }

    private WallData CreateWallData(CellCoord cell, WallDirection direction)
    {
        MazeBoardGenerator.CellRecord record = FindRecord(cell);
        Vector3 position = new Vector3(record.worldX, wallVerticalOffset + wallHeight * 0.5f, record.worldZ);
        Vector3 scale = Vector3.one;

        if (direction == WallDirection.North)
        {
            position.z += cellSize * 0.5f;
            scale = new Vector3(cellSize + wallWidth, wallHeight, wallWidth);
        }
        else if (direction == WallDirection.East)
        {
            position.x += cellSize * 0.5f;
            scale = new Vector3(wallWidth, wallHeight, cellSize + wallWidth);
        }
        else if (direction == WallDirection.South)
        {
            position.z -= cellSize * 0.5f;
            scale = new Vector3(cellSize + wallWidth, wallHeight, wallWidth);
        }
        else
        {
            position.x -= cellSize * 0.5f;
            scale = new Vector3(wallWidth, wallHeight, cellSize + wallWidth);
        }

        return new WallData
        {
            Cell = cell,
            Direction = direction,
            Position = position,
            Scale = scale
        };
    }

    private MazeBoardGenerator.CellRecord FindRecord(CellCoord coord)
    {
        foreach (MazeBoardGenerator.CellRecord record in mazeBoardGenerator.CellTable)
        {
            if (record.column == coord.Column && record.row == coord.Row)
            {
                return record;
            }
        }

        return null;
    }

    private void RemoveRandomWallData(List<WallData> walls)
    {
        int removeCount = Mathf.RoundToInt(walls.Count * wallRemovalPercent);

        for (int i = 0; i < removeCount && walls.Count > 0; i++)
        {
            int index = random.Next(0, walls.Count);
            walls.RemoveAt(index);
        }
    }

    private void RenderWalls(List<WallData> walls)
    {
        GameObject rootObject = new GameObject(generatedRootName);
        rootObject.transform.SetParent(transform, false);
        generatedRoot = rootObject.transform;

        for (int i = 0; i < walls.Count; i++)
        {
            WallData wallData = walls[i];
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = $"Wall {i} [{wallData.Cell.Column}, {wallData.Cell.Row}] {wallData.Direction}";
            wall.transform.SetParent(generatedRoot, false);
            wall.transform.position = wallData.Position;
            wall.transform.localScale = wallData.Scale;

            Renderer renderer = wall.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateWallMaterial(GetRandomWallColor());

            Collider collider = wall.GetComponent<Collider>();
            collider.material = wallPhysicsMaterial;
        }
    }

    private Color GetRandomWallColor()
    {
        return wallColors[random.Next(0, wallColors.Length)];
    }

    private Material CreateWallMaterial(Color color)
    {
        Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = color;
        return material;
    }

    private void CreateWallPhysicsMaterial()
    {
        wallPhysicsMaterial = new PhysicsMaterial("Maze Wall Physics");
        wallPhysicsMaterial.bounciness = bounciness;
        wallPhysicsMaterial.dynamicFriction = dynamicFriction;
        wallPhysicsMaterial.staticFriction = staticFriction;
        wallPhysicsMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;
        wallPhysicsMaterial.frictionCombine = PhysicsMaterialCombine.Average;
    }

    private void ClearGeneratedWalls()
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
        wallRemovalPercent = Mathf.Clamp01(wallRemovalPercent);
        cellSize = Mathf.Max(0.1f, cellSize);
        wallWidth = Mathf.Max(0.01f, wallWidth);
        wallHeight = Mathf.Max(0.1f, wallHeight);
        bounciness = Mathf.Clamp01(bounciness);
        dynamicFriction = Mathf.Clamp01(dynamicFriction);
        staticFriction = Mathf.Clamp01(staticFriction);
        boardReadyWaitTime = Mathf.Max(0f, boardReadyWaitTime);
    }
}
