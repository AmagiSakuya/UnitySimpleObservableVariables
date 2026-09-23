using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;


namespace AmagiSakuya.ObservableVariables
{
    [Serializable]
    public class WatchPropertyReference
    {
        public string propertyName;
    }

    public class DataHolderGetter : MonoBehaviour
    {
        // 缓存的 DataHolder。
        // 注意：这个字段不会被 Unity 序列化，但它会随着组件实例一直存活（编辑器和运行时都一样），
        // 所以一旦层级关系被调整，缓存就会变成脏数据，必须主动失效，否则会一直指向旧的 DataHolder。
        [NonSerialized] private WatchPropertyDataHolder m_cachedHolder;

        public WatchPropertyDataHolder DataHolder
        {
            get { return GetDataHolder(false); }
        }

        /// <summary>
        /// 获取自身及所有祖先节点上的 WatchPropertyDataHolder。
        /// </summary>
        /// <param name="forceRefresh">
        /// 为 true 时忽略缓存，强制沿当前层级重新向上查找。
        /// 编辑器面板每次重绘都会传 true，这样调整完 UI 层级后能立刻拿到最新的 DataHolder。
        /// </param>
        public WatchPropertyDataHolder GetDataHolder(bool forceRefresh)
        {
            if (forceRefresh || m_cachedHolder == null)
            {
                m_cachedHolder = FindDataHolderInParents();
            }
            return m_cachedHolder;
        }

        /// <summary>清空缓存，下一次访问 DataHolder 时会重新向上查找。</summary>
        public void RefreshDataHolder()
        {
            m_cachedHolder = null;
        }

        /// <summary>
        /// 从自身开始沿父级链向上查找。
        /// 这里手动遍历而不是用 GetComponentInParent，原因有二：
        /// 1) GetComponentInParent 会跳过处于非激活状态的节点，UI 面板被隐藏时就找不到 Holder 了；
        /// 2) Unity 2019 没有 GetComponentInParent(bool includeInactive) 这个重载。
        /// </summary>
        protected virtual WatchPropertyDataHolder FindDataHolderInParents()
        {
            Transform current = transform;
            while (current != null)
            {
                WatchPropertyDataHolder holder = current.GetComponent<WatchPropertyDataHolder>();
                if (holder != null)
                {
                    return holder;
                }
                current = current.parent;
            }
            return null;
        }

        public virtual T GetData<T>() where T : WatchPropertyDataHolder
        {
            return DataHolder as T;
        }

        /// <summary>
        /// 层级（父节点）发生变化时立刻让缓存失效。
        /// 运行时由 Unity 自动回调；编辑器下该回调需要 [ExecuteAlways]/[ExecuteInEditMode] 才会触发，
        /// 因此编辑器面板不依赖它，而是靠 DataSelectorEditor 里的强制刷新。
        /// </summary>
        protected virtual void OnTransformParentChanged()
        {
            m_cachedHolder = null;
        }

        protected virtual void OnEnable()
        {
            m_cachedHolder = null;
        }
    }

    public class DataHolderGetter<T> : MonoBehaviour where T : WatchPropertyDataHolder
    {
        [NonSerialized] private T m_cachedHolder;

        public T DataHolder
        {
            get { return GetDataHolder(false); }
        }

        /// <summary>
        /// 获取自身及所有祖先节点上的 T。forceRefresh 为 true 时强制重新向上查找。
        /// </summary>
        public T GetDataHolder(bool forceRefresh)
        {
            if (forceRefresh || m_cachedHolder == null)
            {
                m_cachedHolder = FindDataHolderInParents();
            }
            return m_cachedHolder;
        }

        /// <summary>清空缓存，下一次访问 DataHolder 时会重新向上查找。</summary>
        public void RefreshDataHolder()
        {
            m_cachedHolder = null;
        }

        /// <summary>从自身开始沿父级链向上查找（不依赖激活状态，理由同 DataHolderGetter）。</summary>
        protected virtual T FindDataHolderInParents()
        {
            Transform current = transform;
            while (current != null)
            {
                T holder = current.GetComponent<T>();
                if (holder != null)
                {
                    return holder;
                }
                current = current.parent;
            }
            return null;
        }

        protected virtual void OnTransformParentChanged()
        {
            m_cachedHolder = null;
        }

        protected virtual void OnEnable()
        {
            m_cachedHolder = null;
        }
    }

    /// <summary>
    /// DataSelector 从「哪一个」WatchPropertyDataHolder 取数据。
    /// </summary>
    public enum DataHolderScope
    {
        /// <summary>最近的祖先 Holder（含自身）。插件原有行为，也是默认值。</summary>
        NearestParent = 0,

        /// <summary>
        /// 沿「自身 → 父 → 祖父 → …」向上数第 N 个 Holder。
        /// 1 = 最近的，2 = 再上一层（父亲的父亲），3 = 再再上一层，依此类推。
        /// </summary>
        AncestorLevel = 1,

