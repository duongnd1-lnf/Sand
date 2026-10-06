#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GridObject))]
[CanEditMultipleObjects]
public class GridObjectEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GridObject gridObj = (GridObject)target;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Snap To Nearest Cell"))
        {
            Undo.RecordObject(gridObj.transform, "Snap To Grid");
            gridObj.SnapToNearestCell();
            EditorUtility.SetDirty(gridObj);
        }

        if (GUILayout.Button("Update Collider"))
        {
            gridObj.ConfigureCollider();
            EditorUtility.SetDirty(gridObj);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("1 x 1"))
        {
            Undo.RecordObject(gridObj, "Set Size 1x1");
            gridObj.Size = new Vector2Int(1, 1);
            EditorUtility.SetDirty(gridObj);
        }
        if (GUILayout.Button("2 x 1"))
        {
            Undo.RecordObject(gridObj, "Set Size 2x1");
            gridObj.Size = new Vector2Int(2, 1);
            EditorUtility.SetDirty(gridObj);
        }
        if (GUILayout.Button("1 x 2"))
        {
            Undo.RecordObject(gridObj, "Set Size 1x2");
            gridObj.Size = new Vector2Int(1, 2);
            EditorUtility.SetDirty(gridObj);
        }
        if (GUILayout.Button("2 x 2"))
        {
            Undo.RecordObject(gridObj, "Set Size 2x2");
            gridObj.Size = new Vector2Int(2, 2);
            EditorUtility.SetDirty(gridObj);
        }
        EditorGUILayout.EndHorizontal();
    }
}

[CustomEditor(typeof(GridManager))]
public class GridManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GridManager manager = (GridManager)target;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Grid Setup Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Auto-Fit Bounds from Background Tilemap"))
        {
            Undo.RecordObject(manager, "Fit Tilemap Bounds");
            manager.RefreshBoundaries();
            EditorUtility.SetDirty(manager);
        }

        if (GUILayout.Button("Snap & Register All Scene Objects"))
        {
            manager.RegisterAllSceneObjects();
            EditorUtility.SetDirty(manager);
        }
    }
}
#endif
