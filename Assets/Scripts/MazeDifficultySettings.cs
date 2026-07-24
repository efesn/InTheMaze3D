using System;
using UnityEngine;

[DisallowMultipleComponent]
public class MazeDifficultySettings : MonoBehaviour
{
    public enum DifficultyLevel
    {
        Easy,
        Normal,
        Hard
    }

    public enum TimerMode
    {
        CountUp,
        CountDown
    }

    [Serializable]
    public sealed class DifficultyPreset
    {
        [Header("Preset Identity")]
        [SerializeField] private string presetName = "Normal";
        [SerializeField, TextArea(2, 4)] private string description = "Balanced maze difficulty.";

        [Header("Board Size")]
        [SerializeField, Min(4)] private int columns = 14;
        [SerializeField, Min(4)] private int rows = 12;

        [Header("Path Generation")]
        [SerializeField, Min(0)] private int waypointCount = 5;
        [SerializeField, Min(0)] private int falsePathCount = 5;
        [SerializeField, Range(0.05f, 1f)] private float targetPathCoverage = 0.42f;
        [SerializeField, Min(1)] private int falsePathMinLength = 3;
        [SerializeField, Min(1)] private int falsePathMaxLength = 9;

        [Header("Wall Generation")]
        [SerializeField] private bool removeNonCriticalWalls = false;
        [SerializeField, Range(0f, 0.2f)] private float wallRemovalPercentage = 0f;

        [Header("Player Movement")]
        [SerializeField, Min(0.1f)] private float playerSpeed = 4f;
        [SerializeField, Min(0f)] private float playerAcceleration = 24f;
        [SerializeField, Min(0f)] private float playerDrag = 1.5f;

        [Header("Timer Behavior")]
        [SerializeField] private TimerMode timerMode = TimerMode.CountUp;
        [SerializeField, Min(1f)] private float timeLimitSeconds = 180f;
        [SerializeField] private bool startAutomatically = false;
        [SerializeField] private bool allowRestart = true;

        [Header("Scoring")]
        [SerializeField, Min(0f)] private float scoreTimePenaltyPerSecond = 35f;
        [SerializeField, Min(0f)] private float scoreRouteInefficiencyPenalty = 2500f;

        public string PresetName => presetName;
        public string Description => description;

        public int Columns => columns;
        public int Rows => rows;

        public int WaypointCount => waypointCount;
        public int FalsePathCount => falsePathCount;
        public float TargetPathCoverage => targetPathCoverage;
        public int FalsePathMinLength => falsePathMinLength;
        public int FalsePathMaxLength => falsePathMaxLength;

        public bool RemoveNonCriticalWalls => removeNonCriticalWalls;
        public float WallRemovalPercentage => wallRemovalPercentage;

        public float PlayerSpeed => playerSpeed;
        public float PlayerAcceleration => playerAcceleration;
        public float PlayerDrag => playerDrag;

        public TimerMode TimerMode => timerMode;
        public float TimeLimitSeconds => timeLimitSeconds;
        public bool StartAutomatically => startAutomatically;
        public bool AllowRestart => allowRestart;

        public float ScoreTimePenaltyPerSecond => scoreTimePenaltyPerSecond;
        public float ScoreRouteInefficiencyPenalty => scoreRouteInefficiencyPenalty;

        public void ClampValues()
        {
            columns = Mathf.Max(4, columns);
            rows = Mathf.Max(4, rows);
            waypointCount = Mathf.Max(0, waypointCount);
            falsePathCount = Mathf.Max(0, falsePathCount);
            targetPathCoverage = Mathf.Clamp(targetPathCoverage, 0.05f, 1f);
            falsePathMinLength = Mathf.Max(1, falsePathMinLength);
            falsePathMaxLength = Mathf.Max(falsePathMinLength, falsePathMaxLength);
            wallRemovalPercentage = Mathf.Clamp(wallRemovalPercentage, 0f, 0.2f);
            playerSpeed = Mathf.Max(0.1f, playerSpeed);
            playerAcceleration = Mathf.Max(0f, playerAcceleration);
            playerDrag = Mathf.Max(0f, playerDrag);
            timeLimitSeconds = Mathf.Max(1f, timeLimitSeconds);
            scoreTimePenaltyPerSecond = Mathf.Max(0f, scoreTimePenaltyPerSecond);
            scoreRouteInefficiencyPenalty = Mathf.Max(0f, scoreRouteInefficiencyPenalty);
        }

        public void SetIdentity(string newName, string newDescription)
        {
            presetName = newName;
            description = newDescription;
        }

        public void SetBoardSize(int newColumns, int newRows)
        {
            columns = Mathf.Max(4, newColumns);
            rows = Mathf.Max(4, newRows);
        }

