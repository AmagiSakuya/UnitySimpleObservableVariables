using System;
using UnityEngine;

namespace AmagiSakuya.ObservableVariables
{
    [System.Serializable]
    public class WatchProperty<T>
    {
        [SerializeField] T m_editorValue;
        T m_initValue;
        bool declarationValue;
        T m_value;
        public T Value
        {
            get { return m_value; }
            set
            {
                T oldValue = m_value;
                m_value = value;
                m_editorValue = value;
                OnChangeEvent?.Invoke(oldValue, value);
            }
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

        public void Init()
        {
            if (declarationValue)
            {
                Value = m_initValue;
            }
            else
            {
                Value = m_editorValue;
            }
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