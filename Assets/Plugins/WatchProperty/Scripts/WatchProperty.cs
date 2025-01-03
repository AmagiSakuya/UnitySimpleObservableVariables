using System;
using UnityEngine;

namespace AmagiSakuya.ObservableVariables
{
    [System.Serializable]
    public class WatchProperty<T>
    {
        [SerializeField] T m_value;
        T m_initValue;
        bool declarationValue;
        public T Value
        {
            get { return m_value; }
            set
            {
                T oldValue = m_value;
                m_value = value;
                OnChangeEvent?.Invoke(oldValue, value);
            }
        }

        public Action<T, T> OnChangeEvent;
        public WatchProperty(T value, Action<T, T> OnChangeCallback = null, bool DoOnChange = false)
        {
            declarationValue = true;
            T oldValue = m_value;
            m_initValue = value;
            m_value = value;
            OnChangeEvent = OnChangeCallback;
            if (DoOnChange)
            {
                OnChangeEvent?.Invoke(oldValue, value);
            }
        }

        public WatchProperty(Action<T, T> OnChangeCallback = null, bool DoOnChange = false)
        {
            declarationValue = false;
            T oldValue = m_value;
            m_value = default(T);
            OnChangeEvent = OnChangeCallback;
            if (DoOnChange)
            {
                OnChangeEvent?.Invoke(oldValue, default(T));
            }
        }

        public void EditorUpdateValue()
        {
            OnChangeEvent?.Invoke(Value, m_value);
        }

        public void Init()
        {
            if (declarationValue)
            {
                Value = m_initValue;
            }
            else
            {
                Value = m_value;
            }
        }
    }

    #region Extend
    [System.Serializable]
    public class WatchPropertyInt : WatchProperty<int>
    {
        public WatchPropertyInt(int value, Action<int, int> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyInt(Action<int, int> callback = null, bool doOnChange = false)
           : base(callback, doOnChange) { }
    }

    [System.Serializable]
    public class WatchPropertyFloat : WatchProperty<float>
    {
        public WatchPropertyFloat(float value, Action<float, float> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyFloat(Action<float, float> callback = null, bool doOnChange = false)
           : base(callback, doOnChange) { }
    }

    [System.Serializable]
    public class WatchPropertyBool : WatchProperty<bool>
    {
        public WatchPropertyBool(bool value, Action<bool, bool> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyBool(Action<bool, bool> callback = null, bool doOnChange = false)
          : base(callback, doOnChange) { }
    }

    [System.Serializable]
    public class WatchPropertyString : WatchProperty<string>
    {
        public WatchPropertyString(string value, Action<string, string> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyString(Action<string, string> callback = null, bool doOnChange = false)
         : base(callback, doOnChange) { }
    }

    [System.Serializable]
    public class WatchPropertyVector2 : WatchProperty<Vector2>
    {
        public WatchPropertyVector2(Vector2 value, Action<Vector2, Vector2> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyVector2(Action<Vector2, Vector2> callback = null, bool doOnChange = false)
         : base(callback, doOnChange) { }
    }

    [System.Serializable]
    public class WatchPropertyVector2Int : WatchProperty<Vector2Int>
    {
        public WatchPropertyVector2Int(Vector2Int value, Action<Vector2Int, Vector2Int> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyVector2Int(Action<Vector2Int, Vector2Int> callback = null, bool doOnChange = false)
         : base(callback, doOnChange) { }
    }

    [System.Serializable]
    public class WatchPropertyVector3 : WatchProperty<Vector3>
    {
        public WatchPropertyVector3(Vector3 value, Action<Vector3, Vector3> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyVector3(Action<Vector3, Vector3> callback = null, bool doOnChange = false)
         : base(callback, doOnChange) { }
    }

    [System.Serializable]
    public class WatchPropertyVector3Int : WatchProperty<Vector3Int>
    {
        public WatchPropertyVector3Int(Vector3Int value, Action<Vector3Int, Vector3Int> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyVector3Int(Action<Vector3Int, Vector3Int> callback = null, bool doOnChange = false)
         : base(callback, doOnChange) { }
    }

    [System.Serializable]
    public class WatchPropertyVector4 : WatchProperty<Vector4>
    {
        public WatchPropertyVector4(Vector4 value, Action<Vector4, Vector4> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyVector4(Action<Vector4, Vector4> callback = null, bool doOnChange = false)
         : base(callback, doOnChange) { }
    }

    [System.Serializable]
    public class WatchPropertyGameObject : WatchProperty<GameObject>
    {
        public WatchPropertyGameObject(GameObject value, Action<GameObject, GameObject> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyGameObject(Action<GameObject, GameObject> callback = null, bool doOnChange = false)
         : base(callback, doOnChange) { }
    }

    [System.Serializable]
    public class WatchPropertyTransform : WatchProperty<Transform>
    {
        public WatchPropertyTransform(Transform value, Action<Transform, Transform> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyTransform(Action<Transform, Transform> callback = null, bool doOnChange = false)
         : base(callback, doOnChange) { }
    }

    [System.Serializable]
    public class WatchPropertyRectTransform : WatchProperty<RectTransform>
    {
        public WatchPropertyRectTransform(RectTransform value, Action<RectTransform, RectTransform> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyRectTransform(Action<RectTransform, RectTransform> callback = null, bool doOnChange = false)
         : base(callback, doOnChange) { }
    }
    #endregion
}