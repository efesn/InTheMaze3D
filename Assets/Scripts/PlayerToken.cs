using System;
using System.Reflection;
using UnityEngine;

[ExecuteAlways]
public class PlayerToken : MonoBehaviour
{
    private enum MovementMode
    {
        SetVelocity,
        Accelerate
    }

    [Header("References")]
    [SerializeField] private MazeBoardGenerator boardSource;
    [SerializeField] private MazeWallsGenerator wallsSource;
    [SerializeField] private bool autoFindBoardSource = true;
    [SerializeField] private bool autoFindWallsSource = true;
    [SerializeField] private bool createOnStart = true;

    [Header("Token Geometry")]
    [SerializeField] private string tokenObjectName = "Player Token";
    [SerializeField, Min(0.05f)] private float tokenDiameter = 0.6f;
    [SerializeField] private float tokenCenterY = 0.6f;

    [Header("Input")]
    [SerializeField] private KeyCode upKey = KeyCode.UpArrow;
    [SerializeField] private KeyCode downKey = KeyCode.DownArrow;
    [SerializeField] private KeyCode leftKey = KeyCode.LeftArrow;
    [SerializeField] private KeyCode rightKey = KeyCode.RightArrow;
    [SerializeField] private bool inputEnabled = true;

    [Header("Movement")]
    [SerializeField] private MovementMode movementMode = MovementMode.SetVelocity;
    [SerializeField, Min(0f)] private float movementSpeed = 4f;
    [SerializeField, Min(0f)] private float acceleration = 24f;
    [SerializeField] private bool normalizeDiagonalInput = true;

    [Header("Physics")]
    [SerializeField, Min(0f)] private float linearDamping = 1.5f;
    [SerializeField, Min(0f)] private float angularDamping = 4f;
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.9f;
    [SerializeField, Range(0f, 1f)] private float dynamicFriction = 0.2f;
    [SerializeField, Range(0f, 1f)] private float staticFriction = 0.2f;
    [SerializeField] private PhysicsMaterialCombine frictionCombine = PhysicsMaterialCombine.Average;
    [SerializeField] private PhysicsMaterialCombine bounceCombine = PhysicsMaterialCombine.Maximum;
    [SerializeField] private CollisionDetectionMode collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    [SerializeField] private RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate;
    [SerializeField] private bool freezeRotation = true;

    [Header("Material / Texture Settings")]
    [SerializeField] private Texture2D tokenTexture;
    [SerializeField] private bool generateFallbackCircleTexture = true;
    [SerializeField, Min(32)] private int generatedTextureSize = 256;
    [SerializeField] private Color tokenBaseColor = Color.white;
    [SerializeField] private Color circlePatternColor = Color.black;
    [SerializeField, Range(0.005f, 0.08f)] private float circleLineWidth = 0.018f;
    [SerializeField, Range(0.15f, 0.9f)] private float circleRadius = 0.32f;
    [SerializeField, Range(0f, 1f)] private float smoothness = 0.35f;

    [SerializeField, HideInInspector] private string status = "not generated";

    private Transform playerTransform;
    private Rigidbody tokenRigidbody;
    private SphereCollider tokenCollider;
    private Material tokenMaterial;
    private PhysicsMaterial tokenPhysicsMaterial;
    private Vector3 inputDirection;

    public Transform PlayerTransform => playerTransform;
    public string Status => status;

    private void Start()
    {
        if (Application.isPlaying && createOnStart)
        {
            CreateToken();
        }
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        inputDirection = ReadArrowKeyInput();
    }

    private void FixedUpdate()
    {
        if (!Application.isPlaying || tokenRigidbody == null || playerTransform == null)
        {
            return;
        }

        KeepTokenOnMovementPlane();

        if (!inputEnabled)
        {
            ApplyNoInputMovement();
            return;
        }

        ApplyMovement(inputDirection);
    }

    private void OnValidate()
    {
        tokenDiameter = Mathf.Max(0.05f, tokenDiameter);
        movementSpeed = Mathf.Max(0f, movementSpeed);
        acceleration = Mathf.Max(0f, acceleration);
        linearDamping = Mathf.Max(0f, linearDamping);
        angularDamping = Mathf.Max(0f, angularDamping);
        generatedTextureSize = Mathf.Max(32, generatedTextureSize);
    }

