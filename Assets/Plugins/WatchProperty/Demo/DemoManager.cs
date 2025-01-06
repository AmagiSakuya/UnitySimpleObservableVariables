using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AmagiSakuya.ObservableVariables.Demo
{
    public class DemoManager : MonoBehaviour
    {
        public DemoDataHolder data;
        bool inited = false;

        void Awake()
        {
            data.demoEnum.OnChangeEvent += OnDemoEnumChange;
            data.demoFloat.OnChangeEvent += OnDemoFloatChange;
            data.demoBool.OnChangeEvent += OnDemoBoolChange;
        }

        void Update()
        {
            if (!inited) { data.InitData(); inited = true; }
        }

        void OnDemoEnumChange(ObservableVariablesDemoEnum old, ObservableVariablesDemoEnum now)
        {
            if (old != now)
                Debug.Log($"ObservableVariablesDemoEnum,{old} -> {now}");
        }

        void OnDemoFloatChange(float old, float now)
        {
            if (old != now)
                Debug.Log($"OnDemoFloatChange,{old} -> {now}");
        }

        void OnDemoBoolChange(bool old, bool now)
        {
            if (old != now)
                Debug.Log($"OnDemoBoolChange,{old} -> {now}");
        }

        public void ChangeDemoEnumByScript()
        {
            if (data.demoEnum.Value == ObservableVariablesDemoEnum.Up)
            {
                data.demoEnum.Value = ObservableVariablesDemoEnum.Down;
            }
            else if (data.demoEnum.Value == ObservableVariablesDemoEnum.Down)
            {
                data.demoEnum.Value = ObservableVariablesDemoEnum.None;
            }
            else if (data.demoEnum.Value == ObservableVariablesDemoEnum.None)
            {
                data.demoEnum.Value = ObservableVariablesDemoEnum.Up;
            }
        }

        public void ChangeDemoFloatByScript()
        {
            data.demoFloat.Value += 1f;
        }

        public void ChangeDemoBoolByScript()
        {
            data.demoBool.Value = !data.demoBool.Value;
        }
    }
}