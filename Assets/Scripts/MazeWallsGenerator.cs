using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class MazeWallsGenerator : MonoBehaviour
{
    private enum WallDirection
    {
        North,
        East,
        South,
        West
    }

    private enum WallColorId
    {
        Yellow,
        Azure,
        Magenta,
        Lime,
        Violet,
        Pink,
        Green
    }

    private readonly struct GridPosition : IEquatable<GridPosition>
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

    private sealed class WallRecord
    {
        public string EdgeId;
        public GridPosition SourceCell;
        public WallDirection Direction;
        public Vector3 WorldPosition;
        public Quaternion WorldRotation;
        public Vector3 LocalScale;
        public bool IsBoundaryWall;
        public bool ProtectsUnusedCell;
        public WallColorId ColorId;
    }

    [Header("Board Source")]
    [SerializeField] private MazeBoardGenerator boardSource;
    [SerializeField] private bool autoFindBoardSource = true;
    [SerializeField, Min(0)] private int boardReadyRetryCount = 10;
    [SerializeField, Min(0.01f)] private float boardReadyRetryDelaySeconds = 0.15f;
    [SerializeField] private bool generateOnStart = true;

    [Header("Wall Generation")]
    [SerializeField] private bool removeNonCriticalWalls = false;
    [SerializeField, Range(0f, 0.2f)] private float maxNonCriticalWallRemovalRatio = 0.2f;
    [SerializeField] private bool randomizeWallSeed = true;
    [SerializeField] private int wallRandomSeed = 24680;

    [Header("Wall Geometry")]
    [SerializeField, Min(0.01f)] private float wallWidth = 0.2f;
    [SerializeField, Min(0.01f)] private float wallHeight = 1.2f;
    [SerializeField] private float boardSurfaceY = 0f;
    [SerializeField] private string generatedParentName = "Generated Maze Walls";

    [Header("Wall Physics")]
    [SerializeField] private bool collidersEnabled = true;
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.9f;
    [SerializeField, Range(0f, 1f)] private float dynamicFriction = 0.2f;
    [SerializeField, Range(0f, 1f)] private float staticFriction = 0.2f;
    [SerializeField] private PhysicsMaterialCombine frictionCombine = PhysicsMaterialCombine.Average;
    [SerializeField] private PhysicsMaterialCombine bounceCombine = PhysicsMaterialCombine.Maximum;

    [Header("Wall Materials")]
    [SerializeField] private Color yellowWallColor = Color.yellow;
    [SerializeField] private Color azureWallColor = new Color(0f, 0.65f, 1f, 1f);
    [SerializeField] private Color magentaWallColor = Color.magenta;
    [SerializeField] private Color limeWallColor = new Color(0.45f, 1f, 0f, 1f);
    [SerializeField] private Color violetWallColor = new Color(0.55f, 0f, 1f, 1f);
    [SerializeField] private Color pinkWallColor = new Color(1f, 0.4f, 0.75f, 1f);
    [SerializeField] private Color greenWallColor = Color.green;

    [SerializeField, HideInInspector] private string status = "not generated";

    private readonly List<WallRecord> generatedWallRecords = new List<WallRecord>();
    private readonly Dictionary<string, WallRecord> wallByEdgeId = new Dictionary<string, WallRecord>();
    private System.Random wallRandom;
    private Material yellowMaterial;
    private Material azureMaterial;
    private Material magentaMaterial;
    private Material limeMaterial;
    private Material violetMaterial;
    private Material pinkMaterial;
    private Material greenMaterial;
    private PhysicsMaterial sharedWallPhysicsMaterial;

    public string Status => status;
    public IReadOnlyList<string> GeneratedWallEdgeIds
    {
        get
        {
            List<string> ids = new List<string>(generatedWallRecords.Count);
            for (int i = 0; i < generatedWallRecords.Count; i++)
            {
                ids.Add(generatedWallRecords[i].EdgeId);
            }

            return ids;
        }
    }

    private void Start()
    {
        if (Application.isPlaying && generateOnStart)
        {
            StartCoroutine(GenerateWallsWhenBoardIsReady());
        }
    }

    private void OnValidate()
    {
        wallWidth = Mathf.Max(0.01f, wallWidth);
        wallHeight = Mathf.Max(0.01f, wallHeight);
        boardReadyRetryCount = Mathf.Max(0, boardReadyRetryCount);
        boardReadyRetryDelaySeconds = Mathf.Max(0.01f, boardReadyRetryDelaySeconds);
        maxNonCriticalWallRemovalRatio = Mathf.Clamp(maxNonCriticalWallRemovalRatio, 0f, 0.2f);
    }

    [ContextMenu("Generate Walls")]
    public void GenerateWalls()
    {
        if (!ResolveBoardSource())
        {
            Debug.LogWarning("MazeWallsGenerator could not generate walls because no MazeBoardGenerator component was found.");
            return;
        }

        if (!IsBoardReady())
        {
            Debug.LogWarning("MazeWallsGenerator could not generate walls because the board source status is not exactly 'board ready'.");
            return;
        }

        GenerateWallsImmediately();
    }

    [ContextMenu("Clear Generated Walls")]
    public void ClearGeneratedWalls()
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

        generatedWallRecords.Clear();
        wallByEdgeId.Clear();
        status = "not generated";
    }

    private IEnumerator GenerateWallsWhenBoardIsReady()
    {
        ResolveBoardSource();

        int attemptsRemaining = boardReadyRetryCount;
        while (!IsBoardReady() && attemptsRemaining > 0)
        {
            attemptsRemaining--;
            yield return new WaitForSeconds(boardReadyRetryDelaySeconds);
            ResolveBoardSource();
        }

        if (!IsBoardReady())
        {
            Debug.LogWarning("MazeWallsGenerator waited for MazeBoardGenerator, but the board was not ready. Walls were not generated.");
            yield break;
        }

        GenerateWallsImmediately();
    }

    private bool ResolveBoardSource()
    {
        if (boardSource != null)
        {
            return true;
        }

        if (!autoFindBoardSource)
        {
            return false;
        }

#if UNITY_2023_1_OR_NEWER
        boardSource = FindFirstObjectByType<MazeBoardGenerator>();
#else
        boardSource = FindObjectOfType<MazeBoardGenerator>();
#endif
        return boardSource != null;
    }

    private bool IsBoardReady()
    {
        return boardSource != null && boardSource.Status == "board ready";
    }

    private void GenerateWallsImmediately()
    {
        InitializeRandom();
        ClearGeneratedWalls();
        CreateSharedMaterials();
        CreateSharedPhysicsMaterial();
        BuildWallRecordsFromBoard();

        if (removeNonCriticalWalls)
        {
            RemoveOptionalNonCriticalWalls();
        }

        if (!ValidateOpeningsConnectPathCellsOnly())
        {
            Debug.LogWarning("MazeWallsGenerator validation failed. Wall generation was cancelled because one or more openings would expose an unused or out-of-board cell.");
            generatedWallRecords.Clear();
            wallByEdgeId.Clear();
            return;
        }

        RenderWalls();

        status = "walls ready";
        Debug.Log("walls ready");
    }

    private void InitializeRandom()
    {
        int seed = randomizeWallSeed ? Environment.TickCount : wallRandomSeed;
        wallRandom = new System.Random(seed);
    }

    private void BuildWallRecordsFromBoard()
    {
        generatedWallRecords.Clear();
        wallByEdgeId.Clear();

        IReadOnlyList<MazeBoardGenerator.MazeCellRecord> cells = boardSource.GeneratedCells;
        Dictionary<GridPosition, MazeBoardGenerator.MazeCellRecord> cellLookup = BuildCellLookup(cells);

        for (int i = 0; i < cells.Count; i++)
        {
            MazeBoardGenerator.MazeCellRecord cell = cells[i];

            if (!IsPathCell(cell))
            {
                continue;
            }

            GridPosition sourcePosition = new GridPosition(cell.Column, cell.Row);
            TryAddWallForEdge(sourcePosition, WallDirection.North, cellLookup);
            TryAddWallForEdge(sourcePosition, WallDirection.East, cellLookup);
            TryAddWallForEdge(sourcePosition, WallDirection.South, cellLookup);
            TryAddWallForEdge(sourcePosition, WallDirection.West, cellLookup);
        }
    }

    private Dictionary<GridPosition, MazeBoardGenerator.MazeCellRecord> BuildCellLookup(
        IReadOnlyList<MazeBoardGenerator.MazeCellRecord> cells)
    {
        Dictionary<GridPosition, MazeBoardGenerator.MazeCellRecord> lookup =
            new Dictionary<GridPosition, MazeBoardGenerator.MazeCellRecord>(cells.Count);

        for (int i = 0; i < cells.Count; i++)
        {
            MazeBoardGenerator.MazeCellRecord cell = cells[i];
            lookup[new GridPosition(cell.Column, cell.Row)] = cell;
        }

        return lookup;
    }

    private void TryAddWallForEdge(
        GridPosition sourcePosition,
        WallDirection direction,
        Dictionary<GridPosition, MazeBoardGenerator.MazeCellRecord> cellLookup)
    {
        GridPosition neighborPosition = GetNeighbor(sourcePosition, direction);
        bool neighborInsideBoard = IsInsideBoard(neighborPosition);
        bool neighborIsPathCell = false;

        if (neighborInsideBoard && cellLookup.TryGetValue(neighborPosition, out MazeBoardGenerator.MazeCellRecord neighborCell))
        {
            neighborIsPathCell = IsPathCell(neighborCell);
        }

        if (neighborIsPathCell)
        {
            return;
        }

        string edgeId = GetEdgeId(sourcePosition, direction);

        if (wallByEdgeId.ContainsKey(edgeId))
        {
            return;
        }

        bool protectsUnusedCell = neighborInsideBoard;
        bool isBoundaryWall = !neighborInsideBoard;

        WallRecord wallRecord = new WallRecord
        {
            EdgeId = edgeId,
            SourceCell = sourcePosition,
            Direction = direction,
            IsBoundaryWall = isBoundaryWall,
            ProtectsUnusedCell = protectsUnusedCell,
            ColorId = ChooseWallColor(sourcePosition, direction)
        };

        AssignWallTransform(wallRecord);
        wallByEdgeId.Add(edgeId, wallRecord);
        generatedWallRecords.Add(wallRecord);
    }

    private void RemoveOptionalNonCriticalWalls()
    {
        List<WallRecord> removableWalls = new List<WallRecord>();

        for (int i = 0; i < generatedWallRecords.Count; i++)
        {
            WallRecord wall = generatedWallRecords[i];

            if (IsNonCriticalInternalWall(wall))
            {
                removableWalls.Add(wall);
            }
        }

        if (removableWalls.Count == 0)
        {
            return;
        }

        int maxRemovalCount = Mathf.FloorToInt(generatedWallRecords.Count * maxNonCriticalWallRemovalRatio);
        maxRemovalCount = Mathf.Clamp(maxRemovalCount, 0, removableWalls.Count);

        Shuffle(removableWalls);

        int removedCount = 0;
        for (int i = 0; i < removableWalls.Count && removedCount < maxRemovalCount; i++)
        {
            WallRecord candidate = removableWalls[i];

            if (!CanSafelyRemoveWall(candidate))
            {
                continue;
            }

            generatedWallRecords.Remove(candidate);
            wallByEdgeId.Remove(candidate.EdgeId);
            removedCount++;
        }
    }

    private bool IsNonCriticalInternalWall(WallRecord wall)
    {
        if (wall.IsBoundaryWall)
        {
            return false;
        }

        if (wall.ProtectsUnusedCell)
        {
            return false;
        }

        GridPosition neighbor = GetNeighbor(wall.SourceCell, wall.Direction);
        return IsInsideBoard(wall.SourceCell) && IsInsideBoard(neighbor);
    }

    private bool CanSafelyRemoveWall(WallRecord wall)
    {
        if (wall.IsBoundaryWall || wall.ProtectsUnusedCell)
        {
            return false;
        }

        GridPosition neighbor = GetNeighbor(wall.SourceCell, wall.Direction);

        if (!IsInsideBoard(neighbor))
        {
            return false;
        }

        MazeBoardGenerator.MazeCellRecord sourceCell = FindBoardCell(wall.SourceCell);
        MazeBoardGenerator.MazeCellRecord neighborCell = FindBoardCell(neighbor);

        return sourceCell != null && neighborCell != null && IsPathCell(sourceCell) && IsPathCell(neighborCell);
    }

    private bool ValidateOpeningsConnectPathCellsOnly()
    {
        IReadOnlyList<MazeBoardGenerator.MazeCellRecord> cells = boardSource.GeneratedCells;
        Dictionary<GridPosition, MazeBoardGenerator.MazeCellRecord> cellLookup = BuildCellLookup(cells);

        for (int i = 0; i < cells.Count; i++)
        {
            MazeBoardGenerator.MazeCellRecord cell = cells[i];

            if (!IsPathCell(cell))
            {
                continue;
            }

            GridPosition sourcePosition = new GridPosition(cell.Column, cell.Row);
            WallDirection[] directions =
            {
                WallDirection.North,
                WallDirection.East,
                WallDirection.South,
                WallDirection.West
            };

            for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
            {
                WallDirection direction = directions[directionIndex];
                string edgeId = GetEdgeId(sourcePosition, direction);

                if (wallByEdgeId.ContainsKey(edgeId))
                {
                    continue;
                }

                GridPosition neighborPosition = GetNeighbor(sourcePosition, direction);

                if (!IsInsideBoard(neighborPosition))
                {
                    continue;
                }

                if (!cellLookup.TryGetValue(neighborPosition, out MazeBoardGenerator.MazeCellRecord neighborCell))
                {
                    return false;
                }

                if (!IsPathCell(neighborCell))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void RenderWalls()
    {
        GameObject parentObject = new GameObject(generatedParentName);
        parentObject.transform.SetParent(transform, false);

        for (int i = 0; i < generatedWallRecords.Count; i++)
        {
            WallRecord wall = generatedWallRecords[i];

            GameObject wallObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallObject.name = $"Wall {wall.EdgeId} - {wall.Direction} - Cell [{wall.SourceCell.Column}, {wall.SourceCell.Row}]";
            wallObject.transform.SetParent(parentObject.transform, false);
            wallObject.transform.position = wall.WorldPosition;
            wallObject.transform.rotation = wall.WorldRotation;
            wallObject.transform.localScale = wall.LocalScale;

            MeshRenderer renderer = wallObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetSharedMaterial(wall.ColorId);
            }

            Collider wallCollider = wallObject.GetComponent<Collider>();
            if (wallCollider != null)
            {
                wallCollider.enabled = collidersEnabled;
                wallCollider.sharedMaterial = sharedWallPhysicsMaterial;
            }
        }
    }

    private void AssignWallTransform(WallRecord wall)
    {
        float cellSize = boardSource.CellSize;
        Vector3 sourceWorld = GridToWorld(wall.SourceCell.Column, wall.SourceCell.Row);
        float y = boardSurfaceY + wallHeight * 0.5f;

        switch (wall.Direction)
        {
            case WallDirection.North:
                wall.WorldPosition = new Vector3(sourceWorld.x, y, sourceWorld.z + cellSize * 0.5f);
                wall.WorldRotation = Quaternion.identity;
                wall.LocalScale = new Vector3(cellSize, wallHeight, wallWidth);
                break;

            case WallDirection.South:
                wall.WorldPosition = new Vector3(sourceWorld.x, y, sourceWorld.z - cellSize * 0.5f);
                wall.WorldRotation = Quaternion.identity;
                wall.LocalScale = new Vector3(cellSize, wallHeight, wallWidth);
                break;

            case WallDirection.West:
                wall.WorldPosition = new Vector3(sourceWorld.x - cellSize * 0.5f, y, sourceWorld.z);
                wall.WorldRotation = Quaternion.Euler(0f, 90f, 0f);
                wall.LocalScale = new Vector3(cellSize, wallHeight, wallWidth);
                break;

            case WallDirection.East:
                wall.WorldPosition = new Vector3(sourceWorld.x + cellSize * 0.5f, y, sourceWorld.z);
                wall.WorldRotation = Quaternion.Euler(0f, 90f, 0f);
                wall.LocalScale = new Vector3(cellSize, wallHeight, wallWidth);
                break;
        }
    }

    private Vector3 GridToWorld(int column, int row)
    {
        float worldX = (column - (boardSource.Columns - 1) * 0.5f) * boardSource.CellSize;
        float worldZ = ((boardSource.Rows - 1) * 0.5f - row) * boardSource.CellSize;
        return new Vector3(worldX, boardSurfaceY, worldZ);
    }

    private string GetEdgeId(GridPosition sourcePosition, WallDirection direction)
    {
        switch (direction)
        {
            case WallDirection.North:
                return $"H:{sourcePosition.Column}:{sourcePosition.Row}";

            case WallDirection.South:
                return $"H:{sourcePosition.Column}:{sourcePosition.Row + 1}";

            case WallDirection.West:
                return $"V:{sourcePosition.Column}:{sourcePosition.Row}";

            case WallDirection.East:
                return $"V:{sourcePosition.Column + 1}:{sourcePosition.Row}";

            default:
                return $"Unknown:{sourcePosition.Column}:{sourcePosition.Row}:{direction}";
        }
    }

    private GridPosition GetNeighbor(GridPosition sourcePosition, WallDirection direction)
    {
        switch (direction)
        {
            case WallDirection.North:
                return new GridPosition(sourcePosition.Column, sourcePosition.Row - 1);

            case WallDirection.East:
                return new GridPosition(sourcePosition.Column + 1, sourcePosition.Row);

            case WallDirection.South:
                return new GridPosition(sourcePosition.Column, sourcePosition.Row + 1);

            case WallDirection.West:
                return new GridPosition(sourcePosition.Column - 1, sourcePosition.Row);

            default:
                return sourcePosition;
        }
    }

    private bool IsInsideBoard(GridPosition position)
    {
        return boardSource != null &&
               position.Column >= 0 &&
               position.Column < boardSource.Columns &&
               position.Row >= 0 &&
               position.Row < boardSource.Rows;
    }

    private bool IsPathCell(MazeBoardGenerator.MazeCellRecord cell)
    {
        if (cell == null)
        {
            return false;
        }

        return cell.CellType == MazeBoardGenerator.MazeCellType.MainPath ||
               cell.CellType == MazeBoardGenerator.MazeCellType.FalsePath ||
               cell.CellType == MazeBoardGenerator.MazeCellType.Intersection ||
               cell.CellType == MazeBoardGenerator.MazeCellType.Entrance ||
               cell.CellType == MazeBoardGenerator.MazeCellType.Exit;
    }

    private MazeBoardGenerator.MazeCellRecord FindBoardCell(GridPosition position)
    {
        IReadOnlyList<MazeBoardGenerator.MazeCellRecord> cells = boardSource.GeneratedCells;

        for (int i = 0; i < cells.Count; i++)
        {
            MazeBoardGenerator.MazeCellRecord cell = cells[i];

            if (cell.Column == position.Column && cell.Row == position.Row)
            {
                return cell;
            }
        }

        return null;
    }

    private WallColorId ChooseWallColor(GridPosition sourcePosition, WallDirection direction)
    {
        int value = Mathf.Abs(sourcePosition.Column * 31 + sourcePosition.Row * 17 + (int)direction * 13);
        return (WallColorId)(value % 7);
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int swapIndex = wallRandom.Next(i + 1);
            (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
        }
    }

    private void CreateSharedMaterials()
    {
        yellowMaterial = CreateMaterial("Maze Wall Yellow", yellowWallColor);
        azureMaterial = CreateMaterial("Maze Wall Azure", azureWallColor);
        magentaMaterial = CreateMaterial("Maze Wall Magenta", magentaWallColor);
        limeMaterial = CreateMaterial("Maze Wall Lime", limeWallColor);
        violetMaterial = CreateMaterial("Maze Wall Violet", violetWallColor);
        pinkMaterial = CreateMaterial("Maze Wall Pink", pinkWallColor);
        greenMaterial = CreateMaterial("Maze Wall Green", greenWallColor);
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

    private Material GetSharedMaterial(WallColorId colorId)
    {
        switch (colorId)
        {
            case WallColorId.Yellow:
                return yellowMaterial;

            case WallColorId.Azure:
                return azureMaterial;

            case WallColorId.Magenta:
                return magentaMaterial;

            case WallColorId.Lime:
                return limeMaterial;

            case WallColorId.Violet:
                return violetMaterial;

            case WallColorId.Pink:
                return pinkMaterial;

            case WallColorId.Green:
                return greenMaterial;

            default:
                return greenMaterial;
        }
    }

    private void CreateSharedPhysicsMaterial()
    {
        sharedWallPhysicsMaterial = new PhysicsMaterial("Maze Wall Physics")
        {
            bounciness = bounciness,
            dynamicFriction = dynamicFriction,
            staticFriction = staticFriction,
            frictionCombine = frictionCombine,
            bounceCombine = bounceCombine
        };
    }
}