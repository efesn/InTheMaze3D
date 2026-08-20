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

    [Header("References & Detection")]
    [SerializeField] private MazeBoardGenerator boardSource;
    [SerializeField] private UnityEngine.Object combinedBoardSource;
    [SerializeField] private bool autoFindBoardSource = true;
    [SerializeField] private bool createOnStart = true;
    [SerializeField] private bool debugLogging = true;

    [Header("Character Geometry & Scale")]
    [SerializeField] private string tokenObjectName = "Player Token";
    [SerializeField, Min(0.05f)] private float characterScale = 1f;
    [SerializeField] private float spawnHeight = 0.6f;

    [Header("Movement & Controls")]
    [SerializeField] private MovementMode movementMode = MovementMode.SetVelocity;
    [SerializeField, Min(0f)] private float movementSpeed = 5f;
    [SerializeField, Min(0f)] private float acceleration = 30f;
    [SerializeField, Min(0f)] private float rotationSpeed = 14f;
    [SerializeField] private bool normalizeDiagonalInput = true;
    [SerializeField] private KeyCode upKey = KeyCode.UpArrow;
    [SerializeField] private KeyCode downKey = KeyCode.DownArrow;
    [SerializeField] private KeyCode leftKey = KeyCode.LeftArrow;
    [SerializeField] private KeyCode rightKey = KeyCode.RightArrow;
    [SerializeField] private bool inputEnabled = true;

    [Header("Physics Settings")]
    [SerializeField, Min(0f)] private float linearDamping = 1.5f;
    [SerializeField, Min(0f)] private float angularDamping = 4f;
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.9f;
    [SerializeField, Range(0f, 1f)] private float dynamicFriction = 0.2f;
    [SerializeField, Range(0f, 1f)] private float staticFriction = 0.2f;
    [SerializeField] private PhysicsMaterial CombinePhysicsMaterial;
    [SerializeField] private CollisionDetectionMode collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    [SerializeField] private RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate;
    [SerializeField] private bool freezeRotation = true;

    [Header("Visual Colors & Materials")]
    [SerializeField] private Color bodyColor = new Color(0.92f, 0.93f, 0.95f, 1f); // White / Light Grey
    [SerializeField] private Color headColor = new Color(0.2f, 0.22f, 0.25f, 1f); // Dark Gray
    [SerializeField] private Color eyeColor = new Color(0.1f, 0.8f, 1f, 1f); // Cyan / Blue Glow
    [SerializeField] private Color detailColor = new Color(0.15f, 0.45f, 0.95f, 1f); // Accent Blue

    [SerializeField] private Material customBodyMaterial;
    [SerializeField] private Material customHeadMaterial;
    [SerializeField] private Material customEyeMaterial;
    [SerializeField] private Material customDetailMaterial;

    [SerializeField, HideInInspector] private string status = "not generated";

    private Transform playerTransform;
    private Rigidbody playerRigidbody;
    private CapsuleCollider rootCollider;
    private PhysicsMaterial defaultPhysicsMaterial;
    private Vector3 inputDirection;
    private Quaternion targetRotation;

    public Transform PlayerTransform => playerTransform;
    public Rigidbody PlayerRigidbody => playerRigidbody;
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
        if (!Application.isPlaying || playerRigidbody == null || playerTransform == null)
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
        ApplyRotation(inputDirection);
    }

    private void OnValidate()
    {
        characterScale = Mathf.Max(0.05f, characterScale);
        movementSpeed = Mathf.Max(0f, movementSpeed);
        acceleration = Mathf.Max(0f, acceleration);
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        linearDamping = Mathf.Max(0f, linearDamping);
        angularDamping = Mathf.Max(0f, angularDamping);
    }

    [ContextMenu("Create Token")]
    public void CreateToken()
    {
        ResolveReferences();
        ClearGeneratedToken();

        Vector3 spawnPos = ResolveEntranceWorldPosition();

        GameObject rootObj = new GameObject(tokenObjectName);
        rootObj.transform.SetParent(transform, false);
        rootObj.transform.position = spawnPos;
        rootObj.transform.rotation = Quaternion.identity;

        playerTransform = rootObj.transform;

        // Configure Root Collider & Rigidbody
        ConfigurePhysics(rootObj);

        // Build stylized 3D maze explorer robot from primitives as children
        BuildRobotVisuals(rootObj);

        targetRotation = rootObj.transform.rotation;

        status = "token ready";
        if (debugLogging)
        {
            Debug.Log("token ready");
        }
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
        playerRigidbody = null;
        rootCollider = null;
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

        if (playerRigidbody != null)
        {
            playerRigidbody.linearVelocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
            playerRigidbody.position = entrancePosition;
        }
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;

        if (!enabled && playerRigidbody != null)
        {
            playerRigidbody.linearVelocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
        }
    }

    private void ResolveReferences()
    {
        if (boardSource == null && autoFindBoardSource)
        {
            boardSource = FindFirstObjectByType<MazeBoardGenerator>();
        }

        if (combinedBoardSource == null && autoFindBoardSource)
        {
            MonoBehaviour[] monos = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (MonoBehaviour mono in monos)
            {
                if (mono != null && mono.GetType().Name == "MazeCombinedGenerator")
                {
                    combinedBoardSource = mono;
                    break;
                }
            }
        }
    }

    private Vector3 ResolveEntranceWorldPosition()
    {
        // 1. Check MazeBoardGenerator
        if (boardSource != null)
        {
            if (TryGetEntranceWorldPositionFromObj(boardSource, out Vector3 boardPos))
            {
                boardPos.y = spawnHeight;
                return boardPos;
            }
        }

        // 2. Check MazeCombinedGenerator if present
        if (combinedBoardSource != null)
        {
            if (TryGetEntranceWorldPositionFromObj(combinedBoardSource, out Vector3 combPos))
            {
                combPos.y = spawnHeight;
                return combPos;
            }
        }

        // 3. Check generic grid cell method on board source
        if (boardSource != null && TryGetEntranceCellFromMethod(boardSource, out int column, out int row))
        {
            Vector3 gridPosition = GridToWorld(column, row);
            gridPosition.y = spawnHeight;
            return gridPosition;
        }

        if (debugLogging)
        {
            Debug.LogWarning("PlayerToken fallback used: left-middle entrance position of 14 x 12 board centered at (0,0,0) with cell size 1.");
        }
        // Fallback for 14x12 board with cell size 1 centered at (0,0,0):
        // left middle entrance cell (0, 5.5 -> row 5.5 or 6). worldX = -6.5f, worldZ = -0.5f
        return new Vector3(-6.5f, spawnHeight, -0.5f);
    }

    private bool TryGetEntranceWorldPositionFromObj(UnityEngine.Object source, out Vector3 position)
    {
        position = Vector3.zero;
        if (source == null) return false;

        Type type = source.GetType();

        // Property check
        PropertyInfo property = type.GetProperty("EntranceWorldPosition", BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.PropertyType == typeof(Vector3))
        {
            position = (Vector3)property.GetValue(source);
            return true;
        }

        // Method check
        MethodInfo method = type.GetMethod("GetEntranceWorldPosition", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        if (method != null && method.ReturnType == typeof(Vector3))
        {
            position = (Vector3)method.Invoke(source, null);
            return true;
        }

        return false;
    }

    private bool TryGetEntranceCellFromMethod(UnityEngine.Object source, out int column, out int row)
    {
        column = 0;
        row = 0;
        if (source == null) return false;

        Type type = source.GetType();
        MethodInfo method = type.GetMethod("GetEntranceCell", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        if (method == null) return false;

        object result = method.Invoke(source, null);
        if (result == null) return false;

        Type resultType = result.GetType();
        FieldInfo colField = resultType.GetField("Column", BindingFlags.Instance | BindingFlags.Public);
        FieldInfo rowField = resultType.GetField("Row", BindingFlags.Instance | BindingFlags.Public);

        if (colField != null && rowField != null)
        {
            column = Convert.ToInt32(colField.GetValue(result));
            row = Convert.ToInt32(rowField.GetValue(result));
            return true;
        }

        PropertyInfo colProp = resultType.GetProperty("Column", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo rowProp = resultType.GetProperty("Row", BindingFlags.Instance | BindingFlags.Public);

        if (colProp != null && rowProp != null)
        {
            column = Convert.ToInt32(colProp.GetValue(result));
            row = Convert.ToInt32(rowProp.GetValue(result));
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

        return new Vector3(worldX, spawnHeight, worldZ);
    }

    private void ConfigurePhysics(GameObject rootObj)
    {
        rootCollider = rootObj.AddComponent<CapsuleCollider>();
        rootCollider.radius = 0.28f * characterScale;
        rootCollider.height = 0.85f * characterScale;
        rootCollider.center = new Vector3(0f, 0f, 0f);

        PhysicsMaterial matToUse = CombinePhysicsMaterial;
        if (matToUse == null)
        {
            defaultPhysicsMaterial = new PhysicsMaterial("Player Token Physics")
            {
                bounciness = bounciness,
                dynamicFriction = dynamicFriction,
                staticFriction = staticFriction,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Maximum
            };
            matToUse = defaultPhysicsMaterial;
        }

        rootCollider.sharedMaterial = matToUse;

        playerRigidbody = rootObj.AddComponent<Rigidbody>();
        playerRigidbody.useGravity = false;
        playerRigidbody.linearDamping = linearDamping;
        playerRigidbody.angularDamping = angularDamping;
        playerRigidbody.collisionDetectionMode = collisionDetectionMode;
        playerRigidbody.interpolation = interpolation;

        RigidbodyConstraints constraints = RigidbodyConstraints.FreezePositionY;
        if (freezeRotation)
        {
            constraints |= RigidbodyConstraints.FreezeRotationX |
                           RigidbodyConstraints.FreezeRotationY |
                           RigidbodyConstraints.FreezeRotationZ;
        }
        playerRigidbody.constraints = constraints;
        playerRigidbody.position = rootObj.transform.position;
        playerRigidbody.linearVelocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;
    }

    private void BuildRobotVisuals(GameObject rootObj)
    {
        // Generate materials if custom ones aren't assigned
        Material matBody = customBodyMaterial != null ? customBodyMaterial : CreateMaterial("RobotBodyMat", bodyColor, 0.4f);
        Material matHead = customHeadMaterial != null ? customHeadMaterial : CreateMaterial("RobotHeadMat", headColor, 0.6f);
        Material matEye = customEyeMaterial != null ? customEyeMaterial : CreateMaterial("RobotEyeMat", eyeColor, 0.9f, true);
        Material matDetail = customDetailMaterial != null ? customDetailMaterial : CreateMaterial("RobotDetailMat", detailColor, 0.5f);

        GameObject modelContainer = new GameObject("VisualModel");
        modelContainer.transform.SetParent(rootObj.transform, false);
        modelContainer.transform.localScale = Vector3.one * characterScale;

        // 1. Capsule Body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(modelContainer.transform, false);
        body.transform.localPosition = new Vector3(0f, 0f, 0f);
        body.transform.localScale = new Vector3(0.48f, 0.35f, 0.48f);
        SetPrimitiveProperties(body, matBody);

        // 2. Directional Ring / Stripe around lower body
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "DirectionRing";
        ring.transform.SetParent(modelContainer.transform, false);
        ring.transform.localPosition = new Vector3(0f, -0.12f, 0f);
        ring.transform.localScale = new Vector3(0.52f, 0.03f, 0.52f);
        SetPrimitiveProperties(ring, matDetail);

        // 3. Spherical Head
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(modelContainer.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.28f, 0f);
        head.transform.localScale = new Vector3(0.38f, 0.34f, 0.38f);
        SetPrimitiveProperties(head, matHead);

        // 4. Eyes (Left and Right glowing objects facing forward along +Z)
        GameObject eyeLeft = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eyeLeft.name = "EyeLeft";
        eyeLeft.transform.SetParent(head.transform, false);
        eyeLeft.transform.localPosition = new Vector3(-0.28f, 0.12f, 0.72f);
        eyeLeft.transform.localScale = new Vector3(0.24f, 0.24f, 0.24f);
        SetPrimitiveProperties(eyeLeft, matEye);

        GameObject eyeRight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eyeRight.name = "EyeRight";
        eyeRight.transform.SetParent(head.transform, false);
        eyeRight.transform.localPosition = new Vector3(0.28f, 0.12f, 0.72f);
        eyeRight.transform.localScale = new Vector3(0.24f, 0.24f, 0.24f);
        SetPrimitiveProperties(eyeRight, matEye);

        // 5. Left & Right Arm Shapes
        GameObject armLeft = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        armLeft.name = "ArmLeft";
        armLeft.transform.SetParent(modelContainer.transform, false);
        armLeft.transform.localPosition = new Vector3(-0.32f, 0.02f, 0f);
        armLeft.transform.localRotation = Quaternion.Euler(0f, 0f, 15f);
        armLeft.transform.localScale = new Vector3(0.12f, 0.18f, 0.12f);
        SetPrimitiveProperties(armLeft, matDetail);

        GameObject armRight = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        armRight.name = "ArmRight";
        armRight.transform.SetParent(modelContainer.transform, false);
        armRight.transform.localPosition = new Vector3(0.32f, 0.02f, 0f);
        armRight.transform.localRotation = Quaternion.Euler(0f, 0f, -15f);
        armRight.transform.localScale = new Vector3(0.12f, 0.18f, 0.12f);
        SetPrimitiveProperties(armRight, matDetail);

        // 6. Antenna / Navigation Sensor
        GameObject antennaBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        antennaBase.name = "AntennaBase";
        antennaBase.transform.SetParent(head.transform, false);
        antennaBase.transform.localPosition = new Vector3(0f, 0.95f, -0.1f);
        antennaBase.transform.localScale = new Vector3(0.08f, 0.3f, 0.08f);
        SetPrimitiveProperties(antennaBase, matDetail);

        GameObject antennaTip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        antennaTip.name = "AntennaTip";
        antennaTip.transform.SetParent(antennaBase.transform, false);
        antennaTip.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        antennaTip.transform.localScale = new Vector3(2.2f, 0.6f, 2.2f);
        SetPrimitiveProperties(antennaTip, matEye);

        // Front indicator accent arrow/stripe
        GameObject frontPointer = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frontPointer.name = "FrontIndicator";
        frontPointer.transform.SetParent(modelContainer.transform, false);
        frontPointer.transform.localPosition = new Vector3(0f, -0.1f, 0.26f);
        frontPointer.transform.localScale = new Vector3(0.12f, 0.04f, 0.12f);
        SetPrimitiveProperties(frontPointer, matDetail);
    }

    private void SetPrimitiveProperties(GameObject primitiveObj, Material mat)
    {
        // Strip colliders from child primitives so only the root CapsuleCollider handles physics
        Collider c = primitiveObj.GetComponent<Collider>();
        if (c != null)
        {
            if (Application.isPlaying)
            {
                Destroy(c);
            }
            else
            {
                DestroyImmediate(c);
            }
        }

        MeshRenderer mr = primitiveObj.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sharedMaterial = mat;
        }
    }

    private Material CreateMaterial(string matName, Color color, float smoothness, bool isEmissive = false)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material mat = new Material(shader);
        mat.name = matName;
        mat.color = color;

        if (shader != null && shader.name.Contains("Universal Render Pipeline"))
        {
            mat.SetFloat("_Smoothness", smoothness);
            if (isEmissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 1.5f);
            }
        }
        else
        {
            mat.SetFloat("_Glossiness", smoothness);
            if (isEmissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 1.5f);
            }
        }

        return mat;
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
            playerRigidbody.linearVelocity = new Vector3(
                targetVelocity.x,
                0f,
                targetVelocity.z);
            return;
        }

        Vector3 currentHorizontalVelocity = new Vector3(
            playerRigidbody.linearVelocity.x,
            0f,
            playerRigidbody.linearVelocity.z);

        Vector3 desiredVelocity = direction * movementSpeed;
        Vector3 velocityDelta = desiredVelocity - currentHorizontalVelocity;
        Vector3 accelerationStep = Vector3.ClampMagnitude(
            velocityDelta,
            acceleration * Time.fixedDeltaTime);

        playerRigidbody.linearVelocity = new Vector3(
            currentHorizontalVelocity.x + accelerationStep.x,
            0f,
            currentHorizontalVelocity.z + accelerationStep.z);
    }

    private void ApplyRotation(Vector3 direction)
    {
        if (direction.sqrMagnitude > 0.01f)
        {
            targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        if (rotationSpeed > 0f)
        {
            playerTransform.rotation = Quaternion.Slerp(
                playerTransform.rotation,
                targetRotation,
                rotationSpeed * Time.fixedDeltaTime);
        }
        else
        {
            playerTransform.rotation = targetRotation;
        }
    }

    private void ApplyNoInputMovement()
    {
        if (playerRigidbody != null)
        {
            playerRigidbody.linearVelocity = Vector3.zero;
        }
    }

    private void KeepTokenOnMovementPlane()
    {
        Vector3 position = playerRigidbody.position;

        if (!Mathf.Approximately(position.y, spawnHeight))
        {
            position.y = spawnHeight;
            playerRigidbody.position = position;
            playerTransform.position = position;
        }

        Vector3 velocity = playerRigidbody.linearVelocity;
        if (!Mathf.Approximately(velocity.y, 0f))
        {
            playerRigidbody.linearVelocity = new Vector3(velocity.x, 0f, velocity.z);
        }
    }
}