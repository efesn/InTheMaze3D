using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MazeBestTimeManager : MonoBehaviour
{
    [Header("Script References")]
    [SerializeField] private MazeGameSystem gameSystem;
    [SerializeField] private bool autoFindGameSystem = true;

    [Header("PlayerPrefs Settings")]
    [SerializeField] private string bestTimeKey = "InTheMaze_BestCompletionTime";
    [SerializeField] private bool includeDifficultyInKey = false;
    [SerializeField] private MazeDifficultySettings difficultySettings;
    [SerializeField] private bool autoFindDifficultySettings = true;

    [Header("UI Settings")]
    [SerializeField] private Object bestTimeTextTarget;
    [SerializeField] private Object messageTextTarget;
    [SerializeField] private bool useOnGUIFallback = true;
    [SerializeField] private Rect onGuiBestTimeRect = new Rect(16f, 238f, 520f, 40f);
    [SerializeField] private Rect onGuiMessageRect = new Rect(16f, 278f, 620f, 44f);
    [SerializeField, Min(8)] private int onGuiFontSize = 22;
    [SerializeField] private Color onGuiTextColor = Color.white;

    [Header("Display Text")]
    [SerializeField] private string noBestTimeText = "Best Time: --:--.--";
    [SerializeField] private string bestTimePrefix = "Best Time: ";
    [SerializeField] private string newBestMessage = "New Best Time!";
    [SerializeField] private string currentRunMessagePrefix = "Finished: ";

    [Header("Behavior")]
    [SerializeField] private bool loadBestTimeOnStart = true;
    [SerializeField] private bool updateDisplayContinuously = false;
    [SerializeField] private bool clearMessageOnRestart = true;

    [Header("Testing")]
    [SerializeField] private bool resetBestTimeNow = false;

    [Header("Debug")]
    [SerializeField] private bool printStatusMessages = true;
    [SerializeField] private bool printWarnings = true;

    [SerializeField, HideInInspector] private string status = "not ready";
    [SerializeField, HideInInspector] private float bestTimeSeconds = -1f;
    [SerializeField, HideInInspector] private bool hasBestTime = false;
    [SerializeField, HideInInspector] private bool isNewBestTime = false;

    private MazeGameSystem.MazeGameState lastGameState;
    private GUIStyle onGuiStyle;

    public string Status => status;
    public bool HasBestTime => hasBestTime;
    public bool IsNewBestTime => isNewBestTime;
    public float BestTimeSeconds => bestTimeSeconds;
    public string ActiveBestTimeKey => GetActiveBestTimeKey();

    private void Awake()
    {
        ResolveReferences();

        if (gameSystem != null)
        {
            lastGameState = gameSystem.CurrentState;
        }

        if (loadBestTimeOnStart)
        {
            LoadBestTime();
        }

        UpdateBestTimeDisplay();
    }

    private void Update()
    {
        ResolveReferences();

        if (resetBestTimeNow)
        {
            resetBestTimeNow = false;
            ResetBestTime();
        }

        ObserveGameState();

        if (updateDisplayContinuously)
        {
            UpdateBestTimeDisplay();
        }
    }

    private void OnValidate()
    {
        onGuiFontSize = Mathf.Max(8, onGuiFontSize);
        if (string.IsNullOrWhiteSpace(bestTimeKey))
        {
            bestTimeKey = "InTheMaze_BestCompletionTime";
        }
    }

    private void OnGUI()
    {
        if (!useOnGUIFallback)
        {
            return;
        }

        bool hasBestText = IsTextTargetUsable(bestTimeTextTarget);
        bool hasMessageText = IsTextTargetUsable(messageTextTarget);

        if (hasBestText && hasMessageText)
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

        if (!hasBestText)
        {
            GUI.Label(onGuiBestTimeRect, GetBestTimeDisplayText(), onGuiStyle);
        }

        if (!hasMessageText && !string.IsNullOrEmpty(GetMessageDisplayText()))
        {
            GUI.Label(onGuiMessageRect, GetMessageDisplayText(), onGuiStyle);
        }
    }

    [ContextMenu("Load Best Time")]
    public void LoadBestTime()
    {
        string key = GetActiveBestTimeKey();

        if (PlayerPrefs.HasKey(key))
        {
            bestTimeSeconds = PlayerPrefs.GetFloat(key, -1f);
            hasBestTime = bestTimeSeconds > 0f;
            status = hasBestTime ? "best time loaded" : "no best time";
        }
        else
        {
            bestTimeSeconds = -1f;
            hasBestTime = false;
            status = "no best time";
        }

        isNewBestTime = false;
        UpdateBestTimeDisplay();

        if (printStatusMessages)
        {
            Debug.Log(hasBestTime
                ? "MazeBestTimeManager loaded best time: " + FormatTime(bestTimeSeconds)
                : "MazeBestTimeManager found no saved best time.");
        }
    }

    [ContextMenu("Save Current Time As Best")]
    public void SaveCurrentTimeAsBest()
    {
        if (gameSystem == null)
        {
            if (printWarnings)
            {
                Debug.LogWarning("MazeBestTimeManager could not save because MazeGameSystem was not found.");
            }

            return;
        }

        SaveBestTime(gameSystem.FinalCompletionTime);
    }

    [ContextMenu("Reset Best Time")]
    public void ResetBestTime()
    {
        string key = GetActiveBestTimeKey();

        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();

        bestTimeSeconds = -1f;
        hasBestTime = false;
        isNewBestTime = false;
        status = "best time reset";

        UpdateBestTimeDisplay();
        UpdateMessageDisplay("");

        if (printStatusMessages)
        {
            Debug.Log("MazeBestTimeManager reset best time for key: " + key);
        }
    }

    public void SaveBestTime(float completionTimeSeconds)
    {
        if (completionTimeSeconds <= 0f)
        {
            return;
        }

        string key = GetActiveBestTimeKey();

        bestTimeSeconds = completionTimeSeconds;
        hasBestTime = true;
        isNewBestTime = true;

        PlayerPrefs.SetFloat(key, bestTimeSeconds);
        PlayerPrefs.Save();

        status = "new best time";

        UpdateBestTimeDisplay();
        UpdateMessageDisplay(newBestMessage);

        if (printStatusMessages)
        {
            Debug.Log("MazeBestTimeManager saved new best time: " + FormatTime(bestTimeSeconds));
        }
    }

    public bool TrySubmitCompletionTime(float completionTimeSeconds)
    {
        if (completionTimeSeconds <= 0f)
        {
            return false;
        }

        if (!hasBestTime || completionTimeSeconds < bestTimeSeconds)
        {
            SaveBestTime(completionTimeSeconds);
            return true;
        }

        isNewBestTime = false;
        status = "finished without new best";

        UpdateBestTimeDisplay();
        UpdateMessageDisplay(currentRunMessagePrefix + FormatTime(completionTimeSeconds));

        if (printStatusMessages)
        {
            Debug.Log("MazeBestTimeManager completion time was not a new best: " + FormatTime(completionTimeSeconds));
        }

        return false;
    }

    private void ObserveGameState()
    {
        if (gameSystem == null)
        {
            return;
        }

        MazeGameSystem.MazeGameState currentState = gameSystem.CurrentState;

        if (currentState == lastGameState)
        {
            return;
        }

        if (clearMessageOnRestart &&
            (currentState == MazeGameSystem.MazeGameState.Restarting ||
             currentState == MazeGameSystem.MazeGameState.WaitingForMaze ||
             currentState == MazeGameSystem.MazeGameState.ReadyToStart ||
             currentState == MazeGameSystem.MazeGameState.Playing))
        {
            isNewBestTime = false;
            UpdateMessageDisplay("");
        }

        if (currentState == MazeGameSystem.MazeGameState.Finished)
        {
            TrySubmitCompletionTime(gameSystem.FinalCompletionTime);
        }

        lastGameState = currentState;
    }

    private void ResolveReferences()
    {
        if (!autoFindGameSystem)
        {
            return;
        }

        if (gameSystem == null)
        {
            gameSystem = FindFirstObjectByType<MazeGameSystem>();
        }

        if (difficultySettings == null && autoFindDifficultySettings)
        {
            difficultySettings = FindFirstObjectByType<MazeDifficultySettings>();
        }
    }

    private string GetActiveBestTimeKey()
    {
        if (!includeDifficultyInKey || difficultySettings == null)
        {
            return bestTimeKey;
        }

        return bestTimeKey + "_" + difficultySettings.SelectedDifficulty;
    }

    private void UpdateBestTimeDisplay()
    {
        TrySetText(bestTimeTextTarget, GetBestTimeDisplayText());
    }

    private void UpdateMessageDisplay(string message)
    {
        TrySetText(messageTextTarget, message);
    }

    private string GetBestTimeDisplayText()
    {
        if (!hasBestTime)
        {
            return noBestTimeText;
        }

        return bestTimePrefix + FormatTime(bestTimeSeconds);
    }

    private string GetMessageDisplayText()
    {
        if (isNewBestTime)
        {
            return newBestMessage;
        }

        return "";
    }

    private string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);

        int minutes = Mathf.FloorToInt(seconds / 60f);
        int wholeSeconds = Mathf.FloorToInt(seconds % 60f);
        int hundredths = Mathf.FloorToInt((seconds - Mathf.Floor(seconds)) * 100f);

        return $"{minutes:00}:{wholeSeconds:00}.{hundredths:00}";
    }

    private bool IsTextTargetUsable(Object target)
    {
        if (target == null)
        {
            return false;
        }

        System.Type type = target.GetType();
        System.Reflection.PropertyInfo textProperty = type.GetProperty(
            "text",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

        return textProperty != null && textProperty.PropertyType == typeof(string);
    }

    private bool TrySetText(Object target, string text)
    {
        if (target == null)
        {
            return false;
        }

        System.Type type = target.GetType();
        System.Reflection.PropertyInfo textProperty = type.GetProperty(
            "text",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

        if (textProperty == null || textProperty.PropertyType != typeof(string) || !textProperty.CanWrite)
        {
            return false;
        }

        textProperty.SetValue(target, text);
        return true;
    }
}