using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AmagiSakuya.ObservableVariables
{
    public class DataSetter : DataSelector
    {
        protected void SetPropValueWithOutEvent<T>(T value)
        {
            WatchProperty<T> watchedValue = GetSelectedWatchProperty<T>();
            watchedValue.SetValueWithoutEvent(value);
        }

        /// <summary>
        /// 非泛型版本：运行时类型不确定（例如 int 或任意枚举）时使用，写入不会触发 OnChangeEvent。
        /// </summary>
        protected void SetPropValueWithOutEvent(object value)
        {
            IWatchProperty watchedValue = GetSelectedWatchProperty() as IWatchProperty;
            if (watchedValue == null) return;
            watchedValue.SetValueWithoutEvent(value);
        }
    }
}