using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MazeScoreSystem : MonoBehaviour
{
    private enum ScoreCellType
    {
        Empty,
        MainPath,
        FalsePath,
        Intersection,
        Entrance,
        Exit
    }

    private sealed class ScoreCellRecord
    {
        public int Column;
        public int Row;
        public Vector3 WorldPosition;
        public ScoreCellType CellType;
        public bool IsWalkable;
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

    [Header("Script References")]
    [SerializeField] private MazeBoardGenerator boardGenerator;
    [SerializeField] private UnityEngine.Object combinedMazeGenerator;
    [SerializeField] private MazeGameSystem gameSystem;
    [SerializeField] private PlayerToken playerToken;
    [SerializeField] private Transform playerTarget;
    [SerializeField] private bool autoFindReferences = true;

    [Header("Route Analysis")]
    [SerializeField] private bool calculateOnStart = true;
    [SerializeField] private bool recalculateWhenMazeChanges = true;
    [SerializeField, Min(0.05f)] private float readinessRetryInterval = 0.2f;
    [SerializeField, Min(0f)] private float readinessTimeout = 12f;
    [SerializeField, Min(0.01f)] private float playerDistanceSampleInterval = 0.05f;
    [SerializeField, Min(0.01f)] private float minimumMovementSampleDistance = 0.01f;

    [Header("Scoring")]
    [SerializeField, Min(1f)] private float baseScore = 10000f;
    [SerializeField, Min(0f)] private float timePenaltyPerSecond = 35f;
    [SerializeField, Min(0f)] private float routeInefficiencyPenalty = 2500f;
    [SerializeField, Min(0f)] private float minimumScore = 0f;
    [SerializeField, Min(1f)] private float expectedSecondsPerOptimalStep = 0.75f;
    [SerializeField] private bool clampEfficiencyToOne = true;

    [Header("UI Settings")]
    [SerializeField] private UnityEngine.Object scoreTextTarget;
    [SerializeField] private UnityEngine.Object detailsTextTarget;
    [SerializeField] private bool useOnGUIFallback = true;
    [SerializeField] private Rect onGuiScoreRect = new Rect(16f, 104f, 520f, 42f);
    [SerializeField] private Rect onGuiDetailsRect = new Rect(16f, 146f, 720f, 90f);
    [SerializeField] private int onGuiFontSize = 22;
    [SerializeField] private Color onGuiTextColor = Color.white;
    [SerializeField] private bool showScoreOnlyAfterFinish = true;
    [SerializeField] private string scorePrefix = "Score: ";
    [SerializeField] private string detailsPrefix = "Route Efficiency: ";

    [Header("Debug")]
    [SerializeField] private bool printStatusMessages = true;
    [SerializeField] private bool printWarnings = true;

    [SerializeField, HideInInspector] private string status = "not ready";
    [SerializeField, HideInInspector] private int optimalRouteSteps = -1;
    [SerializeField, HideInInspector] private float optimalRouteWorldDistance;
    [SerializeField, HideInInspector] private float playerTravelDistance;
    [SerializeField, HideInInspector] private float finalScore;
    [SerializeField, HideInInspector] private float finalRouteEfficiency;
    [SerializeField, HideInInspector] private float finalCompletionTime;

    private readonly List<ScoreCellRecord> cells = new List<ScoreCellRecord>();
    private readonly Dictionary<GridPosition, ScoreCellRecord> cellByPosition = new Dictionary<GridPosition, ScoreCellRecord>();
    private GridPosition entrancePosition;
    private GridPosition exitPosition;
    private bool hasEntrance;
    private bool hasExit;
    private bool hasCalculatedOptimalRoute;
    private bool hasFinishedScoring;
    private bool warningPrinted;
    private int lastKnownCellCount = -1;
    private Vector3 lastPlayerPosition;
    private float nextSampleTime;
    private GUIStyle onGuiStyle;

    public string Status => status;
    public int OptimalRouteSteps => optimalRouteSteps;
    public float OptimalRouteWorldDistance => optimalRouteWorldDistance;
    public float PlayerTravelDistance => playerTravelDistance;
    public float FinalScore => finalScore;
    public float FinalRouteEfficiency => finalRouteEfficiency;
    public float FinalCompletionTime => finalCompletionTime;

    private void Start()
    {
        ResolveReferences();

        if (calculateOnStart)
        {
            StartCoroutine(PrepareScoringWhenReady());
        }
    }

    private void Update()
    {
        ResolveReferences();

        if (recalculateWhenMazeChanges && HasMazeDataChanged())
        {
            ResetScoreState();
            StartCoroutine(PrepareScoringWhenReady());
        }

        if (gameSystem != null && gameSystem.CurrentState == MazeGameSystem.MazeGameState.Playing)
        {
            SamplePlayerDistance();
        }

        if (gameSystem != null &&
            gameSystem.CurrentState == MazeGameSystem.MazeGameState.Finished &&
            !hasFinishedScoring)
        {
            FinalizeScore();
        }
    }

    private void OnValidate()
    {
        readinessRetryInterval = Mathf.Max(0.05f, readinessRetryInterval);
        readinessTimeout = Mathf.Max(0f, readinessTimeout);
        playerDistanceSampleInterval = Mathf.Max(0.01f, playerDistanceSampleInterval);
        minimumMovementSampleDistance = Mathf.Max(0.01f, minimumMovementSampleDistance);
        baseScore = Mathf.Max(1f, baseScore);
        timePenaltyPerSecond = Mathf.Max(0f, timePenaltyPerSecond);
        routeInefficiencyPenalty = Mathf.Max(0f, routeInefficiencyPenalty);
        expectedSecondsPerOptimalStep = Mathf.Max(1f, expectedSecondsPerOptimalStep);
        onGuiFontSize = Mathf.Max(8, onGuiFontSize);
    }

    private void OnGUI()
    {
        if (!useOnGUIFallback)
        {
            return;
        }

        if (showScoreOnlyAfterFinish && !hasFinishedScoring)
        {
            return;
        }

        bool hasScoreText = IsTextTargetUsable(scoreTextTarget);
        bool hasDetailsText = IsTextTargetUsable(detailsTextTarget);

        if (hasScoreText && hasDetailsText)
        {
            return;
        }

        if (onGuiStyle == null)
        {
            onGuiStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = onGuiFontSize,
                fontStyle = FontStyle.Bold
            };
        }

        onGuiStyle.fontSize = onGuiFontSize;
        onGuiStyle.normal.textColor = onGuiTextColor;

        if (!hasScoreText)
        {
            GUI.Label(onGuiScoreRect, GetScoreDisplayText(), onGuiStyle);
        }

        if (!hasDetailsText)
        {
            GUI.Label(onGuiDetailsRect, GetDetailsDisplayText(), onGuiStyle);
        }
    }

    [ContextMenu("Calculate Optimal Route")]
    public void CalculateOptimalRoute()
    {
        ResolveReferences();

        if (!LoadMazeCells())
        {
            status = "maze data missing";

            if (printWarnings)
            {
                Debug.LogWarning("MazeScoreSystem could not calculate the optimal route because no ready maze cell table is available.");
            }

            return;
        }

        optimalRouteSteps = RunBreadthFirstSearch();
        hasCalculatedOptimalRoute = optimalRouteSteps >= 0;

        if (hasCalculatedOptimalRoute)
        {
            float sourceCellSize = GetCellSize();
            optimalRouteWorldDistance = optimalRouteSteps * sourceCellSize;
            status = "score ready";

            if (printStatusMessages)
            {
                Debug.Log("MazeScoreSystem optimal route steps: " + optimalRouteSteps);
            }
        }
        else
        {
            status = "route unavailable";

            if (printWarnings)
            {
                Debug.LogWarning("MazeScoreSystem could not find a walkable route from entrance to exit.");
            }
        }

        UpdateScoreDisplay();
    }

    [ContextMenu("Reset Score")]
    public void ResetScoreState()
    {
        playerTravelDistance = 0f;
        finalScore = 0f;
        finalRouteEfficiency = 0f;
        finalCompletionTime = 0f;
        hasFinishedScoring = false;
        lastPlayerPosition = GetPlayerPosition();
        nextSampleTime = 0f;
        UpdateScoreDisplay();
    }

    private IEnumerator PrepareScoringWhenReady()
    {
        float elapsed = 0f;

        while (true)
        {
            ResolveReferences();

            if (IsMazeDataReady())
            {
                CalculateOptimalRoute();
                ResetScoreState();
                yield break;
            }

            if (readinessTimeout > 0f && elapsed >= readinessTimeout)
            {
                status = "maze data missing";

                if (!warningPrinted && printWarnings)
                {
                    warningPrinted = true;
                    Debug.LogWarning("MazeScoreSystem could not find ready maze data before the configured timeout.");
                }

                yield break;
            }

            yield return new WaitForSeconds(readinessRetryInterval);
            elapsed += readinessRetryInterval;
        }
    }

    private bool LoadMazeCells()
    {
        cells.Clear();
        cellByPosition.Clear();
        hasEntrance = false;
        hasExit = false;

        if (boardGenerator != null && boardGenerator.Status == "board ready")
        {
            IReadOnlyList<MazeBoardGenerator.MazeCellRecord> sourceCells = boardGenerator.GeneratedCells;

            if (sourceCells == null || sourceCells.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < sourceCells.Count; i++)
            {
                MazeBoardGenerator.MazeCellRecord source = sourceCells[i];
                ScoreCellType cellType = ConvertBoardCellType(source.CellType);

                ScoreCellRecord record = new ScoreCellRecord
                {
                    Column = source.Column,
                    Row = source.Row,
                    WorldPosition = source.WorldPosition,
                    CellType = cellType,
                    IsWalkable = IsWalkableType(cellType)
                };

                AddCellRecord(record);
            }

            lastKnownCellCount = cells.Count;
            return hasEntrance && hasExit;
        }

        if (combinedMazeGenerator != null && IsCombinedMazeReady())
        {
            bool loaded = LoadCellsFromCombinedGenerator();
            lastKnownCellCount = cells.Count;
            return loaded;
        }

        return false;
    }

    private bool LoadCellsFromCombinedGenerator()
    {
        object generatedCells = GetPublicPropertyOrFieldValue(combinedMazeGenerator, "GeneratedCells");

        if (generatedCells == null)
        {
            generatedCells = GetPublicPropertyOrFieldValue(combinedMazeGenerator, "CellTable");
        }

        IEnumerable enumerable = generatedCells as IEnumerable;
        if (enumerable == null)
        {
            return false;
        }

        foreach (object source in enumerable)
        {
            if (source == null)
            {
                continue;
            }

            int column = 0;
            int row = 0;
            Vector3 worldPosition = Vector3.zero;
            string cellTypeName = "";

            TryReadIntMember(source, "Column", ref column);
            TryReadIntMember(source, "column", ref column);
            TryReadIntMember(source, "Row", ref row);
            TryReadIntMember(source, "row", ref row);

            if (!TryReadVector3Member(source, "WorldPosition", ref worldPosition))
            {
                TryReadVector3Member(source, "worldPosition", ref worldPosition);
            }

            if (worldPosition == Vector3.zero)
            {
                worldPosition = GridToWorld(column, row);
            }

            if (!TryReadStringOrEnumMember(source, "CellType", ref cellTypeName))
            {
                TryReadStringOrEnumMember(source, "cellType", ref cellTypeName);
            }

            ScoreCellType cellType = ConvertCellTypeName(cellTypeName);

            ScoreCellRecord record = new ScoreCellRecord
            {
                Column = column,
                Row = row,
                WorldPosition = worldPosition,
                CellType = cellType,
                IsWalkable = IsWalkableType(cellType)
            };

            AddCellRecord(record);
        }

        return cells.Count > 0 && hasEntrance && hasExit;
    }

    private void AddCellRecord(ScoreCellRecord record)
    {
        GridPosition position = new GridPosition(record.Column, record.Row);

        cells.Add(record);
        cellByPosition[position] = record;

        if (record.CellType == ScoreCellType.Entrance)
        {
            entrancePosition = position;
            hasEntrance = true;
        }

        if (record.CellType == ScoreCellType.Exit)
        {
            exitPosition = position;
            hasExit = true;
        }
    }

    private int RunBreadthFirstSearch()
    {
        if (!hasEntrance || !hasExit)
        {
            return -1;
        }

        Queue<GridPosition> frontier = new Queue<GridPosition>();
        Dictionary<GridPosition, int> distanceByCell = new Dictionary<GridPosition, int>();

        frontier.Enqueue(entrancePosition);
        distanceByCell[entrancePosition] = 0;

        while (frontier.Count > 0)
        {
            GridPosition current = frontier.Dequeue();

            if (current == exitPosition)
            {
                return distanceByCell[current];
            }

            GridPosition[] neighbors =
            {
                new GridPosition(current.Column, current.Row - 1),
                new GridPosition(current.Column + 1, current.Row),
                new GridPosition(current.Column, current.Row + 1),
                new GridPosition(current.Column - 1, current.Row)
            };

            for (int i = 0; i < neighbors.Length; i++)
            {
                GridPosition neighbor = neighbors[i];

                if (distanceByCell.ContainsKey(neighbor))
                {
                    continue;
                }

                if (!cellByPosition.TryGetValue(neighbor, out ScoreCellRecord neighborRecord))
                {
                    continue;
                }

                if (!neighborRecord.IsWalkable)
                {
                    continue;
                }

                distanceByCell[neighbor] = distanceByCell[current] + 1;
                frontier.Enqueue(neighbor);
            }
        }

        return -1;
    }

    private void SamplePlayerDistance()
    {
        if (Time.time < nextSampleTime)
        {
            return;
        }

        nextSampleTime = Time.time + playerDistanceSampleInterval;

        Vector3 currentPosition = GetPlayerPosition();

        if (lastPlayerPosition == Vector3.zero)
        {
            lastPlayerPosition = currentPosition;
            return;
        }

        Vector3 previousHorizontal = new Vector3(lastPlayerPosition.x, 0f, lastPlayerPosition.z);
        Vector3 currentHorizontal = new Vector3(currentPosition.x, 0f, currentPosition.z);

        float delta = Vector3.Distance(previousHorizontal, currentHorizontal);

        if (delta >= minimumMovementSampleDistance)
        {
            playerTravelDistance += delta;
            lastPlayerPosition = currentPosition;
        }
    }

    private void FinalizeScore()
    {
        if (!hasCalculatedOptimalRoute)
        {
            CalculateOptimalRoute();
        }

        finalCompletionTime = gameSystem != null ? gameSystem.FinalCompletionTime : 0f;

        float optimalDistance = Mathf.Max(0.0001f, optimalRouteWorldDistance);
        float measuredDistance = Mathf.Max(optimalDistance, playerTravelDistance);

        finalRouteEfficiency = optimalDistance / measuredDistance;

        if (clampEfficiencyToOne)
        {
            finalRouteEfficiency = Mathf.Clamp01(finalRouteEfficiency);
        }

        float expectedOptimalTime = Mathf.Max(1f, optimalRouteSteps * expectedSecondsPerOptimalStep);
        float timeEfficiency = Mathf.Clamp01(expectedOptimalTime / Mathf.Max(expectedOptimalTime, finalCompletionTime));

        float inefficiency = 1f - finalRouteEfficiency;
        float timePenalty = finalCompletionTime * timePenaltyPerSecond;
        float routePenalty = inefficiency * routeInefficiencyPenalty;

        finalScore = baseScore * timeEfficiency - timePenalty - routePenalty;
        finalScore = Mathf.Max(minimumScore, finalScore);

        hasFinishedScoring = true;
        status = "score finished";

        UpdateScoreDisplay();

        if (printStatusMessages)
        {
            Debug.Log("MazeScoreSystem final score: " + Mathf.RoundToInt(finalScore));
            Debug.Log("MazeScoreSystem route efficiency: " + (finalRouteEfficiency * 100f).ToString("0.0") + "%");
        }
    }

    private void UpdateScoreDisplay()
    {
        TrySetText(scoreTextTarget, GetScoreDisplayText());
        TrySetText(detailsTextTarget, GetDetailsDisplayText());
    }

    private string GetScoreDisplayText()
    {
        if (showScoreOnlyAfterFinish && !hasFinishedScoring)
        {
            return "";
        }

        return scorePrefix + Mathf.RoundToInt(finalScore);
    }

    private string GetDetailsDisplayText()
    {
        if (showScoreOnlyAfterFinish && !hasFinishedScoring)
        {
            return "";
        }

        return detailsPrefix +
               (finalRouteEfficiency * 100f).ToString("0.0") +
               "% | Optimal: " +
               optimalRouteSteps +
               " steps | Distance: " +
               playerTravelDistance.ToString("0.0") +
               " units | Time: " +
               FormatTime(finalCompletionTime);
    }

    private Vector3 GetPlayerPosition()
    {
        if (playerTarget != null)
        {
            return playerTarget.position;
        }

        if (playerToken != null && playerToken.PlayerTransform != null)
        {
            return playerToken.PlayerTransform.position;
        }

        return Vector3.zero;
    }

    private void ResolveReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (boardGenerator == null)
        {
            boardGenerator = FindFirstObjectByType<MazeBoardGenerator>();
        }

        if (combinedMazeGenerator == null)
        {
            combinedMazeGenerator = FindCombinedMazeGenerator();
        }

        if (gameSystem == null)
        {
            gameSystem = FindFirstObjectByType<MazeGameSystem>();
        }

        if (playerToken == null)
        {
            playerToken = FindFirstObjectByType<PlayerToken>();
        }

        if (playerTarget == null && playerToken != null && playerToken.PlayerTransform != null)
        {
            playerTarget = playerToken.PlayerTransform;
        }

        if (playerTarget == null)
        {
            GameObject playerObject = GameObject.Find("Player Token");
            if (playerObject != null)
            {
                playerTarget = playerObject.transform;
            }
        }

        if (playerTarget == null)
        {
            GameObject playerObject = GameObject.Find("PlayerToken");
            if (playerObject != null)
            {
                playerTarget = playerObject.transform;
            }
        }
    }

    private bool IsMazeDataReady()
    {
        if (boardGenerator != null && boardGenerator.Status == "board ready")
        {
            return true;
        }

        return combinedMazeGenerator != null && IsCombinedMazeReady();
    }

    private bool HasMazeDataChanged()
    {
        int currentCount = 0;

        if (boardGenerator != null && boardGenerator.GeneratedCells != null)
        {
            currentCount = boardGenerator.GeneratedCells.Count;
        }
        else if (combinedMazeGenerator != null)
        {
            object generatedCells = GetPublicPropertyOrFieldValue(combinedMazeGenerator, "GeneratedCells");
            IEnumerable enumerable = generatedCells as IEnumerable;

            if (enumerable != null)
            {
                foreach (object unused in enumerable)
                {
                    currentCount++;
                }
            }
        }

        return currentCount > 0 && lastKnownCellCount > 0 && currentCount != lastKnownCellCount;
    }

    private float GetCellSize()
    {
        if (boardGenerator != null)
        {
            return boardGenerator.CellSize;
        }

        if (combinedMazeGenerator != null)
        {
            float combinedCellSize = 1f;
            TryReadFloatMember(combinedMazeGenerator, "CellSize", ref combinedCellSize);
            return combinedCellSize;
        }

        return 1f;
    }

    private Vector3 GridToWorld(int column, int row)
    {
        int columns = 14;
        int rows = 12;
        float sourceCellSize = 1f;

        if (boardGenerator != null)
        {
            columns = boardGenerator.Columns;
            rows = boardGenerator.Rows;
            sourceCellSize = boardGenerator.CellSize;
        }
        else if (combinedMazeGenerator != null)
        {
            TryReadIntMember(combinedMazeGenerator, "Columns", ref columns);
            TryReadIntMember(combinedMazeGenerator, "Rows", ref rows);
            TryReadFloatMember(combinedMazeGenerator, "CellSize", ref sourceCellSize);
        }

        float worldX = (column - (columns - 1) * 0.5f) * sourceCellSize;
        float worldZ = ((rows - 1) * 0.5f - row) * sourceCellSize;

        return new Vector3(worldX, 0f, worldZ);
    }

    private ScoreCellType ConvertBoardCellType(MazeBoardGenerator.MazeCellType sourceType)
    {
        switch (sourceType)
        {
            case MazeBoardGenerator.MazeCellType.MainPath:
                return ScoreCellType.MainPath;
            case MazeBoardGenerator.MazeCellType.FalsePath:
                return ScoreCellType.FalsePath;
            case MazeBoardGenerator.MazeCellType.Intersection:
                return ScoreCellType.Intersection;
            case MazeBoardGenerator.MazeCellType.Entrance:
                return ScoreCellType.Entrance;
            case MazeBoardGenerator.MazeCellType.Exit:
                return ScoreCellType.Exit;
            default:
                return ScoreCellType.Empty;
        }
    }

    private ScoreCellType ConvertCellTypeName(string cellTypeName)
    {
        if (string.IsNullOrEmpty(cellTypeName))
        {
            return ScoreCellType.Empty;
        }

        string normalized = cellTypeName.Replace(" ", "").Replace("_", "").ToLowerInvariant();

        if (normalized.Contains("entrance"))
        {
            return ScoreCellType.Entrance;
        }

        if (normalized.Contains("exit"))
        {
            return ScoreCellType.Exit;
        }

        if (normalized.Contains("intersection"))
        {
            return ScoreCellType.Intersection;
        }

        if (normalized.Contains("main"))
        {
            return ScoreCellType.MainPath;
        }

        if (normalized.Contains("false"))
        {
            return ScoreCellType.FalsePath;
        }

        return ScoreCellType.Empty;
    }

    private bool IsWalkableType(ScoreCellType cellType)
    {
        return cellType == ScoreCellType.MainPath ||
               cellType == ScoreCellType.FalsePath ||
               cellType == ScoreCellType.Intersection ||
               cellType == ScoreCellType.Entrance ||
               cellType == ScoreCellType.Exit;
    }

    private bool IsCombinedMazeReady()
    {
        string combinedStatus = "";

        if (TryReadStringOrEnumMember(combinedMazeGenerator, "Status", ref combinedStatus) ||
            TryReadStringOrEnumMember(combinedMazeGenerator, "status", ref combinedStatus))
        {
            return combinedStatus == "combined maze ready" ||
                   combinedStatus == "board ready" ||
                   combinedStatus == "maze ready";
        }

        return false;
    }

    private UnityEngine.Object FindCombinedMazeGenerator()
    {
        Type combinedType = FindTypeByName("MazeCombinedGenerator");

        if (combinedType == null || !typeof(UnityEngine.Object).IsAssignableFrom(combinedType))
        {
            return null;
        }

        return FindFirstObjectByType(combinedType);
    }

    private Type FindTypeByName(string typeName)
    {
        Type directType = Type.GetType(typeName);

        if (directType != null)
        {
            return directType;
        }

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        for (int i = 0; i < assemblies.Length; i++)
        {
            Type foundType = assemblies[i].GetType(typeName);

            if (foundType != null)
            {
                return foundType;
            }
        }

        return null;
    }

    private object GetPublicPropertyOrFieldValue(object source, string memberName)
    {
        if (source == null)
        {
            return null;
        }

        Type type = source.GetType();

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null)
        {
            return property.GetValue(source);
        }

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            return field.GetValue(source);
        }

        return null;
    }

    private bool TryReadIntMember(object source, string memberName, ref int value)
    {
        object rawValue = GetPublicPropertyOrFieldValue(source, memberName);

        if (rawValue == null)
        {
            return false;
        }

        try
        {
            value = Convert.ToInt32(rawValue);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadFloatMember(object source, string memberName, ref float value)
    {
        object rawValue = GetPublicPropertyOrFieldValue(source, memberName);

        if (rawValue == null)
        {
            return false;
        }

        try
        {
            value = Convert.ToSingle(rawValue);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadVector3Member(object source, string memberName, ref Vector3 value)
    {
        object rawValue = GetPublicPropertyOrFieldValue(source, memberName);

        if (rawValue is Vector3 vector)
        {
            value = vector;
            return true;
        }

        return false;
    }

    private bool TryReadStringOrEnumMember(object source, string memberName, ref string value)
    {
        object rawValue = GetPublicPropertyOrFieldValue(source, memberName);

        if (rawValue == null)
        {
            return false;
        }

        value = rawValue.ToString();
        return true;
    }

    private bool IsTextTargetUsable(UnityEngine.Object target)
    {
        if (target == null)
        {
            return false;
        }

        Type type = target.GetType();
        PropertyInfo textProperty = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
        return textProperty != null && textProperty.PropertyType == typeof(string);
    }

    private bool TrySetText(UnityEngine.Object target, string text)
    {
        if (target == null)
        {
            return false;
        }

        Type type = target.GetType();
        PropertyInfo textProperty = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);

        if (textProperty == null || textProperty.PropertyType != typeof(string) || !textProperty.CanWrite)
        {
            return false;
        }

        textProperty.SetValue(target, text);
        return true;
    }

    private string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);

        int minutes = Mathf.FloorToInt(seconds / 60f);
        int wholeSeconds = Mathf.FloorToInt(seconds % 60f);
        int hundredths = Mathf.FloorToInt((seconds - Mathf.Floor(seconds)) * 100f);

        return $"{minutes:00}:{wholeSeconds:00}.{hundredths:00}";
    }
}