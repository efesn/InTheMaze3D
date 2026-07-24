using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class MazeGameSystem : MonoBehaviour
{
    public enum MazeGameState
    {
        WaitingForMaze,
        ReadyToStart,
        Playing,
        Finished,
        Restarting
    }

    [Header("Script References")]
    [SerializeField] private MazeBoardGenerator boardGenerator;
    [SerializeField] private MazeWallsGenerator wallsGenerator;
    [SerializeField] private PlayerToken playerToken;
    [SerializeField] private PlayerCamera playerCamera;
    [SerializeField] private UnityEngine.Object combinedMazeGenerator;
    [SerializeField] private bool autoFindReferences = true;

    [Header("Startup Behavior")]
    [SerializeField] private bool startAutomatically = true;
    [SerializeField] private KeyCode manualStartKey = KeyCode.Space;
    [SerializeField, Min(0.05f)] private float readinessCheckInterval = 0.15f;
    [SerializeField, Min(0f)] private float startupTimeoutSeconds = 15f;
    [SerializeField] private bool disablePlayerInputUntilPlaying = true;

    [Header("Timer Settings")]
    [SerializeField] private bool showTimerWhileWaiting = true;
    [SerializeField] private string timerPrefix = "";
    [SerializeField] private string waitingMessage = "Waiting for maze...";
    [SerializeField] private string finishMessagePrefix = "Finished: ";

    [Header("Finish Detection")]
    [SerializeField] private Transform finishTriggerObject;
    [SerializeField, Min(0.05f)] private float finishDetectionRadius = 0.45f;
    [SerializeField] private bool allowMovementAfterFinish = false;
    [SerializeField] private bool useHorizontalDistanceOnly = true;

    [Header("UI Settings")]
    [SerializeField] private UnityEngine.Object timerTextTarget;
    [SerializeField] private UnityEngine.Object messageTextTarget;
    [SerializeField] private bool useOnGUIFallback = true;
    [SerializeField] private Rect onGuiTimerRect = new Rect(16f, 16f, 260f, 40f);
    [SerializeField] private Rect onGuiMessageRect = new Rect(16f, 58f, 420f, 42f);
    [SerializeField] private int onGuiFontSize = 24;
    [SerializeField] private Color onGuiTextColor = Color.white;

    [Header("Restart Behavior")]
    [SerializeField] private KeyCode restartKey = KeyCode.R;
    [SerializeField] private bool allowRestartAfterFinish = true;
    [SerializeField] private bool regenerateMazeOnRestart = true;
    [SerializeField] private bool resetPlayerOnRestart = true;
    [SerializeField, Min(0.05f)] private float restartReadinessDelaySeconds = 0.25f;

    [Header("Debug Logging")]
    [SerializeField] private bool printStateChanges = true;
    [SerializeField] private bool printReadinessWarnings = true;
    [SerializeField] private bool printFinishPositionSource = true;

    [SerializeField, HideInInspector] private MazeGameState currentState = MazeGameState.WaitingForMaze;
    [SerializeField, HideInInspector] private string status = "WaitingForMaze";
    [SerializeField, HideInInspector] private float elapsedTime;
    [SerializeField, HideInInspector] private float finalCompletionTime;

    private Coroutine startupCoroutine;
    private GUIStyle onGuiStyle;
    private Vector3 cachedExitWorldPosition;
    private bool hasCachedExitWorldPosition;
    private bool startupWarningPrinted;
    private string currentMessage = "";
    private bool gameStartedOnce;

    public MazeGameState CurrentState => currentState;
    public string Status => status;
    public float ElapsedTime => elapsedTime;
    public float FinalCompletionTime => finalCompletionTime;
    public bool IsPlaying => currentState == MazeGameState.Playing;
    public bool IsFinished => currentState == MazeGameState.Finished;

    private void Awake()
    {
        SetState(MazeGameState.WaitingForMaze);
        elapsedTime = 0f;
        finalCompletionTime = 0f;
        currentMessage = waitingMessage;

        if (autoFindReferences)
        {
            ResolveReferences();
        }

        ApplyPlayerInputAvailability();
        UpdateTimerDisplay();
        UpdateMessageDisplay(currentMessage);
    }

    private void Start()
    {
        startupCoroutine = StartCoroutine(StartupFlow());
    }

    private void Update()
    {
        if (currentState == MazeGameState.ReadyToStart)
        {
            if (startAutomatically || Input.GetKeyDown(manualStartKey))
            {
                StartGame();
            }
        }

        if (currentState == MazeGameState.Playing)
        {
            elapsedTime += Time.deltaTime;
            UpdateTimerDisplay();
            CheckFinishCondition();
        }

        if (currentState == MazeGameState.Finished && allowRestartAfterFinish && Input.GetKeyDown(restartKey))
        {
            RestartGame();
        }
    }

    private void OnValidate()
    {
        readinessCheckInterval = Mathf.Max(0.05f, readinessCheckInterval);
        startupTimeoutSeconds = Mathf.Max(0f, startupTimeoutSeconds);
        finishDetectionRadius = Mathf.Max(0.05f, finishDetectionRadius);
        restartReadinessDelaySeconds = Mathf.Max(0.05f, restartReadinessDelaySeconds);
        onGuiFontSize = Mathf.Max(8, onGuiFontSize);
    }

    private void OnGUI()
    {
        if (!useOnGUIFallback)
        {
            return;
        }

        bool hasTimerText = IsTextTargetUsable(timerTextTarget);
        bool hasMessageText = IsTextTargetUsable(messageTextTarget);

        if (hasTimerText && hasMessageText)
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

        if (!hasTimerText)
        {
            GUI.Label(onGuiTimerRect, GetTimerDisplayText(), onGuiStyle);
        }

        if (!hasMessageText && !string.IsNullOrEmpty(currentMessage))
        {
            GUI.Label(onGuiMessageRect, currentMessage, onGuiStyle);
        }
    }

    [ContextMenu("Start Game")]
    public void StartGame()
    {
        if (currentState != MazeGameState.ReadyToStart && currentState != MazeGameState.Finished)
        {
            return;
        }

        ResolveReferences();

        if (resetPlayerOnRestart || !gameStartedOnce)
        {
            ResetPlayerToEntrance();
        }

        elapsedTime = 0f;
        finalCompletionTime = 0f;
        gameStartedOnce = true;
        hasCachedExitWorldPosition = TryResolveExitWorldPosition(out cachedExitWorldPosition);

        SetState(MazeGameState.Playing);
        currentMessage = "";
        ApplyPlayerInputAvailability();
        UpdateTimerDisplay();
        UpdateMessageDisplay(currentMessage);
    }

    [ContextMenu("Restart Game")]
    public void RestartGame()
    {
        if (currentState == MazeGameState.Restarting)
        {
            return;
        }

        StartCoroutine(RestartFlow());
    }

    public void SetTimerTextTarget(UnityEngine.Object textTarget)
    {
        timerTextTarget = textTarget;
        UpdateTimerDisplay();
    }

    public void SetMessageTextTarget(UnityEngine.Object textTarget)
    {
        messageTextTarget = textTarget;
        UpdateMessageDisplay(currentMessage);
    }

    private IEnumerator StartupFlow()
    {
        SetState(MazeGameState.WaitingForMaze);
        currentMessage = waitingMessage;
        ApplyPlayerInputAvailability();
        UpdateTimerDisplay();
        UpdateMessageDisplay(currentMessage);

        float elapsedWait = 0f;

        while (!AreMazeAndPlayerReady())
        {
            ResolveReferences();

            if (startupTimeoutSeconds > 0f && elapsedWait >= startupTimeoutSeconds)
            {
                if (!startupWarningPrinted && printReadinessWarnings)
                {
                    startupWarningPrinted = true;
                    Debug.LogWarning("MazeGameSystem is still waiting because the maze and/or player token are not ready.");
                }
            }

            yield return new WaitForSeconds(readinessCheckInterval);
            elapsedWait += readinessCheckInterval;
        }

        hasCachedExitWorldPosition = TryResolveExitWorldPosition(out cachedExitWorldPosition);

        SetState(MazeGameState.ReadyToStart);
        ApplyPlayerInputAvailability();
        UpdateTimerDisplay();
        UpdateMessageDisplay(currentMessage);

        if (startAutomatically)
        {
            StartGame();
        }
    }

    private IEnumerator RestartFlow()
    {
        SetState(MazeGameState.Restarting);
        elapsedTime = 0f;
        finalCompletionTime = 0f;
        currentMessage = "Restarting...";
        ApplyPlayerInputAvailability();
        UpdateTimerDisplay();
        UpdateMessageDisplay(currentMessage);

        ResolveReferences();

        if (regenerateMazeOnRestart)
        {
            TryRegenerateMaze();
        }

        yield return new WaitForSeconds(restartReadinessDelaySeconds);

        SetState(MazeGameState.WaitingForMaze);
        currentMessage = waitingMessage;
        UpdateMessageDisplay(currentMessage);

        if (resetPlayerOnRestart)
        {
            ResetPlayerToEntrance();
        }

        if (startupCoroutine != null)
        {
            StopCoroutine(startupCoroutine);
        }

        startupCoroutine = StartCoroutine(StartupFlow());
    }

    private bool AreMazeAndPlayerReady()
    {
        bool mazeReady = IsCombinedMazeReady() || IsSeparateMazeReady();
        bool playerReady = playerToken != null && playerToken.Status == "token ready";
        return mazeReady && playerReady;
    }

    private bool IsSeparateMazeReady()
    {
        return boardGenerator != null &&
               wallsGenerator != null &&
               boardGenerator.Status == "board ready" &&
               wallsGenerator.Status == "walls ready";
    }

    private bool IsCombinedMazeReady()
    {
        if (combinedMazeGenerator == null)
        {
            return false;
        }

        if (TryGetStringPropertyOrField(combinedMazeGenerator, "Status", out string combinedStatus))
        {
            return combinedStatus == "combined maze ready";
        }

        if (TryGetStringPropertyOrField(combinedMazeGenerator, "status", out string lowerStatus))
        {
            return lowerStatus == "combined maze ready";
        }

        return false;
    }

    private void CheckFinishCondition()
    {
        if (playerToken == null || playerToken.PlayerTransform == null)
        {
            return;
        }

        if (!hasCachedExitWorldPosition)
        {
            hasCachedExitWorldPosition = TryResolveExitWorldPosition(out cachedExitWorldPosition);
        }

        Vector3 playerPosition = playerToken.PlayerTransform.position;
        Vector3 exitPosition = cachedExitWorldPosition;

        float distance;
        if (useHorizontalDistanceOnly)
        {
            Vector2 playerXZ = new Vector2(playerPosition.x, playerPosition.z);
            Vector2 exitXZ = new Vector2(exitPosition.x, exitPosition.z);
            distance = Vector2.Distance(playerXZ, exitXZ);
        }
        else
        {
            distance = Vector3.Distance(playerPosition, exitPosition);
        }

        if (distance <= finishDetectionRadius)
        {
            FinishGame();
        }
    }

    private void FinishGame()
    {
        if (currentState == MazeGameState.Finished)
        {
            return;
        }

        finalCompletionTime = elapsedTime;
        SetState(MazeGameState.Finished);
        currentMessage = finishMessagePrefix + FormatTime(finalCompletionTime);

        if (!allowMovementAfterFinish)
        {
            SetPlayerInputEnabled(false);
        }

        UpdateTimerDisplay();
        UpdateMessageDisplay(currentMessage);
        Debug.Log("Final completion time: " + FormatTime(finalCompletionTime));
    }

    private void ResetPlayerToEntrance()
    {
        if (playerToken == null)
        {
            return;
        }

        MethodInfo resetMethod = playerToken.GetType().GetMethod(
            "ResetTokenToEntrance",
            BindingFlags.Instance | BindingFlags.Public);

        if (resetMethod != null)
        {
            resetMethod.Invoke(playerToken, null);
            return;
        }

        if (playerToken.PlayerTransform != null && TryResolveEntranceWorldPosition(out Vector3 entrancePosition))
        {
            playerToken.PlayerTransform.position = entrancePosition;
        }
    }

    private void ApplyPlayerInputAvailability()
    {
        if (playerToken == null)
        {
            return;
        }

        bool shouldEnableInput = currentState == MazeGameState.Playing ||
                                 (currentState == MazeGameState.Finished && allowMovementAfterFinish);

        if (!disablePlayerInputUntilPlaying && currentState == MazeGameState.ReadyToStart)
        {
            shouldEnableInput = true;
        }

        SetPlayerInputEnabled(shouldEnableInput);
    }

    private void SetPlayerInputEnabled(bool enabled)
    {
        if (playerToken == null)
        {
            return;
        }

        MethodInfo setInputMethod = playerToken.GetType().GetMethod(
            "SetInputEnabled",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(bool) },
            null);

        if (setInputMethod != null)
        {
            setInputMethod.Invoke(playerToken, new object[] { enabled });
        }
    }

    private void TryRegenerateMaze()
    {
        if (combinedMazeGenerator != null && TryInvokePublicMethod(combinedMazeGenerator, "GenerateCombinedMaze"))
        {
            return;
        }

        if (combinedMazeGenerator != null && TryInvokePublicMethod(combinedMazeGenerator, "GenerateMaze"))
        {
            return;
        }

        if (combinedMazeGenerator != null && TryInvokePublicMethod(combinedMazeGenerator, "RegenerateMaze"))
        {
            return;
        }

        if (boardGenerator != null)
        {
            boardGenerator.GenerateBoard();
        }

        if (wallsGenerator != null)
        {
            wallsGenerator.GenerateWalls();
        }
    }

    private bool TryResolveExitWorldPosition(out Vector3 exitPosition)
    {
        if (boardGenerator != null)
        {
            if (TryGetVector3PropertyOrMethod(boardGenerator, "ExitWorldPosition", out exitPosition) ||
                TryGetVector3PropertyOrMethod(boardGenerator, "GetExitWorldPosition", out exitPosition) ||
                TryGetGridCellMethodAsWorldPosition(boardGenerator, "GetExitCell", out exitPosition) ||
                TryFindExitRecordInBoardTable(out exitPosition))
            {
                if (printFinishPositionSource)
                {
                    Debug.Log("MazeGameSystem finish detection is using the board generator exit position.");
                }

                return true;
            }
        }

        if (combinedMazeGenerator != null)
        {
            if (TryGetVector3PropertyOrMethod(combinedMazeGenerator, "ExitWorldPosition", out exitPosition) ||
                TryGetVector3PropertyOrMethod(combinedMazeGenerator, "GetExitWorldPosition", out exitPosition) ||
                TryGetGridCellMethodAsWorldPosition(combinedMazeGenerator, "GetExitCell", out exitPosition))
            {
                if (printFinishPositionSource)
                {
                    Debug.Log("MazeGameSystem finish detection is using the combined maze generator exit position.");
                }

                return true;
            }
        }

        if (finishTriggerObject != null)
        {
            exitPosition = finishTriggerObject.position;

            if (printFinishPositionSource)
            {
                Debug.Log("MazeGameSystem finish detection is using the assigned finish trigger object.");
            }

            return true;
        }

        exitPosition = GetFallbackExitWorldPosition();

        if (printFinishPositionSource)
        {
            Debug.LogWarning("MazeGameSystem finish detection is using the fallback right-middle exit position.");
        }

        return true;
    }

    private bool TryResolveEntranceWorldPosition(out Vector3 entrancePosition)
    {
        if (boardGenerator != null)
        {
            if (TryGetVector3PropertyOrMethod(boardGenerator, "EntranceWorldPosition", out entrancePosition) ||
                TryGetVector3PropertyOrMethod(boardGenerator, "GetEntranceWorldPosition", out entrancePosition) ||
                TryGetGridCellMethodAsWorldPosition(boardGenerator, "GetEntranceCell", out entrancePosition) ||
                TryFindEntranceRecordInBoardTable(out entrancePosition))
            {
                return true;
            }
        }

        entrancePosition = GetFallbackEntranceWorldPosition();
        return true;
    }

    private bool TryFindExitRecordInBoardTable(out Vector3 exitPosition)
    {
        exitPosition = Vector3.zero;

        if (boardGenerator == null || boardGenerator.GeneratedCells == null)
        {
            return false;
        }

        foreach (MazeBoardGenerator.MazeCellRecord cell in boardGenerator.GeneratedCells)
        {
            if (cell.CellType == MazeBoardGenerator.MazeCellType.Exit)
            {
                exitPosition = cell.WorldPosition;
                return true;
            }
        }

        return false;
    }

    private bool TryFindEntranceRecordInBoardTable(out Vector3 entrancePosition)
    {
        entrancePosition = Vector3.zero;

        if (boardGenerator == null || boardGenerator.GeneratedCells == null)
        {
            return false;
        }

        foreach (MazeBoardGenerator.MazeCellRecord cell in boardGenerator.GeneratedCells)
        {
            if (cell.CellType == MazeBoardGenerator.MazeCellType.Entrance)
            {
                entrancePosition = cell.WorldPosition;
                return true;
            }
        }

        return false;
    }

    private Vector3 GetFallbackExitWorldPosition()
    {
        int columns = boardGenerator != null ? boardGenerator.Columns : 14;
        int rows = boardGenerator != null ? boardGenerator.Rows : 12;
        float cellSize = boardGenerator != null ? boardGenerator.CellSize : 1f;

        int exitColumn = columns - 1;
        int exitRow = rows / 2;

        float worldX = (exitColumn - (columns - 1) * 0.5f) * cellSize;
        float worldZ = ((rows - 1) * 0.5f - exitRow) * cellSize;

        return new Vector3(worldX, 0f, worldZ);
    }

    private Vector3 GetFallbackEntranceWorldPosition()
    {
        int columns = boardGenerator != null ? boardGenerator.Columns : 14;
        int rows = boardGenerator != null ? boardGenerator.Rows : 12;
        float cellSize = boardGenerator != null ? boardGenerator.CellSize : 1f;

        int entranceColumn = 0;
        int entranceRow = rows / 2;

        float worldX = (entranceColumn - (columns - 1) * 0.5f) * cellSize;
        float worldZ = ((rows - 1) * 0.5f - entranceRow) * cellSize;

        return new Vector3(worldX, 0f, worldZ);
    }

    private bool TryGetGridCellMethodAsWorldPosition(UnityEngine.Object source, string methodName, out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;

        MethodInfo method = source.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);

        if (method == null)
        {
            return false;
        }

        object result = method.Invoke(source, null);
        if (result == null)
        {
            return false;
        }

        if (!TryReadColumnAndRow(result, out int column, out int row))
        {
            return false;
        }

        worldPosition = GridToWorldFromKnownGenerator(column, row);
        return true;
    }

    private Vector3 GridToWorldFromKnownGenerator(int column, int row)
    {
        int columns = 14;
        int rows = 12;
        float cellSize = 1f;

        if (boardGenerator != null)
        {
            columns = boardGenerator.Columns;
            rows = boardGenerator.Rows;
            cellSize = boardGenerator.CellSize;
        }
        else if (combinedMazeGenerator != null)
        {
            TryGetIntPropertyOrField(combinedMazeGenerator, "Columns", ref columns);
            TryGetIntPropertyOrField(combinedMazeGenerator, "Rows", ref rows);
            TryGetFloatPropertyOrField(combinedMazeGenerator, "CellSize", ref cellSize);
        }

        float worldX = (column - (columns - 1) * 0.5f) * cellSize;
        float worldZ = ((rows - 1) * 0.5f - row) * cellSize;

        return new Vector3(worldX, 0f, worldZ);
    }

    private bool TryReadColumnAndRow(object gridCell, out int column, out int row)
    {
        column = 0;
        row = 0;

        Type type = gridCell.GetType();

        FieldInfo columnField = type.GetField("Column", BindingFlags.Instance | BindingFlags.Public);
        FieldInfo rowField = type.GetField("Row", BindingFlags.Instance | BindingFlags.Public);

        if (columnField != null && rowField != null)
        {
            column = Convert.ToInt32(columnField.GetValue(gridCell));
            row = Convert.ToInt32(rowField.GetValue(gridCell));
            return true;
        }

        PropertyInfo columnProperty = type.GetProperty("Column", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo rowProperty = type.GetProperty("Row", BindingFlags.Instance | BindingFlags.Public);

        if (columnProperty != null && rowProperty != null)
        {
            column = Convert.ToInt32(columnProperty.GetValue(gridCell));
            row = Convert.ToInt32(rowProperty.GetValue(gridCell));
            return true;
        }

        return false;
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

        if (wallsGenerator == null)
        {
            wallsGenerator = FindFirstObjectByType<MazeWallsGenerator>();
        }

        if (playerToken == null)
        {
            playerToken = FindFirstObjectByType<PlayerToken>();
        }

        if (playerCamera == null)
        {
            playerCamera = FindFirstObjectByType<PlayerCamera>();
        }

        if (combinedMazeGenerator == null)
        {
            combinedMazeGenerator = FindCombinedMazeGenerator();
        }
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

    private bool TryGetVector3PropertyOrMethod(UnityEngine.Object source, string memberName, out Vector3 value)
    {
        value = Vector3.zero;

        if (source == null)
        {
            return false;
        }

        Type type = source.GetType();

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.PropertyType == typeof(Vector3))
        {
            value = (Vector3)property.GetValue(source);
            return true;
        }

        MethodInfo method = type.GetMethod(
            memberName,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);

        if (method != null && method.ReturnType == typeof(Vector3))
        {
            value = (Vector3)method.Invoke(source, null);
            return true;
        }

        return false;
    }

    private bool TryGetStringPropertyOrField(UnityEngine.Object source, string memberName, out string value)
    {
        value = string.Empty;

        if (source == null)
        {
            return false;
        }

        Type type = source.GetType();

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.PropertyType == typeof(string))
        {
            value = (string)property.GetValue(source);
            return true;
        }

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(string))
        {
            value = (string)field.GetValue(source);
            return true;
        }

        return false;
    }

    private bool TryGetIntPropertyOrField(UnityEngine.Object source, string memberName, ref int value)
    {
        if (source == null)
        {
            return false;
        }

        Type type = source.GetType();

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.PropertyType == typeof(int))
        {
            value = (int)property.GetValue(source);
            return true;
        }

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(int))
        {
            value = (int)field.GetValue(source);
            return true;
        }

        return false;
    }

    private bool TryGetFloatPropertyOrField(UnityEngine.Object source, string memberName, ref float value)
    {
        if (source == null)
        {
            return false;
        }

        Type type = source.GetType();

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.PropertyType == typeof(float))
        {
            value = (float)property.GetValue(source);
            return true;
        }

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(float))
        {
            value = (float)field.GetValue(source);
            return true;
        }

        return false;
    }

    private bool TryInvokePublicMethod(UnityEngine.Object target, string methodName)
    {
        if (target == null)
        {
            return false;
        }

        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);

        if (method == null)
        {
            return false;
        }

        method.Invoke(target, null);
        return true;
    }

    private void UpdateTimerDisplay()
    {
        string text = GetTimerDisplayText();
        TrySetText(timerTextTarget, text);
    }

    private void UpdateMessageDisplay(string message)
    {
        TrySetText(messageTextTarget, message);
    }

    private string GetTimerDisplayText()
    {
        if (currentState == MazeGameState.WaitingForMaze && !showTimerWhileWaiting)
        {
            return "";
        }

        float displayTime = currentState == MazeGameState.Finished ? finalCompletionTime : elapsedTime;
        return timerPrefix + FormatTime(displayTime);
    }

    private string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);

        int minutes = Mathf.FloorToInt(seconds / 60f);
        int wholeSeconds = Mathf.FloorToInt(seconds % 60f);
        int hundredths = Mathf.FloorToInt((seconds - Mathf.Floor(seconds)) * 100f);

        return $"{minutes:00}:{wholeSeconds:00}.{hundredths:00}";
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

    private void SetState(MazeGameState newState)
    {
        if (currentState == newState && status == newState.ToString())
        {
            return;
        }

        currentState = newState;
        status = newState.ToString();

        if (printStateChanges)
        {
            Debug.Log("MazeGameSystem state: " + status);
        }
    }
}