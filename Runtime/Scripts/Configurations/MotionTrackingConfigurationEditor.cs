#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MotionTrackingConfiguration))]
public class MotionTrackingConfigurationEditor : Editor
{
    // cache the previous source to detect changes
    private MotionSource previousSource;

    private void OnEnable()
    {
        var config = (MotionTrackingConfiguration)target;
        previousSource = config.motionSource;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var config = (MotionTrackingConfiguration)target;

        // --- draw top-level config properties ---

        EditorGUILayout.PropertyField(serializedObject.FindProperty("configurationName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));

        EditorGUILayout.Space(5);

        // --- motion source dropdown with change detection ---

        SerializedProperty sourceProp = serializedObject.FindProperty("motionSource");
        EditorGUILayout.PropertyField(sourceProp);

        // detect source change — apply joint defaults when switching to a non-Custom source
        MotionSource currentSource = (MotionSource)sourceProp.enumValueIndex;
        if (currentSource != previousSource)
        {
            // apply immediately so the serialized data updates
            serializedObject.ApplyModifiedProperties();

            if (currentSource != MotionSource.Custom)
            {
                Undo.RecordObject(config, $"Apply {currentSource} joint defaults");
                config.ApplySourceDefaults();
                EditorUtility.SetDirty(config);
            }

            previousSource = currentSource;
            serializedObject.Update();
        }

        if (config.HasFixedJointNames)
        {
            EditorGUILayout.HelpBox(
                $"Joint names are auto-configured for {currentSource}. Switch to Custom to edit them manually.",
                MessageType.Info);
        }

        EditorGUILayout.Space(5);

        // --- system settings ---

        EditorGUILayout.PropertyField(serializedObject.FindProperty("calibrationDelay"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("calibrationFrames"));

        EditorGUILayout.Space(5);

        // --- modules list with conditional joint name display ---

        SerializedProperty modulesProp = serializedObject.FindProperty("modules");

        EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);

        if (modulesProp.arraySize == 0)
        {
            EditorGUILayout.HelpBox("No modules configured. Add modules below.", MessageType.None);
        }

        for (int i = 0; i < modulesProp.arraySize; i++)
        {
            SerializedProperty element = modulesProp.GetArrayElementAtIndex(i);

            if (element.managedReferenceValue == null)
            {
                EditorGUILayout.HelpBox($"Module {i} is null", MessageType.Warning);
                continue;
            }

            string typeName = element.managedReferenceValue.GetType().Name;
            string displayName = FormatModuleName(typeName);

            EditorGUILayout.Space(3);

            // foldout for each module
            element.isExpanded = EditorGUILayout.Foldout(element.isExpanded, displayName, true, EditorStyles.foldoutHeader);

            if (element.isExpanded)
            {
                EditorGUI.indentLevel++;
                DrawModuleProperties(element, config.HasFixedJointNames);
                EditorGUI.indentLevel--;
            }
        }

        // --- add/remove module buttons ---

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Add Module Configuration", EditorStyles.boldLabel);

        var moduleConfigTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(ModuleConfiguration)))
            .OrderBy(t => t.Name)
            .ToArray();

        foreach (var type in moduleConfigTypes)
        {
            bool alreadyAdded = config.modules.Any(m => m != null && m.GetType() == type);

            EditorGUI.BeginDisabledGroup(alreadyAdded);

            string label = alreadyAdded ? $"\u2713 {type.Name}" : $"+ Add {type.Name}";
            if (GUILayout.Button(label))
            {
                Undo.RecordObject(config, $"Add {type.Name}");
                var instance = (ModuleConfiguration)Activator.CreateInstance(type);

                // auto-apply joint defaults for the current source
                if (config.motionSource != MotionSource.Custom)
                {
                    instance.ApplyJointDefaults(config.motionSource);
                }

                config.modules.Add(instance);
                EditorUtility.SetDirty(config);
            }

            EditorGUI.EndDisabledGroup();
        }

        if (config.modules.Count > 0)
        {
            EditorGUILayout.Space(5);
            if (GUILayout.Button("Remove Last Module Config"))
            {
                Undo.RecordObject(config, "Remove Module Config");
                config.modules.RemoveAt(config.modules.Count - 1);
                EditorUtility.SetDirty(config);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    // draws a single module's properties, skipping joint name fields when source is fixed
    private void DrawModuleProperties(SerializedProperty element, bool hideJointNames)
    {
        SerializedProperty iterator = element.Copy();
        int startDepth = iterator.depth;
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;

            // stop when we've exited this element's children
            if (iterator.depth <= startDepth)
                break;

            // skip joint name fields when the source manages them
            if (hideJointNames && IsJointNameProperty(iterator.name))
                continue;

            EditorGUILayout.PropertyField(iterator, true);
        }
    }

    // checks if a serialized property name corresponds to a joint name field.
    // convention: all joint name fields end with "JointName" in their variable names.
    private bool IsJointNameProperty(string propertyName)
    {
        return propertyName.EndsWith("JointName", StringComparison.Ordinal);
    }

    // turns "ArmModuleConfiguration" into "Arm Module"
    private string FormatModuleName(string typeName)
    {
        string name = typeName.Replace("ModuleConfiguration", "").Replace("Configuration", "");
        // insert spaces before capitals
        string spaced = "";
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                spaced += " ";
            spaced += name[i];
        }
        return spaced + " Module";
    }
}
#endif