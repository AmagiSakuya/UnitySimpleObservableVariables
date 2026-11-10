using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AmagiSakuya.ObservableVariables.ObservableVariablesEditor
{
    /// <summary>
    /// DataHolder 替换与拆分工具。
    /// 拆分：把当前 Holder 里勾选的属性生成一份新的 WatchPropertyDataHolder 脚本。
    /// 替换：用另一个 Holder 脚本完整替换当前组件，保留同名同类型属性的值，并修复 DataSelector 的引用。
    /// </summary>
    public class DataHolderReplaceSplitWindow : EditorWindow
    {
        private const string MenuPath = "Tools/ObservableVariables/DataHolder 替换与拆分工具";

        [MenuItem(MenuPath)]
        private static void Open()
        {
            DataHolderReplaceSplitWindow window = GetWindow<DataHolderReplaceSplitWindow>();
            window.titleContent = new GUIContent("DataHolder 替换与拆分工具");
            window.minSize = new Vector2(620, 660);
            window.Show();
        }

        // 数据源
        [SerializeField] private GameObject m_SourceObject;
        [SerializeField] private int m_HolderIndex;
        private WatchPropertyDataHolder m_Holder;

        // 属性列表
        [SerializeField] private string m_SearchText = string.Empty;
        [SerializeField] private bool m_OnlyReferenced;
        private readonly HashSet<string> m_SplitSelection = new HashSet<string>();
        private Vector2 m_Scroll;
        private Vector2 m_PropertyScroll;

        // 引用扫描
        private readonly Dictionary<string, List<DataSelector>> m_References = new Dictionary<string, List<DataSelector>>();
        private readonly List<DataSelector> m_OrphanSelectors = new List<DataSelector>();
        private readonly List<string> m_OrphanNames = new List<string>();
        private int m_TotalSelectorCount;
        private int m_ReferencingSelectorCount;
        private bool m_ScanDirty = true;
        private bool m_ShowReferenceDetails;

        // 代码引用扫描（仅用于显示，不参与替换判断）
        private Type m_CodeScanType;
        private Dictionary<string, List<CodeReference>> m_CodeReferences;
        private int m_CodeScanFileCount;
        private bool m_ShowCodeDetails;
        private readonly HashSet<string> m_InternalScriptPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool m_InternalScriptDirty = true;
        private GameObject m_InternalScriptSource;
        private static readonly Regex MemberAccessRegex =
            new Regex(@"\.\s*([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
        private static readonly List<CodeReference> EmptyCodeReferences = new List<CodeReference>();

        // 拆分
        [SerializeField] private string m_SplitClassName;
        [SerializeField] private string m_SplitNamespace;
        [SerializeField] private DefaultAsset m_SplitFolder;
        [SerializeField] private bool m_UseCurrentValues = true;

        // 替换
        [SerializeField] private MonoScript m_TargetScript;
        private Type m_TargetType;
        private bool m_AllowIncompleteReplace;
        private bool m_ShowCompatibleFields = true;
        private bool m_ShowExtraFields;
        private bool m_CompareDirty = true;
        private Type m_ComparedTargetType;
        private WatchPropertyDataHolder m_ComparedHolder;
        private readonly List<CompareItem> m_CompareItems = new List<CompareItem>();
        private readonly List<CompareItem> m_ExtraItems = new List<CompareItem>();

        // 提示信息
        private string m_Message;
        private MessageType m_MessageType = MessageType.Info;

        // 缓存
        private Type m_CachedFieldOwner;
        private List<WatchFieldInfo> m_CachedFields;
        private static readonly List<DataSelector> EmptySelectors = new List<DataSelector>();
        private static readonly List<WatchFieldInfo> EmptyFields = new List<WatchFieldInfo>();
        private static List<Type> s_AllTypes;
        private static List<Type> s_HolderTypes;

        private class WatchFieldInfo
        {
            public FieldInfo Field;
            public Type GenericArgument;
            public string Name { get { return Field.Name; } }
        }

        private class CompareItem
        {
            public string Name;
            public string TypeLabel;
            public bool Missing;
            public bool TypeMismatch;
        }

        private class CodeReference
        {
            public string AssetPath;
            public int Line;
        }

        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.undoRedoPerformed += OnUndoRedo;

            if (m_TargetScript != null)
            {
                m_TargetType = ResolveHolderType(m_TargetScript);
            }

            if (m_SourceObject == null)
            {
                TryAdoptSelection();
            }
            else
            {
                RefreshHolder(false);
            }
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnHierarchyChanged()
        {
            if (m_SourceObject != null)
            {
                RefreshHolder(false);
            }
            else
            {
                m_Holder = null;
            }

            m_ScanDirty = true;
            m_CompareDirty = true;
            m_InternalScriptDirty = true;
            Repaint();
        }

        private void OnUndoRedo()
        {
            OnHierarchyChanged();
        }

        private void OnSelectionChange()
        {
            if (m_SourceObject == null)
            {
                TryAdoptSelection();
            }
            Repaint();
        }

        private void TryAdoptSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null || EditorUtility.IsPersistent(selected)) return;
            if (selected.GetComponent<WatchPropertyDataHolder>() == null) return;
            SetSource(selected);
        }

        private void SetSource(GameObject source)
        {
            m_SourceObject = source;
            m_HolderIndex = 0;
            m_Holder = null;
            m_CachedFieldOwner = null;
            m_CachedFields = null;
            m_SplitSelection.Clear();
            m_TargetScript = null;
            m_TargetType = null;
            m_CompareDirty = true;
            m_ScanDirty = true;
            m_InternalScriptDirty = true;
            m_Message = null;
            RefreshHolder(true);
        }

        private void RefreshHolder(bool resetDefaults)
        {
            WatchPropertyDataHolder[] holders = null;
            if (m_SourceObject != null)
            {
                holders = m_SourceObject.GetComponents<WatchPropertyDataHolder>();
            }

            if (holders == null || holders.Length == 0)
            {
                m_Holder = null;
                m_CachedFieldOwner = null;
                m_CachedFields = null;
                return;
            }

            m_HolderIndex = Mathf.Clamp(m_HolderIndex, 0, holders.Length - 1);
            m_Holder = holders[m_HolderIndex];
            m_CachedFieldOwner = null;
            m_CachedFields = null;

            if (resetDefaults)
            {
                ResetSplitDefaults();
            }

            m_ScanDirty = true;
            m_CompareDirty = true;
        }

        private void ResetSplitDefaults()
        {
            if (m_Holder == null) return;

            m_SplitClassName = m_Holder.GetType().Name + "_Split";
            m_SplitNamespace = m_Holder.GetType().Namespace ?? string.Empty;
            m_SplitFolder = null;

            MonoScript script = MonoScript.FromMonoBehaviour(m_Holder);
            if (script == null) return;

            string path = AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrEmpty(path)) return;

            string folder = Path.GetDirectoryName(path);
            if (folder == null) return;
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder))
            {
                m_SplitFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
            }
        }

        private void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            DrawSourceSection();

            if (m_SourceObject == null)
            {
                EditorGUILayout.HelpBox(
                    "请选择一个挂载了 WatchPropertyDataHolder 子类组件的场景 GameObject。\n也可以先在 Hierarchy 中选中目标物体，再打开本窗口。",
                    MessageType.Info);
            }
            else if (m_Holder == null)
            {
                EditorGUILayout.HelpBox(
                    "所选 GameObject 上没有挂载任何继承自 WatchPropertyDataHolder 的组件。",
                    MessageType.Warning);
            }
            else
            {
                EnsureReferenceScan();
                EnsureCodeScan();
                DrawReferenceOverview();
                DrawPropertyList();
                DrawSplitSection();
                DrawReplaceSection();
            }

            if (!string.IsNullOrEmpty(m_Message))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(m_Message, m_MessageType);
            }

            EditorGUILayout.EndScrollView();
        }

        #region 数据源与引用扫描

        private void DrawSourceSection()
        {
            EditorGUILayout.LabelField("① 选择数据源", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            GameObject newSource = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("源 GameObject", "必须是场景中的物体，且挂载了 WatchPropertyDataHolder 子类组件"),
                m_SourceObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                if (newSource == null || !EditorUtility.IsPersistent(newSource))
                {
                    SetSource(newSource);
                }
                else
                {
                    m_Message = "请选择场景中的 GameObject，而不是 Project 中的预制体资源。";
                    m_MessageType = MessageType.Warning;
                }
            }

            if (GUILayout.Button("使用当前选中", GUILayout.Width(100)))
            {
                GameObject selected = Selection.activeGameObject;
                if (selected != null && selected.GetComponent<WatchPropertyDataHolder>() != null)
                {
                    SetSource(selected);
                }
                else
                {
                    m_Message = "当前选中的 GameObject 没有挂载 WatchPropertyDataHolder 子类组件。";
                    m_MessageType = MessageType.Warning;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (m_SourceObject == null) return;

            WatchPropertyDataHolder[] holders = m_SourceObject.GetComponents<WatchPropertyDataHolder>();
            if (holders.Length == 0) return;

            if (holders.Length > 1)
            {
                string[] labels = new string[holders.Length];
                for (int i = 0; i < holders.Length; i++)
                {
                    labels[i] = "[" + i + "] " + holders[i].GetType().Name;
                }

                int newIndex = EditorGUILayout.Popup("选择 Holder 组件", Mathf.Clamp(m_HolderIndex, 0, holders.Length - 1), labels);
                if (newIndex != m_HolderIndex)
                {
                    m_HolderIndex = newIndex;
                    RefreshHolder(true);
                }
            }

            if (m_Holder != null)
            {
                EditorGUILayout.LabelField("Holder 类型", m_Holder.GetType().FullName);
            }
        }

        private void EnsureReferenceScan()
        {
            if (!m_ScanDirty) return;
            m_ScanDirty = false;

            m_References.Clear();
            m_OrphanSelectors.Clear();
            m_OrphanNames.Clear();
            m_TotalSelectorCount = 0;
            m_ReferencingSelectorCount = 0;

            if (m_Holder == null) return;

            HashSet<string> validNames = new HashSet<string>();
            foreach (WatchFieldInfo info in GetFieldsOfHolder())
            {
                validNames.Add(info.Name);
            }

            foreach (GameObject root in GetRootGameObjects())
            {
                foreach (DataSelector selector in root.GetComponentsInChildren<DataSelector>(true))
                {
                    m_TotalSelectorCount++;

                    WatchPropertyDataHolder resolved;
                    try
                    {
                        resolved = selector.GetDataHolder(true);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (resolved != m_Holder) continue;
                    m_ReferencingSelectorCount++;

                    string propertyName = selector.targetPropertyRef != null
                        ? selector.targetPropertyRef.propertyName
                        : null;
                    if (string.IsNullOrEmpty(propertyName)) continue;

                    if (validNames.Contains(propertyName))
                    {
                        List<DataSelector> list;
                        if (!m_References.TryGetValue(propertyName, out list))
                        {
                            list = new List<DataSelector>();
                            m_References[propertyName] = list;
                        }
                        list.Add(selector);
                    }
                    else
                    {
                        m_OrphanSelectors.Add(selector);
                        m_OrphanNames.Add(propertyName);
                    }
                }
            }
        }

        private void DrawReferenceOverview()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("② 属性引用情况（绿色高亮 = 被 DataSelector 引用）", EditorStyles.boldLabel);
            if (GUILayout.Button("重扫面板", GUILayout.Width(70)))
            {
                m_ScanDirty = true;
                EnsureReferenceScan();
            }
            if (GUILayout.Button("重扫代码", GUILayout.Width(70)))
            {
                m_CodeScanType = null;
                m_InternalScriptDirty = true;
                EnsureCodeScan();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "当前场景共 " + m_TotalSelectorCount + " 个 DataSelector，其中 " + m_ReferencingSelectorCount +
                " 个通过层级或显式引用指向此 Holder（只扫描当前激活场景）。\n" +
                "「内 N」= 该 GameObject 及其子层级上挂载的脚本中的代码引用；「外 N」= 其它脚本中的代码引用。" +
                "代码引用仅用于排查显示，不参与替换判断。",
                MessageType.None);

            int internalTotal = 0;
            int externalTotal = 0;
            int referencedFieldCount = 0;
            CountCodeReferenceTotals(out internalTotal, out externalTotal, out referencedFieldCount);

            EditorGUILayout.LabelField(
                "代码引用：已扫描 " + m_CodeScanFileCount + " 个 C# 脚本，内部 " + internalTotal +
                " 处 / 外部 " + externalTotal + " 处（" + referencedFieldCount + " 个属性存在引用）。",
                EditorStyles.miniLabel);

            m_ShowReferenceDetails = EditorGUILayout.Foldout(m_ShowReferenceDetails, "面板引用明细（DataSelector）");
            if (m_ShowReferenceDetails)
            {
                if (m_References.Count == 0)
                {
                    EditorGUILayout.LabelField("没有被引用的属性。", EditorStyles.miniLabel);
                }
                else
                {
                    List<string> names = new List<string>(m_References.Keys);
                    names.Sort(StringComparer.Ordinal);
                    foreach (string name in names)
                    {
                        EditorGUILayout.LabelField(name + "  ×  " + m_References[name].Count, EditorStyles.boldLabel);
                        foreach (DataSelector selector in m_References[name])
                        {
                            DrawSelectorLine(selector, null);
                        }
                    }
                }
            }

            m_ShowCodeDetails = EditorGUILayout.Foldout(m_ShowCodeDetails, "代码引用明细（仅显示，不参与替换）");
            if (m_ShowCodeDetails)
            {
                DrawCodeReferenceDetails();
            }

            if (m_OrphanSelectors.Count > 0)
            {
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.75f, 0.75f);
                EditorGUILayout.BeginVertical(GUI.skin.box);
                GUI.backgroundColor = oldBg;
                EditorGUILayout.LabelField(
                    "发现 " + m_OrphanSelectors.Count + " 个 DataSelector 指向此 Holder，但所选属性名已不存在：",
                    EditorStyles.boldLabel);
                for (int i = 0; i < m_OrphanSelectors.Count; i++)
                {
                    DrawSelectorLine(m_OrphanSelectors[i], m_OrphanNames[i]);
                }
                EditorGUILayout.EndVertical();
                GUI.backgroundColor = oldBg;
            }
        }

        private void DrawSelectorLine(DataSelector selector, string propertyName)
        {
            EditorGUILayout.BeginHorizontal();
            string label = "↳ " + GetHierarchyPath(selector.transform);
            if (!string.IsNullOrEmpty(propertyName))
            {
                label += "  →  " + propertyName;
            }
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(50)))
            {
                FocusSelector(selector);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void FocusSelector(DataSelector selector)
        {
            if (selector == null) return;
            Selection.activeGameObject = selector.gameObject;
            EditorGUIUtility.PingObject(selector);
        }

        private List<DataSelector> GetReferences(string propertyName)
        {
            List<DataSelector> list;
            if (m_References.TryGetValue(propertyName, out list))
            {
                return list;
            }
            return EmptySelectors;
        }

        private void DrawCodeReferenceDetails()
        {
            if (GetFieldsOfHolder().Count == 0)
            {
                EditorGUILayout.LabelField("当前 Holder 没有可统计的属性。", EditorStyles.miniLabel);
                return;
            }

            bool anyReference = false;
            int displayedLimit = 30;

            foreach (WatchFieldInfo info in GetFieldsOfHolder())
            {
                List<CodeReference> references = GetCodeReferences(info.Name);
                if (references.Count == 0) continue;

                anyReference = true;
                int internalCount = CountCodeReferences(info.Name, true);
                int externalCount = CountCodeReferences(info.Name, false);

                EditorGUILayout.LabelField(
                    info.Name + "  ×  内部 " + internalCount + " / 外部 " + externalCount,
                    EditorStyles.boldLabel);

                DrawCodeReferenceGroup(info.Name, true, displayedLimit);
                DrawCodeReferenceGroup(info.Name, false, displayedLimit);
            }

            if (!anyReference)
            {
                EditorGUILayout.LabelField("没有属性被其他脚本通过 .字段名.成员 方式引用。", EditorStyles.miniLabel);
            }
        }

        private void DrawCodeReferenceGroup(string propertyName, bool internalOnly, int displayedLimit)
        {
            List<CodeReference> references = GetCodeReferences(propertyName);
            int total = CountCodeReferences(propertyName, internalOnly);

            EditorGUILayout.LabelField(
                (internalOnly ? "内部（本 GameObject 层级脚本）：" : "外部（其它脚本）：") + total,
                EditorStyles.miniLabel);

            if (total == 0) return;

            int shown = 0;
            for (int i = 0; i < references.Count && shown < displayedLimit; i++)
            {
                CodeReference reference = references[i];
                if (IsInternalCodeReference(reference) != internalOnly) continue;

                shown++;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    "↳ " + reference.AssetPath + " : " + reference.Line,
                    EditorStyles.miniLabel);
                if (GUILayout.Button("打开", EditorStyles.miniButton, GUILayout.Width(44)))
                {
                    OpenCodeReference(reference);
                }
                EditorGUILayout.EndHorizontal();
            }

            if (total > shown)
            {
                EditorGUILayout.LabelField(
                    "…… 还有 " + (total - shown) + " 处未显示",
                    EditorStyles.miniLabel);
            }
        }

        private List<CodeReference> GetCodeReferences(string propertyName)
        {
            if (m_CodeReferences == null) return EmptyCodeReferences;

            List<CodeReference> list;
            if (m_CodeReferences.TryGetValue(propertyName, out list))
            {
                return list;
            }
            return EmptyCodeReferences;
        }

        private bool IsInternalCodeReference(CodeReference reference)
        {
            return reference != null && m_InternalScriptPaths.Contains(reference.AssetPath);
        }

        private int CountCodeReferences(string propertyName, bool internalOnly)
        {
            List<CodeReference> references = GetCodeReferences(propertyName);
            int count = 0;

            for (int i = 0; i < references.Count; i++)
            {
                if (IsInternalCodeReference(references[i]) == internalOnly)
                {
                    count++;
                }
            }

            return count;
        }

        private CodeReference GetFirstCodeReference(string propertyName, bool internalOnly)
        {
            List<CodeReference> references = GetCodeReferences(propertyName);

            for (int i = 0; i < references.Count; i++)
            {
                if (IsInternalCodeReference(references[i]) == internalOnly)
                {
                    return references[i];
                }
            }

            return null;
        }

        private void CountCodeReferenceTotals(
            out int internalTotal, out int externalTotal, out int referencedFieldCount)
        {
            internalTotal = 0;
            externalTotal = 0;
            referencedFieldCount = 0;

            foreach (WatchFieldInfo info in GetFieldsOfHolder())
            {
                int internalCount = CountCodeReferences(info.Name, true);
                int externalCount = CountCodeReferences(info.Name, false);

                internalTotal += internalCount;
                externalTotal += externalCount;

                if (internalCount + externalCount > 0)
                {
                    referencedFieldCount++;
                }
            }
        }

        private void OpenCodeReference(CodeReference reference)
        {
            if (reference == null) return;

            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(reference.AssetPath);
            if (script == null) return;

            AssetDatabase.OpenAsset(script, reference.Line);
        }

        /// <summary>
        /// 扫描 Assets 下的 C# 脚本，统计「.字段名.成员」形式的代码访问。
        /// 结果只用于显示，替换流程不会读取它。
        /// </summary>
        private void EnsureCodeScan()
        {
            if (m_Holder == null) return;

            Type holderType = m_Holder.GetType();
            if (m_CodeScanType != holderType || m_CodeReferences == null)
            {
                m_CodeScanType = holderType;
                m_CodeReferences = ScanCodeReferences(holderType, out m_CodeScanFileCount);
            }

            EnsureInternalScriptSet();
        }

        /// <summary>
        /// 收集当前 GameObject 及其所有子层级上挂载的脚本文件，用于把代码引用划分为「内部 / 外部」。
        /// 按脚本文件划分：同一个脚本文件被挂载在层级内即视为内部。
        /// </summary>
        private void EnsureInternalScriptSet()
        {
            if (!m_InternalScriptDirty && m_InternalScriptSource == m_SourceObject) return;

            m_InternalScriptDirty = false;
            m_InternalScriptSource = m_SourceObject;
            m_InternalScriptPaths.Clear();

            if (m_SourceObject == null) return;

            MonoBehaviour[] behaviours = m_SourceObject.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null) continue;

                MonoScript script = MonoScript.FromMonoBehaviour(behaviour);
                if (script == null) continue;

                string assetPath = AssetDatabase.GetAssetPath(script);
                if (string.IsNullOrEmpty(assetPath)) continue;

                m_InternalScriptPaths.Add(assetPath);
            }
        }

        private Dictionary<string, List<CodeReference>> ScanCodeReferences(
            Type holderType, out int scannedFileCount)
        {
            scannedFileCount = 0;
            Dictionary<string, List<CodeReference>> result =
                new Dictionary<string, List<CodeReference>>();

            HashSet<string> fieldNames = new HashSet<string>();
            foreach (WatchFieldInfo info in GetPublicWatchFields(holderType))
            {
                fieldNames.Add(info.Name);
            }
            if (fieldNames.Count == 0) return result;

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot)) return result;

            string assetsRoot = Path.Combine(projectRoot, "Assets");
            if (!Directory.Exists(assetsRoot)) return result;

            string holderScriptPath = GetHolderScriptAbsolutePath();
            string selfScriptPath = GetSelfScriptAbsolutePath();

            foreach (string fullPath in Directory.GetFiles(assetsRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (IsSamePath(fullPath, holderScriptPath) || IsSamePath(fullPath, selfScriptPath))
                {
                    continue;
                }

                scannedFileCount++;

                string assetPath = "Assets" + fullPath.Substring(assetsRoot.Length).Replace('\\', '/');
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(fullPath);
                }
                catch (Exception)
                {
                    continue;
                }

                bool inBlockComment = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripComments(lines[i], ref inBlockComment);
                    if (code.Length == 0) continue;

                    MatchCollection matches = MemberAccessRegex.Matches(code);
                    foreach (Match match in matches)
                    {
                        string propertyName = match.Groups[1].Value;
                        if (!fieldNames.Contains(propertyName)) continue;

                        // 只统计「.字段名.xxx」（例如 holder.speed.Value / speed?.Value），
                        // 避免把其它类型里同名的普通成员访问也算进来。
                        int cursor = match.Groups[1].Index + match.Groups[1].Length;
                        while (cursor < code.Length && char.IsWhiteSpace(code[cursor])) cursor++;
                        bool chained = cursor < code.Length && code[cursor] == '.';
                        if (!chained && cursor + 1 < code.Length
                            && code[cursor] == '?' && code[cursor + 1] == '.')
                        {
                            chained = true;
                        }
                        if (!chained) continue;

                        List<CodeReference> list;
                        if (!result.TryGetValue(propertyName, out list))
                        {
                            list = new List<CodeReference>();
                            result[propertyName] = list;
                        }

                        CodeReference reference = new CodeReference();
                        reference.AssetPath = assetPath;
                        reference.Line = i + 1;
                        list.Add(reference);
                    }
                }
            }

            return result;
        }

        private string GetHolderScriptAbsolutePath()
        {
            if (m_Holder == null) return null;

            MonoScript script = MonoScript.FromMonoBehaviour(m_Holder);
            if (script == null) return null;

            string assetPath = AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrEmpty(assetPath)) return null;

            return GetAbsoluteProjectPath(assetPath);
        }

        private string GetSelfScriptAbsolutePath()
        {
            MonoScript script = MonoScript.FromScriptableObject(this);
            if (script == null) return null;

            string assetPath = AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrEmpty(assetPath)) return null;

            return GetAbsoluteProjectPath(assetPath);
        }

        private static bool IsSamePath(string path, string other)
        {
            if (path == null || other == null) return false;
            return string.Equals(path, other, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripComments(string line, ref bool inBlockComment)
        {
            if (inBlockComment)
            {
                int blockEnd = line.IndexOf("*/", StringComparison.Ordinal);
                if (blockEnd < 0) return string.Empty;
                line = line.Substring(blockEnd + 2);
                inBlockComment = false;
            }

            int blockStart = line.IndexOf("/*", StringComparison.Ordinal);
            if (blockStart >= 0)
            {
                int blockEnd = line.IndexOf("*/", blockStart + 2, StringComparison.Ordinal);
                if (blockEnd < 0)
                {
                    line = line.Substring(0, blockStart);
                    inBlockComment = true;
                }
                else
                {
                    line = line.Substring(0, blockStart) + line.Substring(blockEnd + 2);
                }
            }

            int lineComment = line.IndexOf("//", StringComparison.Ordinal);
            if (lineComment >= 0)
            {
                line = line.Substring(0, lineComment);
            }

            return line;
        }

        #endregion

        #region 属性列表

        private void DrawPropertyList()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("③ 勾选要拆分的属性", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            m_SearchText = EditorGUILayout.TextField(m_SearchText, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
            m_OnlyReferenced = GUILayout.Toggle(m_OnlyReferenced, "仅被引用", EditorStyles.toolbarButton, GUILayout.Width(66));

            if (GUILayout.Button("全选", EditorStyles.toolbarButton, GUILayout.Width(46)))
            {
                foreach (WatchFieldInfo info in GetFieldsOfHolder())
                {
                    m_SplitSelection.Add(info.Name);
                }
            }
            if (GUILayout.Button("全不选", EditorStyles.toolbarButton, GUILayout.Width(56)))
            {
                m_SplitSelection.Clear();
            }
            if (GUILayout.Button("仅选引用", EditorStyles.toolbarButton, GUILayout.Width(66)))
            {
                m_SplitSelection.Clear();
                foreach (WatchFieldInfo info in GetFieldsOfHolder())
                {
                    if (GetReferences(info.Name).Count > 0)
                    {
                        m_SplitSelection.Add(info.Name);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            m_PropertyScroll = EditorGUILayout.BeginScrollView(m_PropertyScroll, GUILayout.Height(340));
            int shown = 0;
            string keyword = m_SearchText != null ? m_SearchText.Trim().ToLowerInvariant() : string.Empty;

            foreach (WatchFieldInfo info in GetFieldsOfHolder())
            {
                if (m_OnlyReferenced && GetReferences(info.Name).Count == 0) continue;
                if (keyword.Length > 0 && !info.Name.ToLowerInvariant().Contains(keyword)) continue;

                DrawPropertyRow(info);
                shown++;
            }

            if (shown == 0)
            {
                EditorGUILayout.HelpBox("没有符合过滤条件的属性。", MessageType.Info);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField(
                "共 " + GetFieldsOfHolder().Count + " 个属性，当前勾选 " + CountSelectedFields() + " 个。",
                EditorStyles.miniLabel);
        }

        private void DrawPropertyRow(WatchFieldInfo info)
        {
            List<DataSelector> references = GetReferences(info.Name);
            bool selected = m_SplitSelection.Contains(info.Name);
            Color defaultBg = GUI.backgroundColor;

            GUI.backgroundColor = references.Count > 0
                ? new Color(0.62f, 1f, 0.62f, 1f)
                : defaultBg;
            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            GUI.backgroundColor = defaultBg;

            bool nowSelected = EditorGUILayout.Toggle(selected, GUILayout.Width(16));
            if (nowSelected != selected)
            {
                if (nowSelected) m_SplitSelection.Add(info.Name);
                else m_SplitSelection.Remove(info.Name);
            }

            object editorValue = GetEditorValue(info.Field.GetValue(m_Holder));
            string typeName = GetFriendlyTypeName(info.GenericArgument);

            EditorGUILayout.LabelField(info.Name + "  [" + typeName + "]", GUILayout.MinWidth(120));
            EditorGUILayout.LabelField("= " + FormatValue(editorValue), EditorStyles.miniLabel, GUILayout.Width(140));

            if (references.Count > 0)
            {
                if (GUILayout.Button("引用 " + references.Count, EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    FocusSelector(references[0]);
                }
            }
            else
            {
                GUILayout.Label("未引用", EditorStyles.miniLabel, GUILayout.Width(70));
            }

            int internalCodeCount = CountCodeReferences(info.Name, true);
            int externalCodeCount = CountCodeReferences(info.Name, false);
            DrawCodeChip("内", internalCodeCount, info.Name, true);
            DrawCodeChip("外", externalCodeCount, info.Name, false);

            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = defaultBg;
        }

        private void DrawCodeChip(string prefix, int count, string propertyName, bool internalOnly)
        {
            if (count <= 0)
            {
                GUILayout.Label(prefix + " 0", EditorStyles.miniLabel, GUILayout.Width(52));
                return;
            }

            string tooltip = internalOnly
                ? "点击打开第一处「本 GameObject 层级内脚本」的代码引用"
                : "点击打开第一处「外部脚本」的代码引用";
            if (GUILayout.Button(
                    new GUIContent(prefix + " " + count, tooltip),
                    EditorStyles.miniButton, GUILayout.Width(52)))
            {
                CodeReference reference = GetFirstCodeReference(propertyName, internalOnly);
                if (reference != null)
                {
                    OpenCodeReference(reference);
                }
            }
        }

        private int CountSelectedFields()
        {
            if (m_SplitSelection.Count == 0) return 0;

            int count = 0;
            foreach (WatchFieldInfo info in GetFieldsOfHolder())
            {
                if (m_SplitSelection.Contains(info.Name)) count++;
            }
            return count;
        }

        private List<WatchFieldInfo> GetSelectedFields()
        {
            List<WatchFieldInfo> selected = new List<WatchFieldInfo>();
            foreach (WatchFieldInfo info in GetFieldsOfHolder())
            {
                if (m_SplitSelection.Contains(info.Name))
                {
                    selected.Add(info);
                }
            }
            return selected;
        }

        private List<WatchFieldInfo> GetFieldsOfHolder()
        {
            if (m_Holder == null) return EmptyFields;

            Type holderType = m_Holder.GetType();
            if (m_CachedFieldOwner != holderType)
            {
                m_CachedFieldOwner = holderType;
                m_CachedFields = GetPublicWatchFields(holderType);
            }
            return m_CachedFields;
        }

        #endregion

        #region 拆分

        private void DrawSplitSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("④ 拆分：把勾选的属性生成新的 DataHolder 脚本", EditorStyles.boldLabel);

            int selectedCount = CountSelectedFields();
            EditorGUILayout.HelpBox(
                "当前勾选 " + selectedCount + " 个属性。拆分只会新建脚本，不会修改原 Holder。",
                MessageType.None);

            m_SplitClassName = EditorGUILayout.TextField("新类名", m_SplitClassName);
            m_SplitNamespace = EditorGUILayout.TextField(
                new GUIContent("命名空间", "留空则生成在全局命名空间"),
                m_SplitNamespace);
            m_SplitFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent("保存目录", "必须是 Assets 下已存在的文件夹"),
                m_SplitFolder, typeof(DefaultAsset), false);
            m_UseCurrentValues = EditorGUILayout.Toggle(
                new GUIContent("使用当前 Inspector 值初始化",
                    "勾选后，生成的字段会带上当前面板值作为构造参数；不勾选则使用类型默认值。"),
                m_UseCurrentValues);

            string className = m_SplitClassName != null ? m_SplitClassName.Trim() : string.Empty;
            string folderPath = null;
            string validationMessage = null;

            if (m_SplitFolder == null)
            {
                validationMessage = "请选择保存目录。";
            }
            else
            {
                folderPath = AssetDatabase.GetAssetPath(m_SplitFolder);
                if (!AssetDatabase.IsValidFolder(folderPath))
                {
                    validationMessage = "保存目录必须是 Assets 下已存在的文件夹。";
                }
            }

            if (validationMessage == null && !IsValidIdentifier(className))
            {
                validationMessage = "类名不是合法的 C# 标识符。";
            }

            if (validationMessage != null)
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("生成路径", folderPath.TrimEnd('/') + "/" + className + ".cs");
            }

            GUI.enabled = validationMessage == null && selectedCount > 0;
            if (GUILayout.Button("生成 " + className + ".cs（" + selectedCount + " 个属性）", GUILayout.Height(26)))
            {
                GenerateSplitScript(folderPath, className);
            }
            GUI.enabled = true;
        }

        private void GenerateSplitScript(string folderPath, string className)
        {
            List<WatchFieldInfo> selected = GetSelectedFields();
            if (selected.Count == 0)
            {
                m_Message = "请至少勾选一个属性。";
                m_MessageType = MessageType.Warning;
                return;
            }

            string assetPath = folderPath.TrimEnd('/') + "/" + className + ".cs";
            string absolutePath = GetAbsoluteProjectPath(assetPath);
            bool overwrite = File.Exists(absolutePath);

            if (overwrite)
            {
                if (!EditorUtility.DisplayDialog(
                        "文件已存在",
                        assetPath + " 已存在，是否覆盖？",
                        "覆盖", "取消"))
                {
                    return;
                }
            }
            else if (TypeNameExists(m_SplitNamespace, className))
            {
                m_Message = "命名空间「" + DisplayNamespace(m_SplitNamespace) + "」下已存在同名类型 " + className + "，请先更换类名。";
                m_MessageType = MessageType.Error;
                return;
            }

            string code = BuildSplitCode(className, m_SplitNamespace, selected);
            try
            {
                string directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(absolutePath, code, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                m_Message = "写入脚本失败：" + e.Message;
                m_MessageType = MessageType.Error;
                return;
            }

            AssetDatabase.ImportAsset(assetPath);
            m_Message = "已生成 " + assetPath + "（" + selected.Count + " 个属性）。";
            m_MessageType = MessageType.Info;

            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            if (script != null)
            {
                Selection.activeObject = script;
                EditorGUIUtility.PingObject(script);
            }
        }

        private string BuildSplitCode(string className, string namespaceName, List<WatchFieldInfo> selected)
        {
            namespaceName = namespaceName != null ? namespaceName.Trim() : string.Empty;

            HashSet<string> usings = new HashSet<string>();
            usings.Add("System");
            usings.Add("System.Collections");
            usings.Add("System.Collections.Generic");
            usings.Add("UnityEngine");
            usings.Add("AmagiSakuya.ObservableVariables");

            string indent = namespaceName.Length > 0 ? "    " : string.Empty;
            string memberIndent = indent + "    ";

            List<string> fieldBlocks = new List<string>();
            foreach (WatchFieldInfo info in selected)
            {
                AddNamespaces(info.Field.FieldType, usings);
                AddNamespaces(info.GenericArgument, usings);

                string concreteType = GetTypeReference(info.Field.FieldType, usings);
                string initializer = "new " + concreteType + "()";

                if (m_UseCurrentValues)
                {
                    object instance = info.Field.GetValue(m_Holder);
                    object editorValue = GetEditorValue(instance);
                    string literal;
                    if (TryFormatLiteral(info.GenericArgument, editorValue, usings, out literal))
                    {
                        initializer = "new " + concreteType + "(" + literal + ")";
                    }
                }

                StringBuilder block = new StringBuilder();

                HeaderAttribute header = GetAttribute<HeaderAttribute>(info.Field);
                if (header != null)
                {
                    block.Append(memberIndent).Append("[Header(\"")
                        .Append(EscapeString(header.header)).AppendLine("\")]");
                }

                TooltipAttribute tooltip = GetAttribute<TooltipAttribute>(info.Field);
                if (tooltip != null)
                {
                    block.Append(memberIndent).Append("[Tooltip(\"")
                        .Append(EscapeString(tooltip.tooltip)).AppendLine("\")]");
                }

                block.Append(memberIndent).Append("public ")
                    .Append(GetTypeReference(info.Field.FieldType, usings))
                    .Append(' ').Append(info.Name)
                    .Append(" = ").Append(initializer).AppendLine(";");

                fieldBlocks.Add(block.ToString());
            }

            StringBuilder sb = new StringBuilder();
            List<string> sortedUsings = new List<string>(usings);
            sortedUsings.Sort(StringComparer.Ordinal);
            foreach (string item in sortedUsings)
            {
                if (string.IsNullOrEmpty(item)) continue;
                sb.Append("using ").Append(item).AppendLine(";");
            }
            sb.AppendLine();

            if (namespaceName.Length > 0)
            {
                sb.Append("namespace ").AppendLine(namespaceName);
                sb.AppendLine("{");
            }

            sb.Append(indent).Append("public class ").Append(className)
                .AppendLine(" : WatchPropertyDataHolder");
            sb.Append(indent).AppendLine("{");

            for (int i = 0; i < fieldBlocks.Count; i++)
            {
                sb.Append(fieldBlocks[i]);
                if (i < fieldBlocks.Count - 1)
                {
                    sb.AppendLine();
                }
            }

            sb.Append(indent).AppendLine("}");
            if (namespaceName.Length > 0)
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        #endregion

        #region 替换

        private void DrawReplaceSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("⑤ 替换：用另一个 DataHolder 脚本替换当前组件", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            MonoScript newScript = (MonoScript)EditorGUILayout.ObjectField(
                new GUIContent("目标 DataHolder 脚本", "拖入一个继承 WatchPropertyDataHolder 的 .cs 脚本"),
                m_TargetScript, typeof(MonoScript), false);
            if (EditorGUI.EndChangeCheck())
            {
                m_TargetScript = newScript;
                m_TargetType = ResolveHolderType(newScript);
                m_CompareDirty = true;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth);
            if (GUILayout.Button("从项目中列出所有 DataHolder 脚本", EditorStyles.miniButton))
            {
                ShowTargetTypeMenu();
            }
            EditorGUILayout.EndHorizontal();

            if (m_TargetScript != null && m_TargetType == null)
            {
                EditorGUILayout.HelpBox("所选脚本不是有效的 WatchPropertyDataHolder 子类。", MessageType.Error);
            }

            if (m_TargetType != null)
            {
                EditorGUILayout.LabelField("目标类型", m_TargetType.FullName);
                if (m_TargetType == m_Holder.GetType())
                {
                    EditorGUILayout.HelpBox("目标脚本与当前 Holder 类型相同，无需替换。", MessageType.Warning);
                }
                else if (m_SourceObject.GetComponent(m_TargetType) != null)
                {
                    EditorGUILayout.HelpBox("该 GameObject 上已经存在目标类型的组件，无法再添加一个相同类型的 Holder。", MessageType.Error);
                }
            }

            EnsureCompare();
            DrawCompareResult();

            bool sameType = m_TargetType != null && m_TargetType == m_Holder.GetType();
            bool alreadyExists = m_TargetType != null && m_SourceObject.GetComponent(m_TargetType) != null;
            bool hasBrokenFields = CountBrokenFields() > 0;

            GUI.enabled = m_TargetType != null && !sameType && !alreadyExists
                && (!hasBrokenFields || m_AllowIncompleteReplace);
            string buttonLabel = hasBrokenFields && !m_AllowIncompleteReplace
                ? "存在缺失属性，已阻止替换"
                : "执行替换（保留属性值并修复引用）";
            if (GUILayout.Button(buttonLabel, GUILayout.Height(28)))
            {
                ExecuteReplace();
            }
            GUI.enabled = true;

            EditorGUILayout.HelpBox(
                "替换会在同一 GameObject 上添加目标组件，复制同名且类型一致的属性值，更新 DataSelector 的显式引用，然后删除原组件。可用 Ctrl+Z 撤销。",
                MessageType.None);
        }

        private void EnsureCompare()
        {
            if (!m_CompareDirty
                && m_ComparedHolder == m_Holder
                && m_ComparedTargetType == m_TargetType)
            {
                return;
            }

            m_CompareDirty = false;
            m_ComparedHolder = m_Holder;
            m_ComparedTargetType = m_TargetType;

            m_CompareItems.Clear();
            m_ExtraItems.Clear();

            if (m_Holder == null || m_TargetType == null) return;

            List<WatchFieldInfo> targetFields = GetPublicWatchFields(m_TargetType);
            Dictionary<string, WatchFieldInfo> targetMap = new Dictionary<string, WatchFieldInfo>();
            foreach (WatchFieldInfo info in targetFields)
            {
                targetMap[info.Name] = info;
            }

            foreach (WatchFieldInfo source in GetFieldsOfHolder())
            {
                CompareItem item = new CompareItem();
                item.Name = source.Name;
                item.TypeLabel = GetFriendlyTypeName(source.GenericArgument);

                WatchFieldInfo target;
                if (!targetMap.TryGetValue(source.Name, out target))
                {
                    item.Missing = true;
                }
                else
                {
                    targetMap.Remove(source.Name);
                    if (target.GenericArgument != source.GenericArgument)
                    {
                        item.TypeMismatch = true;
                        item.TypeLabel = GetFriendlyTypeName(source.GenericArgument)
                            + " → " + GetFriendlyTypeName(target.GenericArgument);
                    }
                }

                m_CompareItems.Add(item);
            }

            foreach (KeyValuePair<string, WatchFieldInfo> pair in targetMap)
            {
                CompareItem extra = new CompareItem();
                extra.Name = pair.Key;
                extra.TypeLabel = GetFriendlyTypeName(pair.Value.GenericArgument);
                m_ExtraItems.Add(extra);
            }
            m_ExtraItems.Sort(delegate (CompareItem a, CompareItem b)
            {
                return string.CompareOrdinal(a.Name, b.Name);
            });
        }

        private void DrawCompareResult()
        {
            if (m_Holder == null || m_TargetType == null) return;

            int missingCount = 0;
            int mismatchCount = 0;
            int brokenReferenceCount = 0;

            foreach (CompareItem item in m_CompareItems)
            {
                if (!item.Missing && !item.TypeMismatch) continue;
                if (item.Missing) missingCount++;
                else mismatchCount++;
                brokenReferenceCount += GetReferences(item.Name).Count;
            }

            if (missingCount == 0 && mismatchCount == 0)
            {
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.62f, 1f, 0.62f, 1f);
                EditorGUILayout.HelpBox(
                    "目标脚本包含当前 Holder 的全部属性（名称与类型一致），替换后所有引用都能保留。",
                    MessageType.Info);
                GUI.backgroundColor = oldBg;
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "目标脚本缺少 " + missingCount + " 个属性、有 " + mismatchCount +
                    " 个类型不匹配；这些属性共被 " + brokenReferenceCount +
                    " 个 DataSelector 引用，替换后这些引用会失效。",
                    MessageType.Error);

                foreach (CompareItem item in m_CompareItems)
                {
                    if (!item.Missing && !item.TypeMismatch) continue;

                    int referenceCount = GetReferences(item.Name).Count;
                    Color oldBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.72f, 0.72f, 1f);
                    EditorGUILayout.BeginHorizontal(GUI.skin.box);
                    GUI.backgroundColor = oldBg;

                    string prefix = item.Missing ? "缺失  " : "类型不匹配  ";
                    EditorGUILayout.LabelField(prefix + item.Name + "  [" + item.TypeLabel + "]", EditorStyles.boldLabel);

                    if (referenceCount > 0)
                    {
                        if (GUILayout.Button("被引用 " + referenceCount, EditorStyles.miniButton, GUILayout.Width(90)))
                        {
                            FocusSelector(GetReferences(item.Name)[0]);
                        }
                    }
                    else
                    {
                        GUILayout.Label("未被引用", EditorStyles.miniLabel, GUILayout.Width(90));
                    }

                    EditorGUILayout.EndHorizontal();
                    GUI.backgroundColor = oldBg;
                }

                m_AllowIncompleteReplace = EditorGUILayout.ToggleLeft(
                    "即使缺失或类型不匹配也允许替换（相关 DataSelector 引用会失效）",
                    m_AllowIncompleteReplace);
            }

            m_ShowCompatibleFields = EditorGUILayout.Foldout(m_ShowCompatibleFields, "保留的属性");
            if (m_ShowCompatibleFields)
            {
                int compatibleCount = 0;
                foreach (CompareItem item in m_CompareItems)
                {
                    if (item.Missing || item.TypeMismatch) continue;
                    compatibleCount++;
                    EditorGUILayout.LabelField("✓ " + item.Name + "  [" + item.TypeLabel + "]", EditorStyles.miniLabel);
                }
                if (compatibleCount == 0)
                {
                    EditorGUILayout.LabelField("没有任何可以直接保留的属性。", EditorStyles.miniLabel);
                }
            }

            if (m_ExtraItems.Count > 0)
            {
                m_ShowExtraFields = EditorGUILayout.Foldout(
                    m_ShowExtraFields,
                    "目标新增的属性（不会被当前场景引用）  " + m_ExtraItems.Count);
                if (m_ShowExtraFields)
                {
                    foreach (CompareItem item in m_ExtraItems)
                    {
                        EditorGUILayout.LabelField("+ " + item.Name + "  [" + item.TypeLabel + "]", EditorStyles.miniLabel);
                    }
                }
            }
        }

        private int CountBrokenFields()
        {
            int count = 0;
            foreach (CompareItem item in m_CompareItems)
            {
                if (item.Missing || item.TypeMismatch) count++;
            }
            return count;
        }

        private void ShowTargetTypeMenu()
        {
            List<Type> holders = GetHolderTypes();
            if (holders.Count == 0)
            {
                m_Message = "项目中没有找到其它 WatchPropertyDataHolder 子类。";
                m_MessageType = MessageType.Warning;
                return;
            }

            GenericMenu menu = new GenericMenu();
            bool hasItem = false;

            foreach (Type type in holders)
            {
                if (m_Holder != null && type == m_Holder.GetType()) continue;
                hasItem = true;

                Type captured = type;
                menu.AddItem(new GUIContent(type.FullName), m_TargetType == type, delegate
                {
                    m_TargetType = captured;
                    m_TargetScript = null;
                    m_CompareDirty = true;
                    Repaint();
                });
            }

            if (!hasItem)
            {
                m_Message = "除了当前 Holder 之外，项目中没有其它可选的 DataHolder 脚本。";
                m_MessageType = MessageType.Warning;
                return;
            }

            menu.ShowAsContext();
        }

        private void ExecuteReplace()
        {
            WatchPropertyDataHolder oldComponent = m_Holder;
            Type targetType = m_TargetType;

            if (oldComponent == null || targetType == null) return;

            GameObject targetObject = oldComponent.gameObject;
            string hierarchyPath = GetHierarchyPath(targetObject.transform);

            if (!EditorUtility.DisplayDialog(
                    "确认替换",
                    "将把 " + hierarchyPath + " 上的 " + oldComponent.GetType().Name +
                    " 替换为 " + targetType.Name + "。\n\n" +
                    "同名且类型一致的属性值会被保留，DataSelector 的显式引用会自动修复。",
                    "执行替换", "取消"))
            {
                return;
            }

            SerializedObject sourceObject = new SerializedObject(oldComponent);
            sourceObject.Update();

            Component added = Undo.AddComponent(targetObject, targetType);
            if (added == null)
            {
                m_Message = "添加目标组件失败，替换已中止。";
                m_MessageType = MessageType.Error;
                return;
            }

            WatchPropertyDataHolder newComponent = (WatchPropertyDataHolder)added;

            SerializedObject targetObjectSerialized = new SerializedObject(newComponent);
            targetObjectSerialized.Update();
            int copiedFieldCount = CopyMatchingValues(
                sourceObject, targetObjectSerialized, oldComponent.GetType(), targetType);
            targetObjectSerialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(newComponent);

            int redirectedCount = RedirectExplicitReferences(oldComponent, newComponent);
            RefreshAllSelectors();

            Undo.DestroyObjectImmediate(oldComponent);
            EditorSceneManager.MarkSceneDirty(targetObject.scene);

            WatchPropertyDataHolder[] holders = targetObject.GetComponents<WatchPropertyDataHolder>();
            m_Holder = newComponent;
            m_HolderIndex = Array.IndexOf(holders, newComponent);
            if (m_HolderIndex < 0) m_HolderIndex = 0;

            m_CachedFieldOwner = null;
            m_CachedFields = null;
            m_SplitSelection.Clear();
            m_TargetScript = null;
            m_TargetType = null;
            m_CompareDirty = true;
            m_ScanDirty = true;

            m_Message = "替换完成：保留 " + copiedFieldCount + " 个字段值，重定向 " +
                        redirectedCount + " 处显式引用，原组件已删除。";
            m_MessageType = MessageType.Info;

            Selection.activeGameObject = targetObject;
            EditorGUIUtility.PingObject(newComponent);
            Repaint();
        }

        private static int CopyMatchingValues(
            SerializedObject source, SerializedObject destination, Type sourceType, Type targetType)
        {
            int copied = 0;
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            HashSet<string> visited = new HashSet<string>();

            foreach (FieldInfo field in GetSerializableFields(sourceType))
            {
                if (!visited.Add(field.Name)) continue;

                FieldInfo targetField = targetType.GetField(field.Name, flags);
                if (targetField == null) continue;

                bool sourceIsWatch = GetWatchPropertyGenericArgument(field.FieldType) != null;
                bool targetIsWatch = GetWatchPropertyGenericArgument(targetField.FieldType) != null;

                if (sourceIsWatch || targetIsWatch)
                {
                    if (!sourceIsWatch || !targetIsWatch) continue;

                    Type sourceArgument = GetWatchPropertyGenericArgument(field.FieldType);
                    Type targetArgument = GetWatchPropertyGenericArgument(targetField.FieldType);
                    if (sourceArgument != targetArgument) continue;
                }
                else if (field.FieldType != targetField.FieldType)
                {
                    continue;
                }

                SerializedProperty sourceProperty = source.FindProperty(field.Name);
                SerializedProperty targetProperty = destination.FindProperty(field.Name);
                if (sourceProperty == null || targetProperty == null) continue;
                if (sourceProperty.propertyType != targetProperty.propertyType) continue;

                CopyPropertyValue(sourceProperty, targetProperty);
                copied++;
            }

            return copied;
        }

        private static List<FieldInfo> GetSerializableFields(Type type)
        {
            List<FieldInfo> result = new List<FieldInfo>();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            Type current = type;
            while (current != null && current != typeof(object))
            {
                foreach (FieldInfo field in current.GetFields(flags))
                {
                    if (field.IsStatic || field.IsNotSerialized) continue;
                    if (!field.IsPublic
                        && field.GetCustomAttributes(typeof(SerializeField), false).Length == 0)
                    {
                        continue;
                    }
                    result.Add(field);
                }
                current = current.BaseType;
            }

            return result;
        }

        private static void CopyPropertyValue(SerializedProperty source, SerializedProperty destination)
        {
            if (source == null || destination == null) return;
            if (source.propertyType != destination.propertyType) return;

            switch (source.propertyType)
            {
                case SerializedPropertyType.Generic:
                    if (source.isArray)
                    {
                        if (destination.arraySize != source.arraySize)
                        {
                            destination.arraySize = source.arraySize;
                        }
                        for (int i = 0; i < source.arraySize; i++)
                        {
                            CopyPropertyValue(
                                source.GetArrayElementAtIndex(i),
                                destination.GetArrayElementAtIndex(i));
                        }
                        return;
                    }

                    if (!source.hasChildren) return;

                    {
                        string rootPath = source.propertyPath;
                        SerializedProperty iterator = source.Copy();
                        bool enterChildren = true;
                        while (iterator.NextVisible(enterChildren))
                        {
                            enterChildren = false;
                            if (!iterator.propertyPath.StartsWith(rootPath + ".", StringComparison.Ordinal))
                            {
                                break;
                            }

                            SerializedProperty target =
                                destination.serializedObject.FindProperty(iterator.propertyPath);
                            if (target == null) continue;
                            CopyPropertyValue(iterator, target);
                        }
                    }
                    return;

                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.ArraySize:
                    destination.intValue = source.intValue;
                    return;

                case SerializedPropertyType.Boolean:
                    destination.boolValue = source.boolValue;
                    return;

                case SerializedPropertyType.Float:
                    destination.floatValue = source.floatValue;
                    return;

                case SerializedPropertyType.String:
                    destination.stringValue = source.stringValue;
                    return;

                case SerializedPropertyType.Color:
                    destination.colorValue = source.colorValue;
                    return;

                case SerializedPropertyType.ObjectReference:
                    destination.objectReferenceValue = source.objectReferenceValue;
                    return;

                case SerializedPropertyType.Enum:
                    destination.enumValueIndex = source.enumValueIndex;
                    return;

                case SerializedPropertyType.Vector2:
                    destination.vector2Value = source.vector2Value;
                    return;

                case SerializedPropertyType.Vector3:
                    destination.vector3Value = source.vector3Value;
                    return;

                case SerializedPropertyType.Vector4:
                    destination.vector4Value = source.vector4Value;
                    return;

                case SerializedPropertyType.Rect:
                    destination.rectValue = source.rectValue;
                    return;

                case SerializedPropertyType.Quaternion:
                    destination.quaternionValue = source.quaternionValue;
                    return;

                case SerializedPropertyType.Bounds:
                    destination.boundsValue = source.boundsValue;
                    return;

                case SerializedPropertyType.Vector2Int:
                    destination.vector2IntValue = source.vector2IntValue;
                    return;

                case SerializedPropertyType.Vector3Int:
                    destination.vector3IntValue = source.vector3IntValue;
                    return;

                case SerializedPropertyType.RectInt:
                    destination.rectIntValue = source.rectIntValue;
                    return;

                case SerializedPropertyType.BoundsInt:
                    destination.boundsIntValue = source.boundsIntValue;
                    return;
            }
        }

        private static int RedirectExplicitReferences(
            WatchPropertyDataHolder oldComponent, WatchPropertyDataHolder newComponent)
        {
            int redirected = 0;

            foreach (GameObject root in GetRootGameObjects())
            {
                foreach (DataSelector selector in root.GetComponentsInChildren<DataSelector>(true))
                {
                    SerializedObject serializedSelector = new SerializedObject(selector);
                    SerializedProperty explicitHolder = serializedSelector.FindProperty("m_explicitHolder");
                    if (explicitHolder == null
                        || explicitHolder.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        continue;
                    }
                    if (explicitHolder.objectReferenceValue != oldComponent) continue;

                    explicitHolder.objectReferenceValue = newComponent;
                    serializedSelector.ApplyModifiedProperties();
                    selector.RefreshDataHolder();
                    redirected++;
                }
            }

            return redirected;
        }

        private static void RefreshAllSelectors()
        {
            foreach (GameObject root in GetRootGameObjects())
            {
                foreach (DataSelector selector in root.GetComponentsInChildren<DataSelector>(true))
                {
                    selector.RefreshDataHolder();
                }
            }
        }

        #endregion

        #region 反射与工具方法

        private static List<WatchFieldInfo> GetPublicWatchFields(Type type)
        {
            List<WatchFieldInfo> result = new List<WatchFieldInfo>();
            if (type == null) return result;

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                Type argument = GetWatchPropertyGenericArgument(field.FieldType);
                if (argument == null) continue;

                WatchFieldInfo info = new WatchFieldInfo();
                info.Field = field;
                info.GenericArgument = argument;
                result.Add(info);
            }

            return result;
        }

        private static Type GetWatchPropertyGenericArgument(Type type)
        {
            Type current = type;
            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType
                    && current.GetGenericTypeDefinition() == typeof(WatchProperty<>))
                {
                    return current.GetGenericArguments()[0];
                }
                current = current.BaseType;
            }
            return null;
        }

        private static object GetEditorValue(object watchPropertyInstance)
        {
            if (watchPropertyInstance == null) return null;

            Type current = watchPropertyInstance.GetType();
            while (current != null && current != typeof(object))
            {
                FieldInfo field = current.GetField(
                    "m_editorValue",
                    BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field.GetValue(watchPropertyInstance);
                }
                current = current.BaseType;
            }
            return null;
        }

        private static Type ResolveHolderType(MonoScript script)
        {
            if (script == null) return null;

            Type type = script.GetClass();
            if (type == null) return null;
            if (type == typeof(WatchPropertyDataHolder)) return null;
            if (!typeof(WatchPropertyDataHolder).IsAssignableFrom(type)) return null;
            return type;
        }

        private static List<Type> GetHolderTypes()
        {
            if (s_HolderTypes != null) return s_HolderTypes;

            List<Type> result = new List<Type>();
            foreach (Type type in GetAllTypes())
            {
                if (type == null || type.IsAbstract) continue;
                if (!typeof(WatchPropertyDataHolder).IsAssignableFrom(type)) continue;
                if (type == typeof(WatchPropertyDataHolder)) continue;
                result.Add(type);
            }

            result.Sort(delegate (Type a, Type b)
            {
                return string.CompareOrdinal(a.FullName, b.FullName);
            });

            s_HolderTypes = result;
            return result;
        }

        private static List<Type> GetAllTypes()
        {
            if (s_AllTypes != null) return s_AllTypes;

            List<Type> result = new List<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }

                foreach (Type type in types)
                {
                    if (type != null) result.Add(type);
                }
            }

            s_AllTypes = result;
            return result;
        }

        private static bool TypeNameExists(string namespaceName, string typeName)
        {
            namespaceName = namespaceName != null ? namespaceName.Trim() : string.Empty;

            foreach (Type type in GetAllTypes())
            {
                if (type.Name != typeName) continue;
                if ((type.Namespace ?? string.Empty) != namespaceName) continue;
                return true;
            }
            return false;
        }

        private static string GetTypeReference(Type type, HashSet<string> usings)
        {
            if (type.IsArray)
            {
                return GetTypeReference(type.GetElementType(), usings) + "[]";
            }

            if (type.IsGenericType)
            {
                if (!string.IsNullOrEmpty(type.Namespace))
                {
                    usings.Add(type.Namespace);
                }

                StringBuilder sb = new StringBuilder();
                sb.Append(StripGenericArity(type.Name));
                sb.Append('<');

                Type[] arguments = type.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(GetTypeReference(arguments[i], usings));
                }

                sb.Append('>');
                return sb.ToString();
            }

            if (type.IsNested)
            {
                return type.FullName.Replace('+', '.');
            }

            if (string.IsNullOrEmpty(type.Namespace))
            {
                return "global::" + type.Name;
            }

            usings.Add(type.Namespace);
            return type.Name;
        }

        private static string StripGenericArity(string name)
        {
            int index = name.IndexOf('`');
            return index < 0 ? name : name.Substring(0, index);
        }

        private static void AddNamespaces(Type type, HashSet<string> usings)
        {
            if (type == null) return;

            if (!string.IsNullOrEmpty(type.Namespace))
            {
                usings.Add(type.Namespace);
            }

            if (type.IsArray)
            {
                AddNamespaces(type.GetElementType(), usings);
                return;
            }

            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    AddNamespaces(argument, usings);
                }
            }
        }

        private static bool TryFormatLiteral(
            Type type, object value, HashSet<string> usings, out string literal)
        {
            literal = null;
            if (type == null || value == null) return false;

            if (type.IsEnum)
            {
                string typeName = GetTypeReference(type, usings);
                string memberName = Enum.GetName(type, value);
                literal = memberName != null
                    ? typeName + "." + memberName
                    : "(" + typeName + ")" + Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (type == typeof(bool))
            {
                literal = ((bool)value) ? "true" : "false";
                return true;
            }

            if (type == typeof(int))
            {
                literal = ((int)value).ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (type == typeof(short) || type == typeof(byte) || type == typeof(sbyte))
            {
                literal = Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (type == typeof(ushort))
            {
                literal = ((ushort)value).ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (type == typeof(uint))
            {
                literal = ((uint)value).ToString(CultureInfo.InvariantCulture) + "u";
                return true;
            }

            if (type == typeof(long))
            {
                literal = ((long)value).ToString(CultureInfo.InvariantCulture) + "L";
                return true;
            }

            if (type == typeof(ulong))
            {
                literal = ((ulong)value).ToString(CultureInfo.InvariantCulture) + "UL";
                return true;
            }

            if (type == typeof(float))
            {
                literal = FormatFloat((float)value);
                return true;
            }

            if (type == typeof(double))
            {
                literal = FormatDouble((double)value);
                return true;
            }

            if (type == typeof(string))
            {
                literal = "\"" + EscapeString((string)value) + "\"";
                return true;
            }

            if (type == typeof(Vector2))
            {
                Vector2 v = (Vector2)value;
                literal = "new Vector2(" + FormatFloat(v.x) + ", " + FormatFloat(v.y) + ")";
                return true;
            }

            if (type == typeof(Vector3))
            {
                Vector3 v = (Vector3)value;
                literal = "new Vector3(" + FormatFloat(v.x) + ", " + FormatFloat(v.y)
                    + ", " + FormatFloat(v.z) + ")";
                return true;
            }

            if (type == typeof(Vector4))
            {
                Vector4 v = (Vector4)value;
                literal = "new Vector4(" + FormatFloat(v.x) + ", " + FormatFloat(v.y)
                    + ", " + FormatFloat(v.z) + ", " + FormatFloat(v.w) + ")";
                return true;
            }

            if (type == typeof(Vector2Int))
            {
                Vector2Int v = (Vector2Int)value;
                literal = "new Vector2Int(" + v.x + ", " + v.y + ")";
                return true;
            }

            if (type == typeof(Vector3Int))
            {
                Vector3Int v = (Vector3Int)value;
                literal = "new Vector3Int(" + v.x + ", " + v.y + ", " + v.z + ")";
                return true;
            }

            if (type == typeof(Color))
            {
                Color v = (Color)value;
                literal = "new Color(" + FormatFloat(v.r) + ", " + FormatFloat(v.g)
                    + ", " + FormatFloat(v.b) + ", " + FormatFloat(v.a) + ")";
                return true;
            }

            if (type == typeof(Color32))
            {
                Color32 v = (Color32)value;
                literal = "new Color32(" + v.r + ", " + v.g + ", " + v.b + ", " + v.a + ")";
                return true;
            }

            if (type == typeof(Quaternion))
            {
                Quaternion v = (Quaternion)value;
                literal = "new Quaternion(" + FormatFloat(v.x) + ", " + FormatFloat(v.y)
                    + ", " + FormatFloat(v.z) + ", " + FormatFloat(v.w) + ")";
                return true;
            }

            if (type == typeof(Rect))
            {
                Rect v = (Rect)value;
                literal = "new Rect(" + FormatFloat(v.x) + ", " + FormatFloat(v.y)
                    + ", " + FormatFloat(v.width) + ", " + FormatFloat(v.height) + ")";
                return true;
            }

            return false;
        }

        private static string FormatFloat(float value)
        {
            if (float.IsNaN(value)) return "float.NaN";
            if (float.IsPositiveInfinity(value)) return "float.PositiveInfinity";
            if (float.IsNegativeInfinity(value)) return "float.NegativeInfinity";

            string text = value.ToString("R", CultureInfo.InvariantCulture);
            if (text.IndexOf('.') < 0 && text.IndexOf('E') < 0 && text.IndexOf('e') < 0)
            {
                text += ".0";
            }
            return text + "f";
        }

        private static string FormatDouble(double value)
        {
            if (double.IsNaN(value)) return "double.NaN";
            if (double.IsPositiveInfinity(value)) return "double.PositiveInfinity";
            if (double.IsNegativeInfinity(value)) return "double.NegativeInfinity";

            string text = value.ToString("R", CultureInfo.InvariantCulture);
            if (text.IndexOf('.') < 0 && text.IndexOf('E') < 0 && text.IndexOf('e') < 0)
            {
                text += ".0";
            }
            return text;
        }

        private static string EscapeString(string text)
        {
            if (text == null) return string.Empty;
            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static T GetAttribute<T>(FieldInfo field) where T : Attribute
        {
            object[] attributes = field.GetCustomAttributes(typeof(T), false);
            return attributes.Length > 0 ? (T)attributes[0] : null;
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == null) return "Unknown";
            if (type == typeof(float)) return "Float";
            if (type == typeof(int)) return "Int";
            if (type == typeof(bool)) return "Bool";
            if (type == typeof(string)) return "String";
            if (type.IsEnum) return type.Name;
            return type.Name;
        }

        private static string FormatValue(object value)
        {
            if (value == null) return "null";
            if (value is string) return "\"" + value + "\"";
            if (value is UnityEngine.Object)
            {
                UnityEngine.Object unityObject = (UnityEngine.Object)value;
                return unityObject == null ? "None" : unityObject.name;
            }
            if (value is bool)
            {
                return ((bool)value) ? "true" : "false";
            }
            return value.ToString();
        }

        private static bool IsValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!(char.IsLetter(name[0]) || name[0] == '_')) return false;

            for (int i = 1; i < name.Length; i++)
            {
                char c = name[i];
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            }
            return true;
        }

        private static string DisplayNamespace(string namespaceName)
        {
            return string.IsNullOrEmpty(namespaceName) ? "全局命名空间" : namespaceName;
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string relative = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(projectRoot, relative);
        }

        private static GameObject[] GetRootGameObjects()
        {
            UnityEngine.SceneManagement.Scene scene =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.isLoaded) return new GameObject[0];
            return scene.GetRootGameObjects();
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return "(null)";

            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        #endregion
    }
}
