using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AmagiSakuya.ObservableVariables.Demo
{
    public enum ObservableVariablesDemoEnum { None, Up, Down }

    [System.Serializable]
    public class WatchPropertyObservableVariablesDemoEnum : WatchProperty<ObservableVariablesDemoEnum>
    {
        public WatchPropertyObservableVariablesDemoEnum(ObservableVariablesDemoEnum value, Action<ObservableVariablesDemoEnum, ObservableVariablesDemoEnum> callback = null, bool doOnChange = false)
            : base(value, callback, doOnChange) { }
        public WatchPropertyObservableVariablesDemoEnum(Action<ObservableVariablesDemoEnum, ObservableVariablesDemoEnum> callback = null, bool doOnChange = false)
           : base(callback, doOnChange) { }
    }

    public class DemoDataHolder : WatchPropertyDataHolder
    {
        //if a value is provided during declaration, the script will use that value for initialization at runtime.
        //If no value is provided during declaration, the value from the inspector panel will be used as the initialization value.
        //如果在声明时传入值 则脚本运行时的初始化会使用该值
        //若声明时传入 则使用面板的值作为初始化的值
        public WatchPropertyObservableVariablesDemoEnum demoEnum = new WatchPropertyObservableVariablesDemoEnum(ObservableVariablesDemoEnum.None);

        public WatchPropertyFloat demoFloat = new WatchPropertyFloat(1f);

        public WatchPropertyBool demoBool = new WatchPropertyBool();
    }
}