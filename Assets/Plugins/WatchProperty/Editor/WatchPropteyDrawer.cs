using UnityEditor;
using UnityEngine;

namespace AmagiSakuya.ObservableVariables.ObservableVariablesEditor
{
    [CustomPropertyDrawer(typeof(WatchProperty<>), true)]
    public class WatchPropteyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var valueProperty = property.FindPropertyRelative("m_editorValue");
            object targetObject = property.serializedObject.targetObject;
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(position, valueProperty, label);
            if (EditorGUI.EndChangeCheck())
            {
                property.serializedObject.ApplyModifiedProperties();
            }
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
            //return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
}

