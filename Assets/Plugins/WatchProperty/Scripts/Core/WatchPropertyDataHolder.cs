using System.Reflection;
using UnityEngine;

namespace AmagiSakuya.ObservableVariables
{
    public class WatchPropertyDataHolder : MonoBehaviour
    {
        public void InitData()
        {
            // Init(bool forceNotify = true)：反射 Invoke 不会自动填充默认参数，必须显式传入
            DoWatchPropertyMethod("Init", new object[] { true });
        }

        void DoWatchPropertyMethod(string methodName, object[] args)
        {
            var fields = GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                if (field.FieldType.BaseType != null
                    && field.FieldType.BaseType.IsGenericType
                    && field.FieldType.BaseType.GetGenericTypeDefinition() == typeof(WatchProperty<>))
                {
                    var propertyInstance = field.GetValue(this);
                    if (propertyInstance != null)
                    {
                        var initMethod = propertyInstance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
                        initMethod?.Invoke(propertyInstance, args);
                    }
                }
            }
        }

        private void OnValidate()
        {
            DoWatchPropertyMethod("EditorUpdateValue", null);
        }
    }
}