    [ContextMenu("Create Token")]
    public void CreateToken()
    {
        ResolveReferences();
        ClearGeneratedToken();

        GameObject tokenObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tokenObject.name = tokenObjectName;
        tokenObject.transform.SetParent(transform, false);
        tokenObject.transform.position = ResolveEntranceWorldPosition();
        tokenObject.transform.localScale = Vector3.one * tokenDiameter;

        playerTransform = tokenObject.transform;

        ConfigureRendering(tokenObject);
        ConfigurePhysics(tokenObject);

        status = "token ready";
        Debug.Log("token ready");
    }

    [ContextMenu("Clear Token")]
    public void ClearGeneratedToken()
    {
        Transform existing = transform.Find(tokenObjectName);
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

        playerTransform = null;
        tokenRigidbody = null;
        tokenCollider = null;
        status = "not generated";
    }

    public void ResetTokenToEntrance()
    {
        if (playerTransform == null)
        {
            CreateToken();
            return;
        }

        Vector3 entrancePosition = ResolveEntranceWorldPosition();
        playerTransform.position = entrancePosition;

        if (tokenRigidbody != null)
        {
            tokenRigidbody.linearVelocity = Vector3.zero;
            tokenRigidbody.angularVelocity = Vector3.zero;
            tokenRigidbody.position = entrancePosition;
        }
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;

        if (!enabled && tokenRigidbody != null)
        {
            tokenRigidbody.linearVelocity = Vector3.zero;
            tokenRigidbody.angularVelocity = Vector3.zero;
        }
    }

    private void ResolveReferences()
    {
        if (boardSource == null && autoFindBoardSource)
        {
            boardSource = FindFirstObjectByType<MazeBoardGenerator>();
        }

        if (wallsSource == null && autoFindWallsSource)
        {
            wallsSource = FindFirstObjectByType<MazeWallsGenerator>();
        }

        if (wallsSource == null)
        {
            Debug.LogWarning("PlayerToken did not find MazeWallsGenerator. The token will still be created, but wall collision depends on generated wall colliders being present in the scene.");
        }
    }

    private Vector3 ResolveEntranceWorldPosition()
    {
        if (boardSource != null)
        {
            if (TryGetEntranceWorldPositionFromProperty(out Vector3 propertyPosition))
            {
                propertyPosition.y = tokenCenterY;
                return propertyPosition;
            }

            if (TryGetEntranceWorldPositionFromMethod(out Vector3 methodPosition))
            {
                methodPosition.y = tokenCenterY;
                return methodPosition;
            }

            if (TryGetEntranceCellFromMethod(out int column, out int row))
            {
                Vector3 gridPosition = GridToWorld(column, row);
                gridPosition.y = tokenCenterY;
                return gridPosition;
            }

            Debug.LogWarning("PlayerToken could not find MazeBoardGenerator.GetEntranceCell() or MazeBoardGenerator.EntranceWorldPosition. Falling back to the baseline left-middle entrance assumption.");
            Vector3 fallbackFromBoard = GridToWorld(0, Mathf.Max(0, boardSource.Rows / 2));
            fallbackFromBoard.y = tokenCenterY;
            return fallbackFromBoard;
        }

        Debug.LogWarning("PlayerToken could not find a MazeBoardGenerator. Falling back to the baseline 14 x 12 left-middle entrance assumption.");
        return new Vector3(-6.5f, tokenCenterY, -0.5f);
    }

    private bool TryGetEntranceWorldPositionFromProperty(out Vector3 position)
    {
        position = Vector3.zero;

        Type boardType = boardSource.GetType();
        PropertyInfo property = boardType.GetProperty(
            "EntranceWorldPosition",
            BindingFlags.Instance | BindingFlags.Public);

        if (property == null || property.PropertyType != typeof(Vector3))
        {
            return false;
        }

        position = (Vector3)property.GetValue(boardSource);
        return true;
    }

    private bool TryGetEntranceWorldPositionFromMethod(out Vector3 position)
    {
        position = Vector3.zero;

        Type boardType = boardSource.GetType();
        MethodInfo method = boardType.GetMethod(
            "GetEntranceWorldPosition",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);

        if (method == null || method.ReturnType != typeof(Vector3))
        {
            return false;
        }

        position = (Vector3)method.Invoke(boardSource, null);
        return true;
    }

