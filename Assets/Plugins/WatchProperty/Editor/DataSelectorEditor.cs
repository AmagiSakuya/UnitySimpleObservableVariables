using MTST_RCS;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls; // 引入 AdvancedDropdown 所需的命名空间
using UnityEngine;

namespace AmagiSakuya.ObservableVariables.ObservableVariablesEditor
{
    [CustomEditor(typeof(DataSelector), true)]
    public class DataSelectorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DataSelector script = (DataSelector)target;

            // 1. 单独在最上面绘制 Script 字段
            SerializedProperty scriptProp = serializedObject.FindProperty("m_Script");
            if (scriptProp != null)
            {
                GUI.enabled = false;
                EditorGUILayout.PropertyField(scriptProp);
                GUI.enabled = true;
            }

            // 3. 寻找父级的 Holder
            MTST_RCSDataHolder holder = script.GetHolder();

            if (holder == null)
            {
                EditorGUILayout.HelpBox("在父级节点中未找到 MTST_RCSDataHolder 组件，请检查层级关系！", MessageType.Warning);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            // 4. 利用反射获取 Holder 里所有满足条件的 WatchProperty 字段
            FieldInfo[] fields = holder.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);

            List<string> rawPropertyNames = new List<string>();
            List<string> displayNames = new List<string>();

            foreach (var field in fields)
            {
                if (IsSubclassOfRawGeneric(typeof(WatchProperty<>), field.FieldType, out Type genericArgument))
                {
                    rawPropertyNames.Add(field.Name);
                    string typeName = genericArgument != null ? GetFriendlyTypeName(genericArgument) : "Unknown";
                    displayNames.Add($"{field.Name} [{typeName}]");
                }
            }

            if (rawPropertyNames.Count == 0)
            {
                EditorGUILayout.HelpBox("MTST_RCSDataHolder 中没有找到任何 WatchProperty 属性！", MessageType.Info);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            // 提示当前绑定的父级是谁（置灰显示）
            GUI.enabled = false;
            EditorGUILayout.ObjectField("DataHolder", holder, typeof(MTST_RCSDataHolder), true);
            GUI.enabled = true;

            // 5. 单独自定义绘制 targetPropertyRef 属性为支持搜索的下拉菜单
            SerializedProperty refProp = serializedObject.FindProperty("targetPropertyRef");
            SerializedProperty nameProp = refProp.FindPropertyRelative("propertyName");

            // ==========================================
            // 检查：如果当前保存的值已经从数据源丢失，进行红牌报错
            // ==========================================
            bool isPropertyLost = !string.IsNullOrEmpty(nameProp.stringValue) && !rawPropertyNames.Contains(nameProp.stringValue);
            if (isPropertyLost)
            {
                EditorGUILayout.HelpBox($"【数据丢失报错】先前选择的属性「{nameProp.stringValue}」在 {holder.GetType().Name} 中已被删除或重命名！请重新选择！", MessageType.Error);
            }

            // 根据保存的干净字段名去对齐索引
            int currentIndex = rawPropertyNames.IndexOf(nameProp.stringValue);

            // 获取当前应该显示的 Text 内容
            string currentDisplayText = "None";
            if (currentIndex != -1)
            {
                currentDisplayText = displayNames[currentIndex];
            }
            else if (isPropertyLost)
            {
                currentDisplayText = $"!! MISSING: {nameProp.stringValue} !!";
            }
            else if (rawPropertyNames.Count > 0)
            {
                // 默认选择第一个
                currentDisplayText = displayNames[0];
                nameProp.stringValue = rawPropertyNames[0];
            }

            // ==========================================
            // 替换 Popup 为带搜索框的 DropdownButton
            // ==========================================
            Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            Rect labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
            Rect buttonRect = new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y, rect.width - EditorGUIUtility.labelWidth, rect.height);

            EditorGUI.LabelField(labelRect, "Target Property");

            if (GUI.Button(buttonRect, new GUIContent(currentDisplayText), EditorStyles.popup))
            {
                // 创建并弹出支持搜索的下拉框
                var dropdown = new PropertySearchDropdown(new AdvancedDropdownState(), displayNames, (selectedIndex) =>
                {
                    // 当在下拉框中选中某一项时的回调
                    nameProp.stringValue = rawPropertyNames[selectedIndex];
                    serializedObject.ApplyModifiedProperties();
                });

                dropdown.Show(buttonRect);
            }

            // 2. 排除 m_Script 和 targetPropertyRef，绘制其余所有默认属性
            DrawPropertiesExcluding(serializedObject, new string[] { "m_Script", "targetPropertyRef" });

            serializedObject.ApplyModifiedProperties();
        }

        private bool IsSubclassOfRawGeneric(Type generic, Type toCheck, out Type genericArgument)
        {
            genericArgument = null;
            while (toCheck != null && toCheck != typeof(object))
            {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == cur)
                {
                    genericArgument = toCheck.GetGenericArguments()[0];
                    return true;
                }
                toCheck = toCheck.BaseType;
            }
            return false;
        }

        private string GetFriendlyTypeName(Type type)
        {
            if (type == typeof(float)) return "Float";
            if (type == typeof(int)) return "Int";
            if (type == typeof(bool)) return "Bool";
            if (type == typeof(string)) return "String";
            return type.Name;
        }
    }

    /// <summary>
    /// 自定义可搜索的 AdvancedDropdown 列表
    /// </summary>
    public class PropertySearchDropdown : AdvancedDropdown
    {
        private readonly List<string> displayNames;
        private readonly Action<int> onItemSelected;

        public PropertySearchDropdown(AdvancedDropdownState state, List<string> displayNames, Action<int> onItemSelected) : base(state)
        {
            this.displayNames = displayNames;
            this.onItemSelected = onItemSelected;

            // 设置下拉框弹出窗口的最小尺寸
            this.minimumSize = new Vector2(250, 300);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Select Property");

            for (int i = 0; i < displayNames.Count; i++)
            {
                var item = new PropertyDropdownItem(displayNames[i], i);
                root.AddChild(item);
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            base.ItemSelected(item);
            if (item is PropertyDropdownItem customItem)
            {
                onItemSelected?.Invoke(customItem.Index);
            }
        }

        /// <summary>
        /// 自定义 Item 用来携带原始列表的 Index
        /// </summary>
        private class PropertyDropdownItem : AdvancedDropdownItem
        {
            public int Index { get; }

            public PropertyDropdownItem(string name, int index) : base(name)
            {
                Index = index;
            }
        }
    }
}