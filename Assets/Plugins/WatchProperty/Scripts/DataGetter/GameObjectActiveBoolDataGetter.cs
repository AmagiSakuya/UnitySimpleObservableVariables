using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AmagiSakuya.ObservableVariables
{
    public class GameObjectActiveBoolDataGetter : DataGetter
    {
        public List<GameObject> objs;
        public bool reserve;

        protected override void OnDataGetUpdate(Type propType)
        {
            if (propType == typeof(bool))
            {
                bool res = GetSelectedWatchProperty<bool>().Value;
                if (reserve) res = !res;
                for (int i = 0; i < objs.Count; i++)
                {
                    objs[i].SetActive(res);
                }
            }
        }
    }
}