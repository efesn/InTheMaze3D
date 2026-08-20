using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class MazeBoardGenerator : MonoBehaviour
{
    public enum MazeCellType
    {
        Empty,
        MainPath,
        FalsePath,
        Intersection,
        Entrance,
        Exit
    }

    [Serializable]
    public sealed class MazeCellRecord
    {
        [SerializeField] private int column;
        [SerializeField] private int row;
        [SerializeField] private Vector3 worldPosition;
        [SerializeField] private MazeCellType cellType;
        [SerializeField] private string colorName;
        [SerializeField] private bool isIntersection;

        public int Column => column;
        public int Row => row;
        public Vector3 WorldPosition => worldPosition;
        public MazeCellType CellType => cellType;
        public string ColorName => colorName;
        public bool IsIntersection => isIntersection;

        internal MazeCellRecord(int column, int row, Vector3 worldPosition)
        {
            this.column = column;
            this.row = row;
            this.worldPosition = worldPosition;
            cellType = MazeCellType.Empty;
            colorName = "Green";
            isIntersection = false;
        }

        internal void SetWorldPosition(Vector3 value)
        {
            worldPosition = value;
        }

        internal void SetCellType(MazeCellType value)
        {
            cellType = value;
        }

        internal void SetColorName(string value)
        {
            colorName = value;
        }

        internal void SetIntersection(bool value)
        {
            isIntersection = value;
        }
    }

    private enum FalsePathColorId
    {
        Yellow,
        Azure,
        Magenta,
        Lime,
        Violet,
        Pink
    }

    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public readonly int Column;
        public readonly int Row;

        public GridPosition(int column, int row)
        {
            Column = column;
            Row = row;
        }

        public bool Equals(GridPosition other)
        {
            return Column == other.Column && Row == other.Row;
        }

        public override bool Equals(object obj)
        {
            return obj is GridPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Column * 397) ^ Row;
            }
        }

        public static bool operator ==(GridPosition left, GridPosition right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GridPosition left, GridPosition right)
        {
            return !left.Equals(right);
        }
    }

    private sealed class MutableCellState
    {
        public bool IsMainPath;
        public bool IsFalsePath;
        public bool IsEntrance;
        public bool IsExit;
        public bool IsIntersection;
        public int FalsePathIndex = -1;
        public string FalsePathColorName = string.Empty;
    }

    [Header("Board Size")]
    [SerializeField, Min(2)] private int columns = 14;
    [SerializeField, Min(2)] private int rows = 12;
    [SerializeField, Min(0.1f)] private float cellSize = 1f;
    [SerializeField] private float boardY = 0f;

    [Header("Path Generation")]
    [SerializeField, Min(0)] private int waypointCount = 5;
    [SerializeField, Min(0)] private int falsePathCount = 5;
    [SerializeField, Min(1)] private int falsePathMinLength = 3;
    [SerializeField, Min(1)] private int falsePathMaxLength = 9;
    [SerializeField] private bool randomizeSeed = true;
    [SerializeField] private int randomSeed = 12345;
    [SerializeField, Min(1)] private int maxFalsePathAttemptsPerPath = 80;
    [SerializeField, Range(0f, 1f)] private float falsePathReconnectChance = 0.25f;
    [SerializeField, Range(0f, 1f)] private float continueStraightChance = 0.45f;

    [Header("Materials")]
    [SerializeField] private Color emptyCellColor = Color.green;
    [SerializeField] private Color mainPathCellColor = Color.red;
    [SerializeField] private Color intersectionCellColor = Color.white;
    [SerializeField] private Color falsePathYellowColor = Color.yellow;
    [SerializeField] private Color falsePathAzureColor = new Color(0f, 0.65f, 1f, 1f);
    [SerializeField] private Color falsePathMagentaColor = Color.magenta;
    [SerializeField] private Color falsePathLimeColor = new Color(0.45f, 1f, 0f, 1f);
    [SerializeField] private Color falsePathVioletColor = new Color(0.55f, 0f, 1f, 1f);
    [SerializeField] private Color falsePathPinkColor = new Color(1f, 0.4f, 0.75f, 1f);

    [Header("Rendering")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool regenerateOnValidate = false;
    [SerializeField, Min(0.001f)] private float tileHeight = 0.05f;
    [SerializeField, Range(0.1f, 1f)] private float tileInset = 0.94f;
    [SerializeField] private string generatedParentName = "Generated Maze Board";
    [SerializeField] private bool addTileColliders = false;

    [SerializeField, HideInInspector] private List<MazeCellRecord> generatedCells = new List<MazeCellRecord>();
    [SerializeField, HideInInspector] private string status = "not generated";

    private MutableCellState[,] cellStates;
    private Transform generatedParent;
    private System.Random rng;
    private GridPosition entranceCell;
    private GridPosition exitCell;

    public IReadOnlyList<MazeCellRecord> GeneratedCells => generatedCells;
    public string Status => status;
    public int Columns => columns;
    public int Rows => rows;
    public float CellSize => cellSize;

    public GridPosition EntranceCell => entranceCell;
    public GridPosition ExitCell => exitCell;

    public Vector3 EntranceWorldPosition => GridToWorld(entranceCell.Column, entranceCell.Row);
    public Vector3 ExitWorldPosition => GridToWorld(exitCell.Column, exitCell.Row);

    public GridPosition GetEntranceCell() => entranceCell;
    public GridPosition GetExitCell() => exitCell;
    public Vector3 GetEntranceWorldPosition() => EntranceWorldPosition;
    public Vector3 GetExitWorldPosition() => ExitWorldPosition;

    private void Start()
    {
        if (Application.isPlaying && generateOnStart)
        {
            GenerateBoard();
        }
    }

    private void OnValidate()
    {
        columns = Mathf.Max(2, columns);
        rows = Mathf.Max(2, rows);
        cellSize = Mathf.Max(0.1f, cellSize);
        waypointCount = Mathf.Max(0, waypointCount);
        falsePathCount = Mathf.Max(0, falsePathCount);
        falsePathMinLength = Mathf.Max(1, falsePathMinLength);
        falsePathMaxLength = Mathf.Max(falsePathMinLength, falsePathMaxLength);
        maxFalsePathAttemptsPerPath = Mathf.Max(1, maxFalsePathAttemptsPerPath);
        tileHeight = Mathf.Max(0.001f, tileHeight);

        if (!Application.isPlaying && regenerateOnValidate)
        {
            GenerateBoard();
        }
    }

    [ContextMenu("Generate Board")]
    public void GenerateBoard()
    {
        InitializeRandom();
        SelectRandomEntranceAndExit();
        ClearGeneratedBoard();
        CreateEmptyCellTable();
        RouteMainPath();
        GenerateFalsePaths();
        FinalizeCellRecords();
        RenderBoard();

        status = "board ready";
        Debug.Log("board ready");
    }

    private void SelectRandomEntranceAndExit()
    {
        // Pick entrance on left border (Column 0, random row)
        int entranceRow = rng != null ? rng.Next(0, rows) : rows / 2;
        entranceCell = new GridPosition(0, entranceRow);

        // Pick exit on right, top, or bottom border ensuring a good distance from entrance
        List<GridPosition> possibleExits = new List<GridPosition>();

        // Right edge
        for (int r = 0; r < rows; r++)
        {
            possibleExits.Add(new GridPosition(columns - 1, r));
        }
        // Top edge
        for (int c = 1; c < columns - 1; c++)
        {
            possibleExits.Add(new GridPosition(c, 0));
        }
        // Bottom edge
        for (int c = 1; c < columns - 1; c++)
        {
            possibleExits.Add(new GridPosition(c, rows - 1));
        }

        // Filter exits to ensure a minimum distance from entrance for challenging gameplay
        List<GridPosition> validExits = possibleExits.FindAll(pos => ManhattanDistance(entranceCell, pos) >= (columns + rows) / 2);
        if (validExits.Count == 0)
        {
            validExits = possibleExits;
        }

        exitCell = rng != null ? validExits[rng.Next(validExits.Count)] : new GridPosition(columns - 1, rows / 2);
    }

    [ContextMenu("Clear Generated Board")]
    public void ClearGeneratedBoard()
    {
        Transform existing = transform.Find(generatedParentName);
        if (existing != null)
        {
            if (Application.isPlaying)
            {
                Destroy(existing.gameObject);
            }
            else
            {
                DestroyImmediate(existing.gameObject);
            }
        }
    }

    private void InitializeRandom()
    {
        int seed = randomizeSeed ? Guid.NewGuid().GetHashCode() ^ Environment.TickCount : randomSeed;
        rng = new System.Random(seed);
    }

    private void CreateEmptyCellTable()
    {
        generatedCells.Clear();
        cellStates = new MutableCellState[columns, rows];

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                cellStates[column, row] = new MutableCellState();
                generatedCells.Add(new MazeCellRecord(column, row, GridToWorld(column, row)));
            }
        }

        MutableCellState entrance = GetState(EntranceCell);
        entrance.IsMainPath = true;
        entrance.IsEntrance = true;

        MutableCellState exit = GetState(ExitCell);
        exit.IsMainPath = true;
        exit.IsExit = true;
    }

    private void RouteMainPath()
    {
        List<GridPosition> waypoints = SelectInteriorWaypoints(waypointCount);
        GridPosition current = EntranceCell;

        while (waypoints.Count > 0)
        {
            int nearestIndex = FindNearestWaypointIndex(current, waypoints);
            GridPosition target = waypoints[nearestIndex];
            RouteOrthogonalSegment(current, target, true, -1, string.Empty);
            current = target;
            waypoints.RemoveAt(nearestIndex);
        }

        RouteOrthogonalSegment(current, ExitCell, true, -1, string.Empty);
    }

    private List<GridPosition> SelectInteriorWaypoints(int count)
    {
        List<GridPosition> waypoints = new List<GridPosition>();
        HashSet<GridPosition> used = new HashSet<GridPosition>();

        int interiorColumnCount = Mathf.Max(0, columns - 2);
        int interiorRowCount = Mathf.Max(0, rows - 2);
        int maxUniqueInteriorCells = interiorColumnCount * interiorRowCount;
        int targetCount = Mathf.Min(count, maxUniqueInteriorCells);

        int guard = Mathf.Max(100, targetCount * 50);
        while (waypoints.Count < targetCount && guard > 0)
        {
            guard--;

            int column = rng.Next(1, columns - 1);
            int row = rng.Next(1, rows - 1);
            GridPosition candidate = new GridPosition(column, row);

            if (used.Contains(candidate))
            {
                continue;
            }

            used.Add(candidate);
            waypoints.Add(candidate);
        }

        return waypoints;
    }

    private int FindNearestWaypointIndex(GridPosition current, List<GridPosition> waypoints)
    {
        int bestDistance = int.MaxValue;
        List<int> tiedIndices = new List<int>();

        for (int i = 0; i < waypoints.Count; i++)
        {
            int distance = ManhattanDistance(current, waypoints[i]);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                tiedIndices.Clear();
                tiedIndices.Add(i);
            }
            else if (distance == bestDistance)
            {
                tiedIndices.Add(i);
            }
        }

        return tiedIndices[rng.Next(tiedIndices.Count)];
    }

    private void RouteOrthogonalSegment(
        GridPosition start,
        GridPosition target,
        bool markAsMainPath,
        int falsePathIndex,
        string falsePathColorName)
    {
        GridPosition current = start;
        MarkCell(current, markAsMainPath, !markAsMainPath, falsePathIndex, falsePathColorName);

        while (current != target)
        {
            bool canMoveHorizontal = current.Column != target.Column;
            bool canMoveVertical = current.Row != target.Row;

            bool moveHorizontal;
            if (canMoveHorizontal && canMoveVertical)
            {
                moveHorizontal = rng.Next(0, 2) == 0;
            }
            else
            {
                moveHorizontal = canMoveHorizontal;
            }

            if (moveHorizontal)
            {
                current = new GridPosition(
                    current.Column + Math.Sign(target.Column - current.Column),
                    current.Row);
            }
            else
            {
                current = new GridPosition(
                    current.Column,
                    current.Row + Math.Sign(target.Row - current.Row));
            }

            MarkCell(current, markAsMainPath, !markAsMainPath, falsePathIndex, falsePathColorName);
        }
    }

    private void GenerateFalsePaths()
    {
        for (int falsePathIndex = 0; falsePathIndex < falsePathCount; falsePathIndex++)
        {
            FalsePathColorId colorId = (FalsePathColorId)(falsePathIndex % 6);
            string colorName = GetFalsePathColorName(colorId);

            bool created = false;
            for (int attempt = 0; attempt < maxFalsePathAttemptsPerPath && !created; attempt++)
            {
                created = TryGenerateFalsePath(falsePathIndex, colorName);
            }
        }
    }

    private bool TryGenerateFalsePath(int falsePathIndex, string colorName)
    {
        List<GridPosition> starts = GetFalsePathStartCandidates();

        if (starts.Count == 0)
        {
            return false;
        }

        GridPosition start = starts[rng.Next(starts.Count)];
        GridPosition current = start;
        GridPosition previous = start;
        Vector2Int previousDirection = Vector2Int.zero;
        int targetLength = rng.Next(falsePathMinLength, falsePathMaxLength + 1);
        List<GridPosition> newlyMarkedCells = new List<GridPosition>();

        MarkCell(start, false, true, falsePathIndex, colorName);

        for (int step = 0; step < targetLength; step++)
        {
            List<GridPosition> candidates = GetFalsePathStepCandidates(current, previous, previousDirection);

            if (candidates.Count == 0)
            {
                break;
            }

            GridPosition next = ChooseFalsePathStep(current, candidates, previousDirection);
            MutableCellState nextState = GetState(next);
            bool wasEmpty = IsEmpty(next);

            MarkCell(next, false, true, falsePathIndex, colorName);

            if (wasEmpty)
            {
                newlyMarkedCells.Add(next);
            }

            previousDirection = new Vector2Int(next.Column - current.Column, next.Row - current.Row);
            previous = current;
            current = next;

            if ((nextState.IsMainPath || nextState.IsFalsePath || nextState.IsIntersection) && !wasEmpty)
            {
                break;
            }
        }

        bool hasNewCells = newlyMarkedCells.Count > 0;
        bool connectedToWalkableCell = CountWalkableNeighbors(start) > 0 || CountWalkableNeighbors(current) > 0;
        return hasNewCells && connectedToWalkableCell;
    }

    private List<GridPosition> GetFalsePathStartCandidates()
    {
        List<GridPosition> candidates = new List<GridPosition>();

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                GridPosition position = new GridPosition(column, row);
                MutableCellState state = GetState(position);

                if (!IsWalkable(position))
                {
                    continue;
                }

                if (state.IsEntrance || state.IsExit)
                {
                    continue;
                }

                if (HasUnusedNeighbor(position))
                {
                    candidates.Add(position);
                }
            }
        }

        return candidates;
    }

    private List<GridPosition> GetFalsePathStepCandidates(
        GridPosition current,
        GridPosition previous,
        Vector2Int previousDirection)
    {
        List<GridPosition> candidates = new List<GridPosition>();
        GridPosition[] neighbors = GetOrthogonalNeighbors(current);

        for (int i = 0; i < neighbors.Length; i++)
        {
            GridPosition candidate = neighbors[i];

            if (!IsInsideBoard(candidate))
            {
                continue;
            }

            if (candidate == previous)
            {
                continue;
            }

            MutableCellState state = GetState(candidate);

            if (state.IsEntrance || state.IsExit)
            {
                continue;
            }

            if (IsEmpty(candidate))
            {
                candidates.Add(candidate);
                continue;
            }

            if (rng.NextDouble() <= falsePathReconnectChance)
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private GridPosition ChooseFalsePathStep(
        GridPosition current,
        List<GridPosition> candidates,
        Vector2Int previousDirection)
    {
        if (previousDirection != Vector2Int.zero && rng.NextDouble() <= continueStraightChance)
        {
            GridPosition straight = new GridPosition(
                current.Column + previousDirection.x,
                current.Row + previousDirection.y);

            if (candidates.Contains(straight))
            {
                return straight;
            }
        }

        List<GridPosition> emptyCandidates = new List<GridPosition>();
        for (int i = 0; i < candidates.Count; i++)
        {
            if (IsEmpty(candidates[i]))
            {
                emptyCandidates.Add(candidates[i]);
            }
        }

        if (emptyCandidates.Count > 0)
        {
            return emptyCandidates[rng.Next(emptyCandidates.Count)];
        }

        return candidates[rng.Next(candidates.Count)];
    }

    private void MarkCell(
        GridPosition position,
        bool asMainPath,
        bool asFalsePath,
        int falsePathIndex,
        string falsePathColorName)
    {
        if (!IsInsideBoard(position))
        {
            return;
        }

        MutableCellState state = GetState(position);

        if (asMainPath)
        {
            state.IsMainPath = true;
        }

        if (asFalsePath)
        {
            state.IsFalsePath = true;

            if (state.FalsePathIndex < 0)
            {
                state.FalsePathIndex = falsePathIndex;
                state.FalsePathColorName = falsePathColorName;
            }
            else if (state.FalsePathIndex != falsePathIndex)
            {
                state.IsIntersection = true;
            }
        }

        if (state.IsMainPath && state.IsFalsePath)
        {
            state.IsIntersection = true;
        }
    }

    private void FinalizeCellRecords()
    {
        for (int i = 0; i < generatedCells.Count; i++)
        {
            MazeCellRecord record = generatedCells[i];
            GridPosition position = new GridPosition(record.Column, record.Row);
            MutableCellState state = GetState(position);

            record.SetWorldPosition(GridToWorld(record.Column, record.Row));

            if (state.IsIntersection)
            {
                record.SetCellType(MazeCellType.Intersection);
                record.SetColorName("White");
                record.SetIntersection(true);
            }
            else if (state.IsEntrance)
            {
                record.SetCellType(MazeCellType.Entrance);
                record.SetColorName("Red");
                record.SetIntersection(false);
            }
            else if (state.IsExit)
            {
                record.SetCellType(MazeCellType.Exit);
                record.SetColorName("Red");
                record.SetIntersection(false);
            }
            else if (state.IsMainPath)
            {
                record.SetCellType(MazeCellType.MainPath);
                record.SetColorName("Red");
                record.SetIntersection(false);
            }
            else if (state.IsFalsePath)
            {
                record.SetCellType(MazeCellType.FalsePath);
                record.SetColorName(string.IsNullOrEmpty(state.FalsePathColorName) ? "Yellow" : state.FalsePathColorName);
                record.SetIntersection(false);
            }
            else
            {
                record.SetCellType(MazeCellType.Empty);
                record.SetColorName("Green");
                record.SetIntersection(false);
            }
        }
    }

    private void RenderBoard()
    {
        GameObject parentObject = new GameObject(generatedParentName);
        parentObject.transform.SetParent(transform, false);
        generatedParent = parentObject.transform;

        Material emptyMaterial = CreateMaterial("Empty Cells", emptyCellColor);
        Material mainPathMaterial = CreateMaterial("Main Path Cells", mainPathCellColor);
        Material intersectionMaterial = CreateMaterial("Intersection Cells", intersectionCellColor);
        Material yellowMaterial = CreateMaterial("False Path Yellow Cells", falsePathYellowColor);
        Material azureMaterial = CreateMaterial("False Path Azure Cells", falsePathAzureColor);
        Material magentaMaterial = CreateMaterial("False Path Magenta Cells", falsePathMagentaColor);
        Material limeMaterial = CreateMaterial("False Path Lime Cells", falsePathLimeColor);
        Material violetMaterial = CreateMaterial("False Path Violet Cells", falsePathVioletColor);
        Material pinkMaterial = CreateMaterial("False Path Pink Cells", falsePathPinkColor);

        for (int i = 0; i < generatedCells.Count; i++)
        {
            MazeCellRecord cell = generatedCells[i];

            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = $"Cell [{cell.Column}, {cell.Row}] - {cell.CellType} - {cell.ColorName}";
            tile.transform.SetParent(generatedParent, false);
            tile.transform.position = new Vector3(cell.WorldPosition.x, boardY - tileHeight * 0.5f, cell.WorldPosition.z);
            tile.transform.localScale = new Vector3(cellSize * tileInset, tileHeight, cellSize * tileInset);

            MeshRenderer renderer = tile.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetMaterialForCell(
                cell,
                emptyMaterial,
                mainPathMaterial,
                intersectionMaterial,
                yellowMaterial,
                azureMaterial,
                magentaMaterial,
                limeMaterial,
                violetMaterial,
                pinkMaterial);

            if (!addTileColliders)
            {
                Collider collider = tile.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(collider);
                    }
                    else
                    {
                        DestroyImmediate(collider);
                    }
                }
            }
        }
    }

    private Material GetMaterialForCell(
        MazeCellRecord cell,
        Material emptyMaterial,
        Material mainPathMaterial,
        Material intersectionMaterial,
        Material yellowMaterial,
        Material azureMaterial,
        Material magentaMaterial,
        Material limeMaterial,
        Material violetMaterial,
        Material pinkMaterial)
    {
        if (cell.IsIntersection || cell.CellType == MazeCellType.Intersection)
        {
            return intersectionMaterial;
        }

        if (cell.CellType == MazeCellType.MainPath ||
            cell.CellType == MazeCellType.Entrance ||
            cell.CellType == MazeCellType.Exit)
        {
            return mainPathMaterial;
        }

        if (cell.CellType == MazeCellType.FalsePath)
        {
            switch (cell.ColorName)
            {
                case "Yellow":
                    return yellowMaterial;
                case "Azure":
                    return azureMaterial;
                case "Magenta":
                    return magentaMaterial;
                case "Lime":
                    return limeMaterial;
                case "Violet":
                    return violetMaterial;
                case "Pink":
                    return pinkMaterial;
                default:
                    return yellowMaterial;
            }
        }

        return emptyMaterial;
    }

    private Material CreateMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.color = color;
        return material;
    }

    private Vector3 GridToWorld(int column, int row)
    {
        float worldX = (column - (columns - 1) * 0.5f) * cellSize;
        float worldZ = ((rows - 1) * 0.5f - row) * cellSize;
        return new Vector3(worldX, boardY, worldZ);
    }

    private int ManhattanDistance(GridPosition a, GridPosition b)
    {
        return Mathf.Abs(a.Column - b.Column) + Mathf.Abs(a.Row - b.Row);
    }

    private bool IsInsideBoard(GridPosition position)
    {
        return position.Column >= 0 &&
               position.Column < columns &&
               position.Row >= 0 &&
               position.Row < rows;
    }

    private bool IsEmpty(GridPosition position)
    {
        MutableCellState state = GetState(position);
        return !state.IsMainPath && !state.IsFalsePath && !state.IsEntrance && !state.IsExit && !state.IsIntersection;
    }

    private bool IsWalkable(GridPosition position)
    {
        if (!IsInsideBoard(position))
        {
            return false;
        }

        MutableCellState state = GetState(position);
        return state.IsMainPath || state.IsFalsePath || state.IsEntrance || state.IsExit || state.IsIntersection;
    }

    private bool HasUnusedNeighbor(GridPosition position)
    {
        GridPosition[] neighbors = GetOrthogonalNeighbors(position);

        for (int i = 0; i < neighbors.Length; i++)
        {
            if (IsInsideBoard(neighbors[i]) && IsEmpty(neighbors[i]))
            {
                return true;
            }
        }

        return false;
    }

    private int CountWalkableNeighbors(GridPosition position)
    {
        int count = 0;
        GridPosition[] neighbors = GetOrthogonalNeighbors(position);

        for (int i = 0; i < neighbors.Length; i++)
        {
            if (IsWalkable(neighbors[i]))
            {
                count++;
            }
        }

        return count;
    }

    private GridPosition[] GetOrthogonalNeighbors(GridPosition position)
    {
        return new[]
        {
            new GridPosition(position.Column, position.Row - 1),
            new GridPosition(position.Column + 1, position.Row),
            new GridPosition(position.Column, position.Row + 1),
            new GridPosition(position.Column - 1, position.Row)
        };
    }

    private MutableCellState GetState(GridPosition position)
    {
        return cellStates[position.Column, position.Row];
    }

    private string GetFalsePathColorName(FalsePathColorId colorId)
    {
        switch (colorId)
        {
            case FalsePathColorId.Yellow:
                return "Yellow";
            case FalsePathColorId.Azure:
                return "Azure";
            case FalsePathColorId.Magenta:
                return "Magenta";
            case FalsePathColorId.Lime:
                return "Lime";
            case FalsePathColorId.Violet:
                return "Violet";
            case FalsePathColorId.Pink:
                return "Pink";
            default:
                return "Yellow";
        }
    }
}
