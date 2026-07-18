using System.Collections;
using UnityEngine;

public class MazeGameSystem : MonoBehaviour
{
    private enum GameState
    {
        WaitingForMaze,
        Ready,
        Running,
        Finished
    }

    [Header("Maze References")]
    [SerializeField] private MazeBoardGenerator mazeBoardGenerator;
    [SerializeField] private PlayerToken playerToken;
    [SerializeField] private bool findObjectsAutomatically = true;
    [SerializeField] private float setupWaitTime = 5f;

    [Header("Start and Finish")]
    [SerializeField] private string playerObjectName = "Player Token";
    [SerializeField] private float markerHeight = 0.12f;
    [SerializeField] private float markerSize = 0.7f;
    [SerializeField] private float finishDetectionRadius = 0.55f;
    [SerializeField] private Color startColor = Color.blue;
    [SerializeField] private Color finishColor = Color.black;

    [Header("Timer")]
    [SerializeField] private bool startTimerAutomatically = true;
    [SerializeField] private bool showTimerOnScreen = true;
    [SerializeField] private int timerFontSize = 28;
    [SerializeField] private Color timerTextColor = Color.white;

    [Header("Generated Objects")]
    [SerializeField] private string generatedRootName = "Generated Game System";

    public string Status { get; private set; } = "game system not ready";
    public float ElapsedTime => elapsedTime;

    private GameState gameState = GameState.WaitingForMaze;
    private Transform playerTarget;
    private Vector3 startPosition;
    private Vector3 finishPosition;
    private float elapsedTime;
    private Transform generatedRoot;
    private GUIStyle timerStyle;

    private void Start()
    {
        StartCoroutine(PrepareGameSystem());
    }

    private void Update()
    {
        if (gameState != GameState.Running)
        {
            return;
        }

        elapsedTime += Time.deltaTime;

        if (playerTarget == null)
        {
            FindPlayerTarget();
            return;
        }

        Vector3 playerFlat = new Vector3(playerTarget.position.x, 0f, playerTarget.position.z);
        Vector3 finishFlat = new Vector3(finishPosition.x, 0f, finishPosition.z);

        if (Vector3.Distance(playerFlat, finishFlat) <= finishDetectionRadius)
        {
            FinishGame();
        }
    }

    private void OnGUI()
    {
        if (!showTimerOnScreen)
        {
            return;
        }

        if (timerStyle == null)
        {
            timerStyle = new GUIStyle(GUI.skin.label);
            timerStyle.fontSize = timerFontSize;
            timerStyle.normal.textColor = timerTextColor;
        }

        string text = $"Time: {elapsedTime:0.00}s";

        if (gameState == GameState.Ready)
        {
            text = "Ready";
        }
        else if (gameState == GameState.Finished)
        {
            text = $"Finished: {elapsedTime:0.00}s";
        }
        else if (gameState == GameState.WaitingForMaze)
        {
            text = "Waiting for maze";
        }

        GUI.Label(new Rect(20f, 20f, 420f, 50f), text, timerStyle);
    }

    [ContextMenu("Prepare Game System")]
    public void PrepareNow()
    {
        StopAllCoroutines();
        StartCoroutine(PrepareGameSystem());
    }

    public void StartGame()
    {
        if (gameState == GameState.Finished)
        {
            return;
        }

        elapsedTime = 0f;
        gameState = GameState.Running;
        Status = "game running";
        Debug.Log(Status);
    }

    public void FinishGame()
    {
        if (gameState == GameState.Finished)
        {
            return;
        }

        gameState = GameState.Finished;
        Status = "game finished";
        Debug.Log($"{Status}. Time: {elapsedTime:0.00}s");
    }

    public void ResetTimer()
    {
        elapsedTime = 0f;
        gameState = GameState.Ready;
        Status = "game ready";
        Debug.Log(Status);
    }