        /// <summary>
        /// 手动指定一个 Holder。不受父子关系限制，
        /// 可以引用与父级同级的节点、叔伯节点，甚至另一棵 Canvas 下的 Holder。
        /// </summary>
        Explicit = 2,
    }

    public class DataSelector : DataHolderGetter
    {
        public WatchPropertyReference targetPropertyRef;

        [SerializeField] private DataHolderScope m_scope = DataHolderScope.NearestParent;
        [SerializeField] private int m_ancestorLevel = 1;
        [SerializeField] private WatchPropertyDataHolder m_explicitHolder;

        /// <summary>取 Holder 的策略。改动后会立刻让缓存失效，下一次访问按新策略重新解析。</summary>
        public DataHolderScope Scope
        {
            get { return m_scope; }
            set { m_scope = value; RefreshDataHolder(); }
        }

        /// <summary>仅在 <see cref="DataHolderScope.AncestorLevel"/> 下生效：1 = 最近的 Holder，2 = 再上一层。</summary>
        public int AncestorLevel
        {
            get { return m_ancestorLevel; }
            set { m_ancestorLevel = Mathf.Max(1, value); RefreshDataHolder(); }
        }

        /// <summary>仅在 <see cref="DataHolderScope.Explicit"/> 下生效：手动指定的 Holder。</summary>
        public WatchPropertyDataHolder ExplicitHolder
        {
            get { return m_explicitHolder; }
            set { m_explicitHolder = value; RefreshDataHolder(); }
        }

        /// <summary>
        /// 沿「自身 → 父 → 祖父 → …」收集链路上所有 WatchPropertyDataHolder，index 0 为最近的一个。
        /// 编辑器面板用它列出可选项；运行时按层级取 Holder 时也走这里。
        /// 手动遍历而非 GetComponentInParent，理由同基类的 FindDataHolderInParents
        /// （不能跳过非激活节点，且 Unity 2019 没有 includeInactive 重载）。
        /// </summary>
        public static List<WatchPropertyDataHolder> CollectAncestorHolders(Transform start)
        {
            List<WatchPropertyDataHolder> result = new List<WatchPropertyDataHolder>();
            Transform current = start;
            while (current != null)
            {
                WatchPropertyDataHolder holder = current.GetComponent<WatchPropertyDataHolder>();
                if (holder != null)
                {
                    result.Add(holder);
                }
                current = current.parent;
            }
            return result;
        }

        /// <summary>
        /// 按当前 Scope 配置解析 DataHolder —— 这是插件里唯一决定「用谁」的地方。
        /// 覆盖基类实现，是因为基类把「撞见第一个就返回」写死了，没法选择更靠上的 Holder。
        /// </summary>
        protected override WatchPropertyDataHolder FindDataHolderInParents()
        {
            if (m_scope == DataHolderScope.Explicit)
            {
                return m_explicitHolder;
            }

            // NearestParent 就是层级链上的第 1 个，所以两种策略可以共用一条逻辑。
            int level = m_scope == DataHolderScope.AncestorLevel ? Mathf.Max(1, m_ancestorLevel) : 1;

            List<WatchPropertyDataHolder> chain = CollectAncestorHolders(transform);
            return level <= chain.Count ? chain[level - 1] : null;
        }

        /// <summary>
        /// 核心扩展用法：根据编辑器选中的值，动态获取具体的 WatchProperty 对象
        /// </summary>
        public object GetSelectedWatchProperty()
        {
            WatchPropertyDataHolder holder = DataHolder;
            if (holder == null || targetPropertyRef == null || string.IsNullOrEmpty(targetPropertyRef.propertyName))
            {
                return null;
            }

            // 使用反射获取字段值
            FieldInfo field = holder.GetType().GetField(targetPropertyRef.propertyName);
            if (field != null)
            {
                return field.GetValue(holder);
            }
            return null;
        }

        /// <summary>
        /// 快捷泛型获取方式
        /// </summary>
        public WatchProperty<T> GetSelectedWatchProperty<T>()
        {
            var res = GetSelectedWatchProperty() as WatchProperty<T>;
            if (res == null)
            {
                Debug.LogError($"没有在DataHolder中找到类型为{typeof(T).ToString()}的{targetPropertyRef.propertyName}");
            }
            return res;
        }

        public Type GetSelectedPropertyGenericType()
        {
            object propertyObj = GetSelectedWatchProperty();
            if (propertyObj == null) return null;

            Type type = propertyObj.GetType();

            // 向上寻找，直到找到继承的泛型基类 WatchProperty<T>
            while (type != null && type != typeof(object))
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AmagiSakuya.ObservableVariables.WatchProperty<>))
                {
                    // 抓取泛型参数列表中的第一个（即 T）
                    return type.GetGenericArguments()[0];
                }
                type = type.BaseType;
            }

            return null;
        }
    }
}