        public void SetPathGeneration(
            int newWaypointCount,
            int newFalsePathCount,
            float newTargetPathCoverage,
            int newFalsePathMinLength,
            int newFalsePathMaxLength)
        {
            waypointCount = Mathf.Max(0, newWaypointCount);
            falsePathCount = Mathf.Max(0, newFalsePathCount);
            targetPathCoverage = Mathf.Clamp(newTargetPathCoverage, 0.05f, 1f);
            falsePathMinLength = Mathf.Max(1, newFalsePathMinLength);
            falsePathMaxLength = Mathf.Max(falsePathMinLength, newFalsePathMaxLength);
        }

        public void SetWallGeneration(bool shouldRemoveNonCriticalWalls, float newWallRemovalPercentage)
        {
            removeNonCriticalWalls = shouldRemoveNonCriticalWalls;
            wallRemovalPercentage = Mathf.Clamp(newWallRemovalPercentage, 0f, 0.2f);
        }

        public void SetPlayerMovement(float newSpeed, float newAcceleration, float newDrag)
        {
            playerSpeed = Mathf.Max(0.1f, newSpeed);
            playerAcceleration = Mathf.Max(0f, newAcceleration);
            playerDrag = Mathf.Max(0f, newDrag);
        }

        public void SetTimerBehavior(
            TimerMode newTimerMode,
            float newTimeLimitSeconds,
            bool newStartAutomatically,
            bool newAllowRestart)
        {
            timerMode = newTimerMode;
            timeLimitSeconds = Mathf.Max(1f, newTimeLimitSeconds);
            startAutomatically = newStartAutomatically;
            allowRestart = newAllowRestart;
        }

        public void SetScoring(float newTimePenaltyPerSecond, float newRouteInefficiencyPenalty)
        {
            scoreTimePenaltyPerSecond = Mathf.Max(0f, newTimePenaltyPerSecond);
            scoreRouteInefficiencyPenalty = Mathf.Max(0f, newRouteInefficiencyPenalty);
        }
    }

    [Header("Selected Difficulty")]
    [SerializeField] private DifficultyLevel selectedDifficulty = DifficultyLevel.Normal;
    [SerializeField] private bool applySelectedPresetOnStart = true;
    [SerializeField] private bool logAppliedDifficulty = true;

    [Header("Easy Preset")]
    [SerializeField] private DifficultyPreset easyPreset = new DifficultyPreset();

    [Header("Normal Preset")]
    [SerializeField] private DifficultyPreset normalPreset = new DifficultyPreset();

    [Header("Hard Preset")]
    [SerializeField] private DifficultyPreset hardPreset = new DifficultyPreset();

    [Header("Runtime Values")]
    [SerializeField, HideInInspector] private string status = "not applied";
    [SerializeField, HideInInspector] private string activePresetName = "Normal";
    [SerializeField, HideInInspector] private int activeColumns = 14;
    [SerializeField, HideInInspector] private int activeRows = 12;
    [SerializeField, HideInInspector] private int activeWaypointCount = 5;
    [SerializeField, HideInInspector] private int activeFalsePathCount = 5;
    [SerializeField, HideInInspector] private float activeTargetPathCoverage = 0.42f;
    [SerializeField, HideInInspector] private int activeFalsePathMinLength = 3;
    [SerializeField, HideInInspector] private int activeFalsePathMaxLength = 9;
    [SerializeField, HideInInspector] private bool activeRemoveNonCriticalWalls = false;
    [SerializeField, HideInInspector] private float activeWallRemovalPercentage = 0f;
    [SerializeField, HideInInspector] private float activePlayerSpeed = 4f;
    [SerializeField, HideInInspector] private float activePlayerAcceleration = 24f;
    [SerializeField, HideInInspector] private float activePlayerDrag = 1.5f;
    [SerializeField, HideInInspector] private TimerMode activeTimerMode = TimerMode.CountUp;
    [SerializeField, HideInInspector] private float activeTimeLimitSeconds = 180f;
    [SerializeField, HideInInspector] private bool activeStartAutomatically = false;
    [SerializeField, HideInInspector] private bool activeAllowRestart = true;
    [SerializeField, HideInInspector] private float activeScoreTimePenaltyPerSecond = 35f;
    [SerializeField, HideInInspector] private float activeScoreRouteInefficiencyPenalty = 2500f;

    public DifficultyLevel SelectedDifficulty => selectedDifficulty;
    public DifficultyPreset ActivePreset => GetSelectedPreset();
    public string Status => status;
    public string ActivePresetName => activePresetName;

    public int Columns => activeColumns;
    public int Rows => activeRows;

