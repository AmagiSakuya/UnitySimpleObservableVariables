using MTST_RCS;
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

    public class DataSelector : MonoBehaviour
    {
        public WatchPropertyReference targetPropertyRef;

        // 缓存查找到的组件
        private MTST_RCSDataHolder m_cachedHolder;

        /// <summary>
        /// 往上寻找第一个 MTST_RCSDataHolder
        /// </summary>
        public MTST_RCSDataHolder GetHolder()
        {
            if (m_cachedHolder == null)
            {
                m_cachedHolder = GetComponentInParent<MTST_RCSDataHolder>();
            }
            return m_cachedHolder;
        }

        /// <summary>
        /// 核心扩展用法：根据编辑器选中的值，动态获取具体的 WatchProperty 对象
        /// </summary>
        public object GetSelectedWatchProperty()
        {
            MTST_RCSDataHolder holder = GetHolder();
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