using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform playerTarget;
    [SerializeField] private bool findPlayerAutomatically = true;
    [SerializeField] private string playerObjectName = "Player Token";

    [Header("Camera Position")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 7f, -7f);
    [SerializeField] private bool lookAtPlayer = true;
    [SerializeField] private float lookAtHeight = 0.4f;

    [Header("Movement")]
    [SerializeField] private bool smoothFollow = true;
    [SerializeField] private float followSmoothTime = 0.15f;
    [SerializeField] private float rotationSpeed = 8f;

    [Header("Camera Settings")]
    [SerializeField] private bool setCameraDefaultsOnStart = true;
    [SerializeField] private float fieldOfView = 60f;
    [SerializeField] private float nearClipPlane = 0.3f;
    [SerializeField] private float farClipPlane = 1000f;

    public string Status { get; private set; } = "camera not ready";

    private Camera controlledCamera;
    private Vector3 followVelocity;

    private void Start()
    {
        controlledCamera = GetComponent<Camera>();

        if (controlledCamera == null)
        {
            controlledCamera = Camera.main;
        }

        if (findPlayerAutomatically && playerTarget == null)
        {
            FindPlayerTarget();
        }

        if (setCameraDefaultsOnStart && controlledCamera != null)
        {
            controlledCamera.fieldOfView = fieldOfView;
            controlledCamera.nearClipPlane = nearClipPlane;
            controlledCamera.farClipPlane = farClipPlane;
        }

        if (playerTarget != null)
        {
            transform.position = playerTarget.position + followOffset;
            RotateTowardPlayer(true);
            Status = "camera ready";
            Debug.Log(Status);
        }
        else
        {
            Status = "player target not found";
            Debug.LogWarning("PlayerCamera: player target was not found.");
        }
    }

    private void LateUpdate()
    {
        if (playerTarget == null)
        {
            if (findPlayerAutomatically)
            {
                FindPlayerTarget();
            }

            return;
        }

        Vector3 targetPosition = playerTarget.position + followOffset;

        if (smoothFollow)
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref followVelocity,
                followSmoothTime);
        }
        else
        {
            transform.position = targetPosition;
        }

        RotateTowardPlayer(false);
    }

    private void FindPlayerTarget()
    {
        GameObject playerObject = GameObject.Find(playerObjectName);

        if (playerObject != null)
        {
            playerTarget = playerObject.transform;
            return;
        }

        PlayerToken playerToken = FindFirstObjectByType<PlayerToken>();

        if (playerToken != null && playerToken.transform.childCount > 0)
        {
            playerTarget = playerToken.transform.GetChild(0);
        }
    }

    private void RotateTowardPlayer(bool instant)
    {
        if (!lookAtPlayer || playerTarget == null)
        {
            return;
        }

        Vector3 lookPosition = playerTarget.position + Vector3.up * lookAtHeight;
        Vector3 direction = lookPosition - transform.position;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

        if (instant)
        {
            transform.rotation = targetRotation;
        }
        else
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }
    }

    private void OnValidate()
    {
        followSmoothTime = Mathf.Max(0.01f, followSmoothTime);
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        fieldOfView = Mathf.Clamp(fieldOfView, 1f, 179f);
        nearClipPlane = Mathf.Max(0.01f, nearClipPlane);
        farClipPlane = Mathf.Max(nearClipPlane + 0.01f, farClipPlane);
    }
}