    public int WaypointCount => activeWaypointCount;
    public int FalsePathCount => activeFalsePathCount;
    public float TargetPathCoverage => activeTargetPathCoverage;
    public int FalsePathMinLength => activeFalsePathMinLength;
    public int FalsePathMaxLength => activeFalsePathMaxLength;

    public bool RemoveNonCriticalWalls => activeRemoveNonCriticalWalls;
    public float WallRemovalPercentage => activeWallRemovalPercentage;

    public float PlayerSpeed => activePlayerSpeed;
    public float PlayerAcceleration => activePlayerAcceleration;
    public float PlayerDrag => activePlayerDrag;

    public TimerMode ActiveTimerMode => activeTimerMode;
    public float TimeLimitSeconds => activeTimeLimitSeconds;
    public bool StartAutomatically => activeStartAutomatically;
    public bool AllowRestart => activeAllowRestart;

    public float ScoreTimePenaltyPerSecond => activeScoreTimePenaltyPerSecond;
    public float ScoreRouteInefficiencyPenalty => activeScoreRouteInefficiencyPenalty;

    private void Reset()
    {
        SetDefaultPresets();
        ApplySelectedPreset();
    }

    private void Awake()
    {
        EnsurePresetDefaults();
        ClampAllPresets();

        if (applySelectedPresetOnStart)
        {
            ApplySelectedPreset();
        }
    }

    private void OnValidate()
    {
        EnsurePresetDefaults();
        ClampAllPresets();
        ApplySelectedPresetSilently();
    }

    [ContextMenu("Apply Selected Preset")]
    public void ApplySelectedPreset()
    {
        DifficultyPreset preset = GetSelectedPreset();
        preset.ClampValues();

        activePresetName = preset.PresetName;
        activeColumns = preset.Columns;
        activeRows = preset.Rows;
        activeWaypointCount = preset.WaypointCount;
        activeFalsePathCount = preset.FalsePathCount;
        activeTargetPathCoverage = preset.TargetPathCoverage;
        activeFalsePathMinLength = preset.FalsePathMinLength;
        activeFalsePathMaxLength = preset.FalsePathMaxLength;
        activeRemoveNonCriticalWalls = preset.RemoveNonCriticalWalls;
        activeWallRemovalPercentage = preset.WallRemovalPercentage;
        activePlayerSpeed = preset.PlayerSpeed;
        activePlayerAcceleration = preset.PlayerAcceleration;
        activePlayerDrag = preset.PlayerDrag;
        activeTimerMode = preset.TimerMode;
        activeTimeLimitSeconds = preset.TimeLimitSeconds;
        activeStartAutomatically = preset.StartAutomatically;
        activeAllowRestart = preset.AllowRestart;
        activeScoreTimePenaltyPerSecond = preset.ScoreTimePenaltyPerSecond;
        activeScoreRouteInefficiencyPenalty = preset.ScoreRouteInefficiencyPenalty;

        status = "difficulty applied";

        if (logAppliedDifficulty)
        {
            Debug.Log("Maze difficulty applied: " + activePresetName);
        }
    }

    public void SetDifficulty(DifficultyLevel difficulty)
    {
        selectedDifficulty = difficulty;
        ApplySelectedPreset();
    }

    public void SetDifficultyByName(string difficultyName)
    {
        if (string.IsNullOrWhiteSpace(difficultyName))
        {
            return;
        }

        string normalized = difficultyName.Trim().ToLowerInvariant();

        if (normalized == "easy")
        {
            SetDifficulty(DifficultyLevel.Easy);
        }
        else if (normalized == "normal")
        {
            SetDifficulty(DifficultyLevel.Normal);
        }
        else if (normalized == "hard")
        {
            SetDifficulty(DifficultyLevel.Hard);
        }
    }

    public DifficultyPreset GetPreset(DifficultyLevel difficulty)
    {
        switch (difficulty)
        {
            case DifficultyLevel.Easy:
                return easyPreset;
            case DifficultyLevel.Hard:
                return hardPreset;
            default:
                return normalPreset;
        }
    }

    public bool TryGetBoardSize(out int columnsValue, out int rowsValue)
    {
        columnsValue = activeColumns;
        rowsValue = activeRows;
        return status == "difficulty applied";
    }

    public bool TryGetPathGenerationSettings(
        out int waypointCountValue,
        out int falsePathCountValue,
        out float targetPathCoverageValue,
        out int falsePathMinLengthValue,
        out int falsePathMaxLengthValue)
    {
        waypointCountValue = activeWaypointCount;
        falsePathCountValue = activeFalsePathCount;
        targetPathCoverageValue = activeTargetPathCoverage;
        falsePathMinLengthValue = activeFalsePathMinLength;
        falsePathMaxLengthValue = activeFalsePathMaxLength;
        return status == "difficulty applied";
    }

