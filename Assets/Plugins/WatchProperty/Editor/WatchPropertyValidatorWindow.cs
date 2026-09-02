using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MTST_RCS;
using AmagiSakuya.ObservableVariables;

namespace AmagiSakuya.ObservableVariables.ObservableVariablesEditor
{
    public class WatchPropertyValidatorWindow : EditorWindow
    {
        private List<ValidationResult> m_Results = new List<ValidationResult>();
        private Vector2 m_ScrollPosition;
        private bool m_HasScanned = false; // 新增：记录是否至少执行过一次扫描

        // 内部结构：保存单条检查结果
        private class ValidationResult
        {
            public DataGetter targetGetter;
            public string propertyName;
            public string errorMsg;
            public bool isCriticalError; // true为严重错误，false为未分配配置的警告
        }

        [MenuItem("Tools/ObservableVariables/DataGetter 配置检查窗口")]
        public static void ShowWindow()
        {
            var window = GetWindow<WatchPropertyValidatorWindow>("DataGetter 检查器");
            window.minSize = new Vector2(500, 400);
            window.Show();
        }

        private void OnGUI()
        {
            // 顶部工具栏
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("全局扫描当前场景", EditorStyles.toolbarButton))
            {
                RunScan();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // 结果显示区
            if (m_Results.Count == 0)
            {
                if (!m_HasScanned)
                {
                    // 初始未扫描状态
                    EditorGUILayout.HelpBox("点击上方按钮开始扫描当前场景！", MessageType.Info);
                }
                else
                {
                    // 核心修改：扫描过但结果为空，说明项目很健康！
                    EditorGUILayout.Space();
                    GUI.backgroundColor = Color.green; // 让提示框变绿，看起来很安全
                    EditorGUILayout.HelpBox("【检测完毕】场景中所有 DataGetter 配置均合法，未发现任何错误与遗漏！", MessageType.Info);
                    GUI.backgroundColor = Color.white; // 还原背景色
                }
                return;
            }

            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);

            int errorCount = 0;
            int warnCount = 0;
            foreach (var res in m_Results)
            {
                if (res.isCriticalError) errorCount++; else warnCount++;
            }

            // 同样加上【检测完毕】的前缀提示
            EditorGUILayout.LabelField($"【检测完毕】发现 {errorCount} 个严重配置错误， {warnCount} 个未配置项", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            for (int i = 0; i < m_Results.Count; i++)
            {
                var res = m_Results[i];

                MessageType msgType = res.isCriticalError ? MessageType.Error : MessageType.Warning;

                EditorGUILayout.BeginVertical(GUI.skin.box);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"物体: {res.targetGetter.gameObject.name}", EditorStyles.boldLabel);

                if (GUILayout.Button("在层级中高亮定位", GUILayout.Width(130)))
                {
                    Selection.activeGameObject = res.targetGetter.gameObject;
                    EditorGUIUtility.PingObject(res.targetGetter.gameObject);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox(res.errorMsg, msgType);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 核心扫描算法（完全兼容 Unity 2019 写法，支持非激活物体过滤）
        /// </summary>
        private void RunScan()
        {
            m_Results.Clear();
            m_HasScanned = true; // 标记已经扫描过一次了

            List<DataGetter> getters = new List<DataGetter>();

            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.isLoaded)
            {
                GameObject[] rootObjects = activeScene.GetRootGameObjects();
                foreach (var root in rootObjects)
                {
                    DataGetter[] results = root.GetComponentsInChildren<DataGetter>(true);
                    getters.AddRange(results);
                }
            }

            foreach (var getter in getters)
            {
                // 1. 检查有没有拿到 Holder
                WatchPropertyDataHolder holder = getter.DataHolder;
                if (holder == null)
                {
                    m_Results.Add(new ValidationResult
                    {
                        targetGetter = getter,
                        propertyName = string.Empty,
                        errorMsg = "致命错误：该物体的祖先节点中完全没有找到 MTST_RCSDataHolder 组件！",
                        isCriticalError = true
                    });
                    continue;
                }

                // 2. 检查变量本身是否为空
                if (getter.targetPropertyRef == null || string.IsNullOrEmpty(getter.targetPropertyRef.propertyName))
                {
                    m_Results.Add(new ValidationResult
                    {
                        targetGetter = getter,
                        propertyName = string.Empty,
                        errorMsg = "警告：目标属性 targetPropertyRef 未进行任何配置（未选择变量）。",
                        isCriticalError = false
                    });
                    continue;
                }

                // 3. 验证配置的值在 Holder 里是否存在
                string savedName = getter.targetPropertyRef.propertyName;
                FieldInfo field = holder.GetType().GetField(savedName, BindingFlags.Public | BindingFlags.Instance);

                if (field == null)
                {
                    m_Results.Add(new ValidationResult
                    {
                        targetGetter = getter,
                        propertyName = savedName,
                        errorMsg = $"致命错误：面板上残留了已失效的配置名「{savedName}」，对应的 DataHolder ({holder.GetType().Name}) 中该变量已被删除或改名！",
                        isCriticalError = true
                    });
                }
                else
                {
                    if (!IsSubclassOfRawGeneric(typeof(WatchProperty<>), field.FieldType))
                    {
                        m_Results.Add(new ValidationResult
                        {
                            targetGetter = getter,
                            propertyName = savedName,
                            errorMsg = $"致命错误：字段「{savedName}」虽然存在，但它不是有效的 WatchProperty 类型！",
                            isCriticalError = true
                        });
                    }
                }
            }

            Repaint();
        }

        private bool IsSubclassOfRawGeneric(Type generic, Type toCheck)
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == cur) return true;
                toCheck = toCheck.BaseType;
            }
            return false;
        }
    }
}