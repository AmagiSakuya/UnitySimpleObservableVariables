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
        // 策略下拉的显示文案，顺序必须与 DataHolderScope 的取值一一对应（0/1/2）。
        private static readonly string[] ScopeLabels =
        {
            "Nearest Parent（最近的父级 Holder）",
            "Ancestor Level（沿层级向上数第 N 个）",
            "Explicit（手动指定，可跨层级）",
        };

        // 手动绘制的字段，不能在 DrawPropertiesExcluding 里重复绘制。
        private static readonly string[] ManuallyDrawnFields =
        {
            "m_Script", "targetPropertyRef", "m_scope", "m_ancestorLevel", "m_explicitHolder", "forceNotifyOnEnable"
        };

        private void OnEnable()
        {
            // 层级发生任何变化（拖动父子关系、增删节点、撤销等）后立刻重绘面板，
            // 保证下一次绘制时会重新向上查找 DataHolder，而不是继续显示旧缓存。
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        private void OnHierarchyChanged()
        {
            Repaint();
        }

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

            // ==========================================
            // 2. 取用策略：决定「从哪个节点拿 DataHolder」
            // ==========================================
            SerializedProperty scopeProp = serializedObject.FindProperty("m_scope");
            SerializedProperty levelProp = serializedObject.FindProperty("m_ancestorLevel");
            SerializedProperty explicitProp = serializedObject.FindProperty("m_explicitHolder");

            // 先收集链路，供「按层级选择」下拉使用
            List<WatchPropertyDataHolder> ancestorChain = DataSelector.CollectAncestorHolders(script.transform);

            int scopeIndex = Mathf.Clamp(scopeProp.intValue, 0, ScopeLabels.Length - 1);
            int newScopeIndex = EditorGUILayout.Popup(
                new GUIContent("Context Scope", "决定从哪个节点取 DataHolder"),
                scopeIndex, ScopeLabels);

            if (newScopeIndex != scopeIndex)
            {
                scopeProp.intValue = newScopeIndex;
            }

            DataHolderScope scope = (DataHolderScope)scopeProp.intValue;

            if (scope == DataHolderScope.AncestorLevel)
            {
                DrawAncestorLevelPicker(script, levelProp, ancestorChain);
            }
            else if (scope == DataHolderScope.Explicit)
            {
                EditorGUILayout.PropertyField(explicitProp,
                    new GUIContent("DataHolder", "手动指定，可拖入同级 / 叔伯 / 其它分支上的 Holder"));
                if (explicitProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("尚未指定 DataHolder。把目标 DataHolder 从 Hierarchy 拖进来即可，不受父子关系限制。", MessageType.Info);
                }
            }

            // 策略改动先写回实例，下面解析 Holder 时才能拿到最新配置
            serializedObject.ApplyModifiedProperties();

            // ==========================================
            // 3. 解析 Holder
            // ==========================================
            WatchPropertyDataHolder holder = script.GetDataHolder(true);

            if (holder == null)
            {
                DrawMissingHolderHelp(script, scope, ancestorChain, levelProp);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            // 只读展示解析结果 + 层级路径，方便确认到底取到了哪一个
            GUI.enabled = false;
            EditorGUILayout.ObjectField("Resolved Holder", holder, typeof(WatchPropertyDataHolder), true);
            GUI.enabled = true;
            EditorGUILayout.LabelField("  ↳ " + GetHierarchyPath(holder.transform), EditorStyles.miniLabel);

            // ==========================================
            // 4. 利用反射获取 Holder 里所有满足条件的 WatchProperty 字段
            // ==========================================
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
                EditorGUILayout.HelpBox("WatchPropertyDataHolder 中没有找到任何 WatchProperty 属性！", MessageType.Info);
                serializedObject.ApplyModifiedProperties();
                return;
            }

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
                var dropdown = new IndexSearchDropdown(new AdvancedDropdownState(), "Select Property", displayNames, (selectedIndex) =>
                {
                    // 当在下拉框中选中某一项时的回调
                    nameProp.stringValue = rawPropertyNames[selectedIndex];
                    serializedObject.ApplyModifiedProperties();
                }, new Vector2(250, 300));

                dropdown.Show(buttonRect);
            }

            // 6. DataGetter 专属开关：OnEnable 时强制触发所选 WatchProperty 的 Notify
            SerializedProperty forceNotifyProp = serializedObject.FindProperty("forceNotifyOnEnable");
            if (forceNotifyProp != null)
            {
                EditorGUILayout.PropertyField(forceNotifyProp,
                    new GUIContent("在OnEnable时 强制触发Notify",
                        "勾选后，组件在 OnEnable 时会对所选 WatchProperty 调用 ForceNotifyCurrent()，强制广播一次当前值"));
            }

            // 7. 排除手动绘制的字段，绘制其余所有默认属性
            DrawPropertiesExcluding(serializedObject, ManuallyDrawnFields);

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 按层级选择 Holder：把链路上所有 Holder 连同层级路径列成下拉，选中的索引即层级。
        /// 比让用户手填一个 int 直观得多，也能一眼看出「到底会取到哪个节点」。
        /// </summary>
        private void DrawAncestorLevelPicker(DataSelector script, SerializedProperty levelProp, List<WatchPropertyDataHolder> chain)
        {
            if (chain.Count == 0)
            {
                EditorGUILayout.HelpBox("自身及祖先节点上没有任何 WatchPropertyDataHolder，无法按层级选择。可改用 Explicit 模式直接引用。", MessageType.Warning);
                return;
            }

            List<string> labels = new List<string>(chain.Count);
            for (int i = 0; i < chain.Count; i++)
            {
                labels.Add($"[{i + 1}] {GetHierarchyPath(chain[i].transform)}");
            }

            int level = Mathf.Clamp(levelProp.intValue, 1, chain.Count);
            string currentLabel = labels[level - 1];

            Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            Rect labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
            Rect buttonRect = new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y, rect.width - EditorGUIUtility.labelWidth, rect.height);

            EditorGUI.LabelField(labelRect, new GUIContent("Ancestor Level", "1 = 最近的父级 Holder，数字越大越靠上"));

            if (GUI.Button(buttonRect, new GUIContent(currentLabel), EditorStyles.popup))
            {
                var dropdown = new IndexSearchDropdown(new AdvancedDropdownState(), "选择 DataHolder", labels, (selectedIndex) =>
                {
                    levelProp.intValue = selectedIndex + 1;
                    serializedObject.ApplyModifiedProperties();
                    script.RefreshDataHolder();
                    Repaint();
                }, new Vector2(420, 260));

                dropdown.Show(buttonRect);
            }

            if (levelProp.intValue > chain.Count)
            {
                EditorGUILayout.HelpBox($"层级 {levelProp.intValue} 超出链路上的 Holder 数量（共 {chain.Count} 个），当前解析不到 Holder。", MessageType.Warning);
            }
        }

        private void DrawMissingHolderHelp(DataSelector script, DataHolderScope scope, List<WatchPropertyDataHolder> chain, SerializedProperty levelProp)
        {
            if (scope == DataHolderScope.Explicit)
            {
                // Explicit 模式下上方已经提示过「尚未指定」
                return;
            }

            if (chain.Count == 0)
            {
                EditorGUILayout.HelpBox("自身及所有祖先节点中都没有找到 WatchPropertyDataHolder 组件，请检查层级关系！\n也可以改用「Explicit（手动指定）」模式，直接引用场景中任意一个 Holder。", MessageType.Warning);
            }
            else if (scope == DataHolderScope.AncestorLevel)
            {
                EditorGUILayout.HelpBox($"层级 {levelProp.intValue} 超出链路上的 Holder 数量（共 {chain.Count} 个），当前解析不到 Holder。", MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox("在父级节点中未找到 WatchPropertyDataHolder 组件，请检查层级关系！", MessageType.Warning);
            }

            if (GUILayout.Button("重新向上查找 DataHolder"))
            {
                script.RefreshDataHolder();
                script.GetDataHolder(true);
                Repaint();
            }
        }

        /// <summary>拼出完整的层级路径，例如 "Canvas/HUD/Panel"。</summary>
        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "(null)";

            string path = t.name;
            Transform current = t.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
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
    /// 通用「带搜索的下拉框」：展示一组字符串，选中后回调其在列表中的索引。
    /// 属性选择、Holder 层级选择都复用它，避免写两份几乎一样的 AdvancedDropdown。
    /// </summary>
    public class IndexSearchDropdown : AdvancedDropdown
    {
        private readonly string rootTitle;
        private readonly List<string> displayNames;
        private readonly Action<int> onItemSelected;

        public IndexSearchDropdown(AdvancedDropdownState state, string rootTitle, List<string> displayNames, Action<int> onItemSelected, Vector2 minimumSize) : base(state)
        {
            this.rootTitle = rootTitle;
            this.displayNames = displayNames;
            this.onItemSelected = onItemSelected;

            // 设置下拉框弹出窗口的最小尺寸
            this.minimumSize = minimumSize;
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(rootTitle);

            for (int i = 0; i < displayNames.Count; i++)
            {
                root.AddChild(new IndexDropdownItem(displayNames[i], i));
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            base.ItemSelected(item);
            if (item is IndexDropdownItem customItem)
            {
                onItemSelected?.Invoke(customItem.Index);
            }
        }

        /// <summary>
        /// 自定义 Item 用来携带原始列表的 Index
        /// </summary>
        private class IndexDropdownItem : AdvancedDropdownItem
        {
            public int Index { get; }

            public IndexDropdownItem(string name, int index) : base(name)
            {
                Index = index;
            }
        }
    }
}
