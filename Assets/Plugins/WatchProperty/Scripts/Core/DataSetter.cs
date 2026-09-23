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
    }
}