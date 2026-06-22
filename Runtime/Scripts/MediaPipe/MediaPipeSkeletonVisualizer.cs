using UnityEngine;

/// <summary>
/// Draws the MediaPipe skeleton in Unity using simple GameObjects.
/// Visible in both Scene and Game view.
///
/// Attach to the same GameObject as MediaPipeMotionTrackingManager,
/// or assign the reference manually.
/// </summary>
public class MediaPipeSkeletonVisualizer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MediaPipeMotionTrackingManager trackingManager;

    [Header("Appearance")]
    [SerializeField] private Color jointColor = Color.green;
    [SerializeField] private Color boneColor = Color.cyan;
    [SerializeField] private Color spineColor = Color.yellow;
    [SerializeField] private float jointSize = 0.02f;
    [SerializeField] private float lineWidth = 0.005f;

    [Header("Options")]
    [SerializeField] private bool showJoints = true;
    [SerializeField] private bool showBones = true;
    [SerializeField] private bool showVirtualJoints = true;

    // bone connections (landmark index pairs)
    private static readonly int[][] BoneConnections = new int[][]
    {
        // arms
        new[] { 11, 13 }, new[] { 13, 15 }, // left arm
        new[] { 12, 14 }, new[] { 14, 16 }, // right arm

        // legs
        new[] { 23, 25 }, new[] { 25, 27 }, new[] { 27, 31 }, // left leg
        new[] { 24, 26 }, new[] { 26, 28 }, new[] { 28, 32 }, // right leg

        // shoulders and hips
        new[] { 11, 12 }, // shoulders
        new[] { 23, 24 }, // hips
    };

    private static readonly int[][] SpineConnections = new int[][]
    {
        new[] { 11, 23 }, new[] { 12, 24 }, // torso sides
        new[] { 0, 11 }, new[] { 0, 12 },   // nose to shoulders
    };

    // rendering objects
    private GameObject jointParent;
    private GameObject[] jointSpheres = new GameObject[33];
    private GameObject[] virtualJointSpheres;
    private LineRenderer[] boneLines;
    private LineRenderer[] spineLines;

    private Material jointMaterial;
    private Material boneMaterial;
    private Material spineMaterial;

    private bool isInitialized = false;

    void Start()
    {
        if (trackingManager == null)
            trackingManager = GetComponent<MediaPipeMotionTrackingManager>();

        if (trackingManager == null)
        {
            Debug.LogError("MediaPipeSkeletonVisualizer: No MediaPipeMotionTrackingManager found!");
            return;
        }

        CreateMaterials();
        CreateJointVisuals();
        CreateBoneVisuals();

        isInitialized = true;
    }

    void Update()
    {
        if (!isInitialized || trackingManager == null || !trackingManager.HasReceivedLandmarks)
        {
            SetVisibility(false);
            return;
        }

        SetVisibility(true);
        UpdateJointPositions();
        UpdateBonePositions();
    }

    #region Setup

    private void CreateMaterials()
    {
        // use unlit color shader so joints/bones are always visible
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader == null)
            unlitShader = Shader.Find("UI/Default");

        jointMaterial = new Material(unlitShader);
        jointMaterial.color = jointColor;

        boneMaterial = new Material(unlitShader);
        boneMaterial.color = boneColor;

        spineMaterial = new Material(unlitShader);
        spineMaterial.color = spineColor;
    }

    private void CreateJointVisuals()
    {
        jointParent = new GameObject("SkeletonVisuals");
        jointParent.transform.SetParent(transform);

        // 33 landmark spheres
        for (int i = 0; i < 33; i++)
        {
            jointSpheres[i] = CreateSphere($"Joint_{i}", jointMaterial);
        }

        // virtual joint spheres (Head, Neck, Hips, Spine, Spine1, Spine4)
        if (showVirtualJoints)
        {
            string[] virtualNames = { "Head", "Neck", "Hips", "Spine", "Spine1", "Spine4" };
            virtualJointSpheres = new GameObject[virtualNames.Length];
            for (int i = 0; i < virtualNames.Length; i++)
            {
                virtualJointSpheres[i] = CreateSphere($"Virtual_{virtualNames[i]}", jointMaterial);
                virtualJointSpheres[i].transform.localScale = Vector3.one * jointSize * 1.5f; // slightly larger
            }
        }
    }

    private GameObject CreateSphere(string name, Material mat)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        sphere.transform.SetParent(jointParent.transform);
        sphere.transform.localScale = Vector3.one * jointSize;

        // remove collider — we don't need physics on debug visuals
        var collider = sphere.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        var renderer = sphere.GetComponent<Renderer>();
        if (renderer != null) renderer.material = mat;

        return sphere;
    }

    private void CreateBoneVisuals()
    {
        // body bones
        boneLines = new LineRenderer[BoneConnections.Length];
        for (int i = 0; i < BoneConnections.Length; i++)
        {
            boneLines[i] = CreateLine($"Bone_{i}", boneMaterial);
        }

        // spine bones
        spineLines = new LineRenderer[SpineConnections.Length];
        for (int i = 0; i < SpineConnections.Length; i++)
        {
            spineLines[i] = CreateLine($"Spine_{i}", spineMaterial);
        }
    }

    private LineRenderer CreateLine(string name, Material mat)
    {
        GameObject lineObj = new GameObject(name);
        lineObj.transform.SetParent(jointParent.transform);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.material = mat;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.positionCount = 2;
        lr.useWorldSpace = true;

        return lr;
    }

    #endregion

    #region Update

    private static bool IsValidPosition(Vector3 p) => p.sqrMagnitude > 0.0001f;

    private void UpdateJointPositions()
    {
        if (!showJoints) return;

        // raw landmarks
        for (int i = 0; i < 33; i++)
        {
            Transform lm = trackingManager.GetLandmarkTransform(i);
            bool valid = lm != null && IsValidPosition(lm.position);
            jointSpheres[i].SetActive(valid);
            if (valid)
                jointSpheres[i].transform.position = lm.position;
        }

        // virtual joints
        if (showVirtualJoints && virtualJointSpheres != null)
        {
            string[] virtualNames = { "Head", "Neck", "Hips", "Spine", "Spine1", "Spine4" };
            for (int i = 0; i < virtualNames.Length; i++)
            {
                Transform joint = trackingManager.GetJointByName(virtualNames[i]);
                if (joint != null)
                    virtualJointSpheres[i].transform.position = joint.position;
            }
        }
    }

    private void UpdateBonePositions()
    {
        if (!showBones) return;

        // body bones
        for (int i = 0; i < BoneConnections.Length; i++)
        {
            int a = BoneConnections[i][0];
            int b = BoneConnections[i][1];
            Transform ta = trackingManager.GetLandmarkTransform(a);
            Transform tb = trackingManager.GetLandmarkTransform(b);
            bool valid = ta != null && tb != null
                      && IsValidPosition(ta.position) && IsValidPosition(tb.position);
            boneLines[i].enabled = valid;
            if (valid)
            {
                boneLines[i].SetPosition(0, ta.position);
                boneLines[i].SetPosition(1, tb.position);
            }
        }

        // spine bones
        for (int i = 0; i < SpineConnections.Length; i++)
        {
            int a = SpineConnections[i][0];
            int b = SpineConnections[i][1];
            Transform ta = trackingManager.GetLandmarkTransform(a);
            Transform tb = trackingManager.GetLandmarkTransform(b);
            bool valid = ta != null && tb != null
                      && IsValidPosition(ta.position) && IsValidPosition(tb.position);
            spineLines[i].enabled = valid;
            if (valid)
            {
                spineLines[i].SetPosition(0, ta.position);
                spineLines[i].SetPosition(1, tb.position);
            }
        }
    }

    private void SetVisibility(bool visible)
    {
        if (jointParent != null && jointParent.activeSelf != visible)
            jointParent.SetActive(visible);
    }

    #endregion

    void OnDestroy()
    {
        if (jointParent != null)
            Destroy(jointParent);

        if (jointMaterial != null) Destroy(jointMaterial);
        if (boneMaterial != null) Destroy(boneMaterial);
        if (spineMaterial != null) Destroy(spineMaterial);
    }
}
