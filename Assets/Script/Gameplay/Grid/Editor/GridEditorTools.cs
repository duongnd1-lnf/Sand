#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GridObject), true)]
[CanEditMultipleObjects]
public class GridObjectEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GridObject gridObj = (GridObject)target;

        if (gridObj is GridRectObject rectObj)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Quick Size", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("1 x 1"))
            {
                Undo.RecordObject(rectObj, "Set Size 1x1");
                rectObj.Size = new Vector2Int(1, 1);
                EditorUtility.SetDirty(rectObj);
            }
            if (GUILayout.Button("2 x 1"))
            {
                Undo.RecordObject(rectObj, "Set Size 2x1");
                rectObj.Size = new Vector2Int(2, 1);
                EditorUtility.SetDirty(rectObj);
            }
            if (GUILayout.Button("1 x 2"))
            {
                Undo.RecordObject(rectObj, "Set Size 1x2");
                rectObj.Size = new Vector2Int(1, 2);
                EditorUtility.SetDirty(rectObj);
            }
            if (GUILayout.Button("2 x 2"))
            {
                Undo.RecordObject(rectObj, "Set Size 2x2");
                rectObj.Size = new Vector2Int(2, 2);
                EditorUtility.SetDirty(rectObj);
            }
            EditorGUILayout.EndHorizontal();
        }
        else if (gridObj is GridCustomObject customObj)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Custom Shape Tools", EditorStyles.boldLabel);

            if (GUILayout.Button("Normalize Pivot to Origin (0,0)"))
            {
                Undo.RecordObject(customObj, "Normalize Pivot");
                customObj.NormalizeOffsets();
                EditorUtility.SetDirty(customObj);
            }
        }
    }
}

[CustomEditor(typeof(GridRenderer))]
public class GridRendererEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GridRenderer renderer = (GridRenderer)target;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Tilemap & Grid Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Rebuild Grid"))
        {
            Undo.RecordObject(renderer, "Rebuild Grid");
            renderer.RebuildGrid();
            EditorUtility.SetDirty(renderer);
        }

        if (GUILayout.Button("Clear Tilemaps"))
        {
            Undo.RecordObject(renderer, "Clear Tilemaps");
            renderer.ClearTilemaps();
            EditorUtility.SetDirty(renderer);
        }
    }
}
#endif