    private IEnumerator PrepareGameSystem()
    {
        gameState = GameState.WaitingForMaze;
        Status = "waiting for maze";

        float elapsedWait = 0f;

        while (elapsedWait < setupWaitTime)
        {
            ResolveReferences();

            if (mazeBoardGenerator != null && mazeBoardGenerator.Status == "board ready")
            {
                break;
            }

            elapsedWait += Time.deltaTime;
            yield return null;
        }

        ResolveReferences();

        if (mazeBoardGenerator == null || mazeBoardGenerator.Status != "board ready")
        {
            Status = "maze not ready";
            Debug.LogWarning("MazeGameSystem: MazeBoardGenerator is not ready.");
            yield break;
        }

        ClearGeneratedObjects();
        CalculateStartAndFinishPositions();
        CreateMarker("Start Marker", startPosition, startColor);
        CreateMarker("Finish Marker", finishPosition, finishColor);
        FindPlayerTarget();

        elapsedTime = 0f;
        gameState = GameState.Ready;
        Status = "game ready";
        Debug.Log(Status);

        if (startTimerAutomatically)
        {
            StartGame();
        }
    }

    private void ResolveReferences()
    {
        if (!findObjectsAutomatically)
        {
            return;
        }

        if (mazeBoardGenerator == null)
        {
            mazeBoardGenerator = FindFirstObjectByType<MazeBoardGenerator>();
        }

        if (playerToken == null)
        {
            playerToken = FindFirstObjectByType<PlayerToken>();
        }
    }

    private void FindPlayerTarget()
    {
        GameObject playerObject = GameObject.Find(playerObjectName);

        if (playerObject != null)
        {
            playerTarget = playerObject.transform;
            return;
        }

        if (playerToken != null && playerToken.transform.childCount > 0)
        {
            playerTarget = playerToken.transform.GetChild(0);
        }
    }

    private void CalculateStartAndFinishPositions()
    {
        int maxColumn = 0;
        int maxRow = 0;

        foreach (MazeBoardGenerator.CellRecord record in mazeBoardGenerator.CellTable)
        {
            maxColumn = Mathf.Max(maxColumn, record.column);
            maxRow = Mathf.Max(maxRow, record.row);
        }

        int entranceColumn = 0;
        int exitColumn = maxColumn;
        int middleRow = (maxRow + 1) / 2;

        startPosition = FindCellWorldPosition(entranceColumn, middleRow);
        finishPosition = FindCellWorldPosition(exitColumn, middleRow);
    }

    private Vector3 FindCellWorldPosition(int column, int row)
    {
        foreach (MazeBoardGenerator.CellRecord record in mazeBoardGenerator.CellTable)
        {
            if (record.column == column && record.row == row)
            {
                return new Vector3(record.worldX, markerHeight * 0.5f, record.worldZ);
            }
        }

        return Vector3.zero;
    }

    private void CreateMarker(string markerName, Vector3 position, Color color)
    {
        Transform root = GetGeneratedRoot();
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = markerName;
        marker.transform.SetParent(root, false);
        marker.transform.position = position;
        marker.transform.localScale = new Vector3(markerSize, markerHeight, markerSize);

        Renderer renderer = marker.GetComponent<Renderer>();
        Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = color;
        renderer.sharedMaterial = material;

        Collider markerCollider = marker.GetComponent<Collider>();
        markerCollider.isTrigger = true;
    }

    private Transform GetGeneratedRoot()
    {
        if (generatedRoot != null)
        {
            return generatedRoot;
        }

        GameObject rootObject = new GameObject(generatedRootName);
        rootObject.transform.SetParent(transform, false);
        generatedRoot = rootObject.transform;
        return generatedRoot;
    }

    private void ClearGeneratedObjects()
    {
        Transform existing = transform.Find(generatedRootName);

        if (existing == null)
        {
            generatedRoot = null;
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

        generatedRoot = null;
    }

    private void OnValidate()
    {
        setupWaitTime = Mathf.Max(0f, setupWaitTime);
        markerHeight = Mathf.Max(0.01f, markerHeight);
        markerSize = Mathf.Max(0.1f, markerSize);
        finishDetectionRadius = Mathf.Max(0.05f, finishDetectionRadius);
        timerFontSize = Mathf.Max(8, timerFontSize);
    }
}
