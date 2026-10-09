using UnityEditor;
using UnityEngine;

/// <summary>
/// Prefab upgrade that adds ShelfAnchor and a reusable shelf hotspot
/// directly to YukineIcon.prefab. The marker lives under ShelfAnchor, outside
/// Follower, so it stays on the bar while the animated icon is carried away.
/// </summary>
public static class YukineHotspotBuilder
{
    private const string PrefabPath = "Packages/com.myname.sharedprefabs/YukineIcon/YukineIcon.prefab";
    private const string RingMeshPath = "Packages/com.myname.sharedprefabs/YukineIcon/YukineHotspotRing.asset";
    private const string RingMaterialPath = "Packages/com.myname.sharedprefabs/YukineIcon/YukineHotspotRing.mat";
    private const string AnchorName = "ShelfAnchor";
    private const string HotspotName = "ShelfHotspot";

    private static readonly Color HotspotColor = new Color(0.22f, 0.9f, 1f, 0.82f);

    [InitializeOnLoadMethod]
    private static void SchedulePrefabUpgrade()
    {
        EditorApplication.delayCall += UpgradePrefabIfNeeded;
    }

    [MenuItem("Misos/Yukine Icon/Rebuild Shelf Hotspot", false, 120)]
    public static void RebuildHotspot()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError("[YukineIcon] Prefab sa nepodarilo otvorit: " + PrefabPath);
            return;
        }

        try
        {
            Transform existing = FindChildRecursive(prefabRoot.transform, HotspotName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            Transform shelfAnchor = prefabRoot.transform.Find(AnchorName);
            if (shelfAnchor == null)
            {
                GameObject anchorObject = new GameObject(AnchorName);
                shelfAnchor = anchorObject.transform;
                shelfAnchor.SetParent(prefabRoot.transform, false);
            }

            shelfAnchor.localPosition = Vector3.zero;
            shelfAnchor.localRotation = Quaternion.identity;
            shelfAnchor.localScale = Vector3.one;

            YukineHeadIcon icon = prefabRoot.GetComponent<YukineHeadIcon>();
            if (icon == null)
                Debug.LogError("[YukineIcon] Na prefabe chýba YukineHeadIcon.");
            else
            {
                icon.shelfAnchor = shelfAnchor;
                EditorUtility.SetDirty(icon);
            }

            BuildHotspot(shelfAnchor);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            Debug.Log("[YukineIcon] ShelfAnchor aj hotspot boli pridané priamo do zdieľaného prefabu.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void UpgradePrefabIfNeeded()
    {
        if (Application.isPlaying || AssetDatabase.IsAssetImportWorkerProcess()) return;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return;

        Transform anchor = prefab.transform.Find(AnchorName);
        YukineHeadIcon icon = prefab.GetComponent<YukineHeadIcon>();
        if (anchor != null && anchor.Find(HotspotName) != null && icon != null && icon.shelfAnchor != null)
            return;

        RebuildHotspot();
    }

    private static Transform FindChildRecursive(Transform parent, string childName)
    {
        Transform[] children = parent.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != parent && children[i].name == childName)
                return children[i];
        }
        return null;
    }

    private static void BuildHotspot(Transform parent)
    {
        Mesh ringMesh = LoadOrCreateRingMesh();
        Material ringMaterial = LoadOrCreateRingMaterial();

        GameObject hotspot = new GameObject(HotspotName);
        hotspot.transform.SetParent(parent, false);
        hotspot.transform.localPosition = Vector3.zero;
        hotspot.transform.localRotation = Quaternion.identity;
        hotspot.transform.localScale = Vector3.one;

        GameObject ring = new GameObject("SnapRing", typeof(MeshFilter), typeof(MeshRenderer));
        ring.transform.SetParent(hotspot.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.003f, 0f);
        ring.GetComponent<MeshFilter>().sharedMesh = ringMesh;
        ring.GetComponent<MeshRenderer>().sharedMaterial = ringMaterial;

        GameObject pngSlot = new GameObject("DropTransparentPNGHere", typeof(SpriteRenderer));
        pngSlot.transform.SetParent(hotspot.transform, false);
        pngSlot.transform.localPosition = new Vector3(0f, 0.004f, 0f);
        pngSlot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        pngSlot.transform.localScale = Vector3.one;

        SpriteRenderer spriteRenderer = pngSlot.GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = null;
        spriteRenderer.color = Color.white;
        spriteRenderer.sortingOrder = 1;
    }

    private static Mesh LoadOrCreateRingMesh()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RingMeshPath);
        if (mesh != null) return mesh;

        const int segments = 64;
        const float innerRadius = 0.205f;
        const float outerRadius = 0.225f;

        Vector3[] vertices = new Vector3[segments * 2];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[segments * 6];

        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);

            vertices[i * 2] = new Vector3(x * innerRadius, 0f, z * innerRadius);
            vertices[i * 2 + 1] = new Vector3(x * outerRadius, 0f, z * outerRadius);
            uv[i * 2] = new Vector2(0f, (float)i / segments);
            uv[i * 2 + 1] = new Vector2(1f, (float)i / segments);

            int next = (i + 1) % segments;
            int t = i * 6;
            triangles[t] = i * 2;
            triangles[t + 1] = next * 2 + 1;
            triangles[t + 2] = next * 2;
            triangles[t + 3] = i * 2;
            triangles[t + 4] = i * 2 + 1;
            triangles[t + 5] = next * 2 + 1;
        }

        mesh = new Mesh { name = "Yukine Hotspot Ring" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, RingMeshPath);
        return mesh;
    }

    private static Material LoadOrCreateRingMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(RingMaterialPath);
        if (material != null) return material;

        Shader shader = Shader.Find("VRChat/Mobile/Particles/Additive");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");

        material = new Material(shader) { name = "Yukine Hotspot Ring" };
        if (material.HasProperty("_Color")) material.SetColor("_Color", HotspotColor);
        if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", HotspotColor);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", HotspotColor * 0.8f);
        material.renderQueue = 3000;
        AssetDatabase.CreateAsset(material, RingMaterialPath);
        return material;
    }
}
