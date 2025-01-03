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
            //Do EveryYouWant

            Debug.Log($"ObservableVariablesDemoEnum,Now Value:{now}");
        }

        void OnDemoFloatChange(float old, float now)
        {

            Debug.Log($"OnDemoFloatChange,Now Value:{now}");
        }

        void OnDemoBoolChange(bool old, bool now)
        {

            Debug.Log($"OnDemoBoolChange,Now Value:{now}");
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