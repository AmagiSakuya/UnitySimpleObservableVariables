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

    public class DataSelector : DataHolderGetter
    {
        public WatchPropertyReference targetPropertyRef;

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
