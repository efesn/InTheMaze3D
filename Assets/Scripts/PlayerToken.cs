using UnityEngine;

public class PlayerToken : MonoBehaviour
{
    [Header("Maze References")]
    [SerializeField] private MazeBoardGenerator mazeBoardGenerator;
    [SerializeField] private MazeWallsGenerator mazeWallsGenerator;
    [SerializeField] private bool findGeneratorsAutomatically = true;

    [Header("Token Geometry")]
    [SerializeField] private float sphereDiameter = 0.6f;
    [SerializeField] private Color sphereColor = Color.white;
    [SerializeField] private string generatedTokenName = "Player Token";

    [Header("Movement")]
    [SerializeField] private float movementSpeed = 4f;
    [SerializeField] private float fixedYCoordinate = 0.6f;
    [SerializeField] private bool useArrowKeys = true;

    [Header("Physics")]
    [SerializeField] private float bounciness = 0.9f;
    [SerializeField] private float dynamicFriction = 0.2f;
    [SerializeField] private float staticFriction = 0.2f;
    [SerializeField] private float mass = 1f;

    public string Status { get; private set; } = "token not ready";

    private GameObject tokenObject;
    private Rigidbody tokenRigidbody;
    private PhysicsMaterial tokenPhysicsMaterial;
    private Material tokenMaterial;

    private void Start()
    {
        ResolveGenerators();
        GenerateToken();
    }

    private void FixedUpdate()
    {
        if (tokenRigidbody == null)
        {
            return;
        }

        Vector3 input = GetMovementInput();
        Vector3 velocity = input * movementSpeed;
        tokenRigidbody.linearVelocity = new Vector3(velocity.x, tokenRigidbody.linearVelocity.y, velocity.z);

        Vector3 position = tokenRigidbody.position;
        position.y = fixedYCoordinate;
        tokenRigidbody.position = position;
    }

    [ContextMenu("Generate Token")]
    public void GenerateToken()
    {
        ClearExistingToken();
        CreatePhysicsMaterial();

        tokenObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tokenObject.name = generatedTokenName;
        tokenObject.transform.SetParent(transform, false);
        tokenObject.transform.localScale = Vector3.one * sphereDiameter;
        tokenObject.transform.position = GetEntranceWorldPosition();

        Renderer renderer = tokenObject.GetComponent<Renderer>();
        tokenMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        tokenMaterial.color = sphereColor;
        renderer.sharedMaterial = tokenMaterial;

        SphereCollider collider = tokenObject.GetComponent<SphereCollider>();
        collider.material = tokenPhysicsMaterial;

        tokenRigidbody = tokenObject.AddComponent<Rigidbody>();
        tokenRigidbody.mass = mass;
        tokenRigidbody.useGravity = false;
        tokenRigidbody.constraints = RigidbodyConstraints.FreezePositionY |
                                     RigidbodyConstraints.FreezeRotationX |
                                     RigidbodyConstraints.FreezeRotationZ;
        tokenRigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        tokenRigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        AddCircleTexture();
        Status = "token ready";
        Debug.Log(Status);
    }

    private void ResolveGenerators()
    {
        if (!findGeneratorsAutomatically)
        {
            return;
        }

        if (mazeBoardGenerator == null)
        {
            mazeBoardGenerator = FindFirstObjectByType<MazeBoardGenerator>();
        }

        if (mazeWallsGenerator == null)
        {
            mazeWallsGenerator = FindFirstObjectByType<MazeWallsGenerator>();
        }
    }

    private Vector3 GetMovementInput()
    {
        if (!useArrowKeys)
        {
            return Vector3.zero;
        }

        float horizontal = 0f;
        float vertical = 0f;

        if (Input.GetKey(KeyCode.LeftArrow))
        {
            horizontal -= 1f;
        }

        if (Input.GetKey(KeyCode.RightArrow))
        {
            horizontal += 1f;
        }

        if (Input.GetKey(KeyCode.DownArrow))
        {
            vertical -= 1f;
        }

        if (Input.GetKey(KeyCode.UpArrow))
        {
            vertical += 1f;
        }

        Vector3 input = new Vector3(horizontal, 0f, vertical);

        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        return input;
    }

    private Vector3 GetEntranceWorldPosition()
    {
        if (mazeBoardGenerator != null && mazeBoardGenerator.CellTable != null)
        {
            int entranceColumn = 0;
            int entranceRow = 6;

            foreach (MazeBoardGenerator.CellRecord record in mazeBoardGenerator.CellTable)
            {
                if (record.column == entranceColumn && record.row == entranceRow)
                {
                    return new Vector3(record.worldX, fixedYCoordinate, record.worldZ);
                }
            }
        }

        return new Vector3(-6.5f, fixedYCoordinate, 0.5f);
    }

    private void CreatePhysicsMaterial()
    {
        tokenPhysicsMaterial = new PhysicsMaterial("Player Token Physics");
        tokenPhysicsMaterial.bounciness = bounciness;
        tokenPhysicsMaterial.dynamicFriction = dynamicFriction;
        tokenPhysicsMaterial.staticFriction = staticFriction;
        tokenPhysicsMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;
        tokenPhysicsMaterial.frictionCombine = PhysicsMaterialCombine.Average;
    }

    private void AddCircleTexture()
    {
        Texture2D texture = new Texture2D(256, 256);
        Color clear = new Color(1f, 1f, 1f, 1f);
        Color line = Color.black;

        for (int y = 0; y < texture.height; y++)
        {
            for (int x = 0; x < texture.width; x++)
            {
                texture.SetPixel(x, y, clear);
            }
        }

        DrawCircle(texture, 128, 128, 80, line);
        DrawCircle(texture, 96, 128, 54, line);
        DrawCircle(texture, 160, 128, 54, line);
        DrawCircle(texture, 128, 96, 54, line);
        DrawCircle(texture, 128, 160, 54, line);
        texture.Apply();

        tokenMaterial.mainTexture = texture;
    }

    private void DrawCircle(Texture2D texture, int centerX, int centerY, int radius, Color color)
    {
        int thickness = 2;

        for (int y = -radius - thickness; y <= radius + thickness; y++)
        {
            for (int x = -radius - thickness; x <= radius + thickness; x++)
            {
                float distance = Mathf.Sqrt(x * x + y * y);

                if (Mathf.Abs(distance - radius) > thickness)
                {
                    continue;
                }

                int pixelX = centerX + x;
                int pixelY = centerY + y;

                if (pixelX >= 0 && pixelX < texture.width && pixelY >= 0 && pixelY < texture.height)
                {
                    texture.SetPixel(pixelX, pixelY, color);
                }
            }
        }
    }

    private void ClearExistingToken()
    {
        Transform existing = transform.Find(generatedTokenName);

        if (existing == null)
        {
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
    }

    private void OnValidate()
    {
        sphereDiameter = Mathf.Max(0.1f, sphereDiameter);
        fixedYCoordinate = Mathf.Max(0f, fixedYCoordinate);
        movementSpeed = Mathf.Max(0f, movementSpeed);
        bounciness = Mathf.Clamp01(bounciness);
        dynamicFriction = Mathf.Clamp01(dynamicFriction);
        staticFriction = Mathf.Clamp01(staticFriction);
        mass = Mathf.Max(0.01f, mass);
    }
}