    private bool TryGetEntranceCellFromMethod(out int column, out int row)
    {
        column = 0;
        row = 0;

        Type boardType = boardSource.GetType();
        MethodInfo method = boardType.GetMethod(
            "GetEntranceCell",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);

        if (method == null)
        {
            return false;
        }

        object result = method.Invoke(boardSource, null);

        if (result == null)
        {
            return false;
        }

        Type resultType = result.GetType();

        FieldInfo columnField = resultType.GetField("Column", BindingFlags.Instance | BindingFlags.Public);
        FieldInfo rowField = resultType.GetField("Row", BindingFlags.Instance | BindingFlags.Public);

        if (columnField != null && rowField != null)
        {
            column = Convert.ToInt32(columnField.GetValue(result));
            row = Convert.ToInt32(rowField.GetValue(result));
            return true;
        }

        PropertyInfo columnProperty = resultType.GetProperty("Column", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo rowProperty = resultType.GetProperty("Row", BindingFlags.Instance | BindingFlags.Public);

        if (columnProperty != null && rowProperty != null)
        {
            column = Convert.ToInt32(columnProperty.GetValue(result));
            row = Convert.ToInt32(rowProperty.GetValue(result));
            return true;
        }

        return false;
    }

    private Vector3 GridToWorld(int column, int row)
    {
        int sourceColumns = boardSource != null ? boardSource.Columns : 14;
        int sourceRows = boardSource != null ? boardSource.Rows : 12;
        float sourceCellSize = boardSource != null ? boardSource.CellSize : 1f;

        float worldX = (column - (sourceColumns - 1) * 0.5f) * sourceCellSize;
        float worldZ = ((sourceRows - 1) * 0.5f - row) * sourceCellSize;

        return new Vector3(worldX, tokenCenterY, worldZ);
    }

    private void ConfigureRendering(GameObject tokenObject)
    {
        MeshRenderer renderer = tokenObject.GetComponent<MeshRenderer>();

        if (renderer == null)
        {
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        tokenMaterial = new Material(shader);
        tokenMaterial.name = "Player Token Material";
        tokenMaterial.color = tokenBaseColor;

        if (shader != null && shader.name.Contains("Universal Render Pipeline"))
        {
            tokenMaterial.SetFloat("_Smoothness", smoothness);
        }
        else
        {
            tokenMaterial.SetFloat("_Glossiness", smoothness);
        }

        Texture2D finalTexture = tokenTexture;
        if (finalTexture == null && generateFallbackCircleTexture)
        {
            finalTexture = GenerateCirclePatternTexture();
        }

        if (finalTexture != null)
        {
            tokenMaterial.mainTexture = finalTexture;
        }

        renderer.sharedMaterial = tokenMaterial;
    }

    private Texture2D GenerateCirclePatternTexture()
    {
        int size = Mathf.Max(32, generatedTextureSize);
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        texture.name = "Generated Player Token Circle Texture";
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2(0.5f, 0.5f);
        Vector2 offsetA = new Vector2(0.28f, 0.5f);
        Vector2 offsetB = new Vector2(0.72f, 0.5f);
        Vector2 offsetC = new Vector2(0.5f, 0.28f);
        Vector2 offsetD = new Vector2(0.5f, 0.72f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 uv = new Vector2(
                    (x + 0.5f) / size,
                    (y + 0.5f) / size);

                float line = 0f;
                line = Mathf.Max(line, CircleLineMask(uv, center, circleRadius));
                line = Mathf.Max(line, CircleLineMask(uv, offsetA, circleRadius));
                line = Mathf.Max(line, CircleLineMask(uv, offsetB, circleRadius));
                line = Mathf.Max(line, CircleLineMask(uv, offsetC, circleRadius));
                line = Mathf.Max(line, CircleLineMask(uv, offsetD, circleRadius));
                line = Mathf.Max(line, CircleLineMask(uv, center, circleRadius * 0.62f));

                Color color = Color.Lerp(tokenBaseColor, circlePatternColor, line);
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply(true, false);
        return texture;
    }

    private float CircleLineMask(Vector2 uv, Vector2 center, float radius)
    {
        float distance = Vector2.Distance(uv, center);
        float lineDistance = Mathf.Abs(distance - radius);
        float halfWidth = Mathf.Max(0.001f, circleLineWidth);
        return 1f - Mathf.SmoothStep(halfWidth * 0.5f, halfWidth, lineDistance);
    }

    private void ConfigurePhysics(GameObject tokenObject)
    {
        tokenCollider = tokenObject.GetComponent<SphereCollider>();
        if (tokenCollider == null)
        {
            tokenCollider = tokenObject.AddComponent<SphereCollider>();
        }

        tokenCollider.radius = 0.5f;

        tokenPhysicsMaterial = new PhysicsMaterial("Player Token Physics")
        {
            bounciness = bounciness,
            dynamicFriction = dynamicFriction,
            staticFriction = staticFriction,
            frictionCombine = frictionCombine,
            bounceCombine = bounceCombine
        };

        tokenCollider.sharedMaterial = tokenPhysicsMaterial;

        tokenRigidbody = tokenObject.GetComponent<Rigidbody>();
        if (tokenRigidbody == null)
        {
            tokenRigidbody = tokenObject.AddComponent<Rigidbody>();
        }

        tokenRigidbody.useGravity = false;
        tokenRigidbody.linearDamping = linearDamping;
        tokenRigidbody.angularDamping = angularDamping;
        tokenRigidbody.collisionDetectionMode = collisionDetectionMode;
        tokenRigidbody.interpolation = interpolation;
        tokenRigidbody.constraints = RigidbodyConstraints.FreezePositionY;

        if (freezeRotation)
        {
            tokenRigidbody.constraints |= RigidbodyConstraints.FreezeRotationX |
                                          RigidbodyConstraints.FreezeRotationY |
                                          RigidbodyConstraints.FreezeRotationZ;
        }

        tokenRigidbody.position = new Vector3(
            tokenObject.transform.position.x,
            tokenCenterY,
            tokenObject.transform.position.z);

        tokenRigidbody.linearVelocity = Vector3.zero;
        tokenRigidbody.angularVelocity = Vector3.zero;
    }

    private Vector3 ReadArrowKeyInput()
    {
        float x = 0f;
        float z = 0f;

        if (Input.GetKey(leftKey))
        {
            x -= 1f;
        }

        if (Input.GetKey(rightKey))
        {
            x += 1f;
        }

        if (Input.GetKey(downKey))
        {
            z -= 1f;
        }

        if (Input.GetKey(upKey))
        {
            z += 1f;
        }

        Vector3 direction = new Vector3(x, 0f, z);

        if (normalizeDiagonalInput && direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        return direction;
    }

    private void ApplyMovement(Vector3 direction)
    {
        if (movementMode == MovementMode.SetVelocity)
        {
            Vector3 targetVelocity = direction * movementSpeed;
            tokenRigidbody.linearVelocity = new Vector3(
                targetVelocity.x,
                0f,
                targetVelocity.z);
            return;
        }

        Vector3 currentHorizontalVelocity = new Vector3(
            tokenRigidbody.linearVelocity.x,
            0f,
            tokenRigidbody.linearVelocity.z);

        Vector3 desiredVelocity = direction * movementSpeed;
        Vector3 velocityDelta = desiredVelocity - currentHorizontalVelocity;
        Vector3 accelerationStep = Vector3.ClampMagnitude(
            velocityDelta,
            acceleration * Time.fixedDeltaTime);

        tokenRigidbody.linearVelocity = new Vector3(
            currentHorizontalVelocity.x + accelerationStep.x,
            0f,
            currentHorizontalVelocity.z + accelerationStep.z);
    }

    private void ApplyNoInputMovement()
    {
        tokenRigidbody.linearVelocity = new Vector3(0f, 0f, 0f);
    }

    private void KeepTokenOnMovementPlane()
    {
        Vector3 position = tokenRigidbody.position;

        if (!Mathf.Approximately(position.y, tokenCenterY))
        {
            position.y = tokenCenterY;
            tokenRigidbody.position = position;
            playerTransform.position = position;
        }

        Vector3 velocity = tokenRigidbody.linearVelocity;
        if (!Mathf.Approximately(velocity.y, 0f))
        {
            tokenRigidbody.linearVelocity = new Vector3(velocity.x, 0f, velocity.z);
        }
    }
}