using System.Reflection;
using UnityEngine;

namespace AmagiSakuya.ObservableVariables
{
    public class WatchPropertyDataHolder : MonoBehaviour
    {
        public void InitData()
        {
            DoWatchPropertyMethod("Init");
        }

        void DoWatchPropertyMethod(string methodName)
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
                        initMethod?.Invoke(propertyInstance, null);
                    }
                }
            }
        }

        private void OnValidate()
        {
            DoWatchPropertyMethod("EditorUpdateValue");
        }
    }
}