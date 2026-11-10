using System;
using System.Collections.Generic;
using UnityEngine;

namespace AmagiSakuya.ObservableVariables
{
    // 1. 定义非泛型接口
    public interface IWatchProperty
    {
        object Value { get; set; }

        /// <summary>
        /// 非泛型写入，且不触发 OnChangeEvent。
        /// 运行时类型不确定（例如 int 或任意枚举）时，用它做单向同步可避免事件回环。
        /// </summary>
        void SetValueWithoutEvent(object value);

        /// <summary>
        /// 强制广播当前值：不修改值，只触发一次 OnChangeEvent(oldValue == newValue)。
        /// 非泛型调用入口，供不知道具体类型的组件（如 DataGetter）使用。
        /// </summary>
        void ForceNotifyCurrent();
    }

    [System.Serializable]
    public class WatchProperty<T> : IWatchProperty
    {
        [SerializeField] T m_editorValue;
        T m_initValue;
        bool declarationValue;
        T m_value;
        public T Value
        {
            get { return m_value; }
            set { SetValue(value, false); }
        }

        /// <summary>
        /// 统一写入入口：只有值真正变化时才触发 OnChangeEvent。
        /// forceNotify 为 true 时无论值是否相同都会触发（供 Init 做首帧强制刷新）。
        /// </summary>
        void SetValue(T value, bool forceNotify)
        {
            T oldValue = m_value;
            m_value = value;
            m_editorValue = value;
            if (forceNotify || !EqualityComparer<T>.Default.Equals(oldValue, value))
            {
                OnChangeEvent?.Invoke(oldValue, value);
            }
        }

        // 实现接口显式转换
        object IWatchProperty.Value
        {
            get => Value;
            set => Value = (T)value;
        }

        void IWatchProperty.SetValueWithoutEvent(object value)
        {
            if (value == null) return;
            SetValueWithoutEvent((T)value);
        }

        public Action<T, T> OnChangeEvent;
        public WatchProperty(T value)
        {
            declarationValue = true;
            m_editorValue = value;
            m_initValue = value;
            m_value = value;
        }

        public WatchProperty()
        {
            declarationValue = false;
            m_editorValue = default(T);
        }

        public void EditorUpdateValue()
        {
            Value = m_editorValue;
        }

        /// <summary>
        /// 初始化运行时值。
        /// </summary>
        public void Init(bool forceNotify = true)
        {
            SetValue(declarationValue ? m_initValue : m_editorValue, forceNotify);
        }

        /// <summary>
        /// 强制广播当前值：不修改值，只触发一次 OnChangeEvent(oldValue == newValue)。
        /// 用于需要让订阅方按当前数据重新刷新一次的场景。
        /// </summary>
        public void ForceNotifyCurrent()
        {
            OnChangeEvent?.Invoke(m_value, m_value);
        }

        public void SetValueWithoutEvent(T value)
        {
            m_value = value;
            m_editorValue = value;
        }
    }

    #region Extend
    [System.Serializable]
    public class WatchPropertyInt : WatchProperty<int>
    {
        public WatchPropertyInt(int value) : base(value) { }
        public WatchPropertyInt() : base() { }
    }

    [System.Serializable]
    public class WatchPropertyFloat : WatchProperty<float>
    {
        public WatchPropertyFloat(float value) : base(value) { }
        public WatchPropertyFloat() : base() { }
    }

    [System.Serializable]
    public class WatchPropertyBool : WatchProperty<bool>
    {
        public WatchPropertyBool(bool value) : base(value) { }
        public WatchPropertyBool() : base() { }
    }

    [System.Serializable]
    public class WatchPropertyString : WatchProperty<string>
    {
        public WatchPropertyString(string value) : base(value) { }
        public WatchPropertyString() : base() { }
    }

    [System.Serializable]
    public class WatchPropertyVector2 : WatchProperty<Vector2>
    {
        public WatchPropertyVector2(Vector2 value) : base(value) { }
        public WatchPropertyVector2() : base() { }
    }

    [System.Serializable]
    public class WatchPropertyVector2Int : WatchProperty<Vector2Int>
    {
        public WatchPropertyVector2Int(Vector2Int value) : base(value) { }
        public WatchPropertyVector2Int() : base() { }
    }

    [System.Serializable]
    public class WatchPropertyVector3 : WatchProperty<Vector3>
    {
        public WatchPropertyVector3(Vector3 value) : base(value) { }
        public WatchPropertyVector3() : base() { }
    }

    [System.Serializable]
    public class WatchPropertyVector3Int : WatchProperty<Vector3Int>
    {
        public WatchPropertyVector3Int(Vector3Int value) : base(value) { }
        public WatchPropertyVector3Int() : base() { }
    }

    [System.Serializable]
    public class WatchPropertyVector4 : WatchProperty<Vector4>
    {
        public WatchPropertyVector4(Vector4 value) : base(value) { }
        public WatchPropertyVector4() : base() { }
    }

    [System.Serializable]
    public class WatchPropertyGameObject : WatchProperty<GameObject>
    {
        public WatchPropertyGameObject(GameObject value) : base(value) { }
        public WatchPropertyGameObject() : base() { }
    }

    [System.Serializable]
    public class WatchPropertyTransform : WatchProperty<Transform>
    {
        public WatchPropertyTransform(Transform value) : base(value) { }
        public WatchPropertyTransform() : base() { }
    }

    [System.Serializable]
    public class WatchPropertyRectTransform : WatchProperty<RectTransform>
    {
        public WatchPropertyRectTransform(RectTransform value) : base(value) { }
        public WatchPropertyRectTransform() : base() { }
    }
    #endregion
}