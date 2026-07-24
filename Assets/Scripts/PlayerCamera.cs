using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class PlayerCamera : MonoBehaviour
{
    [Header("Target Finding")]
    [SerializeField] private Transform playerTarget;
    [SerializeField] private bool findPlayerAutomatically = true;
    [SerializeField, Min(0f)] private float targetSearchTimeout = 8f;
    [SerializeField, Min(0.02f)] private float targetSearchInterval = 0.15f;

    [Header("Follow View")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 7f, -7f);
    [SerializeField] private float lookAtHeight = 0.4f;

    [Header("Follow Smoothing")]
    [SerializeField] private bool smoothFollow = true;
    [SerializeField, Min(0.001f)] private float followSmoothTime = 0.18f;
    [SerializeField] private bool rotationSmoothing = true;
    [SerializeField, Min(0.001f)] private float rotationSmoothTime = 0.12f;

    [Header("Camera Lens")]
    [SerializeField, Range(20f, 90f)] private float fieldOfView = 50f;
    [SerializeField, Min(0.001f)] private float nearClipPlane = 0.03f;
    [SerializeField, Min(1f)] private float farClipPlane = 250f;

    [SerializeField, HideInInspector] private string status = "not ready";

    private Camera controlledCamera;
    private Vector3 followVelocity;
    private Coroutine targetSearchCoroutine;
    private bool warningPrinted;

    public string Status => status;
    public Transform PlayerTarget => playerTarget;

    private void Awake()
    {
        controlledCamera = GetComponent<Camera>();
        ApplyCameraLensSettings();
    }

    private void Start()
    {
        if (playerTarget != null)
        {
            SetCameraReady();
            SnapToTarget();
            return;
        }

        if (findPlayerAutomatically && Application.isPlaying)
        {
            targetSearchCoroutine = StartCoroutine(SearchForTargetWithTimeout());
        }
        else if (findPlayerAutomatically)
        {
            TryFindPlayerTarget();
        }
    }

    private void OnEnable()
    {
        controlledCamera = GetComponent<Camera>();
        ApplyCameraLensSettings();
    }

    private void OnValidate()
    {
        targetSearchTimeout = Mathf.Max(0f, targetSearchTimeout);
        targetSearchInterval = Mathf.Max(0.02f, targetSearchInterval);
        followSmoothTime = Mathf.Max(0.001f, followSmoothTime);
        rotationSmoothTime = Mathf.Max(0.001f, rotationSmoothTime);
        nearClipPlane = Mathf.Max(0.001f, nearClipPlane);
        farClipPlane = Mathf.Max(nearClipPlane + 1f, farClipPlane);

        if (controlledCamera == null)
        {
            controlledCamera = GetComponent<Camera>();
        }

        ApplyCameraLensSettings();
    }

    private void LateUpdate()
    {
        ApplyCameraLensSettings();

        if (playerTarget == null)
        {
            if (findPlayerAutomatically && !Application.isPlaying)
            {
                TryFindPlayerTarget();
            }

            return;
        }

        FollowTarget();
        RotateTowardTarget();
    }

    [ContextMenu("Find Player Target")]
    public void FindPlayerTargetNow()
    {
        if (TryFindPlayerTarget())
        {
            SetCameraReady();
            SnapToTarget();
        }
    }

    [ContextMenu("Snap To Target")]
    public void SnapToTarget()
    {
        if (playerTarget == null)
        {
            return;
        }

        Vector3 desiredPosition = GetDesiredCameraPosition();
        transform.position = desiredPosition;
        transform.rotation = GetDesiredCameraRotation();
        followVelocity = Vector3.zero;
    }

    public void SetPlayerTarget(Transform target)
    {
        playerTarget = target;

        if (playerTarget != null)
        {
            SetCameraReady();
            SnapToTarget();
        }
        else
        {
            status = "not ready";
        }
    }

    private IEnumerator SearchForTargetWithTimeout()
    {
        float elapsed = 0f;

        while (playerTarget == null && elapsed <= targetSearchTimeout)
        {
            if (TryFindPlayerTarget())
            {
                SetCameraReady();
                SnapToTarget();
                yield break;
            }

            yield return new WaitForSeconds(targetSearchInterval);
            elapsed += targetSearchInterval;
        }

        if (playerTarget == null && !warningPrinted)
        {
            warningPrinted = true;
            Debug.LogWarning("PlayerCamera could not find a player target after the configured search timeout.");
        }
    }

    private bool TryFindPlayerTarget()
    {
        if (playerTarget != null)
        {
            return true;
        }

        PlayerToken playerToken = FindFirstObjectByType<PlayerToken>();
        if (playerToken != null && playerToken.PlayerTransform != null)
        {
            playerTarget = playerToken.PlayerTransform;
            return true;
        }

        GameObject playerTokenObjectWithSpace = GameObject.Find("Player Token");
        if (playerTokenObjectWithSpace != null)
        {
            playerTarget = playerTokenObjectWithSpace.transform;
            return true;
        }

        GameObject playerTokenObjectNoSpace = GameObject.Find("PlayerToken");
        if (playerTokenObjectNoSpace != null)
        {
            playerTarget = playerTokenObjectNoSpace.transform;
            return true;
        }

        return false;
    }

    private void FollowTarget()
    {
        Vector3 desiredPosition = GetDesiredCameraPosition();

        if (smoothFollow)
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPosition,
                ref followVelocity,
                followSmoothTime);
        }
        else
        {
            transform.position = desiredPosition;
            followVelocity = Vector3.zero;
        }
    }

    private void RotateTowardTarget()
    {
        Quaternion desiredRotation = GetDesiredCameraRotation();

        if (rotationSmoothing)
        {
            float smoothingFactor = 1f - Mathf.Exp(-Time.deltaTime / rotationSmoothTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, smoothingFactor);
        }
        else
        {
            transform.rotation = desiredRotation;
        }
    }

    private Vector3 GetDesiredCameraPosition()
    {
        return playerTarget.position + followOffset;
    }

    private Quaternion GetDesiredCameraRotation()
    {
        Vector3 lookTarget = playerTarget.position + Vector3.up * lookAtHeight;
        Vector3 lookDirection = lookTarget - transform.position;

        if (lookDirection.sqrMagnitude <= 0.0001f)
        {
            return transform.rotation;
        }

        return Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
    }

    private void ApplyCameraLensSettings()
    {
        if (controlledCamera == null)
        {
            return;
        }

        controlledCamera.fieldOfView = fieldOfView;
        controlledCamera.nearClipPlane = nearClipPlane;
        controlledCamera.farClipPlane = farClipPlane;
    }

    private void SetCameraReady()
    {
        if (status == "camera ready")
        {
            return;
        }

        status = "camera ready";
        Debug.Log("camera ready");
    }
}