    public bool TryGetWallSettings(out bool removeNonCriticalWallsValue, out float wallRemovalPercentageValue)
    {
        removeNonCriticalWallsValue = activeRemoveNonCriticalWalls;
        wallRemovalPercentageValue = activeWallRemovalPercentage;
        return status == "difficulty applied";
    }

    public bool TryGetPlayerSettings(out float speedValue, out float accelerationValue, out float dragValue)
    {
        speedValue = activePlayerSpeed;
        accelerationValue = activePlayerAcceleration;
        dragValue = activePlayerDrag;
        return status == "difficulty applied";
    }

    public bool TryGetTimerSettings(
        out TimerMode timerModeValue,
        out float timeLimitSecondsValue,
        out bool startAutomaticallyValue,
        out bool allowRestartValue)
    {
        timerModeValue = activeTimerMode;
        timeLimitSecondsValue = activeTimeLimitSeconds;
        startAutomaticallyValue = activeStartAutomatically;
        allowRestartValue = activeAllowRestart;
        return status == "difficulty applied";
    }

    public bool TryGetScoringSettings(
        out float scoreTimePenaltyPerSecondValue,
        out float scoreRouteInefficiencyPenaltyValue)
    {
        scoreTimePenaltyPerSecondValue = activeScoreTimePenaltyPerSecond;
        scoreRouteInefficiencyPenaltyValue = activeScoreRouteInefficiencyPenalty;
        return status == "difficulty applied";
    }

    private DifficultyPreset GetSelectedPreset()
    {
        switch (selectedDifficulty)
        {
            case DifficultyLevel.Easy:
                return easyPreset;
            case DifficultyLevel.Hard:
                return hardPreset;
            default:
                return normalPreset;
        }
    }

    private void ApplySelectedPresetSilently()
    {
        bool previousLogValue = logAppliedDifficulty;
        logAppliedDifficulty = false;
        ApplySelectedPreset();
        logAppliedDifficulty = previousLogValue;
    }

    private void EnsurePresetDefaults()
    {
        if (easyPreset == null)
        {
            easyPreset = new DifficultyPreset();
        }

        if (normalPreset == null)
        {
            normalPreset = new DifficultyPreset();
        }

        if (hardPreset == null)
        {
            hardPreset = new DifficultyPreset();
        }

        bool easyLooksDefault = easyPreset.PresetName == "Normal" && easyPreset.Columns == 14 && easyPreset.WaypointCount == 5;
        bool normalLooksDefault = normalPreset.PresetName == "Normal" && normalPreset.Columns == 14 && normalPreset.WaypointCount == 5;
        bool hardLooksDefault = hardPreset.PresetName == "Normal" && hardPreset.Columns == 14 && hardPreset.WaypointCount == 5;

        if (easyLooksDefault && normalLooksDefault && hardLooksDefault)
        {
            SetDefaultPresets();
        }
    }

    private void SetDefaultPresets()
    {
        easyPreset.SetIdentity("Easy", "Smaller, simpler maze with fewer branches and a relaxed timer.");
        easyPreset.SetBoardSize(10, 8);
        easyPreset.SetPathGeneration(3, 2, 0.28f, 2, 5);
        easyPreset.SetWallGeneration(false, 0f);
        easyPreset.SetPlayerMovement(3.5f, 22f, 1.8f);
        easyPreset.SetTimerBehavior(TimerMode.CountUp, 240f, false, true);
        easyPreset.SetScoring(25f, 1600f);

        normalPreset.SetIdentity("Normal", "Balanced default maze for the intended project experience.");
        normalPreset.SetBoardSize(14, 12);
        normalPreset.SetPathGeneration(5, 5, 0.42f, 3, 9);
        normalPreset.SetWallGeneration(false, 0f);
        normalPreset.SetPlayerMovement(4f, 24f, 1.5f);
        normalPreset.SetTimerBehavior(TimerMode.CountUp, 180f, false, true);
        normalPreset.SetScoring(35f, 2500f);

        hardPreset.SetIdentity("Hard", "Larger and denser maze with more misleading branches and stricter scoring.");
        hardPreset.SetBoardSize(18, 14);
        hardPreset.SetPathGeneration(8, 9, 0.58f, 5, 13);
        hardPreset.SetWallGeneration(false, 0f);
        hardPreset.SetPlayerMovement(4.4f, 28f, 1.25f);
        hardPreset.SetTimerBehavior(TimerMode.CountUp, 120f, false, true);
        hardPreset.SetScoring(50f, 3600f);
    }

    private void ClampAllPresets()
    {
        easyPreset.ClampValues();
        normalPreset.ClampValues();
        hardPreset.ClampValues();
    }
}