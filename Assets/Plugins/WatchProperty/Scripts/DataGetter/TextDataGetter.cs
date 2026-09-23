using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AmagiSakuya.ObservableVariables
{
    public class TextDataGetter : DataGetter
    {
        [SerializeField] Text text;
        [SerializeField] string floatValueDecimalPoint = "f2";
        [SerializeField] string appendString = "";
        [SerializeField] float floatOrIntMultiply = 1f;
        protected override void OnDataGetUpdate(Type propType)
        {
            string res = "";
            if (propType == typeof(float))
            {
                float value = GetSelectedWatchProperty<float>().Value;
                value = value * floatOrIntMultiply;
                res = value.ToString(floatValueDecimalPoint);
            }
            else if (propType == typeof(int))
            {
                int value = GetSelectedWatchProperty<int>().Value;
                value = value * (int)floatOrIntMultiply;
                res = value.ToString(floatValueDecimalPoint);
            }
            else if (propType == typeof(string))
            {
                res = GetSelectedWatchProperty<string>().Value;
            }
            text.text = res + appendString;
        }

    }
}